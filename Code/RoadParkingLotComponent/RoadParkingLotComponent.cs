using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// Generates parking lot lines for parking spaces
/// </summary>
[Icon("local_parking")]
public partial class RoadParkingLotComponent : Component, Component.ExecuteInEditor
{
	private MeshBuilder m_MeshBuilder;

	/// <summary>
	/// An optional prefab, if non-empty the parking lot will generate a bunch of child gameobjects positioned at each parking spots center.
	/// (e.g. this allows you to use a gameobject prefab with a car spawner system component attached to it)
	/// </summary>
	[Property, Feature("General", Icon = "public", Tint = EditorTint.White)] private GameObject SpotPrefab { get; set; }

	/// <summary>
	/// The amount of parking spots you want to generate.
	/// </summary>
	[Property, Feature("General"), Range(1, 50)] private int SpotCount { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 10;

	/// <summary>
	/// Well that's the parking spot length
	/// </summary>
	[Property, Feature("General"), Range(10.0f, 1000.0f)] private float SpotLength { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 250.0f;

	/// <summary>
	/// and width...
	/// </summary>
	[Property, Feature("General"), Range(10.0f, 1000.0f)] private float SpotWidth { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 150.0f;

	/// <summary>
	/// The angle of the parking spots in degrees (0 = perpendicular, 45 = angled, 90 = parallel)
	/// </summary>
	[Property, Feature("General"), Range(-90.0f, 90.0f), Step(1.0f)] private float SpotAngle { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 0.0f;
	[Property, Feature("General"), Range(0.5f, 1.0f)] private float SpotAngleThreshold { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 0.5f;



	protected override void OnEnabled()
	{
		m_MeshBuilder = new MeshBuilder(GameObject)
		{
			CastShadows = false
		};

		m_MeshBuilder.OnBuild += BuildAllMeshes;
		m_MeshBuilder.Rebuild();
	}



	protected override void OnDisabled()
	{
		m_MeshBuilder?.OnBuild -= BuildAllMeshes;
		m_MeshBuilder?.Clear();

		RemoveParkingSpots();
	}



	protected override void OnUpdate()
	{
		m_MeshBuilder?.Update();
	}



	private void BuildAllMeshes()
	{
		BuildParkingLines();
		BuildCurbs();

		RemoveParkingSpots();
		CreateParkingSpots();
	}



	protected override void DrawGizmos()
	{
		if (!Gizmo.IsSelected)
			return;

		Gizmo.Draw.LineThickness = 2.0f;
		Gizmo.Draw.Color = Color.Green.WithAlpha(0.5f);

		float angleRad = SpotAngle.DegreeToRadian();
		float sinAngle = float.Sin(angleRad);
		float cosAngle = float.Cos(angleRad);

		float spacing = CalculateSpacing();

		// Draw parking spot outlines
		for (int i = 0; i < SpotCount; i++)
		{
			float xPos = i * spacing;

			Vector3 frontLeft = new Vector3(xPos, 0, LinesOffset);
			Vector3 frontRight = new Vector3(xPos + SpotWidth * cosAngle, SpotWidth * sinAngle, LinesOffset);
			Vector3 backLeft = new Vector3(xPos - SpotLength * sinAngle, SpotLength * cosAngle, LinesOffset);
			Vector3 backRight = new Vector3(xPos + SpotWidth * cosAngle - SpotLength * sinAngle, SpotWidth * sinAngle + SpotLength * cosAngle, LinesOffset);

			Gizmo.Draw.Line(frontLeft, frontRight);
			Gizmo.Draw.Line(frontRight, backRight);
			Gizmo.Draw.Line(backRight, backLeft);
			Gizmo.Draw.Line(backLeft, frontLeft);
		}
	}



	private void CreateParkingSpots()
	{
		if (!SpotPrefab.IsValid())
			return;

		GameObject containerObject = GameObject.Children.FirstOrDefault(x => x.Name == "ParkingSpots");

		if (!containerObject.IsValid())
			containerObject = new GameObject(GameObject, true, "ParkingSpots");

		float angleRad = SpotAngle.DegreeToRadian();
		float sinAngle = float.Sin(angleRad);
		float cosAngle = float.Cos(angleRad);

		float spacing = CalculateSpacing();

		for (int i = 0; i < SpotCount; i++)
		{
			float xPos = i * spacing;

			float centerX = xPos + (SpotWidth * 0.5f * cosAngle) - (SpotLength * 0.5f * sinAngle);
			float centerY = (SpotWidth * 0.5f * sinAngle) + (SpotLength * 0.5f * cosAngle);

			Vector3 position = new Vector3(centerX, centerY, 0);

			GameObject gameObject = SpotPrefab.Clone(new Transform(), containerObject);
			gameObject.LocalPosition = position;
			gameObject.LocalRotation = Rotation.FromYaw(SpotAngle);
		}
	}



	private void RemoveParkingSpots()
	{
		GameObject containerObject = GameObject.Children.FirstOrDefault(x => x.Name == "ParkingSpots");

		if (!containerObject.IsValid())
			return;

		foreach (var gameObject in containerObject.Children.Where(x => x.IsValid()))
		{
			gameObject.Destroy();
		}
	}



	/// <summary>
	/// Utility button to directly snap the parking lot to the nearest solid ground
	/// </summary>
	[Button("Snap to Ground"), Feature("General"), Order(100)]
	public void SnapToGround()
	{
		SceneTraceResult trace = Scene.Trace.Ray(WorldPosition, WorldPosition + Vector3.Down * 10000.0f).Run();

		if (trace.Distance < 0.1f) // Ignore really close hits, bcs that mean the parking lot is already properly grounded
			return;

		if (!trace.Hit)
			return;

		WorldPosition = trace.HitPosition;
		WorldRotation = Rotation.LookAt(trace.Normal, Vector3.Up) * Rotation.FromPitch(90.0f);
	}



	private float CalculateSpacing()
	{
		float angleRad = SpotAngle.DegreeToRadian();
		float cosAngle = float.Cos(angleRad);

		return SpotWidth / float.Max(cosAngle, SpotAngleThreshold);
	}
}
