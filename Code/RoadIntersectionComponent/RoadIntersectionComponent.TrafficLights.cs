using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public enum DrivingSide
{
	/// <summary>
	/// Drive on the right side (USA, Europe, etc...)
	/// </summary>
	Right,

	/// <summary>
	/// Drive on the left side (UK, Japan, etc...)
	/// </summary>
	Left
}

public enum TrafficLightSystem
{
	/// <summary>
	/// Traffic lights placed near the intersection (before crossing)
	/// </summary>
	European,

	/// <summary>
	/// Traffic lights placed far from the intersection (after crossing)
	/// </summary>
	US
}

/// <summary>
/// Scene event fired when a traffic light changes state. Wire up to the prefab's own component to
/// drive the actual light visuals / signals.
/// </summary>
public interface ITrafficLightEvents : ISceneEvent<ITrafficLightEvents>
{
	void OnTrafficLightGoesRed() { }
	void OnTrafficLightGoesGreen() { }
}

public partial class RoadIntersectionComponent
{
	private bool m_DoesTrafficLightsNeedRebuild = false;

	// Each spawned light paired with its phase group (0 = North/South axis, 1 = East/West axis).
	private readonly List<(GameObject Obj, int Group)> m_TrafficLightObjects = new();
	private int m_TrafficPhase = 0;
	private float m_TrafficPhaseTimer = 0.0f;

	[Property, FeatureEnabled("Traffic Lights", Icon = "traffic", Tint = EditorTint.Red), Change] public bool HasTrafficLights { get; set; } = false;
	[Property(Title = "Prefab"), Feature("Traffic Lights")] public GameObject TrafficLightPrefab { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } }
	[Property, Feature("Traffic Lights")] private DrivingSide DrivingSystem { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = DrivingSide.Right;
	[Property(Title = "Placement System"), Feature("Traffic Lights")] private TrafficLightSystem TrafficLightPlacementSystem { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = TrafficLightSystem.European;
	[Property(Title = "Offset From Road (X)"), Feature("Traffic Lights"), Range(-200.0f, 200.0f)] private float TrafficLightOffsetFromRoadX { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = 25.0f;
	[Property(Title = "Offset From Road (Y)"), Feature("Traffic Lights"), Range(-200.0f, 200.0f)] private float TrafficLightOffsetFromRoadY { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = 25.0f;
	[Property(Title = "Height Offset"), Feature("Traffic Lights"), Range(0.0f, 10.0f)] private float TrafficLightHeightOffset { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = 0.0f;
	[Property(Title = "Rotation Offset"), Feature("Traffic Lights"), Range(0.0f, 360.0f)] private float TrafficLightRotationOffset { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = 0.0f;
	[Property(Title = "Cycle Duration"), Feature("Traffic Lights"), Range(5.0f, 120.0f)] private float TrafficLightCycleDuration { get; set; } = 30.0f;



	private void OnHasTrafficLightsChanged(bool _OldValue, bool _NewValue)
	{
		m_DoesTrafficLightsNeedRebuild = true;
	}



	private void CreateTrafficLights()
	{
		RemoveTrafficLights();

		if (!HasTrafficLights || !TrafficLightPrefab.IsValid())
			return;

		BuildTrafficLights();

		// Fire initial state so scripts attached to the lights get the correct colour on spawn.
		foreach (var (obj, group) in m_TrafficLightObjects)
		{
			if (!obj.IsValid())
				continue;

			if (group == m_TrafficPhase)
				ITrafficLightEvents.PostToGameObject(obj, x => x.OnTrafficLightGoesGreen());
			else
				ITrafficLightEvents.PostToGameObject(obj, x => x.OnTrafficLightGoesRed());
		}
	}



	private void RemoveTrafficLights()
	{
		m_TrafficLightObjects.Clear();
		m_TrafficPhase = 0;
		m_TrafficPhaseTimer = 0.0f;

		GameObject containerObject = GameObject.Children.FirstOrDefault(x => x.Name == "TrafficLights");

		if (containerObject.IsValid())
			containerObject.Destroy();
	}



	private void UpdateTrafficLights()
	{
		if (m_DoesTrafficLightsNeedRebuild)
		{
			CreateTrafficLights();
			m_DoesTrafficLightsNeedRebuild = false;
		}

		UpdateTrafficLightPhase();
	}



	private void UpdateTrafficLightPhase()
	{
		if (!HasTrafficLights || m_TrafficLightObjects.Count == 0)
			return;

		m_TrafficPhaseTimer += Time.Delta;
		float halfCycle = MathF.Max(1.0f, TrafficLightCycleDuration) * 0.5f;

		if (m_TrafficPhaseTimer < halfCycle)
			return;

		m_TrafficPhaseTimer -= halfCycle;
		m_TrafficPhase = 1 - m_TrafficPhase;

		foreach (var (obj, group) in m_TrafficLightObjects)
		{
			if (!obj.IsValid())
				continue;

			if (group == m_TrafficPhase)
				ITrafficLightEvents.PostToGameObject(obj, x => x.OnTrafficLightGoesGreen());
			else
				ITrafficLightEvents.PostToGameObject(obj, x => x.OnTrafficLightGoesRed());
		}
	}



	/// <summary>
	/// Returns true if vehicles approaching from <paramref name="_ApproachDir"/> should proceed.
	/// Always true when traffic lights are disabled (the caller handles priority-to-right).
	/// </summary>
	public bool IsApproachGreen(Vector3 _ApproachDir)
	{
		if (!HasTrafficLights)
			return true;

		// Exits aligned with WorldForward (North/South) are group 0; perpendicular (East/West) are group 1.
		float dot = MathF.Abs(Vector3.Dot(_ApproachDir.WithZ(0).Normal, WorldRotation.Forward.WithZ(0).Normal));
		int group = dot > 0.5f ? 0 : 1;
		return group == m_TrafficPhase;
	}



	private void BuildTrafficLights()
	{
		// Only rectangle intersections support traffic lights for now.
		if (Shape != IntersectionShape.Rectangle)
			return;

		GameObject containerObject = new GameObject(GameObject, true, "TrafficLights");
		containerObject.Flags |= GameObjectFlags.NotSaved;

		Vector3 up = WorldRotation.Up;
		float sidewalkOffset = SidewalkWidth;

		EnsureRectangleExits();

		var roads = Scene.GetAll<RoadComponent>().ToList();

		foreach (var exit in Exits)
		{
			if (exit is null)
				continue;

			// Only light an exit that traffic ARRIVES from: a road leaving the intersection here (or no connected road)
			// gets no light. Matched against the snap point (sidewalk edge), where a connecting road's endpoint sits.
			Transform worldExit = GetRectangleExitTransform(exit.Side, true, exit.Offset);
			float connectRadius = exit.Width * 0.5f + SidewalkWidth;

			if (!roads.Any(road => road.IsValid() && !road.ExcludeTraffic && road.HasIncomingTrafficAt(worldExit.Position, connectRadius)))
				continue;

			Transform exitTransform = GetRectangleExitLocalTransform(exit.Side, false, exit.Offset);

			Vector3 exitRight = exitTransform.Rotation.Right;
			Vector3 exitForward = exitTransform.Rotation.Forward;

			float exitRoadWidth = exit.Width;
			float halfRoadWidth = exitRoadWidth * 0.5f;

			float placementDistance = TrafficLightPlacementSystem == TrafficLightSystem.US ? -sidewalkOffset - exitRoadWidth : sidewalkOffset;
			placementDistance += TrafficLightOffsetFromRoadY;

			float sideMultiplier = DrivingSystem == DrivingSide.Left ? 1.0f : -1.0f;

			Vector3 position = exitTransform.Position
				+ exitForward * placementDistance
				+ exitRight * sideMultiplier * (halfRoadWidth + TrafficLightOffsetFromRoadX)
				+ up * (TrafficLightHeightOffset + SidewalkHeight);

			Rotation rotation = exitTransform.Rotation * Rotation.FromYaw(TrafficLightRotationOffset);

			// North/South exits share the WorldForward axis → group 0; East/West → group 1.
			int group = (exit.Side == RectangleExit.North || exit.Side == RectangleExit.South) ? 0 : 1;

			GameObject lightObj = CreateTrafficLight(containerObject, position, rotation);

			if (lightObj.IsValid())
				m_TrafficLightObjects.Add((lightObj, group));
		}
	}



	private float GetExitRoadWidth(RectangleExit _Exit)
	{
		return _Exit switch
		{
			RectangleExit.North or RectangleExit.South => Width,
			RectangleExit.East or RectangleExit.West => Length,
			_ => Width
		};
	}



	private GameObject CreateTrafficLight(GameObject _Parent, Vector3 _Position, Rotation _Rotation)
	{
		if (!TrafficLightPrefab.IsValid())
			return null;

		GameObject trafficLightObject = TrafficLightPrefab.Clone(_Parent, _Position, _Rotation, Vector3.One);

		if (!trafficLightObject.IsValid())
			return null;

		trafficLightObject.BreakFromPrefab();

		trafficLightObject.Flags |= GameObjectFlags.NotSaved;
		trafficLightObject.LocalPosition = _Position;
		trafficLightObject.LocalRotation = _Rotation;

		return trafficLightObject;
	}
}
