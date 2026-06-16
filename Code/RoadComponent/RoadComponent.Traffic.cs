using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>One drivable lane of a road: its signed lateral offset from the centerline (+ = right side) and travel direction.</summary>
public readonly struct TrafficLaneSlot
{
	public float Offset { get; init; }
	public bool Forward { get; init; }
}

public partial class RoadComponent
{
	/// <summary>
	/// When enabled, this road is ignored by the traffic system: no vehicles spawn on it and no AI will route through it.
	/// </summary>
	[Property, Feature("General"), Category("Traffic")] public bool ExcludeTraffic { get; set; } = false;

	/// <summary>Speed limit for traffic on this road, in km/h.</summary>
	[Property, Feature("General"), Category("Traffic"), Range(5.0f, 130.0f)] public float SpeedLimit { get; set; } = 50.0f;



	/// <summary>
	/// Number of lane-dividing lines on this road. 0 lines = a single one-way lane, 1 line = 2 lanes, 2 lines = 3 lanes, etc.
	/// </summary>
	public int TrafficLineCount => HasLines && LineDefinitions != null ? LineDefinitions.Length : 0;



	/// <summary>
	/// Lane layout derived from the Lines: <c>lines + 1</c> equal-width lanes across the road. Lanes are split between
	/// directions as evenly as possible, with the odd lane going to the forward (start→end) direction, and forward lanes
	/// placed on the right (+right) side for right-hand traffic. So: 1 lane = one-way; 2 = 1+1; 3 = 2 forward / 1 back; 4 = 2+2.
	/// </summary>
	public List<TrafficLaneSlot> GetTrafficLaneLayout()
	{
		int totalLanes = TrafficLineCount + 1;
		float laneWidth = RoadWidth / totalLanes;
		int forwardCount = (totalLanes + 1) / 2; // ceil — extra lane goes forward

		var slots = new List<TrafficLaneSlot>(totalLanes);

		for (int k = 0; k < totalLanes; k++)
		{
			float offset = -RoadWidth * 0.5f + (k + 0.5f) * laneWidth;
			bool forward = k >= totalLanes - forwardCount; // top indices (+right side) are forward
			slots.Add(new TrafficLaneSlot { Offset = offset, Forward = forward });
		}

		return slots;
	}



	/// <summary>
	/// Samples the road centerline in WORLD space at roughly even spacing.
	/// Fills <paramref name="_Positions"/> with surface points and <paramref name="_Rights"/> with the matching
	/// right vector at each point (so the traffic graph can offset lanes to one side). Both lists are cleared first.
	/// </summary>
	public void GetTrafficCenterline(float _Spacing, List<Vector3> _Positions, List<Vector3> _Rights)
	{
		_Positions.Clear();
		_Rights.Clear();

		float length = Spline.Length;

		if (length <= 1.0f)
			return;

		int count = Math.Max(2, (int)Math.Ceiling(length / Math.Max(1.0f, _Spacing)) + 1);

		for (int i = 0; i < count; i++)
		{
			float t = (float)i / (count - 1);
			float distance = t * length;

			var sample = Spline.SampleAtDistance(distance);

			Vector3 worldPos = WorldTransform.PointToWorld(sample.Position);
			Vector3 worldTangent = (WorldRotation * sample.Tangent).Normal;

			if (worldTangent.IsNearZeroLength)
				worldTangent = Vector3.Forward;

			Vector3 right = Rotation.LookAt(worldTangent, Vector3.Up).Right;

			_Positions.Add(worldPos);
			_Rights.Add(right);
		}
	}
}
