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

	[Property, FeatureEnabled("Bridge", Icon = "architecture", Tint = EditorTint.Blue)]
	private bool HasBridge { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = false;

	/// <summary>
	/// This will prevent the bridge from being rebuilt if any property is edited or if the road component get disable and re-enabled.
	/// Really useful if you plan to edit the mesh with the mapping tool so you don't accidently erase/rebuild the bridge.
	/// </summary>
	[Property(Title = "🔒 Locked"), Feature("Bridge")]
	private bool IsLocked { get; set; } = false;

	/// <summary>
	/// This is your bridge material you wanna use.
	/// I recommend using a tileable texture for better result.
	/// </summary>
	[Property(Title = "Material"), Feature("Bridge"), Group("Texturing"), Order(1)]
	private Material BridgeMaterial { get; set { field = value; m_DoesBridgeNeedRebuild = true; } }

	[Property(Title = "Texture Repeat"), Feature("Bridge"), Group("Texturing"), Order(1), Step(1)]
	private float BridgeTextureRepeat { get; set { field = value.Clamp(10.0f, 10000.0f); m_DoesBridgeNeedRebuild = true; } } = 500.0f;

	[Property(Title = "Border Width"), Feature("Bridge"), Group("Shape"), Order(0), Range(10.0f, 500.0f)]
	private float BridgeBorderWidth { get; set { field = value.Clamp(10.0f, 500.0f); m_DoesBridgeNeedRebuild = true; } } = 80.0f;

	[Property(Title = "Border Height"), Feature("Bridge"), Group("Shape"), Range(10.0f, 500.0f)]
	private float BridgeBorderHeight { get; set { field = value.Clamp(10.0f, 500.0f); m_DoesBridgeNeedRebuild = true; } } = 80.0f;

	[Property(Title = "Bottom Depth"), Feature("Bridge"), Group("Shape"), Range(10.0f, 500.0f)]
	private float BridgeBottomDepth { get; set { field = value.Clamp(10.0f, 500.0f); m_DoesBridgeNeedRebuild = true; } } = 80.0f;

	[Property(Title = "Close Caps"), Feature("Bridge"), Group("Shape")]
	private bool BridgeCloseCaps { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = true;

	[Property(Title = "Pillars"), Feature("Bridge"), ToggleGroup("Pillars"), Order(2)]
	private bool Pillars { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = true;

	[Property(Title = "Shape"), Feature("Bridge"), ToggleGroup("Pillars")]
	private BridgePillarShape BridgePillarType { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = BridgePillarShape.Square;

	[Property(Title = "Size"), Feature("Bridge"), ToggleGroup("Pillars"), Range(10.0f, 1000.0f)]
	private float BridgePillarSize { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = 200.0f;

	[Property(Title = "Height"), Feature("Bridge"), ToggleGroup("Pillars"), Range(10.0f, 5000.0f)]
	private float BridgePillarHeight { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = 600.0f;

	[Property(Title = "Spacing"), Feature("Bridge"), ToggleGroup("Pillars"), Range(100.0f, 10000.0f)]
	private float BridgePillarSpacing { get; set { field = value.Clamp(100.0f, 100000.0f); m_DoesBridgeNeedRebuild = true; } } = 1200.0f;

	[Property(Title = "Inset"), Feature("Bridge"), ToggleGroup("Pillars"), Range(0.0f, 200.0f)]
	private float BridgePillarInset { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = 20.0f;

	[Property(Title = "Segments"), Feature("Bridge"), ToggleGroup("Pillars"), Range(3, 24), ShowIf(nameof(BridgePillarType), BridgePillarShape.Cylinder)]
	private int BridgePillarRoundSegments { get; set { field = value.Clamp(3, 64); m_DoesBridgeNeedRebuild = true; } } = 12;

	/// <summary>
	/// Does the pillars follow world up vector or follow the road shape ?
	/// </summary>
	[Property(Title = "Keep Vertical (World Up)"), Feature("Bridge"), ToggleGroup("Pillars")]
	private bool BridgePillarsKeepVertical { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = true;
	
	[Property, FeatureEnabled("Crosswalks", Icon = "menu", Tint = EditorTint.Pink), Change] private bool HasCrosswalks { get; set; } = false;
	[Property(Title = "Config"), Feature("Crosswalks")] public CrosswalkConfig CrosswalkConfig { get; set { field = value; m_DoesCrosswalksNeedsRebuild = true; } } = CrosswalkConfig.Both;
	[Property(Title = "Decal Definition"), Feature("Crosswalks")] public DecalDefinition CrosswalkDefinition { get; set { field = value; m_DoesCrosswalksNeedsRebuild = true; } }
	[Property(Title = "Decal Size"), Feature("Crosswalks"), Range(0.1f, 10.0f)] private Vector2 CrosswalkSize { get; set { field = value; m_DoesCrosswalksNeedsRebuild = true; } } = Vector2.One;
	
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
