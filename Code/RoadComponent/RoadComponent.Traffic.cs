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

/// <summary>Travel direction of a single road lane, relative to the spline's start→end direction.</summary>
public enum LaneDirection
{
	Forward,
	Backward
}

public partial class RoadComponent
{
	/// <summary>
	/// When enabled, this road is ignored by the traffic system: no vehicles spawn on it and no AI will route through it.
	/// </summary>
	[Property, Feature("General"), Category("Traffic")] public bool ExcludeTraffic { get; set; } = false;

	/// <summary>Speed limit for traffic on this road, in km/h.</summary>
	[Property, Feature("General"), Category("Traffic"), Range(5.0f, 130.0f)] public float SpeedLimit { get; set; } = 50.0f;

	/// <summary>How far this road's dead-end U-turn loop is held back from the road end, in units (larger = the bulb sits further back up the road).</summary>
	[Property, Feature("General"), Category("Traffic"), Range(-1000.0f, 1000.0f)] public float UTurnClearance { get; set; } = 100.0f;



	/// <summary>
	/// The road's lanes in order across its width (−right edge → +right edge). Each entry is one lane plus its travel
	/// direction, so the list length IS the lane count. This is the source of truth: the line separators (and their
	/// RoadLineDefinition textures) are derived from it. Empty until first enabled, then seeded with a normal 2-lane road.
	/// </summary>
	[Property, Feature("General"), Category("Traffic"), Change] public LaneDirection[] Lanes { get; set; } = [];

	private void OnLanesChanged(LaneDirection[] _OldValue, LaneDirection[] _NewValue)
	{
		if (_NewValue.Length > 0)
		{
			_NewValue[^1] = new LaneDirection();
			
			ResizeLineDefinitions(_NewValue.Length - 1);
		}
		else
		{
			ResizeLineDefinitions(0);
		}
		
		IsDirty = true;
	}

	/// <summary>Flips every lane's direction at once — swaps which side traffic flows on (right-hand ↔ left-hand drive).</summary>
	[Property, Feature("General"), Category("Traffic")] public bool InvertDirection { get; set { field = value; IsDirty = true; } } = false;

	/// <summary>Number of lane-dividing lines: one between each pair of lanes (lanes − 1).</summary>
	public int TrafficLineCount => Math.Max(0, (Lanes?.Length ?? 1) - 1);



	/// <summary>
	/// Initialises <see cref="Lanes"/> the first time it's needed. A brand-new road defaults to a 2-lane (one each way)
	/// road; a road saved under the old "lines define the lane count" model is migrated to the equivalent lane list with
	/// the old direction split. Once <see cref="Lanes"/> holds anything it is never overwritten, so authored data is safe.
	/// </summary>
	public void EnsureLanes()
	{
		if (Lanes != null && Lanes.Length > 0)
			return;

		int count = (LineDefinitions != null && LineDefinitions.Length > 0) ? LineDefinitions.Length + 1 : 2;
		Lanes = BuildDefaultLanes(count);
		ResizeLineDefinitions(Lanes.Length - 1);
	}

	/// <summary>The legacy split: lanes shared as evenly as possible, the odd lane forward, forward lanes on the +right side.</summary>
	private static LaneDirection[] BuildDefaultLanes(int _Count)
	{
		_Count = Math.Max(1, _Count);
		int forwardCount = (_Count + 1) / 2;

		var lanes = new List<LaneDirection>(_Count);

		for (int k = 0; k < _Count; k++)
			lanes.Add(k >= _Count - forwardCount ? LaneDirection.Forward : LaneDirection.Backward);

		return lanes.ToArray();
	}

	/// <summary>Keeps the per-separator line array exactly one shorter than the lane count, preserving existing entries.</summary>
	private void ResizeLineDefinitions(int _Count)
	{
		_Count = Math.Max(0, _Count);

		int current = LineDefinitions?.Length ?? 0;

		if (current == _Count)
			return;

		var resized = new RoadLineDefinition[_Count];

		if (LineDefinitions != null)
			Array.Copy(LineDefinitions, resized, Math.Min(current, _Count));

		LineDefinitions = resized;
	}



	/// <summary>
	/// Lane layout straight from <see cref="Lanes"/>: equal-width lanes across the road, each carrying its authored
	/// direction (optionally flipped by <see cref="InvertDirection"/>). Forward = the spline's start→end direction.
	/// </summary>
	public List<TrafficLaneSlot> GetTrafficLaneLayout()
	{
		EnsureLanes();

		int totalLanes = Lanes.Length;
		float laneWidth = RoadWidth / totalLanes;

		var slots = new List<TrafficLaneSlot>(totalLanes);

		for (int k = 0; k < totalLanes; k++)
		{
			float offset = -RoadWidth * 0.5f + (k + 0.5f) * laneWidth;
			bool forward = (Lanes[k] == LaneDirection.Forward) ^ InvertDirection;
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
