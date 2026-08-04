using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// Pedestrian streaming: keep a population of walkers alive on the pavement around the players, and let the ones
/// that drift out of range go.
///
/// The deliberate limit here is that this half knows how to PLACE people and nothing about what they then do. It
/// spawns a prefab on a pavement waypoint, hands it to <see cref="RoadManager.OnPedestrianSpawned"/>, and from
/// that moment only ever asks "how far is it from a player". Walking, routing, animation, reacting to gunfire —
/// all of it belongs to whatever the game bolts on at that hook, the same way traffic works without the tool
/// knowing what a siren is.
///
/// Which is why there's no pedestrian equivalent of <see cref="TrafficVehicle"/> in here. A car on rails is
/// road-layout knowledge; a person deciding where to go is not.
/// </summary>
public sealed partial class RoadManager
{
	private const float PedestrianStreamInterval = 0.35f; // slower than traffic: people move less far per pass, so there's less to correct

	[Property(Title = "Prefabs"), Feature("Traffic"), Category("Pedestrians")] private List<GameObject> PedestrianPrefabs { get; set; } = [];

	[Property(Title = "Density"), Feature("Traffic"), Category("Pedestrians"), Range(0.0f, 1.0f)] public float PedestrianDensity { get; set; } = 0.0f;
	[Property(Title = "Spawn Gap"), Feature("Traffic"), Category("Pedestrians"), Range(50.0f, 2000.0f)] private float PedestrianSpawnGap { get; set; } = 400.0f;
	[Property(Title = "Spawn Min Range"), Feature("Traffic"), Category("Pedestrians"), Range(0.0f, 20000.0f)] private float PedestrianSpawnMinDistance { get; set; } = 1500.0f;
	[Property(Title = "Spawn Range"), Feature("Traffic"), Category("Pedestrians"), Range(500.0f, 20000.0f)] private float PedestrianSpawnDistance { get; set; } = 3500.0f;
	[Property(Title = "Despawn Range"), Feature("Traffic"), Category("Pedestrians"), Range(500.0f, 20000.0f)] private float PedestrianDespawnDistance { get; set; } = 5000.0f;

	/// <summary>
	/// Raised on the host for each pedestrian as it joins the population — whether the tool spawned it or the
	/// game handed it over through <see cref="AdoptPedestrian"/>.
	///
	/// The seam. Everything a game wants its people to actually DO gets attached here — a brain, a gait, a
	/// wardrobe, a reason to be somewhere. The tool has placed it on a pavement facing along the kerb and will
	/// not touch it again except to despawn it.
	///
	/// It covers adoptions too so that "what a pedestrian is" is answered in ONE place. Someone rejoining the
	/// crowd after a job should be indistinguishable from someone who was always in it, and they won't be if the
	/// game only gets to configure one of the two routes in.
	///
	/// AFTER network spawn rather than before, so anything parented on in the hook replicates normally.
	/// </summary>
	public static Action<GameObject> OnPedestrianSpawned { get; set; }

	/// <summary>
	/// Optional override for which prefab to spawn. Set this from your game when picking a person is a decision
	/// the game should make — weighting by district, time of day, a wardrobe system — and the tool asks this
	/// instead of drawing uniformly from <c>Prefabs</c>. Return null to skip this spawn.
	///
	/// The same seam as <see cref="ResolveVehicleDriver"/>: default behaviour in the tool, real behaviour in the
	/// game, and no reference from one to the other.
	/// </summary>
	public static Func<GameObject> ResolvePedestrianPrefab { get; set; }

	private readonly List<GameObject> m_Pedestrians = [];
	private readonly List<(TrafficLane Lane, int Index)> m_PedestrianSlots = []; // candidate spawn points along the pavements, rebuilt with the graph
	private float m_PedestrianStreamCooldown;



	/// <summary>Live pedestrians the tool is streaming. Read-only — the manager owns their lifetime.</summary>
	public IReadOnlyList<GameObject> Pedestrians => m_Pedestrians;



	/// <summary>
	/// Hands an EXISTING character to the streaming system, so it lives and dies like one the tool spawned.
	///
	/// The counterpart to <see cref="OnPedestrianSpawned"/>: that one is the tool giving a person to the game,
	/// this is the game giving one back. A game that pulls someone out of the crowd for a job — a passenger, a
	/// courier, a witness — needs somewhere to put them when the job is over, and "delete them" is a poor answer
	/// when they're stood in front of the player. Rejoining the crowd means they wander off and get streamed out
	/// later, at a distance, like everybody else.
	///
	/// Clears the <c>keep_alive</c> tag, which is the same claim read the other way: the tool skips tagged
	/// objects precisely so a game can hold one back, so handing it over has to withdraw that.
	///
	/// Host only, and the object must be host-owned by the time it gets here — the streaming pass despawns
	/// these, and destroying something another machine owns is not the host's to do.
	/// </summary>
	public void AdoptPedestrian(GameObject _Pedestrian)
	{
		if (!Networking.IsHost || !_Pedestrian.IsValid() || m_Pedestrians.Contains(_Pedestrian))
			return;

		_Pedestrian.Tags.Remove("keep_alive");

		m_Pedestrians.Add(_Pedestrian);

		OnPedestrianSpawned?.Invoke(_Pedestrian);
	}



	private void StreamPedestrians()
	{
		m_PedestrianStreamCooldown -= Time.Delta;

		if (m_PedestrianStreamCooldown > 0.0f)
			return;

		m_PedestrianStreamCooldown = PedestrianStreamInterval;

		var players = new List<Vector3>();

		foreach (GameObject player in GetPlayers())
		{
			if (player.IsValid())
				players.Add(player.WorldPosition);
		}

		DespawnStrayPedestrians(players);

		if (players.Count > 0 && m_PedestrianSlots.Count > 0)
			TopUpPedestrians(players);
	}



	// Per-pedestrian despawn, judged by each one's OWN distance to the nearest player — so someone who walked
	// away disappears while the person still on screen beside you does not.
	private void DespawnStrayPedestrians(List<Vector3> _Players)
	{
		float despawnSq = PedestrianDespawnDistance * PedestrianDespawnDistance;

		for (int i = m_Pedestrians.Count - 1; i >= 0; i--)
		{
			GameObject pedestrian = m_Pedestrians[i];

			if (!pedestrian.IsValid())
			{
				m_Pedestrians.RemoveAt(i);
				continue;
			}

			// A pedestrian the game has taken over — recruited as a follower, pulled into a mission, sat in a
			// car — is no longer ours to delete. Tagging it is how a game says "this one's mine now" without
			// the tool needing to know why.
			if (pedestrian.Tags.Has("keep_alive"))
			{
				m_Pedestrians.RemoveAt(i);
				continue;
			}

			if (NearestDistanceSq(pedestrian.WorldPosition, _Players) <= despawnSq)
				continue;

			var entityFade = pedestrian.GetComponent<EntityFade>();

			if (entityFade.IsValid())
			{
				entityFade.FadeOutAndDestroyBroadcasted();
			}
			else
			{
				pedestrian.Destroy();
			}

			m_Pedestrians.RemoveAt(i);
		}
	}



	// Same shape as the traffic top-up: capacity is the pavement near a player, target is that times density,
	// and we spawn into the ring — near enough to matter, far enough that nobody watches a person appear.
	private void TopUpPedestrians(List<Vector3> _Players)
	{
		float despawnSq = PedestrianDespawnDistance * PedestrianDespawnDistance;
		float minSq = PedestrianSpawnMinDistance * PedestrianSpawnMinDistance;
		float maxSq = PedestrianSpawnDistance * PedestrianSpawnDistance;
		float clearanceSq = PedestrianSpawnGap * PedestrianSpawnGap;

		int capacity = 0;

		foreach (var slot in m_PedestrianSlots)
		{
			if (NearestDistanceSq(slot.Lane.Waypoints[slot.Index], _Players) <= despawnSq)
				capacity++;
		}

		int live = 0;

		foreach (GameObject pedestrian in m_Pedestrians)
		{
			if (pedestrian.IsValid() && NearestDistanceSq(pedestrian.WorldPosition, _Players) <= despawnSq)
				live++;
		}

		int deficit = Math.Min((int)(capacity * PedestrianDensity) - live, MaxSpawnsPerTick);

		if (deficit <= 0)
			return;

		// Always shuffled, never run-ordered. Cars queue nose-to-tail so the tool offers clustering; people
		// walking a street in lockstep looks like a parade, so the slots are simply scattered.
		int count = m_PedestrianSlots.Count;
		int start = Game.Random.Next(count);

		for (int n = 0; n < count && deficit > 0; n++)
		{
			var slot = m_PedestrianSlots[(start + n) % count];
			Vector3 position = slot.Lane.Waypoints[slot.Index];
			float nearestSq = NearestDistanceSq(position, _Players);

			if (nearestSq < minSq || nearestSq > maxSq)
				continue;

			if (IsPedestrianAreaOccupied(position, clearanceSq))
				continue;

			if (SpawnPedestrianAt(slot.Lane, slot.Index))
				deficit--;
		}
	}



	// Candidate spawn points, spaced along every pavement. Crossings are excluded: their only two waypoints are
	// the kerbs either side of a road mouth, and a person appearing there is a person appearing in the road.
	private void BuildPedestrianSlots()
	{
		m_PedestrianSlots.Clear();

		if (m_Graph is null)
			return;

		int step = Math.Max(1, (int)MathF.Ceiling(PedestrianSpawnGap / MathF.Max(1.0f, WaypointSpacing)));

		foreach (TrafficLane lane in m_Graph.SidewalkLanes.Where(x => !x.IsCrossing && x.Waypoints.Count >= 2))
		{
			for (int index = 0; index < lane.Waypoints.Count; index += step)
				m_PedestrianSlots.Add((lane, index));
		}
	}



	// Spawns one networked pedestrian at a slot and hands it straight to the game. Host only.
	private bool SpawnPedestrianAt(TrafficLane _Lane, int _Index)
	{
		GameObject prefab = ResolvePedestrianPrefab is not null ? ResolvePedestrianPrefab() : PickRandomPedestrianPrefab();

		if (!prefab.IsValid())
			return false;

		// Faced along the pavement, so whatever takes over inherits a sensible heading instead of everyone
		// starting out pointing north.
		Vector3 position = _Lane.Waypoints[_Index];
		Vector3 forward = PavementDirection(_Lane, _Index);

		GameObject clone = prefab.Clone(position, Rotation.LookAt(forward, Vector3.Up), Vector3.One);

		if (!clone.IsValid())
			return false;

		clone.NetworkSpawn(Connection.Host);
		clone.Network.SetOrphanedMode(NetworkOrphaned.Host);
		clone.Network.SetOwnerTransfer(OwnerTransfer.Request);

		m_Pedestrians.Add(clone);

		OnPedestrianSpawned?.Invoke(clone);

		return true;
	}



	private GameObject PickRandomPedestrianPrefab()
	{
		if (PedestrianPrefabs is null || PedestrianPrefabs.Count == 0)
			return null;

		return Game.Random.FromList(PedestrianPrefabs);
	}



	/// <summary>Which way the pavement runs at a waypoint, so a spawn can face along it rather than across it.</summary>
	private static Vector3 PavementDirection(TrafficLane _Lane, int _Index)
	{
		int from = Math.Min(_Index, _Lane.Waypoints.Count - 2);
		Vector3 direction = (_Lane.Waypoints[from + 1] - _Lane.Waypoints[from]).WithZ(0.0f);

		return direction.IsNearZeroLength ? Vector3.Forward : direction.Normal;
	}



	private bool IsPedestrianAreaOccupied(Vector3 _Point, float _RadiusSq)
	{
		foreach (GameObject pedestrian in m_Pedestrians)
		{
			if (pedestrian.IsValid() && pedestrian.WorldPosition.DistanceSquared(_Point) < _RadiusSq)
				return true;
		}

		return false;
	}



	private void RemovePedestrians()
	{
		foreach (GameObject pedestrian in m_Pedestrians.ToArray())
		{
			if (pedestrian.IsValid())
				pedestrian.Destroy();
		}

		m_Pedestrians.Clear();
	}
}
