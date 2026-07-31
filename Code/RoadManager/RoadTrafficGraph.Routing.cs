using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// Routing queries over the lane graph — how far it actually is to drive from one place to another.
///
/// "Actually" is the point: straight-line distance is useless for anything that has to obey roads. A delivery
/// two blocks away as the crow flies can be a mile of one-way streets, and a job that times you on crow-flight
/// distance is unwinnable in exactly the places that are most interesting to drive.
/// </summary>
public sealed partial class RoadTrafficGraph
{
	/// <summary>
	/// Driving distance from <paramref name="_From"/> to <paramref name="_To"/>, following lanes in their legal
	/// direction. False when either end isn't near a road, or when no route exists at all — a one-way system can
	/// genuinely have no way round.
	///
	/// Dijkstra over whole lanes rather than individual waypoints: lanes are the unit the graph is linked in, and
	/// a city's worth of them is a few thousand nodes, which is nothing. The partial lengths at each end are
	/// added on afterwards so the answer is measured from the actual points, not from the nearest lane ends.
	/// </summary>
	public bool TryGetDrivingDistance(Vector3 _From, Vector3 _To, out float _Distance)
	{
		_Distance = 0.0f;

		TrafficLane start = FindNearestLane(_From, out int startIndex);
		TrafficLane goal = FindNearestLane(_To, out int goalIndex);

		if (start is null || goal is null)
			return false;

		// Same lane: just walk between the two waypoints. Only meaningful forwards — a lane is one-way, so a
		// goal BEHIND the start really does mean driving off and coming back round, which the search below
		// would find. Falling through to it is the honest answer.
		if (start == goal && goalIndex >= startIndex)
		{
			_Distance = start.DistanceFromStart(goalIndex) - start.DistanceFromStart(startIndex);

			return true;
		}

		// Cost recorded at the END of each lane, so relaxing a successor is a single addition of its length.
		var best = new Dictionary<TrafficLane, float>();
		var queue = new PriorityQueue<TrafficLane, float>();

		float startCost = start.DistanceToEnd(startIndex);

		best[start] = startCost;
		queue.Enqueue(start, startCost);

		while (queue.TryDequeue(out TrafficLane lane, out float cost))
		{
			// A stale entry from before we found a cheaper way here.
			if (best.TryGetValue(lane, out float known) && cost > known)
				continue;

			if (lane == goal)
			{
				// cost is to the END of the goal lane; we want a point partway along it.
				_Distance = Math.Max(0.0f, cost - lane.Length + lane.DistanceFromStart(goalIndex));

				return true;
			}

			foreach (TrafficLane next in lane.Successors)
			{
				float nextCost = cost + next.Length;

				if (best.TryGetValue(next, out float existing) && existing <= nextCost)
					continue;

				best[next] = nextCost;

				queue.Enqueue(next, nextCost);
			}
		}

		return false;
	}



	/// <summary>
	/// The drivable lane whose nearest waypoint is closest to <paramref name="_Point"/>, and which waypoint that
	/// was. Road lanes only — snapping a pickup onto an intersection cross-lane would measure the route from the
	/// middle of a junction.
	/// </summary>
	public TrafficLane FindNearestLane(Vector3 _Point, out int _Index)
	{
		TrafficLane bestLane = null;
		float bestDistance = float.MaxValue;

		_Index = 0;

		foreach (TrafficLane lane in Lanes)
		{
			if (!lane.IsRoadLane)
				continue;

			for (int i = 0; i < lane.Waypoints.Count; i++)
			{
				float distance = lane.Waypoints[i].DistanceSquared(_Point);

				if (distance >= bestDistance)
					continue;

				bestDistance = distance;
				bestLane = lane;

				_Index = i;
			}
		}

		return bestLane;
	}
}
