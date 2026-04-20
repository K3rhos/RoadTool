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
	private bool m_IsDirty;

	private const string IntersectionRoadTag = "intersection_road";
	private const string IntersectionSidewalkTag = "intersection_sidewalk";

	[Property, Feature("General", Icon = "public", Tint = EditorTint.White), Order(0)] private IntersectionShape Shape { get; set { field = value; m_IsDirty = true; } } = IntersectionShape.Rectangle;

	[Property(Title = "Material"), Feature("General"), Order(0)] private Material RoadMaterial { get; set { field = value; m_IsDirty = true; } }
	[Property(Title = "Texture Repeat"), Feature("General"), Order(0)] private float RoadTextureRepeat { get; set { field = value; m_IsDirty = true; } } = 500.0f;

	[Property(Title = "Material"), Feature("General"), Category("Sidewalk"), Order(3)] private Material SidewalkMaterial { get; set { field = value; m_IsDirty = true; } }
	[Property(Title = "Width"), Feature("General"), Category("Sidewalk"), Order(3)] private float SidewalkWidth { get; set { field = value; m_IsDirty = true; } } = 150.0f;
	[Property(Title = "Height"), Feature("General"), Category("Sidewalk"), Order(3)] private float SidewalkHeight { get; set { field = value; m_IsDirty = true; } } = 5.0f;
	[Property(Title = "Texture Repeat"), Feature("General"), Category("Sidewalk"), Order(3)] private float SidewalkTextureRepeat { get; set { field = value; m_IsDirty = true; } } = 200.0f;



	protected override void OnEnabled()
	{
		BuildAllMeshes();
		CreateTrafficLights();
	}



	protected override void OnDisabled()
	{
		DestroyMeshChildren();
		RemoveTrafficLights();
	}



	protected override void OnUpdate()
	{
		if (m_IsDirty)
		{
			if (!SandboxUtility.IsInPlayMode)
			{
				DestroyMeshChildren();
				BuildAllMeshes();
			}

			m_IsDirty = false;
		}

		UpdateTrafficLights();
	}



	protected override void DrawGizmos()
	{
		if (!Gizmo.IsSelected)
			return;

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



	private void DestroyMeshChildren()
	{
		var toRemove = GameObject.Children
			.Where(c => c.Tags.Has(IntersectionRoadTag) || c.Tags.Has(IntersectionSidewalkTag))
			.ToList();

		foreach (var child in toRemove)
			child.Destroy();
	}



	private void BuildAllMeshes()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		var roadMat = RoadMaterial ?? Material.Load("materials/dev/reflectivity_30.vmat");
		var sidewalkMat = SidewalkMaterial ?? Material.Load("materials/dev/reflectivity_70.vmat");

		var roadMesh = new PolygonMesh();
		BuildRoad(roadMesh, roadMat);

		var roadChild = new GameObject(GameObject, true, "Intersection_Road");
		roadChild.Tags.Add(IntersectionRoadTag);
		var roadMC = roadChild.AddComponent<MeshComponent>();
		roadMC.Mesh = roadMesh;
		roadMC.SmoothingAngle = 40.0f;

		if (SidewalkWidth > 0 && SidewalkHeight > 0)
		{
			var sidewalkMesh = new PolygonMesh();
			BuildSidewalk(sidewalkMesh, sidewalkMat);

			var swChild = new GameObject(GameObject, true, "Intersection_Sidewalk");
			swChild.Tags.Add(IntersectionSidewalkTag);
			var swMC = swChild.AddComponent<MeshComponent>();
			swMC.Mesh = sidewalkMesh;
			swMC.SmoothingAngle = 40.0f;
		}
	}



	private void BuildRoad(PolygonMesh _Mesh, Material _Material)
	{
		if (Shape == IntersectionShape.Rectangle)
			BuildRectangleRoad(_Mesh, _Material);
		else
			BuildCircleRoad(_Mesh, _Material);
	}



	private void BuildSidewalk(PolygonMesh _Mesh, Material _Material)
	{
		if (SidewalkWidth <= 0 || SidewalkHeight <= 0)
			return;

		if (Shape == IntersectionShape.Rectangle)
			BuildRectangleSidewalk(_Mesh, _Material);
		else
			BuildCircleSidewalk(_Mesh, _Material);
	}



	[Button("Snap Nearby Roads"), Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle), Order(10)]
	public void SnapNearbyRoads()
	{
		var roads = Scene.GetAll<RoadComponent>().ToList();

		const float snapDistance = 300.0f;

		foreach (RectangleExit side in System.Enum.GetValues<RectangleExit>())
		{
			if (side == RectangleExit.None || !RectangleExits.HasFlag(side))
				continue;

			Transform exitTransform = GetRectangleExitTransform(side, true);
			float roadWidth = side is RectangleExit.North or RectangleExit.South ? Width : Length;

			foreach (RoadComponent road in roads)
			{
				// Snap start: first spline point is at local origin, so WorldPosition == its world position
				if (Vector3.DistanceBetween(road.WorldPosition, exitTransform.Position) < snapDistance)
				{
					road.WorldPosition = exitTransform.Position;
					road.RoadWidth = roadWidth;
					continue;
				}

				// Snap end: check the last spline point's world position
				if (road.Spline.PointCount > 0)
				{
					int lastIdx = road.Spline.PointCount - 1;
					Vector3 lastWorldPos = road.WorldTransform.PointToWorld(road.Spline.GetPoint(lastIdx).Position);

					if (Vector3.DistanceBetween(lastWorldPos, exitTransform.Position) < snapDistance)
					{
						var point = road.Spline.GetPoint(lastIdx);
						point.Position = road.WorldTransform.PointToLocal(exitTransform.Position);
						road.Spline.UpdatePoint(lastIdx, point);
						road.RoadWidth = roadWidth;
					}
				}
			}
		}
		
		SandboxUtility.ShowEditorNotification("Snapped Nearby Roads Succesfully");
	}
}
