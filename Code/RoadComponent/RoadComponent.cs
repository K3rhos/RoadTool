using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// Represents a road component that can be manipulated within the editor and at runtime.
/// </summary>
[Icon("signpost")]
public partial class RoadComponent : Component, Component.ExecuteInEditor, Component.IHasBounds
{
	[Property, Feature("General"), Hide]
	public Spline Spline = new();

	private bool m_DoesRoadMeshNeedRebuild;

	private const string RoadMeshTag = "road_mesh";
	private const string RoadSurfaceTag = "road_surface";
	private const string SidewalkSurfaceTag = "road_sidewalk";
	private const string LineSurfaceTag = "road_lines";

	[Property, Feature("General", Icon = "public", Tint = EditorTint.White), Category("Optimization")] private bool AutoSimplify { get; set { field = value; IsDirty = true; } } = false;
	[Property, Feature("General"), Category("Optimization"), Range(0.1f, 10.0f)] private float StraightThreshold { get; set { field = value; IsDirty = true; } } = 1.0f; // Degrees - how straight before merging
	[Property, Feature("General"), Category("Optimization"), Range(2, 50)] private int MinSegmentsToMerge { get; set { field = value; IsDirty = true; } } = 3; // Minimum consecutive straight segments before merging

	[Property, Feature("General"), Category("Miscellaneous")] public bool UseRotationMinimizingFrames { get; set { field = value; IsDirty = true; } }

	private bool IsDirty
	{
		get;
		set
		{
			field = value;

			m_DoesRoadMeshNeedRebuild = value;
			m_DoesLamppostsNeedRebuild = value;
		}
	}

	public BBox LocalBounds => Spline.Bounds;



	public RoadComponent()
	{
		Spline.InsertPoint(Spline.PointCount, new Spline.Point { Position = new Vector3(0, 0, 0) });
		Spline.InsertPoint(Spline.PointCount, new Spline.Point { Position = new Vector3(1000, 0, 0) });
		Spline.InsertPoint(Spline.PointCount, new Spline.Point { Position = new Vector3(1600, 1000, 0) });
	}



	protected override void OnEnabled()
	{
		Spline.SplineChanged += UpdateData;

		EnsureRoadMeshExist();
		EnsureSidewalkMeshExist();
		EnsureBridgeMeshExist();

		CreateLines();
		CreateDecals();
		CreateLampposts();
		CreateCrosswalks();
	}



	protected override void OnDisabled()
	{
		Spline.SplineChanged -= UpdateData;

		RemoveRoadMesh();
		RemoveSidewalkMesh();
		RemoveLines();
		RemoveDecals();
		RemoveLampposts();
		RemoveCrosswalks();
		RemoveBridge();
	}



	protected override void OnUpdate()
	{
		UpdateRoadMeshes();
		UpdateLines();
		UpdateDecals();
		UpdateLampposts();
		UpdateCrosswalks();
		UpdateBridge();
	}



	private void UpdateRoadMeshes()
	{
		if (!m_DoesRoadMeshNeedRebuild)
			return;

		RebuildRoadMesh();
		RebuildSidewalkMesh();
		RebuildLinesMesh();

		m_DoesRoadMeshNeedRebuild = false;
	}



	private void RebuildRoadMesh()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		if (IsRoadLocked)
			return;

		RemoveGeneratedMeshChildren(RoadSurfaceTag);
		BuildRoadMesh();
	}



	private void RebuildSidewalkMesh()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		if (IsSidewalkLocked)
			return;

		RemoveGeneratedMeshChildren(SidewalkSurfaceTag);
		BuildSidewalkMesh();
	}



	private void RemoveRoadMesh()
	{
		if (IsRoadLocked)
			return;

		RemoveGeneratedMeshChildren(RoadSurfaceTag);
	}



	private void RemoveSidewalkMesh()
	{
		if (IsSidewalkLocked)
			return;

		RemoveGeneratedMeshChildren(SidewalkSurfaceTag);
	}



	private void RemoveGeneratedMeshChildren(string _Tag)
	{
		var toRemove = GameObject.Children.Where(child => child.Tags.Has(_Tag)).ToList();

		foreach (var child in toRemove)
			child.Destroy();
	}



	private bool HasGeneratedMeshChildren(string _Tag)
	{
		return GameObject.Children.Any(child => child.Tags.Has(_Tag));
	}



	private void UpdateData()
	{
		if (Scene.IsEditor)
			IsDirty = true;
	}
}
