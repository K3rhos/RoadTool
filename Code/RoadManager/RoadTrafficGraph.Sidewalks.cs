using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// The pedestrian half of the graph: pavement down every road, round every intersection corner, and across
/// the mouth of each junction arm.
///
/// Separate from the traffic lanes on purpose. Everything reading <see cref="RoadTrafficGraph.Lanes"/> is
/// looking for somewhere to DRIVE, and handing it pavements would put cars on them. Same
/// <see cref="TrafficLane"/> type though, so pedestrians get the same waypoint/successor machinery vehicles
/// already use — including <see cref="TrafficLane.Successors"/>, which is populated here in BOTH directions.
/// A pavement is not one-way.
/// </summary>
public sealed partial class RoadTrafficGraph
{
	// ── Roads → one walkable lane down each pavement ──────────────────────────────────────────────────────────
	private void AddSidewalks(Scene _Scene, RoadTrafficSettings _Settings)
	{
		var left = new List<Vector3>();
		var right = new List<Vector3>();

		foreach (var road in _Scene.GetAll<RoadComponent>())
		{
			if (!road.IsValid() || !road.Active || !road.HasSidewalk)
				continue;

			// ExcludeTraffic is deliberately NOT checked: a pedestrianised street has no cars on it and the
			// most pavement.
			road.GetSidewalkCenterlines(_Settings.WaypointSpacing, left, right);

			if (left.Count < 2)
				continue;

			AddSidewalkLane(road, left, SidewalkKind.Pavement);
			AddSidewalkLane(road, right, SidewalkKind.Pavement);
		}
	}



	/// <summary>
	/// Intersections → the bits that make the pavement a NETWORK rather than a pile of unconnected segments.
	///
	/// Two things per junction. A CORNER arc between each neighbouring pair of arms, which is how you get from
	/// one street's pavement onto the next one's without stepping into the road. And a CROSSING over the mouth
	/// of each arm, which is how you get to the other side of a street at all.
	///
	/// Both are derived from the traffic exits, so they line up with the roads that actually meet here — no
	/// separate authoring, and a junction someone rebuilds keeps its pavement automatically.
	/// </summary>
	private void AddIntersectionSidewalks(Scene _Scene, RoadTrafficSettings _Settings)
	{
		foreach (var intersection in _Scene.GetAll<RoadIntersectionComponent>())
		{
			if (!intersection.IsValid() || !intersection.Active || intersection.SidewalkGraph == SidewalkGraphMode.None)
				continue;

			var exits = intersection.GetTrafficExits();

			if (exits.Count < 1)
				continue;

			// The junction's own pavement outline, as a closed loop. Following the real shape is what keeps
			// the path on the kerb: for a rectangle, an arc around the centre bows out into the road along
			// each side and clips the corners off entirely.
			List<Vector3> outline = intersection.GetSidewalkOutline(_Settings.WaypointSpacing);

			if (outline.Count < 4)
				continue;

			Vector3 centre = intersection.WorldPosition;

			// Exits sit on the road surface, the pavement sits on top of the kerb. Without this the ends of every
			// span drop a kerb-height below its own middle — and below the road pavement they join onto.
			Vector3 lift = intersection.WorldRotation.Up * intersection.SidewalkHeight;

			// Where each arm interrupts the loop, ordered by ANGLE around the junction centre.
			//
			// Angle rather than nearest-outline-sample, because angle is what actually decides which stretch of
			// kerb lies between two arms. A nearest-point search doesn't: an arm sitting near a corner can have
			// its two kerbs snap either side of that corner, and then the pair the loop treats as "one arm's
			// mouth" is really two different arms. That's how a span ends up classified as pavement — different
			// arms, so not a crossing by definition — while running straight over a road.
			//
			// Two kerbs of the same arm are symmetric about that arm's own bearing, so nothing can sort between
			// them unless two arms physically overlap.
			var kerbs = new List<(Vector3 Point, float Angle, int Exit)>();

			for (int e = 0; e < exits.Count; e++)
			{
				Vector3 position = exits[e].Transform.Position;
				Vector3 outward = exits[e].Transform.Forward.Normal;
				Vector3 right = Rotation.LookAt(outward, Vector3.Up).Right;

				// Same offset a road uses for its own pavement, measured from the same exit transform — so these
				// two points ARE the points where this arm's road pavement begins, not an approximation of them.
				float offset = exits[e].RoadWidth * 0.5f + intersection.SidewalkWidth * 0.5f;

				Vector3 near = position - right * offset + lift;
				Vector3 far = position + right * offset + lift;

				kerbs.Add((near, AngleAround(centre, near), e));
				kerbs.Add((far, AngleAround(centre, far), e));
			}

			kerbs.Sort((a, b) => a.Angle.CompareTo(b.Angle));

			for (int i = 0; i < kerbs.Count; i++)
			{
				var from = kerbs[i];
				var to = kerbs[(i + 1) % kerbs.Count];

				// One arm's own two kerbs, with the road mouth between them — so this span isn't pavement,
				// it's the crossing. Straight over, and only its two ends are waypoints: nothing in the
				// middle, so a query for "somewhere to stand" can never land a pedestrian in the road.
				//
				// A mouth subtends only a small angle from the centre. The half-turn test is what tells it
				// apart from the way round the OUTSIDE of a dead end, where the arm's two kerbs are also the
				// only two kerbs there are — otherwise that junction gets two crossings stacked on its mouth
				// and no pavement round the back at all.
				if (from.Exit == to.Exit && SweepBetween(from.Angle, to.Angle) < MathF.PI)
				{
					// Suppressed per arm, or for the whole junction. Either way it drops the lane and nothing
					// else: the corner spans either side of this mouth still reach their kerbs and still link
					// to the roads arriving there, so the pavement stays joined all the way round. There's
					// simply no lane leading off the kerb into the road.
					if (intersection.SidewalkGraph != SidewalkGraphMode.NoCrossing && !exits[from.Exit].NoCrossing)
						AddSidewalkLane(intersection, [from.Point, to.Point], SidewalkKind.Crossing);

					continue;
				}

				AddSidewalkLane(intersection, CornerSpan(outline, centre, from.Point, from.Angle, to.Point, to.Angle),
				                SidewalkKind.Pavement);
			}
		}
	}



	/// <summary>
	/// The pavement between two arms: from one kerb, round whatever corner lies between them, to the next.
	///
	/// The ends are the real kerbs, which is what makes this join up — those are the exact points the two roads'
	/// own pavements run to, so the span meets them rather than landing near them. The middle is the junction's
	/// outline, so the path follows the actual kerb instead of cutting the corner off or bowing into the road.
	/// </summary>
	private static List<Vector3> CornerSpan(List<Vector3> _Outline, Vector3 _Centre,
	                                        Vector3 _From, float _FromAngle, Vector3 _To, float _ToAngle)
	{
		float gap = SweepBetween(_FromAngle, _ToAngle);

		// Sorted by how far round they are from the first kerb, so the span reads in order no matter which way
		// the outline was wound or where in the list it happens to start.
		var corner = new List<(Vector3 Point, float Sweep)>();

		foreach (Vector3 point in _Outline)
		{
			float sweep = SweepBetween(_FromAngle, AngleAround(_Centre, point));

			if (sweep > 0.0f && sweep < gap)
				corner.Add((point, sweep));
		}

		corner.Sort((a, b) => a.Sweep.CompareTo(b.Sweep));

		var span = new List<Vector3> { _From };

		foreach (var (point, _) in corner)
			span.Add(point);

		// Two arms close enough that no outline sample falls between them just get a straight line, which at
		// that separation is the right shape anyway — and beats dropping the span and leaving a hole.
		span.Add(_To);

		return span;
	}



	/// <summary>Bearing of a point about the junction centre, in radians, ignoring height.</summary>
	private static float AngleAround(Vector3 _Centre, Vector3 _Point)
	{
		Vector3 offset = _Point - _Centre;

		return MathF.Atan2(offset.y, offset.x);
	}



	/// <summary>How far round it is from one bearing to another, always forwards, always in [0, Tau).</summary>
	private static float SweepBetween(float _From, float _To)
	{
		float sweep = (_To - _From) % MathF.Tau;

		return sweep < 0.0f ? sweep + MathF.Tau : sweep;
	}



	/// <summary>
	/// Joins pavement lanes whose ends meet, in BOTH directions — a pavement has no one-way rule, and a
	/// pedestrian that could only ever walk one way down a street would be worse than no network at all.
	///
	/// Endpoint proximity, same as the vehicle graph uses, because the pieces are generated independently:
	/// a road traces its own pavement, an intersection traces its corners, and they line up geometrically
	/// without either knowing about the other.
	/// </summary>
	private void LinkSidewalks(RoadTrafficSettings _Settings)
	{
		// Deliberately NOT generous. Pavement ends that are meant to meet are built from the same exit transform
		// and the same offset, so they coincide — the tolerance only has to absorb a road and a junction being
		// authored with different sidewalk widths. Widen it much past this and it starts reaching clean across
		// a road mouth to the kerb opposite, which links the two sides of a street directly and lets a
		// pedestrian route over the carriageway without ever using the crossing.
		m_SidewalkLinkThreshold = _Settings.LinkThreshold;

		float thresholdSquared = m_SidewalkLinkThreshold * m_SidewalkLinkThreshold;

		SidewalkLinkCount = 0;

		for (int a = 0; a < SidewalkLanes.Count; a++)
		{
			for (int b = a + 1; b < SidewalkLanes.Count; b++)
			{
				TrafficLane first = SidewalkLanes[a];
				TrafficLane second = SidewalkLanes[b];

				// Two PAVEMENTS of the same owner never link. That's a road joining its own two sides straight
				// across itself whenever it's narrower than the tolerance, which routes pedestrians over the
				// carriageway and bypasses the crossing sitting right beside it.
				//
				// A crossing is exempt, because bridging its owner's own kerbs is the entire job. Blocking it
				// too costs nothing at a junction where a road arrives — the road's pavement links to both
				// sides and carries the connection — but at an arm with no RoadComponent on it, a forecourt or
				// a car park entrance, that bridge doesn't exist and the crossing is orphaned at both ends.
				if (!first.IsCrossing && !second.IsCrossing && ReferenceEquals(first.Owner, second.Owner))
					continue;

				if (ClosestEnds(first, second).DistanceSquared > thresholdSquared)
					continue;

				first.Successors.Add(second);
				second.Successors.Add(first);

				SidewalkLinkCount++;
			}
		}
	}



	/// <summary>
	/// How many pavement lanes ended up joined to each other. Nothing reads it but the debug draw and the
	/// layout readout — but "how many segments, how many joins" is the difference between a network and a
	/// pile of unconnected pieces, and you can't tell those apart by looking at the lines alone.
	/// </summary>
	public int SidewalkLinkCount { get; private set; }



	/// <summary>
	/// Every joined pair of pavement lanes, as the two endpoints that were close enough to link. For drawing
	/// the connectivity — a gap you can see is not the same as a gap the routing cares about, and until you
	/// can see the joins there's no telling which one you're looking at.
	/// </summary>
	public IEnumerable<(Vector3 From, Vector3 To)> GetSidewalkLinks()
	{
		foreach (TrafficLane lane in SidewalkLanes)
		{
			foreach (TrafficLane other in lane.Successors)
			{
				// Once per pair, not twice — the links are stored in both directions.
				if (SidewalkLanes.IndexOf(other) <= SidewalkLanes.IndexOf(lane))
					continue;

				var (from, to, _) = ClosestEnds(lane, other);

				yield return (from, to);
			}
		}
	}



	/// <summary>
	/// Every pavement end that joins onto nothing.
	///
	/// This is the fault worth seeing. A network that draws correctly and routes nowhere looks identical to one
	/// that works — the entire difference is which ends found a neighbour, and that isn't visible in the lines
	/// themselves. Two pavements meeting at a junction should have no dead end between them; one at the edge of
	/// the map legitimately does.
	/// </summary>
	public IEnumerable<Vector3> GetSidewalkDeadEnds()
	{
		foreach (TrafficLane lane in SidewalkLanes)
		{
			if (!IsEndLinked(lane, lane.StartPos))
				yield return lane.StartPos;

			if (!IsEndLinked(lane, lane.EndPos))
				yield return lane.EndPos;
		}
	}



	/// <summary>Whether anything this lane is linked to actually meets it at <paramref name="_End"/>.</summary>
	private bool IsEndLinked(TrafficLane _Lane, Vector3 _End)
	{
		float thresholdSquared = m_SidewalkLinkThreshold * m_SidewalkLinkThreshold;

		foreach (TrafficLane other in _Lane.Successors)
		{
			// A lane linked at its OTHER end doesn't help here — that's a chain that arrives and stops.
			if (_End.DistanceSquared(other.StartPos) <= thresholdSquared || _End.DistanceSquared(other.EndPos) <= thresholdSquared)
				return true;
		}

		return false;
	}



	private float m_SidewalkLinkThreshold = RoadTrafficSettings.Default.LinkThreshold;



	/// <summary>
	/// The nearest pair of endpoints between two lanes, and how far apart they are.
	///
	/// One function answers both "should these link?" and "where do they meet?", which is the point. Asking
	/// those separately lets them disagree: two pavements running either side of a road are equidistant at BOTH
	/// ends, so a per-lane "which end is nearer" picks the near kerb for one and the far kerb for the other and
	/// reports a join straight across the carriageway that was never the pair the distance test passed on.
	/// </summary>
	private static (Vector3 First, Vector3 Second, float DistanceSquared) ClosestEnds(TrafficLane _First, TrafficLane _Second)
	{
		var best = (First: _First.StartPos, Second: _Second.StartPos, DistanceSquared: float.MaxValue);

		Consider(_First.StartPos, _Second.StartPos);
		Consider(_First.StartPos, _Second.EndPos);
		Consider(_First.EndPos, _Second.StartPos);
		Consider(_First.EndPos, _Second.EndPos);

		return best;

		void Consider(Vector3 _A, Vector3 _B)
		{
			float distance = _A.DistanceSquared(_B);

			if (distance >= best.DistanceSquared)
				return;

			best = (_A, _B, distance);
		}
	}



	private void AddSidewalkLane(object _Owner, List<Vector3> _Points, SidewalkKind _Kind)
	{
		if (_Points.Count < 2)
			return;

		var lane = new TrafficLane
		{
			Owner = _Owner,
			IsRoadLane = false,
			IsCrossing = _Kind == SidewalkKind.Crossing,
			LaneWidth = 100.0f,
			SpeedLimit = 0.0f
		};

		lane.Waypoints.AddRange(_Points);

		SidewalkLanes.Add(lane);
	}



	private enum SidewalkKind
	{
		Pavement,
		Crossing
	}
}
