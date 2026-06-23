using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadIntersectionComponent
{
	/// <summary>
	/// A single drivable exit of an intersection, expressed in world space.
	/// <see cref="Transform"/>.Forward points outward (away from the intersection), matching the snap targets.
	/// </summary>
	public readonly struct TrafficExit
	{
		public Transform Transform { get; init; }
		public float RoadWidth { get; init; }
	}

	/// <summary>
	/// When enabled, this intersection is ignored by the traffic system: vehicles will not route through it.
	/// </summary>
	[Property, Feature("General"), Category("Traffic"), Order(2)] public bool ExcludeTraffic { get; set; } = false;

	/// <summary>Speed limit for traffic crossing this intersection, in km/h.</summary>
	[Property, Feature("General"), Category("Traffic"), Order(2), Range(5.0f, 130.0f)] public float SpeedLimit { get; set; } = 30.0f;



	/// <summary>
	/// Enumerates every active exit of this intersection (rectangle or circle) as a world transform plus road width.
	/// These are the same outer-edge positions that <see cref="SnapNearbyRoads"/> snaps roads to, so the traffic
	/// graph can match road endpoints against them by proximity.
	/// </summary>
	public List<TrafficExit> GetTrafficExits()
	{
		var exits = new List<TrafficExit>();

		if (Shape == IntersectionShape.Rectangle)
		{
			EnsureRectangleExits();

			foreach (var exit in Exits)
			{
				if (exit is null)
					continue;

				exits.Add(new TrafficExit
				{
					Transform = GetRectangleExitTransform(exit.Side, true, exit.Offset),
					RoadWidth = exit.Width
				});
			}
		}
		else
		{
			var circleExits = CircleExits ?? Array.Empty<CircleExit>();

			for (int i = 0; i < circleExits.Length; i++)
			{
				exits.Add(new TrafficExit
				{
					Transform = GetCircleExitTransform(i, true),
					RoadWidth = circleExits[i].RoadWidth
				});
			}
		}

		return exits;
	}
}
