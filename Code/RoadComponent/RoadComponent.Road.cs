using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	[Property(Title = "🔒 Locked"), Feature("Road")] private bool IsRoadLocked { get; set; } = false;
	[Property(Title = "Material"), Feature("Road", Icon = "fork_left", Tint = EditorTint.Green)] private Material RoadMaterial { get; set { field = value; IsDirty = true; } }
	[Property(Title = "Width"), Feature("Road"), Range(10.0f, 1000.0f)] public float RoadWidth { get; set { field = value; IsDirty = true; } } = 500.0f;
	[Property(Title = "Precision"), Feature("Road"), Range(10.0f, 100.0f)] private float RoadPrecision { get; set { field = value.Clamp(1.0f, 10000.0f); IsDirty = true; } } = 40.0f;
	[Property(Title = "Texture Repeat"), Feature("Road")] private float RoadTextureInchesPerRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 500.0f;



	private void BuildRoadMesh()
	{
		GetSplineFrameData(out var frames, out var segmentsToKeep);

		if (segmentsToKeep.Count < 2)
			return;

		var polygonMesh = new PolygonMesh();
		var material = RoadMaterial ?? Material.Load("materials/dev/reflectivity_30.vmat");
		float halfWidth = RoadWidth * 0.5f;
		var frameVertices = new HalfEdgeMesh.VertexHandle[segmentsToKeep.Count][];

		for (int i = 0; i < segmentsToKeep.Count; i++)
		{
			Transform frame = frames[segmentsToKeep[i]];
			Vector3 p = frame.Position;
			Vector3 right = frame.Rotation.Right;
			Vector3 l = p - right * halfWidth;
			Vector3 r = p + right * halfWidth;

			frameVertices[i] = polygonMesh.AddVertices(l, r);
		}

		for (int i = 0; i < segmentsToKeep.Count - 1; i++)
		{
			int idx0 = segmentsToKeep[i];
			int idx1 = segmentsToKeep[i + 1];

			Transform f0 = frames[idx0];
			Transform f1 = frames[idx1];

			Vector3 p0 = f0.Position;
			Vector3 p1 = f1.Position;

			Vector3 right0 = f0.Rotation.Right;
			Vector3 right1 = f1.Rotation.Right;

			Vector3 l0 = p0 - right0 * halfWidth;
			Vector3 r0 = p0 + right0 * halfWidth;
			Vector3 l1 = p1 - right1 * halfWidth;
			Vector3 r1 = p1 + right1 * halfWidth;

			Vector2 uv00 = new Vector2(l0.x, l0.y) / RoadTextureInchesPerRepeat;
			Vector2 uv10 = new Vector2(r0.x, r0.y) / RoadTextureInchesPerRepeat;
			Vector2 uv11 = new Vector2(r1.x, r1.y) / RoadTextureInchesPerRepeat;
			Vector2 uv01 = new Vector2(l1.x, l1.y) / RoadTextureInchesPerRepeat;

			MeshUtility.AddTexturedQuad(polygonMesh, material, frameVertices[i][0], frameVertices[i][1], frameVertices[i + 1][1], frameVertices[i + 1][0], uv00, uv10, uv11, uv01);
		}

		CreateRoadMeshComponent(polygonMesh);
	}



	private void CreateRoadMeshComponent(PolygonMesh _PolygonMesh)
	{
		var child = new GameObject(GameObject, true, "Road");
		child.Tags.Add(RoadMeshTag);
		child.Tags.Add(RoadSurfaceTag);

		var meshComponent = child.AddComponent<MeshComponent>();
		meshComponent.Mesh = _PolygonMesh;
		meshComponent.SmoothingAngle = 40.0f;
	}



	private void EnsureRoadMeshExist()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		if (IsRoadLocked)
			return;

		if (HasGeneratedMeshChildren(RoadSurfaceTag))
			return;

		BuildRoadMesh();
	}
}
