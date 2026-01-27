using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public enum IntersectionShape
{
	/// <summary>
	/// Rectangle mode is allowing you to toggle between 4 differents road exits
	/// </summary>
	Rectangle,

	/// <summary>
	/// (WIP) Circle mode is incomplete and really experimental yet
	/// </summary>
	Circle
}

/// <summary>
/// Represents a road intersection component allowing you to select specific exit points
/// </summary>
[Icon("roundabout_left")]
public partial class RoadIntersectionComponent : Component, Component.ExecuteInEditor
{
	private MeshBuilder m_MeshBuilder;

	[Property, Feature("General", Icon = "public", Tint = EditorTint.White), Order(0)] private IntersectionShape Shape { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = IntersectionShape.Rectangle;

	[Property(Title = "Material"), Feature("General"), Order(0)] private Material RoadMaterial { get; set { field = value; m_MeshBuilder?.IsDirty = true; } }
	[Property(Title = "Texture Repeat"), Feature("General"), Order(0)] private float RoadTextureRepeat { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 500.0f;

	[Property(Title = "Material"), Feature("General"), Category("Sidewalk"), Order(3)] private Material SidewalkMaterial { get; set { field = value; m_MeshBuilder?.IsDirty = true; } }
	[Property(Title = "Width"), Feature("General"), Category("Sidewalk"), Order(3)] private float SidewalkWidth { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 150.0f;
	[Property(Title = "Height"), Feature("General"), Category("Sidewalk"), Order(3)] private float SidewalkHeight { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 5.0f;
	[Property(Title = "Texture Repeat"), Feature("General"), Category("Sidewalk"), Order(3)] private float SidewalkTextureRepeat { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 200.0f;



	protected override void OnEnabled()
	{
		m_MeshBuilder = new MeshBuilder(GameObject);
		m_MeshBuilder.OnBuild += BuildAllMeshes;
		m_MeshBuilder.Rebuild();

		CreateTrafficLights();
	}



	protected override void OnDisabled()
	{
		m_MeshBuilder?.OnBuild -= BuildAllMeshes;
		m_MeshBuilder?.Clear();

		RemoveTrafficLights();
	}



	protected override void OnUpdate()
	{
		m_MeshBuilder?.Update();

		UpdateTrafficLights();
	}



	protected override void DrawGizmos()
	{
		if (!Gizmo.IsSelected)
			return;

		// Draw the bounds of the intersection
		Gizmo.Draw.LineThickness = 2.0f;

		if (Shape == IntersectionShape.Rectangle)
		{
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineBBox(new BBox(new Vector3(-Length / 2, -Width / 2, 0), new Vector3(Length / 2, Width / 2, SidewalkHeight)));

			Gizmo.Draw.Color = Color.Yellow;
			Gizmo.Draw.LineBBox(new BBox(new Vector3(-Length / 2 - SidewalkWidth, -Width / 2 - SidewalkWidth, 0), new Vector3(Length / 2 + SidewalkWidth, Width / 2 + SidewalkWidth, SidewalkHeight)));
		}
		else
		{
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineCylinder(Vector3.Zero, Vector3.Up * SidewalkHeight, Radius, Radius, 32);

			Gizmo.Draw.Color = Color.Yellow;
			Gizmo.Draw.LineCylinder(Vector3.Zero, Vector3.Up * SidewalkHeight, Radius + SidewalkWidth, Radius + SidewalkWidth, 32);
		}

		// Draw exit indicators
		if (Shape == IntersectionShape.Rectangle)
		{
			foreach (RectangleExit val in System.Enum.GetValues<RectangleExit>())
			{
				if (val == RectangleExit.None || !RectangleExits.HasFlag(val))
					continue;

				Transform transform = GetRectangleExitLocalTransform(val);

				Gizmo.Draw.Color = Color.Cyan;
				Gizmo.Draw.Arrow(transform.Position, transform.Position + transform.Forward * 100.0f);

				transform = GetRectangleExitLocalTransform(val, true);

				Gizmo.Draw.Color = Color.Green;
				Gizmo.Draw.Arrow(transform.Position, transform.Position + transform.Forward * 100.0f);
			}
		}
	}



	private void BuildAllMeshes()
	{
		BuildRoad();
		BuildSidewalk();
	}



	private void BuildRoad()
	{
		if (Shape == IntersectionShape.Rectangle)
			BuildRectangleRoad();
		else
			BuildCircleRoad();
	}



	private void BuildSidewalk()
	{
		if (SidewalkWidth <= 0 || SidewalkHeight <= 0)
			return;

		if (Shape == IntersectionShape.Rectangle)
			BuildRectangleSidewalk();
		else
			BuildCircleSidewalk();
	}



	[Button("Snap Nearby Roads (WIP)"), Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle), Order(10)]
	public void SnapNearbyRoads()
	{
		var roads = Scene.GetAll<RoadComponent>().ToList();

		const float snapDistance = 300.0f;

		foreach (RectangleExit side in System.Enum.GetValues<RectangleExit>())
		{
			if (side == RectangleExit.None || !RectangleExits.HasFlag(side))
				continue;

			Transform exitTransform = GetRectangleExitTransform(side, true);

			foreach (RoadComponent road in roads)
			{
				// Check start point of the road
				if (Vector3.DistanceBetween(road.WorldPosition, exitTransform.Position) < snapDistance)
				{
					road.WorldPosition = exitTransform.Position;

					road.RoadWidth = side is RectangleExit.North or RectangleExit.South ? Width : Length;
				}
			}
		}
	}
}
