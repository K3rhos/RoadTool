using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// A single AI vehicle. It follows its current lane's waypoints, and when it reaches the end it picks a random
/// successor lane (or U-turns at a dead end) and smoothly connects onto it. Spawned and configured by <see cref="RoadManager"/>.
/// </summary>
public sealed class TrafficVehicle : Component
{
	public RoadTrafficGraph Graph { get; set; }
	public float HoverHeight { get; set; } = 40.0f;
	public float TurnRate { get; set; } = 6.0f;

	/// <summary>Speed (s&amp;box units/s) used on connectors and dead-end U-turns — anything that isn't a road or intersection lane.</summary>
	public float DefaultSpeed { get; set; } = 200.0f;

	/// <summary>Desired following gap; the vehicle brakes for anything ahead within this distance.</summary>
	public float Spacing { get; set { field = value.Clamp(10.0f, 1000.0f); } } = 180.0f;

	/// <summary>Shared list of all live vehicles (owned by the manager) used for collision awareness.</summary>
	public List<TrafficVehicle> Neighbors { get; set; }

	/// <summary>Entity tags (players, NPCs, …) the vehicle brakes for when sensed directly ahead.</summary>
	public TagSet AwareTags { get; set; }

	/// <summary>Radius of the forward sweep used to sense tagged entities.</summary>
	public float DetectRadius { get; set; } = 50.0f;

	/// <summary>True while driving a road lane; false while crossing an intersection or connector.</summary>
	public bool IsOnRoadLane => m_Lane?.IsRoadLane ?? true;

	/// <summary>Current normalized travel direction — used by other vehicles to tell same-way from crossing traffic.</summary>
	public Vector3 Heading => m_Heading;

	/// <summary>The intersection this vehicle is currently crossing (or about to enter), else null.</summary>
	public RoadIntersectionComponent CurrentIntersection => (!IsOnRoadLane && m_Lane?.Owner is RoadIntersectionComponent i) ? i : null;

	/// <summary>True while crossing an intersection on a lane that turns left (and so must yield to oncoming traffic).</summary>
	public bool IsLeftTurningNow => CurrentIntersection is not null && IsLeftTurnLane(m_Lane);

	/// <summary>True if this vehicle is turning left through the junction, or has committed to do so on entry.</summary>
	public bool IsLeftTurnPlannedOrActive => IsLeftTurningNow || (m_PlannedNext is not null && IsLeftTurnLane(m_PlannedNext));

	/// <summary>Direction this vehicle entered its current lane heading — used to tell oncoming from crossing traffic.</summary>
	public Vector3 LaneEntryDir => m_Lane?.StartDir ?? m_Heading;

	/// <summary>False when effectively stopped (red light, queued). Lets others ignore us as a non-threat.</summary>
	public bool IsMoving => m_SpeedScale > 0.05f;

	/// <summary>The lane this vehicle is currently driving (road, intersection cross-lane, or return lane mid-U-turn).</summary>
	public TrafficLane ActiveLane => m_Lane;

	/// <summary>True while physically turning around on a dead-end U-turn arc (i.e. passing through the merge).</summary>
	public bool IsTurningAround => m_UsingUTurnArc && m_Target <= m_ConnectorEnd;

	/// <summary>The return lane this vehicle is about to U-turn onto while still approaching the dead end; else null.</summary>
	public TrafficLane PendingUTurnReturn =>
		(m_Lane?.UTurnArc is { Count: >= 2 } && m_Target > m_ConnectorEnd && m_Lane.Successors.Count > 0)
			? m_Lane.Successors[0]
			: null;

	/// <summary>Distance from the pivot to the back of the car body, along its forward axis (0 if it has no model).</summary>
	public float RearExtent => m_RearExtent;

	/// <summary>The intersection cross-lane we're driving (if in the box) or committed to (if approaching); else null.</summary>
	public TrafficLane CurrentOrPlannedCrossLane =>
		CurrentIntersection is not null ? m_Lane
		: (m_PlannedNext is { IsRoadLane: false } ? m_PlannedNext : null);

	private TrafficLane m_Lane;
	private List<Vector3> m_Path = new();
	private int m_Target;
	private int m_ConnectorEnd;
	private Vector3 m_Pos;
	private Vector3 m_Heading = Vector3.Forward;
	private float m_SpeedScale = 1.0f;
	private TrafficLane m_PlannedNext;
	private bool m_UsingUTurnArc;
	private int m_Id;
	private float m_FrontExtent;
	private float m_RearExtent;
	private System.Random m_Rng;



	/// <summary>Places the vehicle on a lane at a given waypoint index and seeds its successor picking.</summary>
	public void Initialize(RoadTrafficGraph _Graph, TrafficLane _Lane, int _StartIndex, int _Seed)
	{
		Graph = _Graph;
		m_Rng = new System.Random(_Seed);
		m_Id = _Seed;

		SetPath(new List<Vector3>(_Lane.Waypoints), _Lane, 0);

		int startIndex = Math.Clamp(_StartIndex, 0, m_Path.Count - 1);
		m_Pos = m_Path[startIndex];
		m_Target = Math.Min(startIndex + 1, m_Path.Count - 1);
		m_Heading = (m_Path[m_Target] - m_Pos).Normal;

		if (m_Heading.IsNearZeroLength)
			m_Heading = m_Lane.StartDir;

		WorldPosition = m_Pos + Vector3.Up * HoverHeight;
		WorldRotation = Rotation.LookAt(m_Heading, Vector3.Up);

		ComputeForwardExtents();
	}



	// Measures how far the car body reaches ahead of and behind its pivot, along its own forward axis, from the
	// model bounds. Lets the following gap be bumper-to-bumper instead of pivot-to-pivot. Stays 0 with no model,
	// which falls back to the original pivot-based behaviour.
	private void ComputeForwardExtents()
	{
		m_FrontExtent = 0.0f;
		m_RearExtent = 0.0f;

		float minX = float.MaxValue;
		float maxX = float.MinValue;

		foreach (var renderer in GetComponentsInChildren<ModelRenderer>(true))
		{
			if (renderer.Model is null)
				continue;

			BBox bounds = renderer.Model.Bounds;

			// Project every corner of the model bounds into our local frame (forward = +X) so the result is
			// correct even if the renderer is an offset/rotated child of the vehicle root.
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = new Vector3(
					(i & 1) == 0 ? bounds.Mins.x : bounds.Maxs.x,
					(i & 2) == 0 ? bounds.Mins.y : bounds.Maxs.y,
					(i & 4) == 0 ? bounds.Mins.z : bounds.Maxs.z);

				Vector3 local = WorldTransform.PointToLocal(renderer.WorldTransform.PointToWorld(corner));
				minX = MathF.Min(minX, local.x);
				maxX = MathF.Max(maxX, local.x);
			}
		}

		if (maxX < minX)
			return; // no model renderer found — keep pivot-based behaviour

		m_FrontExtent = MathF.Max(0.0f, maxX);
		m_RearExtent = MathF.Max(0.0f, -minX);
	}



	protected override void OnUpdate()
	{
		if (Graph is null || m_Path.Count < 2)
			return;

		// Speed comes from the current segment: the road/intersection lane's limit, or DefaultSpeed while crossing
		// a connector or dead-end U-turn. ComputeSpeedScale then brakes for vehicles / tagged entities ahead.
		float segmentSpeed = m_Target <= m_ConnectorEnd ? DefaultSpeed : m_Lane.SpeedLimit;
		m_SpeedScale = ComputeSpeedScale();
		float remaining = segmentSpeed * m_SpeedScale * Time.Delta;
		int guard = 0;

		// Advance along the polyline, consuming as many short segments as this frame's travel allows.
		while (remaining > 0.0f && guard++ < 256)
		{
			Vector3 toTarget = m_Path[m_Target] - m_Pos;
			float distance = toTarget.Length;

			if (distance <= remaining)
			{
				m_Pos = m_Path[m_Target];
				remaining -= distance;

				if (m_Target >= m_Path.Count - 1)
				{
					AdvanceToNextLane();

					if (m_Path.Count < 2)
						return;
				}
				else
				{
					m_Target++;
				}
			}
			else
			{
				Vector3 dir = toTarget / distance;
				m_Pos += dir * remaining;
				m_Heading = dir;
				remaining = 0.0f;
			}
		}

		// Keep facing the next waypoint even while crossing a long segment.
		Vector3 desired = m_Path[m_Target] - m_Pos;

		if (!desired.IsNearZeroLength)
			m_Heading = desired.Normal;

		WorldPosition = m_Pos + Vector3.Up * HoverHeight;

		if (!m_Heading.IsNearZeroLength)
			WorldRotation = Rotation.Slerp(WorldRotation, Rotation.LookAt(m_Heading, Vector3.Up), Time.Delta * TurnRate);
	}



	// Returns 1 (full speed) when the road ahead is clear, ramping down to 0 (stopped) as the nearest obstacle —
	// another vehicle in the same lane, tagged entity, or a red traffic light — closes within range.
	private float ComputeSpeedScale()
	{
		// Two brakes only: the gap to whatever's ahead on our own path, and the intersection-entry decision.
		// There is deliberately NO in-junction yielding — once a vehicle is engaged it always drives through,
		// so it can never stop dead in the middle. All conflicts are resolved at the entry line instead.
		float gapScale = ComputeGapScale(MathF.Min(NearestVehicleAhead(), NearestEntityAhead()));
		float scale = MathF.Min(gapScale, IntersectionApproachScale());
		return MathF.Min(scale, UTurnApproachScale());
	}



	private float ComputeGapScale(float _Nearest)
	{
		if (_Nearest == float.MaxValue)
			return 1.0f;

		float stopGap = Spacing * 0.45f;

		if (_Nearest <= stopGap)
			return 0.0f;

		return ((_Nearest - stopGap) / (Spacing - stopGap)).Clamp(0.0f, 1.0f);
	}



	// Decides whether to slow/stop before the upcoming intersection. Conflicts are resolved HERE (at the
	// entry), so that once a vehicle commits it can cross without stopping. A vehicle yields if:
	//   • the light is red for its approach (lit intersections), or
	//   • a closer vehicle is approaching from its right (priority-to-right, unlit intersections), or
	//   • a crossing vehicle is still inside the intersection (clear it before entering — also covers the
	//     tail end of the previous green phase at a lit intersection).
	private float IntersectionApproachScale()
	{
		// Skip while on a connector (just left an intersection) so we don't brake for the next one too early.
		if (!m_Lane.IsRoadLane || m_Lane.Successors.Count == 0 || m_Target <= m_ConnectorEnd)
			return 1.0f;

		RoadIntersectionComponent upcoming = null;

		foreach (var successor in m_Lane.Successors)
		{
			if (successor.Owner is RoadIntersectionComponent intr)
			{
				upcoming = intr;
				break;
			}
		}

		if (upcoming is null)
			return 1.0f;

		float approachDist = Spacing * 3.5f;
		float distToEnd = DistanceToLaneEnd();

		if (distToEnd > approachDist)
			return 1.0f;

		// Commit to a route through the junction now and keep it stable, so we know our turn before entering.
		if (m_PlannedNext is null || !m_Lane.Successors.Contains(m_PlannedNext))
			m_PlannedNext = PickSuccessor(m_Lane);

		bool shouldYield = upcoming.HasTrafficLights
			? !upcoming.IsApproachGreen(m_Heading)
			: MustYieldToRight(upcoming, approachDist);

		// Near the line, also wait for the intersection to clear of crossing (perpendicular) traffic.
		if (!shouldYield && distToEnd < approachDist * 0.5f)
			shouldYield = IntersectionHasCrossingTraffic(upcoming);

		// Unprotected left turn: hold at the line until oncoming has a gap, then commit and complete the turn
		// in one motion. Resolving it here — not mid-junction — is what guarantees an engaged car never stops.
		if (!shouldYield && IsLeftTurnLane(m_PlannedNext))
			shouldYield = HasMovingOncoming(upcoming);

		if (!shouldYield)
			return 1.0f;

		float stopDist = Spacing * 0.25f;

		if (distToEnd <= stopDist)
			return 0.0f;

		return ((distToEnd - stopDist) / (approachDist - stopDist)).Clamp(0.0f, 1.0f);
	}



	// Dead-end turn-around merge gate. Where two same-direction lanes both U-turn onto a single return lane, they
	// converge to one point — so they must take turns. Hold at the dead end until the merge is clear and no other
	// turner has priority; then commit and complete the arc in one motion (never stall halfway through it).
	private float UTurnApproachScale()
	{
		// Only while still approaching the dead end on a lane that turns around (not once committed to the arc).
		if (m_Lane is null || m_Lane.UTurnArc is not { Count: >= 2 } || m_Lane.Successors.Count == 0)
			return 1.0f;

		if (m_Target <= m_ConnectorEnd || Neighbors is null)
			return 1.0f;

		TrafficLane returnLane = m_Lane.Successors[0];
		Vector3 mergePoint = m_Lane.UTurnArc[m_Lane.UTurnArc.Count - 1];

		float approachDist = Spacing * 2.0f;
		float distToEnd = DistanceToLaneEnd();

		if (distToEnd > approachDist)
			return 1.0f;

		if (!UTurnMergeBusy(returnLane, mergePoint))
			return 1.0f;

		float stopDist = Spacing * 0.3f;

		if (distToEnd <= stopDist)
			return 0.0f;

		return ((distToEnd - stopDist) / (approachDist - stopDist)).Clamp(0.0f, 1.0f);
	}



	private bool UTurnMergeBusy(TrafficLane _ReturnLane, Vector3 _MergePoint)
	{
		float mergeRadius = MathF.Max(m_Lane.LaneWidth, m_Lane.RoadHalfWidth * 0.6f);
		float myDist = WorldPosition.Distance(_MergePoint);

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid())
				continue;

			float otherDist = other.WorldPosition.Distance(_MergePoint);

			// Someone already committed to turning around onto this return lane occupies the merge for the whole
			// arc — wait, no matter where on the arc they are, so we don't both converge on the same point.
			if (other.IsTurningAround && other.ActiveLane == _ReturnLane)
				return true;

			// …or has just merged and is still sitting near the start of the return lane.
			if (other.ActiveLane == _ReturnLane && otherDist < mergeRadius)
				return true;

			// Another car waiting to make the SAME U-turn that's nearer the merge goes first (ties break by id).
			// Strict ordering ⇒ two simultaneous arrivals never both yield, so they can't deadlock.
			if (other.PendingUTurnReturn == _ReturnLane)
			{
				if (otherDist < myDist - 1.0f || (MathF.Abs(otherDist - myDist) <= 1.0f && other.m_Id > m_Id))
					return true;
			}
		}

		return false;
	}



	// Priority-to-right: yield to a perpendicular vehicle approaching from our right that is closer to the
	// intersection than we are. The "closer wins" tie-break is a strict ordering, so two vehicles can never
	// each yield to the other — circular deadlocks are impossible.
	private bool MustYieldToRight(RoadIntersectionComponent _Intersection, float _Range)
	{
		if (Neighbors is null)
			return false;

		Vector3 rightDir = Rotation.LookAt(m_Heading, Vector3.Up).Right;
		float myDist = Vector3.DistanceBetween(WorldPosition, _Intersection.WorldPosition);

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid() || !other.IsOnRoadLane)
				continue;

			// Only crossing traffic counts — not parallel cars in a neighbouring lane.
			if (MathF.Abs(Vector3.Dot(other.Heading, m_Heading)) > 0.5f)
				continue;

			float otherDist = Vector3.DistanceBetween(other.WorldPosition, _Intersection.WorldPosition);

			if (otherDist > _Range || otherDist >= myDist)
				continue;

			Vector3 toOther = (other.WorldPosition - WorldPosition).Normal;

			if (Vector3.Dot(toOther, rightDir) > 0.5f)
				return true;
		}

		return false;
	}



	// True if genuine cross traffic is currently inside the intersection.
	//
	// The conflict is judged by the AXIS a car ENTERED on, never its instantaneous heading. A car turning
	// through the junction sweeps its heading across 90°, so heading-based tests wrongly flag same-road traffic
	// (e.g. a car that came off our own road and is now turning off it) as crossing. A car that entered on our
	// own axis shares the road with us and never crosses our path; only perpendicular-entry traffic does.
	// This also subsumes the left-turner case: an oncoming left-turner entered on our axis, so it's not flagged.
	private bool IntersectionHasCrossingTraffic(RoadIntersectionComponent _Intersection)
	{
		if (Neighbors is null)
			return false;

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid())
				continue;

			if (other.CurrentIntersection != _Intersection)
				continue;

			// Same entry axis (parallel or anti-parallel) → shares our road, never crosses. Perpendicular → cross.
			if (MathF.Abs(Vector3.Dot(other.LaneEntryDir, m_Heading)) < 0.5f)
				return true;
		}

		return false;
	}



	// True if oncoming traffic (same axis, opposite direction) is moving toward or through the junction within
	// turning range. A left-turner holds at the line while this is true, then completes its turn in one motion.
	// Cars stopped at a red light or queued (not moving) are no threat, and two opposing left-turns don't cross.
	private bool HasMovingOncoming(RoadIntersectionComponent _Intersection)
	{
		if (Neighbors is null)
			return false;

		Vector3 center = _Intersection.WorldPosition;
		float rangeSqr = (Spacing * 2.0f) * (Spacing * 2.0f);

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid())
				continue;

			// Oncoming = heading roughly opposite to our approach direction.
			if (Vector3.Dot(other.Heading, m_Heading) > -0.5f)
				continue;

			// Two opposing left turns don't cross — ignore oncoming that is itself turning left.
			if (other.IsLeftTurnPlannedOrActive)
				continue;

			// Stopped at a red light or queued — not a threat, we can turn in front of it.
			if (!other.IsMoving)
				continue;

			bool inside = other.CurrentIntersection == _Intersection;
			bool approaching = Vector3.Dot(center - other.WorldPosition, other.Heading) > 0.0f
				&& other.WorldPosition.DistanceSquared(center) < rangeSqr;

			if (inside || approaching)
				return true;
		}

		return false;
	}



	// A cross-lane is a left turn when its heading rotates left (counter-clockwise) from entry to exit.
	// s&box is right-handed Z-up, so a left turn gives a positive up-component on the heading cross product.
	private static bool IsLeftTurnLane(TrafficLane _Lane)
	{
		if (_Lane is null || _Lane.IsRoadLane)
			return false;

		Vector3 a = _Lane.StartDir;
		Vector3 b = _Lane.EndDir;

		if (Vector3.Dot(a, b) > 0.7f)
			return false; // going straight, not a turn

		return Vector3.Cross(a, b).Dot(Vector3.Up) > 0.0f;
	}



	// Remaining path length from the current position through all remaining waypoints.
	private float DistanceToLaneEnd()
	{
		if (m_Path.Count < 2 || m_Target >= m_Path.Count)
			return 0.0f;

		float dist = Vector3.DistanceBetween(m_Pos, m_Path[m_Target]);

		for (int i = m_Target; i < m_Path.Count - 1; i++)
			dist += Vector3.DistanceBetween(m_Path[i], m_Path[i + 1]);

		return dist;
	}



	// Nearest vehicle ahead that we must brake for. A car is "ahead" when it sits in a tight corridor in front
	// of us (so cars in other lanes are ignored). For a car going the same way it's normal following. For a
	// crossing/opposing car it's a conflict, and only the lower-priority side yields (ShouldYieldTo): priority
	// goes to whoever is deeper into the junction, who is therefore always *ahead* — so the yielder (behind) is
	// the only one that brakes, the priority car drives clear, and the two can neither freeze nor phase through.
	private float NearestVehicleAhead()
	{
		if (Neighbors is null)
			return float.MaxValue;

		float laneWidth = m_Lane?.LaneWidth ?? 100.0f;
		float lateralLimit = MathF.Min(Spacing * 0.45f, laneWidth * 0.6f);
		float nearest = float.MaxValue;

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid())
				continue;

			Vector3 toOther = other.WorldPosition - WorldPosition;
			float forward = Vector3.Dot(toOther, m_Heading);

			if (forward <= 0.0f)
				continue;

			float lateral = (toOther - m_Heading * forward).Length;

			if (lateral > lateralLimit)
				continue;

			// Real bumper-to-bumper gap: pivot distance minus our front overhang and the other car's rear
			// overhang, so a long car is kept clear instead of being driven through. Extents are 0 with no model.
			float gap = MathF.Max(0.0f, forward - m_FrontExtent - other.RearExtent);

			if (gap > Spacing)
				continue;

			// Same lane → we're following the leader on our exact path: always brake. Different lane in our
			// corridor → a conflict (crossing OR merging onto a shared exit): brake only when we must yield.
			// The priority car never brakes for the lower-priority one, so a conflict can't freeze or clip.
			bool sameLane = ReferenceEquals(other.ActiveLane, m_Lane);

			if (!sameLane && !ShouldYieldTo(other))
				continue;

			if (gap < nearest)
				nearest = gap;
		}

		return nearest;
	}



	// Deterministic right-of-way between two vehicles on conflicting paths: exactly one yields, so they never
	// deadlock. Both rules pick the car that reaches the conflict first — which is always the one *ahead* — so
	// the yielder is always behind it and can actually stop (never a freeze, never a clip).
	private bool ShouldYieldTo(TrafficVehicle _Other)
	{
		TrafficLane mine = CurrentOrPlannedCrossLane;
		TrafficLane theirs = _Other.CurrentOrPlannedCrossLane;

		if (mine is not null && theirs is not null && !ReferenceEquals(mine, theirs))
		{
			// Merging onto a shared exit (e.g. a right turn and a left turn both feeding the same lane): whoever
			// is nearer that exit takes it first. The shorter movement reaches it first, as expected.
			Vector3 exit = mine.EndPos;

			if (exit.DistanceSquared(theirs.EndPos) < 3600.0f) // ~60u apart → same exit lane
			{
				float myToExit = WorldPosition.DistanceSquared(exit);
				float otherToExit = _Other.WorldPosition.DistanceSquared(exit);

				if (MathF.Abs(myToExit - otherToExit) > 1.0f)
					return otherToExit < myToExit;
			}
			// Crossing paths (e.g. a left turn cutting across oncoming straight traffic): whoever is nearer the
			// actual point where the two paths cross goes first; the other, being behind it, yields and can stop.
			else if (mine.Conflicts.TryGetValue(theirs, out Vector3 crossing))
			{
				float myToCross = WorldPosition.DistanceSquared(crossing);
				float otherToCross = _Other.WorldPosition.DistanceSquared(crossing);

				if (MathF.Abs(myToCross - otherToCross) > 1.0f)
					return otherToCross < myToCross;
			}
		}

		// Fallback: whoever is deeper into the junction (nearer its centre) clears first.
		RoadIntersectionComponent junction = CurrentIntersection ?? _Other.CurrentIntersection;

		if (junction is not null)
		{
			float myDist = WorldPosition.DistanceSquared(junction.WorldPosition);
			float otherDist = _Other.WorldPosition.DistanceSquared(junction.WorldPosition);

			if (MathF.Abs(myDist - otherDist) > 1.0f)
				return otherDist < myDist;
		}

		return _Other.m_Id > m_Id;
	}



	// Nearest tagged entity ahead, via a forward sphere sweep filtered by the manager's tag set. The tag filter
	// means the flat road/sidewalk meshes are never hit — only players/NPCs/whatever carries one of the tags.
	private float NearestEntityAhead()
	{
		if (AwareTags == null || AwareTags.IsEmpty)
			return float.MaxValue;

		Vector3 from = WorldPosition;
		Vector3 to = from + m_Heading * Spacing;

		SceneTraceResult trace = Scene.Trace
			.Sphere(DetectRadius, from, to)
			.WithAnyTags(AwareTags)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();

		return trace.Hit ? trace.Distance : float.MaxValue;
	}



	private void AdvanceToNextLane()
	{
		// Use the route we committed to on approach (so we take the turn we yielded for); otherwise pick now.
		TrafficLane next = (m_PlannedNext is not null && m_Lane.Successors.Contains(m_PlannedNext))
			? m_PlannedNext
			: PickSuccessor(m_Lane);

		m_PlannedNext = null;

		if (next is null || next.Waypoints.Count < 2)
		{
			// No way forward at all — reverse the current lane so the vehicle never gets stuck.
			next = MakeReversed(m_Lane);
		}

		List<Vector3> connector;
		bool usingUTurnArc = m_Lane.UTurnArc is { Count: >= 2 };

		if (usingUTurnArc)
		{
			// Dead-end turn-around: follow the contained on-road arc instead of looping past the road end.
			connector = m_Lane.UTurnArc;
		}
		else
		{
			// Bridge the gap between where we are now and the start of the next lane (smooths the offset
			// mismatch where a road lane meets an intersection cross-lane).
			Vector3 nextDir = (next.Waypoints[1] - next.Waypoints[0]).Normal;
			connector = TrafficMath.BuildConnector(m_Pos, m_Heading, next.Waypoints[0], nextDir);
		}

		var path = new List<Vector3>(connector.Count + next.Waypoints.Count);
		path.AddRange(connector);

		// connector ends at next.Waypoints[0], so skip the duplicate.
		for (int i = 1; i < next.Waypoints.Count; i++)
			path.Add(next.Waypoints[i]);

		// Segments up to the connector's last point run at DefaultSpeed; the rest run at next.SpeedLimit.
		SetPath(path, next, connector.Count - 1);

		// Remember whether we're mid turn-around so others can tell we're occupying the merge.
		m_UsingUTurnArc = usingUTurnArc;
	}



	private TrafficLane PickSuccessor(TrafficLane _Lane)
	{
		var successors = _Lane.Successors;

		if (successors.Count == 0)
			return null;

		return successors[m_Rng.Next(successors.Count)];
	}



	// Emergency only: a one-way lane that dead-ends with no opposing lane to turn onto. Drive back along itself.
	private static TrafficLane MakeReversed(TrafficLane _Lane)
	{
		var reversed = new TrafficLane
		{
			Owner = _Lane.Owner,
			IsRoadLane = _Lane.IsRoadLane,
			LaneWidth = _Lane.LaneWidth,
			RoadHalfWidth = _Lane.RoadHalfWidth,
			SpeedLimit = _Lane.SpeedLimit
		};

		for (int i = _Lane.Waypoints.Count - 1; i >= 0; i--)
			reversed.Waypoints.Add(_Lane.Waypoints[i]);

		reversed.Successors.Add(_Lane);
		return reversed;
	}



	private void SetPath(List<Vector3> _Path, TrafficLane _Lane, int _ConnectorEnd)
	{
		m_Path = _Path;
		m_Lane = _Lane;
		m_ConnectorEnd = _ConnectorEnd;
		m_Target = 1;
	}
}
