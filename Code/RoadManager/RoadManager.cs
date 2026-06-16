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
public sealed class RoadManager : Component, Component.ExecuteInEditor
{
	private const string VehicleContainerName = "Traffic_Vehicles";

	[Property(Title = "Prefab"), Feature("Traffic"), Category("Vehicles")] private GameObject VehiclePrefab { get; set; }
	[Property, Feature("Traffic"), Category("Vehicles"), Range(0, 500)] private int VehicleCount { get; set { field = value; m_IsDirty = true; } } = 30;
	[Property(Title = "Default Speed"), Feature("Traffic"), Category("Vehicles"), Range(5.0f, 130.0f)] private float DefaultSpeed { get; set; } = 20.0f;
	[Property(Title = "Following Gap"), Feature("Traffic"), Category("Vehicles"), Range(50.0f, 600.0f)] private float VehicleSpacing { get; set; } = 180.0f;
	[Property(Title = "Height Offset"), Feature("Traffic"), Category("Vehicles"), Range(0.0f, 300.0f)] private float HoverHeight { get; set; } = 45.0f;

	[Property(Title = "Brake For Tags"), Feature("Traffic"), Category("Awareness")] private TagSet AwarenessTags { get; set; }
	[Property(Title = "Detect Radius"), Feature("Traffic"), Category("Awareness"), Range(10.0f, 200.0f)] private float AwarenessRadius { get; set; } = 50.0f;

	[Property, Feature("Traffic"), Category("Layout"), Range(50.0f, 500.0f)] private float WaypointSpacing { get; set { field = value; m_IsDirty = true; } } = 150.0f;
	[Property(Title = "Connection Distance"), Feature("Traffic"), Category("Layout"), Range(20.0f, 600.0f)] private float LinkThreshold { get; set { field = value; m_IsDirty = true; } } = 200.0f;

	[Property, Feature("Traffic"), Category("Debug")] private bool ShowLayoutGizmos { get; set; } = true;

	private RoadTrafficGraph m_Graph;
	private GameObject m_VehicleContainer;
	private readonly List<TrafficVehicle> m_Vehicles = new();
	private bool m_IsDirty = true;
	private bool m_HasSpawned;



	private RoadTrafficSettings Settings => new()
	{
		WaypointSpacing = WaypointSpacing,
		LinkThreshold = LinkThreshold
	};



	protected override void OnEnabled()
	{
		m_IsDirty = true;
		m_HasSpawned = false;
	}



	protected override void OnDisabled()
	{
		RemoveVehicles();
		m_Graph = null;
		m_HasSpawned = false;
	}



	protected override void OnUpdate()
	{
		if (SandboxUtility.IsInPlayMode)
		{
			if (!m_HasSpawned)
			{
				RebuildGraph();
				SpawnVehicles();
				m_HasSpawned = true;
			}

			return;
		}

		// Editor: keep the gizmo layout in sync when a layout property changes.
		if (m_IsDirty)
		{
			RebuildGraph();
			m_IsDirty = false;
		}
	}



	[Button("Rebuild Pathfinding Layout"), Feature("Traffic"), Order(10)]
	public void RebuildPathfindingLayout()
	{
		RebuildGraph();

		if (SandboxUtility.IsInPlayMode)
		{
			SpawnVehicles();
			m_HasSpawned = true;
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
	}



	private void SpawnVehicles()
	{
		RemoveVehicles();

		if (m_Graph is null || m_Graph.Lanes.Count == 0 || VehicleCount <= 0)
			return;

		if (!VehiclePrefab.IsValid())
		{
			SandboxUtility.ShowEditorNotification("Traffic: assign a Vehicle Prefab to spawn vehicles");
			return;
		}

		m_VehicleContainer = new GameObject(GameObject, true, VehicleContainerName);
		m_VehicleContainer.Flags |= GameObjectFlags.NotSaved;

		// Spawn on road lanes when available so cars start out on actual roads, not mid-intersection.
		var pool = m_Graph.Lanes.Where(l => l.IsRoadLane && l.Waypoints.Count >= 2).ToList();

		if (pool.Count == 0)
			pool = m_Graph.Lanes.Where(l => l.Waypoints.Count >= 2).ToList();

		if (pool.Count == 0)
			return;

		var rng = new System.Random();

		for (int i = 0; i < VehicleCount; i++)
		{
			TrafficLane lane = pool[rng.Next(pool.Count)];
			int startIndex = rng.Next(0, lane.Waypoints.Count - 1);
			Vector3 spawnPos = lane.Waypoints[startIndex] + Vector3.Up * HoverHeight;

			GameObject clone = VehiclePrefab.Clone(m_VehicleContainer, spawnPos, Rotation.Identity, Vector3.One);

			if (!clone.IsValid())
				continue;

			clone.Name = $"Vehicle_{i}";
			clone.BreakFromPrefab();
			clone.Flags |= GameObjectFlags.NotSaved;

			// The prefab is just the visual/body — the manager attaches the driver and points it at its lane.
			var vehicle = clone.AddComponent<TrafficVehicle>();
			vehicle.DefaultSpeed = DefaultSpeed * TrafficMath.KmhToUnits;
			vehicle.HoverHeight = HoverHeight;
			vehicle.Spacing = VehicleSpacing;
			vehicle.Neighbors = m_Vehicles;
			vehicle.AwareTags = AwarenessTags;
			vehicle.DetectRadius = AwarenessRadius;
			vehicle.Initialize(m_Graph, lane, startIndex, rng.Next());

			m_Vehicles.Add(vehicle);
		}
	}
	
	
	
	private void RemoveVehicles()
	{
		m_Vehicles.Clear();

		var existing = GameObject.Children.Where(c => c.Name == VehicleContainerName).ToList();

		foreach (var child in existing)
			child.Destroy();

		m_VehicleContainer = null;
	}



	protected override void DrawGizmos()
	{
		if (!ShowLayoutGizmos || !Gizmo.IsSelected || m_Graph is null)
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
