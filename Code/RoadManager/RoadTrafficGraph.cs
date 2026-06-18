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

			if (intersection.IsRoundabout)
			{
				AddRoundabout(intersection, exits, _Settings);
				IntersectionCount++;
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



	// ── Roundabout: route every movement around a one-way ring so paths never cross ──────────────────────────────
	// Each exit becomes a node on the ring. Incoming road lanes merge onto the ring (entry links); the ring carries
	// traffic one way (counter-clockwise for right-hand driving) between nodes; at each node a car may peel off onto
	// that exit's outgoing road lanes (exit links). The only conflicts left are merges, which the right-of-way rules
	// already resolve — there is no crossing through the centre at all.
	private void AddRoundabout(RoadIntersectionComponent _It, List<RoadIntersectionComponent.TrafficExit> _Exits, RoadTrafficSettings _Settings)
	{
		const float twoPi = MathF.PI * 2.0f;
		bool ccw = true; // right-hand traffic circulates counter-clockwise (flip for left-hand)
		float sign = ccw ? 1.0f : -1.0f;

		Vector3 center = _It.WorldPosition;
		Rotation rot = _It.WorldRotation;
		float speed = _It.SpeedLimit * TrafficMath.KmhToUnits;
		float ringRadius = _It.RoundaboutRingRadius;
		int n = _Exits.Count;

		var legAngle = new float[n];
		var incoming = new List<TrafficLane>[n];
		var outgoing = new List<TrafficLane>[n];
		float maxHalfRoad = 1.0f;

		for (int e = 0; e < n; e++)
		{
			Vector3 pos = _Exits[e].Transform.Position;
			Vector3 outward = _Exits[e].Transform.Forward.Normal;
			Vector3 right = Rotation.LookAt(outward, Vector3.Up).Right;
			float threshold = _Exits[e].RoadWidth * 0.6f + _Settings.LinkThreshold;

			incoming[e] = CollectExitLanes(pos, -outward, right, threshold, _IsIncoming: true);
			outgoing[e] = CollectExitLanes(pos, outward, right, threshold, _IsIncoming: false);

			// Angle of this leg around the centre, in the intersection's own (possibly tilted) plane.
			Vector3 localOut = rot.Inverse * (pos - center);
			legAngle[e] = MathF.Atan2(localOut.y, localOut.x);
			maxHalfRoad = MathF.Max(maxHalfRoad, _Exits[e].RoadWidth * 0.5f);
		}

		// Offset (radians) between a leg's centre and its entry/exit points on the ring — i.e. half the arm width.
		// The natural value matches the lane offset (half a road width projected onto the ring); the intersection's
		// Arm Spread scales it. It's hard-clamped so the arms never reach a neighbouring leg and interleave.
		float minGap = twoPi;
		if (n > 1)
		{
			var sorted = new float[n];
			Array.Copy(legAngle, sorted, n);
			Array.Sort(sorted);

			for (int i = 0; i < n; i++)
			{
				float g = sorted[(i + 1) % n] - sorted[i];
				while (g <= 0.0f) g += twoPi;
				minGap = MathF.Min(minGap, g);
			}
		}

		float baseDelta = maxHalfRoad / MathF.Max(1.0f, ringRadius);
		float delta = MathF.Min(MathF.Min(baseDelta * _It.RoundaboutArmSpread, 1.2f), minGap * 0.45f);

		// Build a ring "event" upstream (an exit divergence) and downstream (an entry merge) of each leg. For CCW
		// flow the exit sits at θ-δ and the entry at θ+δ, each on the same side as its road lanes — so the entry
		// and exit links stay on opposite sides of the leg and never cross (no more bow-tie).
		int m = 2 * n;
		var evtAngle = new float[m];
		var evtIsEntry = new bool[m];
		var evtRoads = new List<TrafficLane>[m];

		for (int e = 0; e < n; e++)
		{
			evtAngle[2 * e] = NormalizeAngle(legAngle[e] - sign * delta, twoPi);
			evtIsEntry[2 * e] = false;
			evtRoads[2 * e] = outgoing[e];

			evtAngle[2 * e + 1] = NormalizeAngle(legAngle[e] + sign * delta, twoPi);
			evtIsEntry[2 * e + 1] = true;
			evtRoads[2 * e + 1] = incoming[e];
		}

		// Order events counter-clockwise so consecutive ones are adjacent on the ring.
		var order = new int[m];
		for (int i = 0; i < m; i++) order[i] = i;
		Array.Sort(order, (a, b) => evtAngle[a].CompareTo(evtAngle[b]));

		var arcs = new TrafficLane[m];           // arcs[q] leaves the q-th event (CCW order)
		var exitLinksAt = new List<TrafficLane>[m];

		for (int q = 0; q < m; q++)
		{
			int ev = order[q];
			int evNext = order[(q + 1) % m];

			float a0 = evtAngle[ev];
			float a1 = evtAngle[evNext];
			if (ccw) { while (a1 <= a0) a1 += twoPi; } else { while (a1 >= a0) a1 -= twoPi; }

			arcs[q] = BuildRingArc(_It, center, rot, ringRadius, a0, a1, speed, _Settings.WaypointSpacing);
			Lanes.Add(arcs[q]);

			Vector3 point = RingPoint(center, rot, ringRadius, evtAngle[ev]);
			Vector3 tangent = RingTangent(rot, evtAngle[ev], ccw);
			exitLinksAt[q] = new List<TrafficLane>();

			if (evtIsEntry[ev])
			{
				// Each incoming road lane merges onto the ring here, then follows the arc leaving this event.
				foreach (var road in evtRoads[ev])
				{
					var entry = BuildRoundaboutLink(road.EndPos, road.EndDir, point, tangent, speed, road.LaneWidth, _It);
					entry.Successors.Add(arcs[q]);
					Lanes.Add(entry);
				}
			}
			else
			{
				// The ring peels off here to each outgoing road lane.
				foreach (var road in evtRoads[ev])
				{
					var exit = BuildRoundaboutLink(point, tangent, road.StartPos, road.StartDir, speed, road.LaneWidth, _It);
					Lanes.Add(exit);
					exitLinksAt[q].Add(exit);
				}
			}
		}

		// Ring continuation: each arc flows into the next; arriving at an exit event, a car may peel off instead.
		for (int q = 0; q < m; q++)
		{
			int qNext = (q + 1) % m;
			arcs[q].Successors.Add(arcs[qNext]);

			if (!evtIsEntry[order[qNext]])
			{
				foreach (var exit in exitLinksAt[qNext])
					arcs[q].Successors.Add(exit);
			}
		}
	}



	private static Vector3 RingPoint(Vector3 _Center, Rotation _Rot, float _Radius, float _Angle)
	{
		return _Center + _Rot * (new Vector3(MathF.Cos(_Angle), MathF.Sin(_Angle), 0.0f) * _Radius);
	}



	private static float NormalizeAngle(float _Angle, float _TwoPi)
	{
		float a = _Angle % _TwoPi;
		return a < 0.0f ? a + _TwoPi : a;
	}



	private static TrafficLane BuildRingArc(RoadIntersectionComponent _Owner, Vector3 _Center, Rotation _Rot, float _Radius, float _A0, float _A1, float _Speed, float _Spacing)
	{
		float arcLength = MathF.Abs(_A1 - _A0) * _Radius;
		int segs = Math.Clamp((int)MathF.Ceiling(arcLength / MathF.Max(1.0f, _Spacing)), 2, 48);

		var lane = new TrafficLane { Owner = _Owner, IsRoadLane = false, SpeedLimit = _Speed, LaneWidth = 100.0f };

		for (int k = 0; k <= segs; k++)
		{
			float a = _A0 + (_A1 - _A0) * ((float)k / segs);
			lane.Waypoints.Add(_Center + _Rot * (new Vector3(MathF.Cos(a), MathF.Sin(a), 0.0f) * _Radius));
		}

		return lane;
	}



	private static TrafficLane BuildRoundaboutLink(Vector3 _A, Vector3 _DirA, Vector3 _B, Vector3 _DirB, float _Speed, float _LaneWidth, RoadIntersectionComponent _Owner)
	{
		var lane = new TrafficLane { Owner = _Owner, IsRoadLane = false, SpeedLimit = _Speed, LaneWidth = _LaneWidth };
		lane.Waypoints.AddRange(TrafficMath.BuildConnector(_A, _DirA, _B, _DirB));
		return lane;
	}



	// Unit tangent to the ring at the given angle, pointing in the circulation direction (CCW for right-hand).
	private static Vector3 RingTangent(Rotation _Rot, float _Angle, bool _Ccw)
	{
		Vector3 local = new Vector3(-MathF.Sin(_Angle), MathF.Cos(_Angle), 0.0f);

		if (!_Ccw)
			local = -local;

		return (_Rot * local).Normal;
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



	private const float BulbWidthFactor = 0.75f;   // bulb radius as a fraction of the road half-width (↑ = wider loop, nearer the kerb)
	private const int BulbBodySegments = 20;       // samples around the bulb's main arc
	private const int BulbFlareSegments = 6;       // samples on each easing flare into / out of the bulb



	// Builds the dead-end turn-around as a "light-bulb" (teardrop) loop instead of a tight semicircle. A real car's
	// steering is limited, so a hairpin whose radius is half the lane spacing gets cut — the car climbs the kerb. Here
	// the path eases out through a flare onto a much larger BULB CIRCLE (radius scaled to the road width), loops over
	// the top and eases back in: the minimum radius is the bulb radius everywhere, so a steering-limited car holds it.
	//
	// Geometry: the bulb circle (centre `bulb`, radius R) sits forward of the lane ends. Each flare is itself a radius-R
	// turn; two radius-R circles that touch are 2R apart, which fixes the bulb's forward offset F. The flare↔bulb
	// tangent points are the midpoints of the centre-to-centre lines. We sample the bulb's major arc, then ease the
	// straight lanes onto it with short tangent-matched Béziers.
	private static void BuildContainedUTurn(TrafficLane _Approach, TrafficLane _Exit, HashSet<TrafficLane> _TrimmedHeads)
	{
		Vector3 endDir = _Approach.EndDir;          // 3-D travel direction (carries the road's slope)
		Vector3 outward = endDir.WithZ(0.0f);       // its horizontal part — the footprint is laid out on the ground plane

		if (outward.IsNearZeroLength)
			return;

		outward = outward.Normal;

		// Lateral spacing between the dead-ending lane and the opposing lane it turns onto (perpendicular to travel).
		Vector3 between = (_Exit.StartPos - _Approach.EndPos).WithZ(0.0f);
		Vector3 sideRaw = between - outward * Vector3.Dot(between, outward);
		float spacing = sideRaw.Length;

		// Bulb radius: as wide as the road sensibly allows, but never tighter than a plain semicircle of the spacing.
		float radius = MathF.Max(spacing * 0.5f, _Approach.RoadHalfWidth * BulbWidthFactor);

		// Forward offset of the bulb centre so each flare meets the bulb tangentially: |bulb − flareCentre| = 2R solves
		// to F = √(3R² − R·spacing − spacing²/4).
		float forward = MathF.Sqrt(MathF.Max(0.0f, 3.0f * radius * radius - radius * spacing - spacing * spacing * 0.25f));

		float clearance = _Approach.RoadHalfWidth * 0.3f;
		float margin = forward + radius + clearance;

		// If the lanes are too short to contain the bulb (or run straight into each other), fall back to a plain hairpin.
		float room = MathF.Min(LaneLength(_Approach), LaneLength(_Exit)) - _Approach.RoadHalfWidth * 0.5f;

		if (spacing < 1.0f || margin > room)
		{
			BuildSimpleHairpin(_Approach, _Exit, _TrimmedHeads);
			return;
		}

		Vector3 side = sideRaw / spacing;

		// Pull both lane ends back by `margin` of arc length, following each lane's polyline so the trimmed ends stay
		// ON the road surface (even over a crest or dip) — a straight-line pull-back would float off a curved road.
		TrimTail(_Approach, margin);

		if (_TrimmedHeads.Add(_Exit))
			TrimHead(_Exit, margin);

		Vector3 pf = _Approach.EndPos;   // actual trimmed dead-end of the approach lane (on the surface)
		Vector3 pb = _Exit.StartPos;     // actual trimmed start of the return lane (on the surface)
		Vector3 mid = (pf + pb) * 0.5f;

		Vector3 bulb = mid + outward * forward;                       // bulb (body) circle centre
		Vector3 leftCentre = mid - side * (spacing * 0.5f + radius);  // left flare's turn centre
		Vector3 rightCentre = mid + side * (spacing * 0.5f + radius); // right flare's turn centre
		Vector3 leftTangent = (leftCentre + bulb) * 0.5f;            // where the left flare meets the bulb circle
		Vector3 rightTangent = (rightCentre + bulb) * 0.5f;

		// Main body: the bulb circle's major arc, looping over the top toward the dead end.
		var body = new List<Vector3>(BulbBodySegments + 1);
		AppendArcToward(body, bulb, leftTangent, rightTangent, outward, BulbBodySegments);

		Vector3 bodyIn = (body[1] - body[0]).Normal;          // heading as the car enters the bulb at the left tangent
		Vector3 bodyOut = (body[^1] - body[^2]).Normal;       // heading as it leaves at the right tangent
		Vector3 exitDir = _Exit.StartDir.WithZ(0.0f).Normal;  // heading down the return lane as it leaves the loop

		float flareIn = Vector3.DistanceBetween(pf, leftTangent) * 0.4f;
		float flareOut = Vector3.DistanceBetween(rightTangent, pb) * 0.4f;

		// Easing flare in → bulb body → easing flare out. The flares own the shared tangent points, so the body's
		// first/last samples are dropped to avoid duplicates.
		var arc = new List<Vector3>(BulbBodySegments + 2 * BulbFlareSegments);

		AppendBezier(arc, pf, pf + outward * flareIn, leftTangent - bodyIn * flareIn, leftTangent, BulbFlareSegments);

		for (int i = 1; i < body.Count - 1; i++)
			arc.Add(body[i]);

		AppendBezier(arc, rightTangent, rightTangent + bodyOut * flareOut, pb - exitDir * flareOut, pb, BulbFlareSegments);

		// The footprint above is laid out flat; drape it onto the ACTUAL road surface so it follows the real vertical
		// profile — crest, sag, ramp — instead of a straight grade that flies off where the road curves. We sample the
		// owning road's centerline and snap each point to the surface height directly beneath it.
		List<Vector3> centerline = null;

		if (_Approach.Owner is RoadComponent road && road.IsValid())
		{
			centerline = new List<Vector3>();
			road.GetTrafficCenterline(64.0f, centerline, new List<Vector3>());
		}

		if (centerline is { Count: >= 2 })
		{
			for (int i = 0; i < arc.Count; i++)
				arc[i] = arc[i].WithZ(RoadHeightAt(centerline, arc[i]));
		}
		else
		{
			// Fallback (no road surface to sample): a straight grade from the lane-end slope.
			float horizontal = MathF.Max(0.001f, endDir.WithZ(0.0f).Length);
			float slopeForward = endDir.z / horizontal;
			float slopeLateral = spacing > 1.0f ? (pb.z - pf.z) / spacing : 0.0f;

			for (int i = 0; i < arc.Count; i++)
			{
				Vector3 d = (arc[i] - mid).WithZ(0.0f);
				arc[i] = arc[i].WithZ(mid.z + slopeForward * Vector3.Dot(d, outward) + slopeLateral * Vector3.Dot(d, side));
			}
		}

		// Pin the endpoints exactly to the (already on-surface) lane ends so the loop meets the lanes with no step.
		arc[0] = pf;
		arc[^1] = pb;

		_Approach.UTurnArc = arc;
	}



	// Fallback turn-around: the original tight semicircle (a single cubic Bézier hairpin). Used when the road is too
	// short to contain a bulb, or the opposing lanes are effectively collinear.
	private static void BuildSimpleHairpin(TrafficLane _Approach, TrafficLane _Exit, HashSet<TrafficLane> _TrimmedHeads)
	{
		Vector3 outward = _Approach.EndDir;

		if (outward.IsNearZeroLength)
			return;

		outward = outward.Normal;

		// Pull both lanes back by ~half the road width so the turn-around is a smooth, symmetric loop on the road.
		float margin = _Approach.RoadHalfWidth * 2;

		if (Vector3.DistanceBetween(_Approach.StartPos, _Approach.EndPos) < margin * 2.0f)
			margin *= 0.5f;

		TrimTail(_Approach, margin);

		// Pull the return lane's head back by the same amount so both ends of the loop start level — otherwise the
		// arc hooks sharply at the un-trimmed end. Only do it once even if several lanes turn onto this one.
		if (_TrimmedHeads.Add(_Exit))
			TrimHead(_Exit, margin);

		Vector3 pf = _Approach.EndPos;   // on the surface, `margin` of arc length back from the dead end
		Vector3 pb = _Exit.StartPos;
		Vector3 exitInward = _Exit.StartDir.Normal;

		// Symmetric hairpin: both handles point outward toward the road end so the apex rounds off near the tip.
		float handle = margin * 0.5f;
		Vector3 c1 = pf + outward * handle;
		Vector3 c2 = pb - exitInward * handle;

		var arc = new List<Vector3>(15);

		for (int i = 0; i <= 14; i++)
			arc.Add(TrafficMath.SampleCubic(pf, c1, c2, pb, i / 14.0f));

		_Approach.UTurnArc = arc;
	}



	// Appends a circular arc (centre → from → to) sampled into _Into, choosing whichever way round keeps the arc's
	// midpoint on the _Toward side — so the bulb always loops away from the lanes rather than doubling back across them.
	private static void AppendArcToward(List<Vector3> _Into, Vector3 _Center, Vector3 _From, Vector3 _To, Vector3 _Toward, int _Segments)
	{
		float radius = (_From - _Center).WithZ(0.0f).Length;
		float two = MathF.PI * 2.0f;
		float a0 = MathF.Atan2(_From.y - _Center.y, _From.x - _Center.x);
		float a1 = MathF.Atan2(_To.y - _Center.y, _To.x - _Center.x);

		float s1 = a1 - a0;
		while (s1 <= -MathF.PI) s1 += two;
		while (s1 > MathF.PI) s1 -= two;

		float s2 = s1 > 0.0f ? s1 - two : s1 + two; // the other way round

		Vector3 m1 = new Vector3(MathF.Cos(a0 + s1 * 0.5f), MathF.Sin(a0 + s1 * 0.5f), 0.0f);
		Vector3 m2 = new Vector3(MathF.Cos(a0 + s2 * 0.5f), MathF.Sin(a0 + s2 * 0.5f), 0.0f);
		float sweep = Vector3.Dot(m1, _Toward) >= Vector3.Dot(m2, _Toward) ? s1 : s2;

		float z = _Center.z;

		for (int i = 0; i <= _Segments; i++)
		{
			float a = a0 + sweep * (i / (float)_Segments);
			_Into.Add(new Vector3(_Center.x + radius * MathF.Cos(a), _Center.y + radius * MathF.Sin(a), z));
		}
	}



	private static void AppendBezier(List<Vector3> _Into, Vector3 _P0, Vector3 _P1, Vector3 _P2, Vector3 _P3, int _Segments)
	{
		for (int k = 0; k <= _Segments; k++)
			_Into.Add(TrafficMath.SampleCubic(_P0, _P1, _P2, _P3, k / (float)_Segments));
	}



	private static float LaneLength(TrafficLane _Lane)
	{
		float len = 0.0f;
		var w = _Lane.Waypoints;

		for (int i = 0; i < w.Count - 1; i++)
			len += Vector3.DistanceBetween(w[i], w[i + 1]);

		return len;
	}



	// Surface height of the owning road directly under a query point: the nearest point on the road centerline (in
	// plan view) with its height interpolated along that segment. Lets the U-turn loop sit on the real road surface
	// even where the road climbs, dips or crests, instead of on a flat plane through the lane ends.
	private static float RoadHeightAt(List<Vector3> _Centerline, Vector3 _Query)
	{
		float bestSqr = float.MaxValue;
		float bestZ = _Query.z;

		for (int i = 0; i < _Centerline.Count - 1; i++)
		{
			Vector3 a = _Centerline[i];
			Vector3 b = _Centerline[i + 1];
			Vector3 ab = (b - a).WithZ(0.0f);
			float lenSqr = ab.LengthSquared;
			float t = lenSqr > 0.0001f ? Math.Clamp(Vector3.Dot((_Query - a).WithZ(0.0f), ab) / lenSqr, 0.0f, 1.0f) : 0.0f;

			Vector3 proj = a + ab * t;
			float dSqr = (_Query - proj).WithZ(0.0f).LengthSquared;

			if (dSqr < bestSqr)
			{
				bestSqr = dSqr;
				bestZ = float.Lerp(a.z, b.z, t);
			}
		}

		return bestZ;
	}



	// Trims _Margin of ARC LENGTH off the tail, following the lane polyline so the new end is interpolated between two
	// real surface waypoints — it therefore stays on the road, even over a crest or dip. (A straight-line pull-back
	// would float off a curved road.)
	private static void TrimTail(TrafficLane _Lane, float _Margin)
	{
		var wps = _Lane.Waypoints;
		float remaining = _Margin;

		while (wps.Count > 2)
		{
			float seg = Vector3.DistanceBetween(wps[^1], wps[^2]);

			if (seg >= remaining)
			{
				wps[^1] = wps[^1] + (wps[^2] - wps[^1]) * (remaining / MathF.Max(seg, 0.0001f));
				return;
			}

			remaining -= seg;
			wps.RemoveAt(wps.Count - 1);
		}
	}



	private static void TrimHead(TrafficLane _Lane, float _Margin)
	{
		var wps = _Lane.Waypoints;
		float remaining = _Margin;

		while (wps.Count > 2)
		{
			float seg = Vector3.DistanceBetween(wps[0], wps[1]);

			if (seg >= remaining)
			{
				wps[0] = wps[0] + (wps[1] - wps[0]) * (remaining / MathF.Max(seg, 0.0001f));
				return;
			}

			remaining -= seg;
			wps.RemoveAt(0);
		}
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
