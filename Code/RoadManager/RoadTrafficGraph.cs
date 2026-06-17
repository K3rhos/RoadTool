using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// Tunables for how the traffic graph is generated from the road network.
/// </summary>
public struct RoadTrafficSettings
{
	/// <summary>Distance between sampled waypoints along a road centerline (world units).</summary>
	public float WaypointSpacing;

	/// <summary>Two endpoints closer than this are treated as connected (mirrors the snap tolerance).</summary>
	public float LinkThreshold;

	public static RoadTrafficSettings Default => new()
	{
		WaypointSpacing = 150.0f,
		LinkThreshold = 200.0f
	};
}



/// <summary>
/// A directed drivable path of world-space waypoints. Lanes are linked into a graph by endpoint proximity + heading,
/// so a vehicle finishing one lane can roll onto any lane that begins where it ended and faces the same way.
/// </summary>
public sealed class TrafficLane
{
	public List<Vector3> Waypoints = new();

	/// <summary>Owning component (road or intersection). Lanes never link to another lane of the same owner.</summary>
	public object Owner;

	/// <summary>True for road lanes, false for intersection cross-lanes (spawn preference + gizmo color).</summary>
	public bool IsRoadLane;

	/// <summary>Width of this lane — used to size the endpoint-matching tolerance so neighbouring lanes don't cross-link.</summary>
	public float LaneWidth = 100.0f;

	/// <summary>Half the owning road's width — the pull-back distance for a contained dead-end U-turn.</summary>
	public float RoadHalfWidth = 250.0f;

	/// <summary>Speed limit while driving this lane, already converted to s&amp;box units per second.</summary>
	public float SpeedLimit = 500.0f;

	/// <summary>For a dead-end lane, the contained turn-around path onto an opposing lane; null otherwise.</summary>
	public List<Vector3> UTurnArc;

	/// <summary>Lanes that can be entered after finishing this one.</summary>
	public readonly List<TrafficLane> Successors = new();

	/// <summary>For cross-lanes: other cross-lanes whose path crosses this one, mapped to the crossing point.
	/// Lets vehicles resolve right-of-way by who is nearer the actual conflict point.</summary>
	public readonly Dictionary<TrafficLane, Vector3> Conflicts = new();

	public Vector3 StartPos => Waypoints[0];
	public Vector3 EndPos => Waypoints[^1];

	public Vector3 StartDir
	{
		get
		{
			for (int i = 1; i < Waypoints.Count; i++)
			{
				Vector3 d = Waypoints[i] - Waypoints[0];
				if (!d.IsNearZeroLength)
					return d.Normal;
			}

			return Vector3.Forward;
		}
	}

	public Vector3 EndDir
	{
		get
		{
			for (int i = Waypoints.Count - 2; i >= 0; i--)
			{
				Vector3 d = Waypoints[^1] - Waypoints[i];
				if (!d.IsNearZeroLength)
					return d.Normal;
			}

			return Vector3.Forward;
		}
	}
}



/// <summary>
/// The whole drivable network for a scene. Build it with <see cref="Build"/>, then vehicles walk the lanes and pick
/// random successors at each junction.
/// </summary>
public sealed class RoadTrafficGraph
{
	// Successor heading must agree to at least this dot product — blocks flow into an opposing/over-sharp lane.
	private const float DirectionDot = 0.25f;

	public readonly List<TrafficLane> Lanes = new();

	private readonly List<TrafficLane> m_RoadLanes = new();

	public int RoadCount { get; private set; }
	public int IntersectionCount { get; private set; }



	public static RoadTrafficGraph Build(Scene _Scene, RoadTrafficSettings _Settings)
	{
		var graph = new RoadTrafficGraph();

		if (_Scene is null)
			return graph;

		graph.AddRoads(_Scene, _Settings);
		graph.AddIntersections(_Scene, _Settings);
		graph.ComputeSuccessors();
		graph.ApplyDeadEndUTurns();

		return graph;
	}



	// ── Roads → one lane per slot in the road's line-derived layout ───────────────────────────────────────────
	private void AddRoads(Scene _Scene, RoadTrafficSettings _Settings)
	{
		var positions = new List<Vector3>();
		var rights = new List<Vector3>();

		foreach (var road in _Scene.GetAll<RoadComponent>())
		{
			if (!road.IsValid() || !road.Active || road.ExcludeTraffic)
				continue;

			road.GetTrafficCenterline(_Settings.WaypointSpacing, positions, rights);

			if (positions.Count < 2)
				continue;

			var slots = road.GetTrafficLaneLayout();
			float laneWidth = road.RoadWidth / Math.Max(1, slots.Count);
			float halfWidth = road.RoadWidth * 0.5f;
			int last = positions.Count - 1;

			float speedUnits = road.SpeedLimit * TrafficMath.KmhToUnits;

			foreach (var slot in slots)
			{
				var lane = new TrafficLane
				{
					Owner = road,
					IsRoadLane = true,
					LaneWidth = laneWidth,
					RoadHalfWidth = halfWidth,
					SpeedLimit = speedUnits
				};

				if (slot.Forward)
				{
					for (int i = 0; i <= last; i++)
						lane.Waypoints.Add(positions[i] + rights[i] * slot.Offset);
				}
				else
				{
					for (int i = last; i >= 0; i--)
						lane.Waypoints.Add(positions[i] + rights[i] * slot.Offset);
				}

				Lanes.Add(lane);
				m_RoadLanes.Add(lane);
			}

			RoadCount++;
		}
	}



	// ── Intersections → clamped lane-to-lane cross-lanes between connected exits ───────────────────────────────
	// The lane count at each exit is read straight off the road lanes that terminate there, so it tracks whatever
	// the connected road's lines produce. Exits with no connected road carry no traffic.
	private void AddIntersections(Scene _Scene, RoadTrafficSettings _Settings)
	{
		foreach (var intersection in _Scene.GetAll<RoadIntersectionComponent>())
		{
			if (!intersection.IsValid() || !intersection.Active || intersection.ExcludeTraffic)
				continue;

			var exits = intersection.GetTrafficExits();

			if (exits.Count < 1)
			{
				continue;
			}

			Vector3 center = intersection.WorldPosition;
			float intersectionSpeed = intersection.SpeedLimit * TrafficMath.KmhToUnits;
			int n = exits.Count;

			var incoming = new List<TrafficLane>[n];
			var outgoing = new List<TrafficLane>[n];

			for (int e = 0; e < n; e++)
			{
				Vector3 pos = exits[e].Transform.Position;
				Vector3 outward = exits[e].Transform.Forward.Normal;
				Vector3 right = Rotation.LookAt(outward, Vector3.Up).Right;
				float threshold = exits[e].RoadWidth * 0.6f + _Settings.LinkThreshold;

				incoming[e] = CollectExitLanes(pos, -outward, right, threshold, _IsIncoming: true);
				outgoing[e] = CollectExitLanes(pos, outward, right, threshold, _IsIncoming: false);
			}

			var crossLanes = new List<TrafficLane>();

			for (int i = 0; i < n; i++)
			{
				if (incoming[i].Count == 0)
					continue;

				for (int j = 0; j < n; j++)
				{
					if (i == j || outgoing[j].Count == 0)
						continue;

					for (int k = 0; k < incoming[i].Count; k++)
					{
						TrafficLane src = incoming[i][k];
						TrafficLane dst = outgoing[j][Math.Min(k, outgoing[j].Count - 1)];

						TrafficLane cross = BuildCrossLane(src, dst, center, intersection, _Settings.WaypointSpacing);
						cross.SpeedLimit = intersectionSpeed;
						Lanes.Add(cross);
						crossLanes.Add(cross);
					}
				}
			}

			ComputeCrossLaneConflicts(crossLanes);

			IntersectionCount++;
		}
	}



	// For every pair of this intersection's cross-lanes that genuinely CROSS (not ones that share an entry and
	// diverge, nor ones that share an exit and merge), record where their paths intersect. Vehicles then resolve
	// right-of-way by who is nearer that point — the one ahead clears, the one behind yields and can stop.
	private static void ComputeCrossLaneConflicts(List<TrafficLane> _CrossLanes)
	{
		const float sameSqr = 3600.0f; // ~60u: shared entry/exit endpoints

		for (int a = 0; a < _CrossLanes.Count; a++)
		{
			for (int b = a + 1; b < _CrossLanes.Count; b++)
			{
				TrafficLane A = _CrossLanes[a];
				TrafficLane B = _CrossLanes[b];

				if (A.StartPos.DistanceSquared(B.StartPos) < sameSqr)
					continue; // same entry → diverge, never cross

				if (A.EndPos.DistanceSquared(B.EndPos) < sameSqr)
					continue; // same exit → merge, handled separately

				if (TryFindPathCrossing(A.Waypoints, B.Waypoints, out Vector3 point))
				{
					A.Conflicts[B] = point;
					B.Conflicts[A] = point;
				}
			}
		}
	}



	private static bool TryFindPathCrossing(List<Vector3> _A, List<Vector3> _B, out Vector3 _Point)
	{
		for (int i = 0; i < _A.Count - 1; i++)
		{
			for (int j = 0; j < _B.Count - 1; j++)
			{
				if (SegmentsCross(_A[i], _A[i + 1], _B[j], _B[j + 1], out _Point))
					return true;
			}
		}

		_Point = default;
		return false;
	}



	// 2D (XY) segment-segment intersection. Returns the crossing point if the segments properly cross.
	private static bool SegmentsCross(Vector3 _P1, Vector3 _P2, Vector3 _P3, Vector3 _P4, out Vector3 _Point)
	{
		_Point = default;

		float d = (_P2.x - _P1.x) * (_P4.y - _P3.y) - (_P2.y - _P1.y) * (_P4.x - _P3.x);

		if (MathF.Abs(d) < 1e-5f)
			return false; // parallel

		float t = ((_P3.x - _P1.x) * (_P4.y - _P3.y) - (_P3.y - _P1.y) * (_P4.x - _P3.x)) / d;
		float u = ((_P3.x - _P1.x) * (_P2.y - _P1.y) - (_P3.y - _P1.y) * (_P2.x - _P1.x)) / d;

		if (t < 0.0f || t > 1.0f || u < 0.0f || u > 1.0f)
			return false;

		_Point = _P1 + (_P2 - _P1) * t;
		return true;
	}



	// Road lanes whose relevant endpoint sits near an exit and heads the right way (in for incoming, out for outgoing),
	// ordered left→right across the carriageway so cross-lane index mapping is consistent.
	private List<TrafficLane> CollectExitLanes(Vector3 _ExitPos, Vector3 _Heading, Vector3 _Right, float _Threshold, bool _IsIncoming)
	{
		float thresholdSqr = _Threshold * _Threshold;
		var found = new List<TrafficLane>();

		foreach (var lane in m_RoadLanes)
		{
			Vector3 point = _IsIncoming ? lane.EndPos : lane.StartPos;
			Vector3 dir = _IsIncoming ? lane.EndDir : lane.StartDir;

			if (point.DistanceSquared(_ExitPos) > thresholdSqr)
				continue;

			if (Vector3.Dot(dir, _Heading) < 0.3f)
				continue;

			found.Add(lane);
		}

		found.Sort((a, b) =>
		{
			float la = Vector3.Dot((_IsIncoming ? a.EndPos : a.StartPos) - _ExitPos, _Right);
			float lb = Vector3.Dot((_IsIncoming ? b.EndPos : b.StartPos) - _ExitPos, _Right);
			return la.CompareTo(lb);
		});

		return found;
	}



	private static TrafficLane BuildCrossLane(TrafficLane _Src, TrafficLane _Dst, Vector3 _Center, object _Owner, float _Spacing)
	{
		Vector3 entry = _Src.EndPos;
		Vector3 entryDir = _Src.EndDir;     // heading into the intersection
		Vector3 exit = _Dst.StartPos;
		Vector3 exitDir = _Dst.StartDir;    // heading back out

		float gap = Vector3.DistanceBetween(entry, exit);
		float handle = MathF.Min(gap * 0.4f, Vector3.DistanceBetween(entry, _Center));
		Vector3 c1 = entry + entryDir * handle;
		Vector3 c2 = exit - exitDir * handle;

		int n = Math.Clamp((int)MathF.Ceiling(gap / MathF.Max(1.0f, _Spacing)) + 1, 2, 16);

		var lane = new TrafficLane
		{
			Owner = _Owner,
			IsRoadLane = false,
			LaneWidth = MathF.Min(_Src.LaneWidth, _Dst.LaneWidth)
		};

		for (int k = 0; k <= n; k++)
			lane.Waypoints.Add(TrafficMath.SampleCubic(entry, c1, c2, exit, (float)k / n));

		return lane;
	}



	// ── Successors: a lane M follows L when M starts where L ends and faces the same way ──────────────────────
	private void ComputeSuccessors()
	{
		foreach (var from in Lanes)
		{
			Vector3 end = from.EndPos;
			Vector3 endDir = from.EndDir;

			foreach (var to in Lanes)
			{
				if (ReferenceEquals(from, to) || ReferenceEquals(from.Owner, to.Owner))
					continue;

				float tolerance = MathF.Min(from.LaneWidth, to.LaneWidth) * 0.5f;

				if (end.DistanceSquared(to.StartPos) > tolerance * tolerance)
					continue;

				if (Vector3.Dot(endDir, to.StartDir) < DirectionDot)
					continue;

				from.Successors.Add(to);
			}
		}
	}



	// ── Contained dead-end U-turn onto an opposing lane of the same road ──────────────────────────────────────
	private void ApplyDeadEndUTurns()
	{
		// A return lane may be the target of several approach lanes; only pull its head back once.
		var trimmedHeads = new HashSet<TrafficLane>();

		foreach (var lane in m_RoadLanes)
		{
			if (lane.Successors.Count > 0)
				continue;

			TrafficLane target = FindOpposingLane(lane);

			if (target is null)
				continue; // one-way dead end — vehicle reverses in place as a last resort

			BuildContainedUTurn(lane, target, trimmedHeads);
			lane.Successors.Add(target);
		}
	}



	// The same-road lane heading back the other way whose start is nearest this lane's dead end.
	private TrafficLane FindOpposingLane(TrafficLane _Lane)
	{
		Vector3 end = _Lane.EndPos;
		Vector3 endDir = _Lane.EndDir;
		float bestSqr = _Lane.RoadHalfWidth * 2.5f;
		bestSqr *= bestSqr;
		TrafficLane best = null;

		foreach (var other in m_RoadLanes)
		{
			if (ReferenceEquals(other, _Lane) || !ReferenceEquals(other.Owner, _Lane.Owner))
				continue;

			if (Vector3.Dot(endDir, other.StartDir) > -0.3f)
				continue; // must genuinely oppose

			float distSqr = end.DistanceSquared(other.StartPos);

			if (distSqr < bestSqr)
			{
				bestSqr = distSqr;
				best = other;
			}
		}

		return best;
	}



	private static void BuildContainedUTurn(TrafficLane _Approach, TrafficLane _Exit, HashSet<TrafficLane> _TrimmedHeads)
	{
		Vector3 outward = _Approach.EndDir;

		if (outward.IsNearZeroLength)
			return;

		outward = outward.Normal;

		// Pull both lanes back by ~half the road width so the turn-around is a smooth, symmetric loop on the road.
		float margin = _Approach.RoadHalfWidth;

		if (Vector3.DistanceBetween(_Approach.StartPos, _Approach.EndPos) < margin * 2.0f)
			margin *= 0.5f;

		Vector3 pf = _Approach.EndPos - outward * margin;
		TrimTail(_Approach, margin, pf);

		// Pull the return lane's head back by the same amount so both ends of the loop start level — otherwise the
		// arc hooks sharply at the un-trimmed end. Only do it once even if several lanes turn onto this one.
		if (_TrimmedHeads.Add(_Exit))
		{
			Vector3 pbNew = _Exit.StartPos + _Exit.StartDir.Normal * margin;
			TrimHead(_Exit, margin, pbNew);
		}

		Vector3 pb = _Exit.StartPos;
		Vector3 exitInward = _Exit.StartDir.Normal;

		// Symmetric hairpin: both handles point outward toward the road end so the apex rounds off near the tip.
		float handle = margin * 1.2f;
		Vector3 c1 = pf + outward * handle;
		Vector3 c2 = pb - exitInward * handle;

		var arc = new List<Vector3>(15);

		for (int i = 0; i <= 14; i++)
			arc.Add(TrafficMath.SampleCubic(pf, c1, c2, pb, i / 14.0f));

		_Approach.UTurnArc = arc;
	}



	private static void TrimTail(TrafficLane _Lane, float _Margin, Vector3 _NewEnd)
	{
		var wps = _Lane.Waypoints;
		Vector3 endPos = wps[^1];

		while (wps.Count > 2 && Vector3.DistanceBetween(wps[^1], endPos) < _Margin)
			wps.RemoveAt(wps.Count - 1);

		wps[^1] = _NewEnd;
	}



	private static void TrimHead(TrafficLane _Lane, float _Margin, Vector3 _NewStart)
	{
		var wps = _Lane.Waypoints;
		Vector3 startPos = wps[0];

		while (wps.Count > 2 && Vector3.DistanceBetween(wps[0], startPos) < _Margin)
			wps.RemoveAt(0);

		wps[0] = _NewStart;
	}
}



/// <summary>Small math helpers shared by the graph and the vehicle.</summary>
public static class TrafficMath
{
	/// <summary>
	/// km/h → s&amp;box units (inches) per second. 1 km/h = (1000 m / 0.0254 m-per-inch) / 3600 s ≈ 10.9361 inches/s.
	/// </summary>
	public const float KmhToUnits = 10.9361f;




	public static Vector3 SampleCubic(Vector3 _P0, Vector3 _P1, Vector3 _P2, Vector3 _P3, float _T)
	{
		float u = 1.0f - _T;
		float uu = u * u;
		float tt = _T * _T;

		return uu * u * _P0
			+ 3.0f * uu * _T * _P1
			+ 3.0f * u * tt * _P2
			+ tt * _T * _P3;
	}



	/// <summary>
	/// Builds a smooth connector from A (heading <paramref name="_DirA"/>) to B (heading <paramref name="_DirB"/>) —
	/// bridges the small gap between one lane's end and the next lane's start.
	/// </summary>
	public static List<Vector3> BuildConnector(Vector3 _A, Vector3 _DirA, Vector3 _B, Vector3 _DirB)
	{
		var points = new List<Vector3>();
		float gap = Vector3.DistanceBetween(_A, _B);

		if (gap < 1.0f)
		{
			points.Add(_A);
			points.Add(_B);
			return points;
		}

		float handle = gap * 0.5f;
		Vector3 c1 = _A + _DirA.Normal * handle;
		Vector3 c2 = _B - _DirB.Normal * handle;

		int n = Math.Clamp((int)MathF.Ceiling(gap / 60.0f), 2, 16);

		for (int k = 0; k <= n; k++)
			points.Add(SampleCubic(_A, c1, c2, _B, (float)k / n));

		return points;
	}
}
