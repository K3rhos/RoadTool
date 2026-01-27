using System;
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

public partial class RoadIntersectionComponent
{
	private bool m_DoesTrafficLightsNeedRebuild = false;

	[Property, FeatureEnabled("Traffic Lights", Icon = "traffic", Tint = EditorTint.Red), Change] private bool HasTrafficLights { get; set; } = false;
	[Property(Title = "Prefab"), Feature("Traffic Lights")] public GameObject TrafficLightPrefab { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } }
	[Property, Feature("Traffic Lights")] private DrivingSide DrivingSystem { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = DrivingSide.Right;
	[Property(Title = "Placement System"), Feature("Traffic Lights")] private TrafficLightSystem TrafficLightPlacementSystem { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = TrafficLightSystem.European;
	[Property(Title = "Offset From Road (X)"), Feature("Traffic Lights"), Range(-200.0f, 200.0f)] private float TrafficLightOffsetFromRoadX { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = 25.0f;
	[Property(Title = "Offset From Road (Y)"), Feature("Traffic Lights"), Range(-200.0f, 200.0f)] private float TrafficLightOffsetFromRoadY { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = 25.0f;
	[Property(Title = "Height Offset"), Feature("Traffic Lights"), Range(0.0f, 10.0f)] private float TrafficLightHeightOffset { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = 0.0f;
	[Property(Title = "Rotation Offset"), Feature("Traffic Lights"), Range(0.0f, 360.0f)] private float TrafficLightRotationOffset { get; set { field = value; m_DoesTrafficLightsNeedRebuild = true; } } = 0.0f;



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
	}



	private void RemoveTrafficLights()
	{
		// If we're in play mode, do not clear them
		if (LoadingScreen.IsVisible || Game.IsPlaying)
			return;

		GameObject containerObject = GameObject.Children.FirstOrDefault(x => x.Name == "TrafficLights");

		if (containerObject.IsValid())
		{
			foreach (var gameObject in containerObject.Children.Where(x => x.IsValid()))
			{
				gameObject.Destroy();
			}
		}
	}



	private void UpdateTrafficLights()
	{
		if (m_DoesTrafficLightsNeedRebuild)
		{
			CreateTrafficLights();

			m_DoesTrafficLightsNeedRebuild = false;
		}
	}



	private void BuildTrafficLights()
	{
		// If we're in play mode, do not rebuild them
		if (LoadingScreen.IsVisible || Game.IsPlaying)
			return;

		// Only build for rectangle intersections
		if (Shape != IntersectionShape.Rectangle)
			return;

		GameObject containerObject = GameObject.Children.FirstOrDefault(x => x.Name == "TrafficLights");

		if (!containerObject.IsValid())
			containerObject = new GameObject(GameObject, true, "TrafficLights");

		Vector3 up = WorldRotation.Up;
		float sidewalkOffset = SidewalkWidth;

		foreach (RectangleExit exit in Enum.GetValues<RectangleExit>())
		{
			if (exit == RectangleExit.None || !RectangleExits.HasFlag(exit))
				continue;

			Transform exitTransform = GetRectangleExitLocalTransform(exit);

			Vector3 exitRight = exitTransform.Rotation.Right;
			Vector3 exitForward = exitTransform.Rotation.Forward;

			float exitRoadWidth = GetExitRoadWidth(exit);
			float halfRoadWidth = exitRoadWidth * 0.5f;

			float placementDistance = TrafficLightPlacementSystem == TrafficLightSystem.US ? -sidewalkOffset - exitRoadWidth : sidewalkOffset;
			placementDistance += TrafficLightOffsetFromRoadY;

			float sideMultiplier = DrivingSystem == DrivingSide.Left ? 1.0f : -1.0f;

			Vector3 position = exitTransform.Position
				+ exitForward * placementDistance
				+ exitRight * sideMultiplier * (halfRoadWidth + TrafficLightOffsetFromRoadX)
				+ up * (TrafficLightHeightOffset + SidewalkHeight);

			Rotation rotation = exitTransform.Rotation * Rotation.FromYaw(TrafficLightRotationOffset);

			CreateTrafficLight(containerObject, position, rotation);
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



	private void CreateTrafficLight(GameObject _Parent, Vector3 _Position, Rotation _Rotation)
	{
		if (!TrafficLightPrefab.IsValid())
			return;

		GameObject trafficLightObject = TrafficLightPrefab.Clone(_Parent, _Position, _Rotation, Vector3.One);
		trafficLightObject.BreakFromPrefab();

		if (!trafficLightObject.IsValid())
			return;

		trafficLightObject.Name = "TrafficLight";
		trafficLightObject.LocalPosition = _Position;
		trafficLightObject.LocalRotation = _Rotation;
	}
}
