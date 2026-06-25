using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// Scene-wide traffic director. It scans every <see cref="RoadComponent"/> and <see cref="RoadIntersectionComponent"/>,
/// works out how they connect (the same proximity test "Snap Nearby Roads" uses), and builds a drivable lane graph.
/// In play mode it spawns vehicle-prefab instances that drive the network; in the editor it draws the computed layout.
/// </summary>
[Icon("directions_car")]
public sealed class RoadManager : Component, Component.ExecuteInEditor, IHotloadManaged
{
	private const float TrafficStreamInterval = 0.25f; // seconds between streaming passes (despawn strays, top up near players)
	private const int MaxSpawnsPerTick = 5;            // cap cars spawned per pass so a fresh area fills in gradually, not in one burst

	[SkipHotload] public static RoadManager Current { get; private set; } = null;
	
	[Property, Feature("Traffic", Icon = "traffic"), Category("Vehicles")] private VehicleSetResource VehicleSet { get; set; }
	[Property(Title = "Default Speed"), Feature("Traffic"), Category("Vehicles"), Range(5.0f, 130.0f)] private float DefaultSpeed { get; set; } = 30.0f;
	[Property(Title = "Following Gap"), Feature("Traffic"), Category("Vehicles"), Range(50.0f, 600.0f)] private float VehicleSpacing { get; set; } = 180.0f;
	[Property(Title = "Spawn Gap"), Feature("Traffic"), Category("Vehicles"), Range(50.0f, 1000.0f)] private float SpawnGap { get; set; } = 250.0f;
	[Property(Title = "Stop Margin"), Feature("Traffic"), Category("Vehicles"), Range(0.0f, 500.0f)] private float StopMargin { get; set; } = 150.0f;

	[Property(Title = "Density"), Feature("Traffic"), Category("Streaming"), Range(0.0f, 1.0f)] private float TrafficDensity { get; set; } = 0.15f;
	[Property(Title = "Spawn Min Range"), Feature("Traffic"), Category("Streaming"), Range(0.0f, 20000.0f)] private float TrafficSpawnMinDistance { get; set; } = 3000.0f;
	[Property(Title = "Spawn Range"), Feature("Traffic"), Category("Streaming"), Range(500.0f, 20000.0f)] private float TrafficSpawnDistance { get; set; } = 5000.0f;
	[Property(Title = "Despawn Range"), Feature("Traffic"), Category("Streaming"), Range(500.0f, 20000.0f)] private float TrafficDespawnDistance { get; set; } = 7000.0f;
	[Property(Title = "Height Offset"), Feature("Traffic"), Category("Streaming"), Range(0.0f, 300.0f)] private float SpawnHeightOffset { get; set; } = 0.0f;
	
	[Property(Title = "Brake For Tags"), Feature("Traffic"), Category("Awareness")] private TagSet AwarenessTags { get; set; }
	[Property(Title = "Detect Radius"), Feature("Traffic"), Category("Awareness"), Range(10.0f, 200.0f)] private float AwarenessRadius { get; set; } = 100.0f;

	[Property(Title = "Lose Patience After"), Feature("Traffic"), Category("Road Rage"), Range(1.0f, 120.0f)] private float LoosePatience { get; set; } = 10.0f;
	[Property(Title = "Road Rage Duration"), Feature("Traffic"), Category("Road Rage"), Range(1.0f, 120.0f)] private float RoadRageDuration { get; set; } = 5.0f;

	[Property, Feature("Traffic"), Category("Layout"), Range(50.0f, 500.0f)] private float WaypointSpacing { get; set { field = value.Clamp(10.0f, 10000.0f); m_IsDirty = true; } } = 150.0f;
	[Property(Title = "Connection Distance"), Feature("Traffic"), Category("Layout"), Range(20.0f, 600.0f)] private float LinkThreshold { get; set { field = value; m_IsDirty = true; } } = 200.0f;

	[Property, Feature("Debug", Icon = "bug_report", Tint = EditorTint.Red)] private bool ShowGizmos { get; set; } = true;
	[Property, Feature("Debug")] public bool ShowOverlays { get; set; } = true;

	[Property, Feature("Parking Lot", Icon = "local_parking", Tint = EditorTint.Yellow)] public float ParkedVehicleDespawnDistance { get; set; } = 6000.0f;
	[Property, Feature("Parking Lot")] public float ParkedVehicleRespawnDistance { get; set; } = 4000.0f;

	/// <summary>
	/// Optional override for how the library locates players. Set this from your game to feed players in from your own
	/// (faster) source — e.g. a static player list — and the library queries it instead of scanning the scene. Leave it
	/// null and the default applies: every object carrying the "player" tag. The default is universal — it never depends
	/// on a specific player component — but it walks the whole scene each call, so plug this in for big maps / many spots.
	/// </summary>
	public static Func<IEnumerable<GameObject>> FindPlayers { get; set; }

	/// <summary>
	/// Optional override for how the tool drives a vehicle as NPC traffic. Set this from your game to hook your OWN car
	/// controller in WITHOUT touching the road tool (and without your vehicle code referencing this library): given a
	/// spawned vehicle GameObject, return a <see cref="RoadVehicleDriver"/> wired to its controller, or null if it isn't
	/// a drivable physics car (the tool then moves it kinematically on rails). This is the single seam between the
	/// traffic AI and a vehicle's physics/controller. Leave it null and the default applies — it wires the included
	/// <see cref="DemoCarController"/> so the demo works out of the box.
	/// </summary>
	public static Func<GameObject, RoadVehicleDriver> ResolveVehicleDriver { get; set; }

	private RoadTrafficGraph m_Graph;
	private readonly List<TrafficVehicle> m_Vehicles = [];
	private readonly List<(TrafficLane Lane, int Index)> m_SpawnSlots = []; // candidate spawn points along the lanes, rebuilt with the graph
	private bool m_IsDirty = true;
	private float m_TrafficStreamCooldown;



	private RoadTrafficSettings Settings => new()
	{
		WaypointSpacing = WaypointSpacing,
		LinkThreshold = LinkThreshold
	};
	
	
	
	protected override void OnAwake()
	{
		Current ??= this;
	}
	
	
	
	void IHotloadManaged.Destroyed(Dictionary<string, object> _State)
	{
		_State["IsActive"] = Current == this;
	}



	void IHotloadManaged.Created(IReadOnlyDictionary<string, object> _State)
	{
		if (_State.GetValueOrDefault("IsActive") is true)
			Current = this;
	}
	
	
	
	protected override void OnEnabled()
	{
		m_IsDirty = true;
	}



	protected override void OnDisabled()
	{
		RemoveVehicles();
		m_Graph = null;
		m_SpawnSlots.Clear();
	}
	
	
	
	protected override void OnDestroy()
	{
		if (Current == this)
			Current = null;
	}
	
	
	
	protected override void OnUpdate()
	{
		if (SandboxUtility.IsInPlayMode)
		{
			// The RoadManager object is networked (Orphaned: Host), so it runs on clients too — but the traffic is owned
			// entirely by the host: it builds the graph, spawns and despawns. A proxy just receives the networked cars.
			if (IsProxy)
				return;

			// The host needs the lane graph before it can spawn anything; build it once.
			if (m_Graph is null)
				RebuildGraph();

			// GTA-style streaming: despawn cars that have drifted away from every player, and spawn new ones at empty
			// lane slots in a ring around players, up to the density target.
			StreamTraffic();

			// Debug
			DrawLayoutDebugOverlay();

			return;
		}

		// Editor: keep the gizmo layout in sync when a layout property changes.
		if (m_IsDirty)
		{
			RebuildGraph();
			m_IsDirty = false;
		}
	}



	/// <summary>
	/// Every player in the scene. Comes from <see cref="FindPlayers"/> when a game has plugged in its own (faster)
	/// source, otherwise the default universal scan for objects tagged "player". Iterate this for distance checks so a
	/// game's override applies to ALL callers (parking spots, etc.) at once, instead of each one scanning the scene.
	/// </summary>
	public static IEnumerable<GameObject> GetPlayers()
	{
		if (FindPlayers is null)
		{
			var players = new List<GameObject>();

			foreach (var player in Game.ActiveScene.FindAllWithTag("player"))
			{
				// We ignore all child gameobjects of the player
				if (player.Parent.IsValid() && player.Parent.Tags.Has("player"))
					continue;
				
				players.Add(player);
			}
			
			return players;
		}
		
		return FindPlayers();
	}



	/// <summary>True if any player is within <paramref name="_Distance"/> of <paramref name="_Point"/> — built on <see cref="GetPlayers"/>, so a game's override is honored here too.</summary>
	public static bool ArePlayersWithin(Vector3 _Point, float _Distance)
	{
		float distanceSq = _Distance * _Distance;

		foreach (GameObject player in GetPlayers())
		{
			if (player.WorldPosition.DistanceSquared(_Point) < distanceSq)
				return true;
		}

		return false;
	}



	/// <summary>
	/// The control surface the traffic AI drives for <paramref name="_Vehicle"/>. Comes from
	/// <see cref="ResolveVehicleDriver"/> when a game has hooked its own controller, otherwise the built-in default:
	/// wire the included <see cref="DemoCarController"/> if present. Returns null when the vehicle has no drivable
	/// controller — the brain then falls back to lightweight on-rails movement.
	/// </summary>
	public static RoadVehicleDriver GetVehicleDriver(GameObject _Vehicle)
	{
		if (ResolveVehicleDriver is not null)
			return ResolveVehicleDriver(_Vehicle);

		// Default: wire the demo car so the included demo works with no game-side hook. A real game sets
		// ResolveVehicleDriver to map its own controller instead — that controller never references this library.
		var demo = _Vehicle.GetComponent<DemoCarController>();

		if (demo is null)
			return null;

		return new RoadVehicleDriver
		{
			IsPlayerDriving = () => demo.IsDriven,
			Velocity = () => demo.Rigidbody.IsValid() ? demo.Rigidbody.Velocity : Vector3.Zero,
			SetAiControlled = _Ai => demo.IsAiControlled = _Ai,
			Drive = (_Throttle, _Steer, _Handbrake) =>
			{
				demo.AiThrottle = _Throttle;
				demo.AiSteer = _Steer;
				demo.AiHandbrake = _Handbrake;
			},
			MaxSteering = demo.GetMaxSteering
		};
	}
	
	
	
	// GTA-style streaming (host only, throttled). Two passes: cull cars that have drifted out of every player's range,
	// then top traffic back up to the density target by spawning at empty lane slots in a ring around the players.
	private void StreamTraffic()
	{
		m_TrafficStreamCooldown -= Time.Delta;

		if (m_TrafficStreamCooldown > 0.0f)
			return;

		m_TrafficStreamCooldown = TrafficStreamInterval;

		// Grab the player positions once for this whole pass.
		var players = new List<Vector3>();

		foreach (GameObject player in GetPlayers())
		{
			if (player.IsValid())
				players.Add(player.WorldPosition);
		}

		DespawnStrayVehicles(players);

		if (players.Count > 0 && m_SpawnSlots.Count > 0 && VehicleSet.IsValid() && VehicleSet.Prefabs.Length > 0)
			TopUpTraffic(players);
	}



	// Per-vehicle despawn: each car is judged by ITS OWN distance to the nearest player, so only the ones that have
	// genuinely drifted out of range disappear. The car the player last drove ("last_vehicle") is never culled.
	private void DespawnStrayVehicles(List<Vector3> _Players)
	{
		float despawnSq = TrafficDespawnDistance * TrafficDespawnDistance;

		for (int i = m_Vehicles.Count - 1; i >= 0; i--)
		{
			TrafficVehicle vehicle = m_Vehicles[i];

			if (!vehicle.IsValid())
			{
				m_Vehicles.RemoveAt(i);
				continue;
			}

			if (vehicle.GameObject.Tags.Has("last_vehicle"))
				continue;

			if (NearestDistanceSq(vehicle.WorldPosition, _Players) > despawnSq)
			{
				var entityFade = vehicle.GetComponent<EntityFade>();

				if (entityFade.IsValid())
				{
					entityFade.FadeOutAndDestroy();
				}
				else
				{
					vehicle.DestroyGameObject();
				}
				
				m_Vehicles.RemoveAt(i);
			}
		}
	}



	// Tops the area up to the density target by spawning at empty slots inside the spawn ring around players. Density is
	// a fraction of the road capacity currently near a player, so "1" packs the nearby roads and "0.1" leaves them nearly
	// empty. Spawns are capped per pass so a fresh area fills in over a second or two rather than in one burst.
	private void TopUpTraffic(List<Vector3> _Players)
	{
		float despawnSq = TrafficDespawnDistance * TrafficDespawnDistance;
		float minSq = TrafficSpawnMinDistance * TrafficSpawnMinDistance;
		float maxSq = TrafficSpawnDistance * TrafficSpawnDistance;
		float clearanceSq = SpawnGap * SpawnGap;

		// Capacity = slots near a player; target = that × density; deficit = how far below target we are right now.
		int capacity = 0;

		foreach (var slot in m_SpawnSlots)
		{
			if (NearestDistanceSq(slot.Lane.Waypoints[slot.Index], _Players) <= despawnSq)
				capacity++;
		}

		int live = 0;

		foreach (var vehicle in m_Vehicles)
		{
			if (vehicle.IsValid() && NearestDistanceSq(vehicle.WorldPosition, _Players) <= despawnSq)
				live++;
		}

		int deficit = Math.Min((int)(capacity * TrafficDensity) - live, MaxSpawnsPerTick);

		if (deficit <= 0)
			return;

		// Walk the slots from a random offset so we don't always favour the same lanes, spawning at any that sit in the
		// ring (near enough to matter, far enough not to pop in) and aren't already occupied by another car.
		int start = Game.Random.Next(m_SpawnSlots.Count);

		for (int n = 0; n < m_SpawnSlots.Count && deficit > 0; n++)
		{
			var slot = m_SpawnSlots[(start + n) % m_SpawnSlots.Count];
			Vector3 pos = slot.Lane.Waypoints[slot.Index];
			float nearestSq = NearestDistanceSq(pos, _Players);

			if (nearestSq < minSq || nearestSq > maxSq)
				continue;

			if (IsAreaOccupied(pos, clearanceSq))
				continue;

			SpawnVehicleAt(slot.Lane, slot.Index);
			deficit--;
		}
	}



	private bool IsAreaOccupied(Vector3 _Point, float _RadiusSq)
	{
		foreach (var vehicle in m_Vehicles)
		{
			if (vehicle.IsValid() && vehicle.WorldPosition.DistanceSquared(_Point) < _RadiusSq)
				return true;
		}

		return false;
	}



	private static float NearestDistanceSq(Vector3 _Point, List<Vector3> _Players)
	{
		float best = float.MaxValue;

		foreach (Vector3 player in _Players)
			best = MathF.Min(best, player.DistanceSquared(_Point));

		return best;
	}



	private void DrawLayoutDebugOverlay()
	{
		if (!ShowOverlays)
			return;
		
		Vector3 offset = Vector3.Up * 25;
		
		foreach (var lane in m_Graph.Lanes)
		{
			if (lane.Waypoints.Count < 2)
				continue;

			Color color = lane.IsRoadLane ? Color.Cyan : new Color(0.4f, 1.0f, 0.4f);
			
			for (int i = 0; i < lane.Waypoints.Count - 1; i++)
			{
				Vector3 a = lane.Waypoints[i] + offset;
				Vector3 b = lane.Waypoints[i + 1] + offset;
				
				DebugOverlay.Line(a, b, color);
			}
			
			// Contained dead-end "U-turn" arc.
			if (lane.UTurnArc is { Count: >= 2 })
			{
				color = new Color(1.0f, 0.6f, 0.1f);

				for (int i = 0; i < lane.UTurnArc.Count - 1; i++)
				{
					Vector3 ua = lane.UTurnArc[i] + offset;
					Vector3 ub = lane.UTurnArc[i + 1] + offset;
					
					DebugOverlay.Line(ua, ub, color);
				}
			}
		}
	}
	
	
	
	// TODO: Uncomment this when my VehicleController library will be publicly available
	// [InfoBox("It's heavily recommend to use my VehicleController library with this tool or building your own vehicle physics/controller.", "info", EditorTint.Yellow)]
	[Button("Rebuild Pathfinding Layout"), Feature("Traffic"), Order(10)]
	public void RebuildPathfindingLayout()
	{
		RebuildGraph();

		if (SandboxUtility.IsInPlayMode)
		{
			// Drop the current fleet; streaming re-populates it on the fresh graph next pass (around any nearby player).
			RemoveVehicles();
			m_TrafficStreamCooldown = 0.0f;
		}

		m_IsDirty = false;

		int roads = m_Graph?.RoadCount ?? 0;
		int intersections = m_Graph?.IntersectionCount ?? 0;
		int lanes = m_Graph?.Lanes.Count ?? 0;

		SandboxUtility.ShowEditorNotification($"Traffic layout rebuilt — {roads} roads, {intersections} intersections, {lanes} lanes");
	}



	private void RebuildGraph()
	{
		m_Graph = RoadTrafficGraph.Build(Scene, Settings);
		BuildSpawnSlots();
	}



	// (Re)builds the list of candidate spawn points — a waypoint index every few steps along every road lane, so two
	// cars never land on the same spot. Rebuilt with the graph; the streaming pass spawns at these on demand.
	private void BuildSpawnSlots()
	{
		m_SpawnSlots.Clear();

		if (m_Graph is null)
			return;

		// Prefer road lanes so cars start out on actual roads, not mid-intersection.
		var lanes = m_Graph.Lanes.Where(l => l.IsRoadLane && l.Waypoints.Count >= 2).ToList();

		if (lanes.Count == 0)
			lanes = m_Graph.Lanes.Where(l => l.Waypoints.Count >= 2).ToList();

		int step = Math.Max(1, (int)MathF.Ceiling(SpawnGap / MathF.Max(1.0f, WaypointSpacing)));

		foreach (var lane in lanes)
		{
			for (int idx = 0; idx < lane.Waypoints.Count - 1; idx += step)
				m_SpawnSlots.Add((lane, idx));
		}
	}



	// Spawns one networked traffic car at a slot and wires up its brain. Host only (called from the streaming pass).
	private void SpawnVehicleAt(TrafficLane _Lane, int _Index)
	{
		Vector3 spawnPos = _Lane.Waypoints[_Index] + Vector3.Up * SpawnHeightOffset;
		GameObject prefab = VehicleSet.Prefabs[Game.Random.Next(VehicleSet.Prefabs.Length)];
		GameObject clone = prefab.Clone(spawnPos, Rotation.Identity, Vector3.One);

		if (!clone.IsValid())
			return;

		clone.NetworkSpawn(Connection.Host);
		clone.Network.SetOrphanedMode(NetworkOrphaned.Host);
		clone.Network.SetOwnerTransfer(OwnerTransfer.Request);
		
		// The prefab is just the visual/body — the manager attaches the driver and points it at its lane.
		var vehicle = clone.GetOrAddComponent<TrafficVehicle>();
		vehicle.DefaultSpeed = DefaultSpeed * TrafficMath.KmhToUnits;
		vehicle.HeightOffset = SpawnHeightOffset;
		vehicle.Spacing = VehicleSpacing;
		vehicle.StopMargin = StopMargin;
		vehicle.LoosePatience = LoosePatience;
		vehicle.RoadRageDuration = RoadRageDuration;
		vehicle.Neighbors = m_Vehicles;
		vehicle.AwareTags = AwarenessTags;
		vehicle.DetectRadius = AwarenessRadius;
		vehicle.Initialize(m_Graph, _Lane, _Index, Game.Random.Next());

		var renderer = clone.GetComponent<ModelRenderer>();

		// Give it a cool random tint
		if (renderer.IsValid())
			renderer.Tint = new Color(Game.Random.NextSingle(), Game.Random.NextSingle(), Game.Random.NextSingle(), renderer.Tint.a);

		Network.Refresh(renderer);
		
		m_Vehicles.Add(vehicle);
	}
	
	
	
	private void RemoveVehicles()
	{
		foreach (var vehicle in m_Vehicles.ToArray())
			vehicle.DestroyGameObject();
		
		m_Vehicles.Clear();
	}



	protected override void DrawGizmos()
	{
		if (!ShowGizmos || !Gizmo.IsSelected || m_Graph is null)
			return;

		Gizmo.Draw.LineThickness = 2.0f;

		foreach (var lane in m_Graph.Lanes)
		{
			if (lane.Waypoints.Count < 2)
				continue;

			Gizmo.Draw.Color = lane.IsRoadLane ? Color.Cyan : new Color(0.4f, 1.0f, 0.4f);

			for (int i = 0; i < lane.Waypoints.Count - 1; i++)
			{
				Vector3 a = WorldTransform.PointToLocal(lane.Waypoints[i]);
				Vector3 b = WorldTransform.PointToLocal(lane.Waypoints[i + 1]);
				Gizmo.Draw.Line(a, b);
			}

			// Direction arrow at the lane end.
			Vector3 end = WorldTransform.PointToLocal(lane.EndPos);
			Vector3 dir = WorldRotation.Inverse * lane.EndDir;
			Gizmo.Draw.Arrow(end - dir * 80.0f, end);

			// Contained dead-end U-turn arc.
			if (lane.UTurnArc is { Count: >= 2 })
			{
				Gizmo.Draw.Color = new Color(1.0f, 0.6f, 0.1f);

				for (int i = 0; i < lane.UTurnArc.Count - 1; i++)
				{
					Vector3 ua = WorldTransform.PointToLocal(lane.UTurnArc[i]);
					Vector3 ub = WorldTransform.PointToLocal(lane.UTurnArc[i + 1]);
					Gizmo.Draw.Line(ua, ub);
				}
			}
		}

		DrawSpeedLabels();
	}



	// Floating "NN km/h" labels above each road's midpoint and each intersection's center.
	private void DrawSpeedLabels()
	{
		Gizmo.Draw.Color = Color.White;

		foreach (var road in Scene.GetAll<RoadComponent>())
		{
			if (!road.IsValid() || road.ExcludeTraffic || road.Spline is null)
				continue;

			Vector3 mid = road.WorldTransform.PointToWorld(road.Spline.SampleAtDistance(road.Spline.Length * 0.5f).Position);
			Vector3 local = WorldTransform.PointToLocal(mid + Vector3.Up * 160.0f);
			Gizmo.Draw.Text($"{road.SpeedLimit:0} km/h", new Transform(local));
		}

		foreach (var intersection in Scene.GetAll<RoadIntersectionComponent>())
		{
			if (!intersection.IsValid() || intersection.ExcludeTraffic)
				continue;

			Vector3 local = WorldTransform.PointToLocal(intersection.WorldPosition + Vector3.Up * 160.0f);
			Gizmo.Draw.Text($"{intersection.SpeedLimit:0} km/h", new Transform(local));
		}
	}
}
