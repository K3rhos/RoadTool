using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	[Property(Title = "🔒 Locked"), Feature("Sidewalk")] private bool IsSidewalkLocked { get; set; } = false;
	[Property, FeatureEnabled("Sidewalk", Icon = "directions_walk", Tint = EditorTint.Blue)] private bool HasSidewalk { get; set { field = value; IsDirty = true; } } = true;
	[Property(Title = "Material"), Feature("Sidewalk")] private Material SidewalkMaterial { get; set { field = value; IsDirty = true; } }
	[Property(Title = "Width"), Feature("Sidewalk"), Range(10.0f, 500.0f)] private float SidewalkWidth { get; set { field = value; IsDirty = true; } } = 150.0f;
	[Property(Title = "Height"), Feature("Sidewalk"), Range(0.1f, 100.0f)] private float SidewalkHeight { get; set { field = value; IsDirty = true; } } = 5.0f;
	[Property(Title = "Texture Repeat"), Feature("Sidewalk")] private float SidewalkTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 200.0f;



	private void BuildSidewalkMesh()
	{
		if (!HasSidewalk)
			return;

		GetSplineFrameData(out var frames, out var segmentsToKeep);

		if (segmentsToKeep.Count < 2)
			return;

		var polygonMesh = new PolygonMesh();
		var material = SidewalkMaterial ?? Material.Load("materials/dev/reflectivity_70.vmat");
		var frameVertices = new HalfEdgeMesh.VertexHandle[segmentsToKeep.Count][];

		float roadEdgeOffset = RoadWidth * 0.5f;

		float leftInnerEdge = -roadEdgeOffset;
		float leftOuterEdge = -(roadEdgeOffset + SidewalkWidth);

		float rightInnerEdge = roadEdgeOffset;
		float rightOuterEdge = roadEdgeOffset + SidewalkWidth;

		float leftAvgUVDist = 0f;
		float rightAvgUVDist = 0f;

		for (int i = 0; i < segmentsToKeep.Count; i++)
		{
			Transform frame = frames[segmentsToKeep[i]];
			Vector3 u = frame.Rotation.Up;
			Vector3 r = frame.Rotation.Right;
			Vector3 p = frame.Position;

			Vector3 lb = p + r * leftInnerEdge;
			Vector3 lo = p + r * leftOuterEdge;
			Vector3 lt = lb + u * SidewalkHeight;
			Vector3 lto = lo + u * SidewalkHeight;

			Vector3 rb = p + r * rightInnerEdge;
			Vector3 ro = p + r * rightOuterEdge;
			Vector3 rt = rb + u * SidewalkHeight;
			Vector3 rto = ro + u * SidewalkHeight;

			frameVertices[i] = polygonMesh.AddVertices(lb, lo, lt, lto, rb, ro, rt, rto);
		}

		for (int i = 0; i < segmentsToKeep.Count - 1; i++)
		{
			int idx0 = segmentsToKeep[i];
			int idx1 = segmentsToKeep[i + 1];

			float v2 = SidewalkHeight / SidewalkTextureRepeat;

			Transform f0 = frames[idx0];
			Transform f1 = frames[idx1];

			Vector3 r0 = f0.Rotation.Right;
			Vector3 r1 = f1.Rotation.Right;

			Vector3 p0 = f0.Position;
			Vector3 p1 = f1.Position;

			// Left sidewalk positions
			Vector3 lb0 = p0 + r0 * leftInnerEdge;
			Vector3 lb1 = p1 + r1 * leftInnerEdge;

			Vector3 lo0 = p0 + r0 * leftOuterEdge;
			Vector3 lo1 = p1 + r1 * leftOuterEdge;

			// Right sidewalk positions
			Vector3 rb0 = p0 + r0 * rightInnerEdge;
			Vector3 rb1 = p1 + r1 * rightInnerEdge;

			Vector3 ro0 = p0 + r0 * rightOuterEdge;
			Vector3 ro1 = p1 + r1 * rightOuterEdge;

			float leftInnerLen3D = Vector3.DistanceBetween(lb0, lb1);
			float leftOuterLen3D = Vector3.DistanceBetween(lo0, lo1);
			float rightInnerLen3D = Vector3.DistanceBetween(rb0, rb1);
			float rightOuterLen3D = Vector3.DistanceBetween(ro0, ro1);

			float leftAvgV0 = leftAvgUVDist;
			float rightAvgV0 = rightAvgUVDist;

			leftAvgUVDist += ((leftInnerLen3D + leftOuterLen3D) * 0.5f) / SidewalkTextureRepeat;
			rightAvgUVDist += ((rightInnerLen3D + rightOuterLen3D) * 0.5f) / SidewalkTextureRepeat;

			float leftAvgV1 = leftAvgUVDist;
			float rightAvgV1 = rightAvgUVDist;

			MeshUtility.AddTexturedQuad(
				polygonMesh,
				material,
				frameVertices[i][2], frameVertices[i + 1][2], frameVertices[i + 1][3], frameVertices[i][3],
				new Vector2(0, leftAvgV0), new Vector2(0, leftAvgV1), new Vector2(1, leftAvgV1), new Vector2(1, leftAvgV0));

			MeshUtility.AddTexturedQuad(
				polygonMesh,
				material,
				frameVertices[i][0], frameVertices[i + 1][0], frameVertices[i + 1][2], frameVertices[i][2],
				new Vector2(v2, leftAvgV0), new Vector2(v2, leftAvgV1), new Vector2(0, leftAvgV1), new Vector2(0, leftAvgV0));

			MeshUtility.AddTexturedQuad(
				polygonMesh,
				material,
				frameVertices[i][1], frameVertices[i][3], frameVertices[i + 1][3], frameVertices[i + 1][1],
				new Vector2(1 - v2, 1 - leftAvgV0), new Vector2(1, 1 - leftAvgV0), new Vector2(1, 1 - leftAvgV1), new Vector2(1 - v2, 1 - leftAvgV1));

			MeshUtility.AddTexturedQuad(
				polygonMesh,
				material,
				frameVertices[i][6], frameVertices[i][7], frameVertices[i + 1][7], frameVertices[i + 1][6],
				new Vector2(0, rightAvgV0), new Vector2(1, rightAvgV0), new Vector2(1, rightAvgV1), new Vector2(0, rightAvgV1));

			MeshUtility.AddTexturedQuad(
				polygonMesh,
				material,
				frameVertices[i][4], frameVertices[i][6], frameVertices[i + 1][6], frameVertices[i + 1][4],
				new Vector2(v2, rightAvgV0), new Vector2(0, rightAvgV0), new Vector2(0, rightAvgV1), new Vector2(v2, rightAvgV1));

			MeshUtility.AddTexturedQuad(
				polygonMesh,
				material,
				frameVertices[i][5], frameVertices[i + 1][5], frameVertices[i + 1][7], frameVertices[i][7],
				new Vector2(1 - v2, 1 - rightAvgV0), new Vector2(1 - v2, 1 - rightAvgV1), new Vector2(1, 1 - rightAvgV1), new Vector2(1, 1 - rightAvgV0));
		}

		CreateSidewalkMeshComponent(polygonMesh);
	}



	private void CreateSidewalkMeshComponent(PolygonMesh _PolygonMesh)
	{
		var child = new GameObject(GameObject, true, "Sidewalk");
		child.Tags.Add(RoadMeshTag);
		child.Tags.Add(SidewalkSurfaceTag);

		var meshComponent = child.AddComponent<MeshComponent>();
		meshComponent.Mesh = _PolygonMesh;
		meshComponent.SmoothingAngle = 40.0f;
	}



	private void EnsureSidewalkMeshExist()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		if (IsSidewalkLocked)
			return;

		if (HasGeneratedMeshChildren(SidewalkSurfaceTag))
			return;

		BuildSidewalkMesh();
	}
}
