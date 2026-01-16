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

	private MeshBuilder m_MeshBuilder;

	[Property, Feature("General", Icon = "people", Tint = EditorTint.White), Category("Optimization")] private bool AutoSimplify { get; set { field = value; IsDirty = true; } } = false;
	[Property, Feature("General"), Category("Optimization"), Range(0.1f, 10.0f)] private float StraightThreshold { get; set { field = value; IsDirty = true; } } = 1.0f; // Degrees - how straight before merging
	[Property, Feature("General"), Category("Optimization"), Range(2, 50)] private int MinSegmentsToMerge { get; set { field = value; IsDirty = true; } } = 3; // Minimum consecutive straight segments before merging

	[Property, Feature("General"), Category("Miscellaneous")] public bool UseRotationMinimizingFrames { get; set { field = value; IsDirty = true; } }

	private bool IsDirty
	{
		get;
		set
		{
			field = value;

			m_MeshBuilder?.IsDirty = value;
			m_LanesBuilder?.IsDirty = value;
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

		CreateMeshBuilder();
		CreateLanes();
		CreateDecals();
		CreateLampposts();
	}



	protected override void OnDisabled()
	{
		Spline.SplineChanged -= UpdateData;

		RemoveMeshBuilder();
		RemoveLanes();
		RemoveDecals();
		RemoveLampposts();
	}



	protected override void OnUpdate()
	{
		UpdateMeshBuilder();
		UpdateLanes();
		UpdateDecals();
		UpdateLampposts();
	}



	protected override void OnValidate()
	{
		UpdateData();
	}



	private void CreateMeshBuilder()
	{
		m_MeshBuilder = new MeshBuilder(GameObject);
		m_MeshBuilder.OnBuild += BuildAllMeshes;
		m_MeshBuilder.Rebuild();
	}



	private void UpdateMeshBuilder()
	{
		m_MeshBuilder?.Update();
	}



	private void RemoveMeshBuilder()
	{
		m_MeshBuilder?.OnBuild -= BuildAllMeshes;
		m_MeshBuilder?.Clear();
	}



	private void BuildAllMeshes()
	{
		BuildRoad();
		BuildSidewalk();
	}



	private void UpdateData()
	{
		if (Scene.IsEditor)
			IsDirty = true;
	}
}
