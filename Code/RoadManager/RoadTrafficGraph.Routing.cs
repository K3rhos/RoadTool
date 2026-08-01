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
	/// How far either end of a route may be from a lane and still count as being on it. A road carries a lane
	/// per direction, so a single point sits near several — see <see cref="FindNearbyLanes"/> for why taking
	/// them all matters.
	/// </summary>
	public const float DefaultRouteSnapRadius = 1000.0f;



	/// <summary>
	/// Driving distance from <paramref name="_From"/> to <paramref name="_To"/>, following lanes in their legal
	/// direction. False when neither end is anywhere near a road, or when no route exists at all — a one-way
	/// system can genuinely have no way round.
	///
	/// Dijkstra over whole lanes rather than individual waypoints: lanes are the unit the graph is linked in,
	/// and a city's worth of them is a few thousand nodes. Costs are measured to the START of each lane, with
	/// the partial lengths at both ends added on, so the answer is measured between the actual points.
	/// </summary>
	public bool TryGetDrivingDistance(Vector3 _From, Vector3 _To, out float _Distance)
	{
		_Distance = 0.0f;

		List<(TrafficLane Lane, int Index)> starts = FindNearbyLanes(_From);
		List<(TrafficLane Lane, int Index)> goals = FindNearbyLanes(_To);

		if (starts.Count == 0 || goals.Count == 0)
			return false;

		var goalIndices = new Dictionary<TrafficLane, int>();

		foreach ((TrafficLane lane, int index) in goals)
			goalIndices[lane] = index;

		var best = new Dictionary<TrafficLane, float>();
		var queue = new PriorityQueue<TrafficLane, float>();

		float bestTotal = float.MaxValue;

		foreach ((TrafficLane lane, int index) in starts)
		{
			// Goal on the same lane and ahead of us: straight down the road, no junction involved. Behind us
			// doesn't count — a lane is one-way, so that really does mean driving round and coming back, which
			// the search below works out properly.
			if (goalIndices.TryGetValue(lane, out int goalIndex) && goalIndex >= index)
				bestTotal = Math.Min(bestTotal, lane.DistanceFromStart(goalIndex) - lane.DistanceFromStart(index));

			// We start partway along, so what's reachable is the successors, at the cost of finishing this lane.
			float toEnd = lane.DistanceToEnd(index);

			foreach (TrafficLane next in lane.Successors)
				Relax(next, toEnd, best, queue);
		}

		while (queue.TryDequeue(out TrafficLane lane, out float cost))
		{
			// Min-ordered, so once the cheapest thing left already costs more than an answer we have, nothing
			// better can come out of it.
			if (cost >= bestTotal)
				break;

			if (best.TryGetValue(lane, out float known) && cost > known)
				continue;

			// Reaching a goal lane doesn't end the search: another route might arrive at a different goal
			// candidate — the other side of the same street, say — for less.
			if (goalIndices.TryGetValue(lane, out int goalIndex))
				bestTotal = Math.Min(bestTotal, cost + lane.DistanceFromStart(goalIndex));

			float exit = cost + lane.Length;

			foreach (TrafficLane next in lane.Successors)
				Relax(next, exit, best, queue);
		}

		if (bestTotal >= float.MaxValue)
			return false;

		_Distance = Math.Max(0.0f, bestTotal);

		return true;
	}



	private static void Relax(TrafficLane _Lane, float _Cost, Dictionary<TrafficLane, float> _Best, PriorityQueue<TrafficLane, float> _Queue)
	{
		if (_Best.TryGetValue(_Lane, out float existing) && existing <= _Cost)
			return;

		_Best[_Lane] = _Cost;

		_Queue.Enqueue(_Lane, _Cost);
	}



	/// <summary>
	/// Every drivable lane with a waypoint within <paramref name="_Radius"/> of the point, and which waypoint
	/// that was — at most one entry per lane.
	///
	/// Taking ALL of them, rather than just the closest, is what makes routing reliable. A road carries a lane
	/// per direction, so any point on it is near at least two; picking only the nearest is a coin flip that can
	/// land on the one pointing away from where you're going, or on one nothing feeds into. The route then comes
	/// back as impossible even though the lane a few metres over is trivially routable. Seeding the search with
	/// every candidate — and accepting any of them at the far end — also gets the natural answer for free:
	/// either side of the street will do, whichever is closer to drive.
	///
	/// Road lanes only. Snapping an endpoint onto an intersection cross-lane would measure from the middle of
	/// a junction.
	/// </summary>
	public List<(TrafficLane Lane, int Index)> FindNearbyLanes(Vector3 _Point, float _Radius = DefaultRouteSnapRadius)
	{
		var results = new List<(TrafficLane, int)>();

		TrafficLane nearestLane = null;
		int nearestIndex = 0;
		float nearestDistance = float.MaxValue;

		float radiusSquared = _Radius * _Radius;

		foreach (TrafficLane lane in Lanes)
		{
			if (!lane.IsRoadLane)
				continue;

			int laneIndex = -1;
			float laneDistance = float.MaxValue;

			for (int i = 0; i < lane.Waypoints.Count; i++)
			{
				float distance = lane.Waypoints[i].DistanceSquared(_Point);

				if (distance >= laneDistance)
					continue;

				laneDistance = distance;
				laneIndex = i;
			}

			if (laneIndex < 0)
				continue;

			if (laneDistance < nearestDistance)
			{
				nearestDistance = laneDistance;
				nearestLane = lane;
				nearestIndex = laneIndex;
			}

			if (laneDistance <= radiusSquared)
				results.Add((lane, laneIndex));
		}

		// Off-road entirely (a car park, a field) — the closest lane is still the honest answer, so don't come
		// back empty and turn a long route into "no route".
		if (results.Count == 0 && nearestLane is not null)
			results.Add((nearestLane, nearestIndex));

		return results;
	}



	/// <summary>
	/// The single drivable lane closest to a point, and which waypoint that was. Prefer
	/// <see cref="FindNearbyLanes"/> for routing — one lane is rarely the whole answer for a two-way road.
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
