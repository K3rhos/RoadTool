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



	/// <summary>
	/// The actual waypoints to DRIVE from one point to another, following lanes in their legal direction. False
	/// when there's no route, same as <see cref="TryGetDrivingDistance"/>.
	///
	/// This is the vehicle counterpart of a navmesh path, and it has to be a separate structure rather than the
	/// navmesh itself. A navmesh is baked around a person — it runs over pavements, through doorways and up
	/// stairs, and its corridors are person-wide. A route down one is a perfectly valid walk and an impossible
	/// drive, and unlike a pedestrian scraping a wall, a car routed somewhere it doesn't fit is stuck for good.
	/// Lanes are the drivable surface by construction, and they carry direction, which a navmesh has no concept
	/// of at all.
	///
	/// The list is reused rather than returned, because anything chasing anything re-routes constantly.
	/// </summary>
	public bool TryGetDrivingRoute(Vector3 _From, Vector3 _To, List<Vector3> _Route)
	{
		_Route.Clear();

		if (!TrySearchRoute(_From, _To, out RouteSearch search))
			return false;

		BuildRoute(search.Starts, search.GoalIndices, search.CameFrom, search.Goal, search.Start, search.StartIndex, _Route);

		return _Route.Count > 0;
	}



	/// <summary>
	/// The same route as <see cref="TryGetDrivingRoute"/>, but as the LANES to drive rather than the waypoints
	/// along them — including the one we're starting on.
	///
	/// This is the form anything that already knows how to drive a lane wants. <see cref="TrafficVehicle"/>
	/// follows lanes and picks a successor at every junction; handing it a lane list turns "wander" into "go
	/// here" without touching a single line of how it actually steers, brakes or corners.
	/// </summary>
	public bool TryGetDrivingLaneRoute(Vector3 _From, Vector3 _To, List<TrafficLane> _Route)
	{
		return TryGetDrivingLaneRoute(FindNearbyLanes(_From), _To, _Route);
	}



	/// <summary>
	/// The same, but starting from the lane a vehicle is ALREADY DRIVING rather than from its position.
	///
	/// This is the overload anything mid-journey wants, and the difference is not subtle. Asking by position
	/// seeds the search with every lane in range — including the one going the other way down the same road —
	/// and Dijkstra will happily return the shortest route from whichever of those is cheapest. That route
	/// starts on a lane the vehicle is not on, so it never matches, and a driver that can't find itself in its
	/// own route falls back to picking turns at random. The symptom is a car that mostly goes the right way and
	/// occasionally sets off round the block for no visible reason.
	/// </summary>
	public bool TryGetDrivingLaneRoute(TrafficLane _FromLane, Vector3 _FromPosition, Vector3 _To, List<TrafficLane> _Route)
	{
		_Route.Clear();

		if (_FromLane is null || _FromLane.Waypoints.Count == 0)
			return false;

		return TryGetDrivingLaneRoute([(_FromLane, NearestWaypointIndex(_FromLane, _FromPosition))], _To, _Route);
	}



	/// <summary>Which waypoint of a lane is closest to a point.</summary>
	private static int NearestWaypointIndex(TrafficLane _Lane, Vector3 _Point)
	{
		int best = 0;
		float bestDistance = float.MaxValue;

		for (int i = 0; i < _Lane.Waypoints.Count; i++)
		{
			float distance = _Lane.Waypoints[i].DistanceSquared(_Point);

			if (distance >= bestDistance)
				continue;

			bestDistance = distance;
			best = i;
		}

		return best;
	}



	private bool TryGetDrivingLaneRoute(List<(TrafficLane Lane, int Index)> _Starts, Vector3 _To, List<TrafficLane> _Route)
	{
		_Route.Clear();

		if (!TrySearchRoute(_Starts, _To, out RouteSearch search))
			return false;

		// Never left the start lane — the route is just that one.
		if (search.Goal is null)
		{
			if (search.Start is null)
				return false;

			_Route.Add(search.Start);

			return true;
		}

		List<TrafficLane> chain = BuildLaneChain(search.CameFrom, search.Goal);

		if (chain.Count == 0)
			return false;

		// The lane we're ON isn't in the chain (the chain begins at one of its successors), and a driver already
		// travelling it needs to see it in the list or its very first junction is an unplanned one.
		foreach ((TrafficLane lane, int _) in search.Starts)
		{
			if (!lane.Successors.Contains(chain[0]))
				continue;

			_Route.Add(lane);

			break;
		}

		_Route.AddRange(chain);

		return true;
	}



	/// <summary>What a completed search found: the winning route's ends, and the map to walk it back with.</summary>
	private struct RouteSearch
	{
		public List<(TrafficLane Lane, int Index)> Starts;
		public Dictionary<TrafficLane, int> GoalIndices;
		public Dictionary<TrafficLane, TrafficLane> CameFrom;

		/// <summary>The lane the route ends on, or null when it never left the lane it started on.</summary>
		public TrafficLane Goal;

		public TrafficLane Start;
		public int StartIndex;
	}



	/// <summary>
	/// The Dijkstra itself, shared by both route shapes so there's one search to be correct rather than two to
	/// keep in step.
	/// </summary>
	private bool TrySearchRoute(Vector3 _From, Vector3 _To, out RouteSearch _Result)
	{
		return TrySearchRoute(FindNearbyLanes(_From), _To, out _Result);
	}



	/// <inheritdoc cref="TrySearchRoute(Vector3, Vector3, out RouteSearch)"/>
	private bool TrySearchRoute(List<(TrafficLane Lane, int Index)> _Starts, Vector3 _To, out RouteSearch _Result)
	{
		_Result = default;

		List<(TrafficLane Lane, int Index)> starts = _Starts;
		List<(TrafficLane Lane, int Index)> goals = FindNearbyLanes(_To);

		if (starts is null || starts.Count == 0 || goals.Count == 0)
			return false;

		var goalIndices = new Dictionary<TrafficLane, int>();

		foreach ((TrafficLane lane, int index) in goals)
			goalIndices[lane] = index;

		var best = new Dictionary<TrafficLane, float>();
		var cameFrom = new Dictionary<TrafficLane, TrafficLane>();
		var queue = new PriorityQueue<TrafficLane, float>();

		// The winning route so far: where it ends, and which of the several starts it began at.
		float bestTotal = float.MaxValue;
		TrafficLane bestGoal = null;
		TrafficLane bestStart = null;
		int bestStartIndex = 0;

		foreach ((TrafficLane lane, int index) in starts)
		{
			// Goal on the same lane and ahead of us — no junction involved, so the route is just this stretch.
			if (goalIndices.TryGetValue(lane, out int sameLaneGoal) && sameLaneGoal >= index)
			{
				float direct = lane.DistanceFromStart(sameLaneGoal) - lane.DistanceFromStart(index);

				if (direct < bestTotal)
				{
					bestTotal = direct;
					bestGoal = null;      // null goal marks "never left the start lane"
					bestStart = lane;
					bestStartIndex = index;
				}
			}

			float toEnd = lane.DistanceToEnd(index);

			foreach (TrafficLane next in lane.Successors)
			{
				if (Relax(next, toEnd, best, queue))
					cameFrom[next] = lane;
			}
		}

		while (queue.TryDequeue(out TrafficLane lane, out float cost))
		{
			if (cost >= bestTotal)
				break;

			if (best.TryGetValue(lane, out float known) && cost > known)
				continue;

			if (goalIndices.TryGetValue(lane, out int goalIndex))
			{
				float total = cost + lane.DistanceFromStart(goalIndex);

				if (total < bestTotal)
				{
					bestTotal = total;
					bestGoal = lane;
				}
			}

			float exit = cost + lane.Length;

			foreach (TrafficLane next in lane.Successors)
			{
				if (Relax(next, exit, best, queue))
					cameFrom[next] = lane;
			}
		}

		if (bestTotal >= float.MaxValue)
			return false;

		_Result = new RouteSearch
		{
			Starts = starts,
			GoalIndices = goalIndices,
			CameFrom = cameFrom,
			Goal = bestGoal,
			Start = bestStart,
			StartIndex = bestStartIndex
		};

		return true;
	}



	/// <summary>
	/// Walks the predecessor chain back from the winning goal lane to whichever start it came from, then lays
	/// the waypoints down in travel order.
	///
	/// The two ends are partial lanes — we join partway along the first and stop partway along the last — which
	/// is why they're handled separately from the whole lanes in between.
	/// </summary>
	private void BuildRoute(List<(TrafficLane Lane, int Index)> _Starts, Dictionary<TrafficLane, int> _GoalIndices,
	                        Dictionary<TrafficLane, TrafficLane> _CameFrom, TrafficLane _Goal,
	                        TrafficLane _Start, int _StartIndex, List<Vector3> _Route)
	{
		// Never left the start lane: one straight run down it.
		if (_Goal is null)
		{
			if (_Start is null || !_GoalIndices.TryGetValue(_Start, out int stop))
				return;

			for (int i = _StartIndex; i <= stop; i++)
				_Route.Add(_Start.Waypoints[i]);

			return;
		}

		List<TrafficLane> chain = BuildLaneChain(_CameFrom, _Goal);

		if (chain.Count == 0)
			return;

		// The first lane in the chain is a successor of the start lane, so the start lane itself isn't in it —
		// find which of the candidates fed it and lay down the tail of that one first.
		TrafficLane head = chain[0];

		foreach ((TrafficLane lane, int index) in _Starts)
		{
			if (!lane.Successors.Contains(head))
				continue;

			for (int i = index; i < lane.Waypoints.Count; i++)
				_Route.Add(lane.Waypoints[i]);

			break;
		}

		for (int c = 0; c < chain.Count; c++)
		{
			TrafficLane lane = chain[c];

			// The last one stops at the goal waypoint rather than running to the end of the road.
			int stop = c == chain.Count - 1 && _GoalIndices.TryGetValue(lane, out int goalIndex)
				? goalIndex
				: lane.Waypoints.Count - 1;

			for (int i = 0; i <= stop; i++)
				_Route.Add(lane.Waypoints[i]);
		}
	}



	/// <summary>Walks the predecessor map back from a goal lane and returns the chain in travel order.</summary>
	private List<TrafficLane> BuildLaneChain(Dictionary<TrafficLane, TrafficLane> _CameFrom, TrafficLane _Goal)
	{
		var chain = new List<TrafficLane>();
		TrafficLane current = _Goal;

		// Bounded by the lane count so a cycle in the map can't spin forever.
		for (int step = 0; step <= Lanes.Count && current is not null; step++)
		{
			chain.Add(current);

			if (!_CameFrom.TryGetValue(current, out TrafficLane previous))
				break;

			current = previous;
		}

		chain.Reverse();

		return chain;
	}



	/// <summary>True when this was an improvement, so the caller knows whether to record the predecessor.</summary>
	private static bool Relax(TrafficLane _Lane, float _Cost, Dictionary<TrafficLane, float> _Best, PriorityQueue<TrafficLane, float> _Queue)
	{
		if (_Best.TryGetValue(_Lane, out float existing) && existing <= _Cost)
			return false;

		_Best[_Lane] = _Cost;

		_Queue.Enqueue(_Lane, _Cost);

		return true;
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
