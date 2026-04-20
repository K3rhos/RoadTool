using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadParkingLotComponent
{
	[Property, FeatureEnabled("Curbs", Icon = "block", Tint = EditorTint.Pink), Change] private bool HasCurbs { get; set; } = false;
	[Property, Feature("Curbs")] private Material CurbsMaterial { get; set { field = value; m_IsDirty = true; } }
	[Property, Feature("Curbs"), Range(1, 5)] private int CurbsSegments { get; set { field = value; m_IsDirty = true; } } = 3;
	[Property, Feature("Curbs"), Range(0.1f, 1.0f)] private float CurbsFillRatio { get; set { field = value; m_IsDirty = true; } } = 0.667f; // 2/3 of the SpotWidth
	[Property, Feature("Curbs"), Range(1.0f, 100.0f)] private float CurbsHeight { get; set { field = value; m_IsDirty = true; } } = 8.0f;
	[Property, Feature("Curbs"), Range(1.0f, 100.0f)] private float CurbsDepth { get; set { field = value; m_IsDirty = true; } } = 12.0f;
	[Property, Feature("Curbs"), Range(-50.0f, 50.0f)] private float CurbsOffset { get; set { field = value; m_IsDirty = true; } } = 6.0f;
	[Property(Title = "Texture Repeat"), Feature("Curbs")] private float CurbsTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); m_IsDirty = true; } } = 10.0f;



	private void OnHasCurbsChanged(bool _OldValue, bool _NewValue)
	{
		m_IsDirty = true;
	}



	private void BuildCurbs()
	{
		if (!HasCurbs || SpotCount <= 0)
			return;

		var material = CurbsMaterial ?? Material.Load("materials/dev/reflectivity_90.vmat");
		var mesh = new PolygonMesh();
		var cache = new Dictionary<Vector3, HalfEdgeMesh.VertexHandle>();

		float spacing = CalculateSpacing();

		for (int i = 0; i < SpotCount; i++)
		{
			float xCenter = (i * spacing) + (SpotWidth * 0.5f);

			if (CurbsSegments <= 1)
				DrawSimpleCurb(mesh, material, cache, xCenter);
			else
				DrawBeveledCurb(mesh, material, cache, xCenter);
		}

		var child = new GameObject(GameObject, true, "ParkingCurbs");
		child.Tags.Add(CurbsTag);

		var meshComponent = child.AddComponent<MeshComponent>();
		meshComponent.Mesh = mesh;
		meshComponent.SmoothingAngle = 40.0f;
	}



	private void DrawSimpleCurb(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, float _CenterX)
	{
		Vector3 up = Vector3.Up;

		float angleRad = SpotAngle.DegreeToRadian();
		float sinAngle = float.Sin(angleRad);
		float cosAngle = float.Cos(angleRad);

		float curbWidth = SpotWidth * CurbsFillRatio;
		float halfW = curbWidth * 0.5f;
		float halfD = CurbsDepth * 0.5f;
		float h = CurbsHeight;

		float spotX = _CenterX - (SpotWidth * 0.5f);
		float backOffsetFromFront = SpotLength - halfD - CurbsOffset;
		float curbCenterX = spotX + (SpotWidth * 0.5f * cosAngle) - (backOffsetFromFront * sinAngle);
		float curbCenterY = (SpotWidth * 0.5f * sinAngle) + (backOffsetFromFront * cosAngle);

		Vector3 perpDir = new Vector3(cosAngle, sinAngle, 0);
		Vector3 lengthDir = new Vector3(-sinAngle, cosAngle, 0);
		Vector3 center = new Vector3(curbCenterX, curbCenterY, 0);

		float uW = curbWidth / CurbsTextureRepeat;
		float uD = CurbsDepth / CurbsTextureRepeat;
		float uH = CurbsHeight / CurbsTextureRepeat;

		Vector3 fbl = center - perpDir * halfW - lengthDir * halfD;
		Vector3 fbr = center + perpDir * halfW - lengthDir * halfD;
		Vector3 ftl = fbl + up * h;
		Vector3 ftr = fbr + up * h;
		Vector3 bbl = center - perpDir * halfW + lengthDir * halfD;
		Vector3 bbr = center + perpDir * halfW + lengthDir * halfD;
		Vector3 btl = bbl + up * h;
		Vector3 btr = bbr + up * h;

		var vFbl = MeshUtility.GetOrAddVertex(_Mesh, _Cache, fbl);
		var vFbr = MeshUtility.GetOrAddVertex(_Mesh, _Cache, fbr);
		var vFtl = MeshUtility.GetOrAddVertex(_Mesh, _Cache, ftl);
		var vFtr = MeshUtility.GetOrAddVertex(_Mesh, _Cache, ftr);
		var vBbl = MeshUtility.GetOrAddVertex(_Mesh, _Cache, bbl);
		var vBbr = MeshUtility.GetOrAddVertex(_Mesh, _Cache, bbr);
		var vBtl = MeshUtility.GetOrAddVertex(_Mesh, _Cache, btl);
		var vBtr = MeshUtility.GetOrAddVertex(_Mesh, _Cache, btr);

		// Top face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vFtl, vFtr, vBtr, vBtl,
			new Vector2(0, 0), new Vector2(uW, 0), new Vector2(uW, uD), new Vector2(0, uD));

		// Front face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vFbl, vFbr, vFtr, vFtl,
			new Vector2(0, 0), new Vector2(uW, 0), new Vector2(uW, uH), new Vector2(0, uH));

		// Back face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vBbr, vBbl, vBtl, vBtr,
			new Vector2(0, 0), new Vector2(uW, 0), new Vector2(uW, uH), new Vector2(0, uH));

		// Left face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vBbl, vFbl, vFtl, vBtl,
			new Vector2(0, 0), new Vector2(uD, 0), new Vector2(uD, uH), new Vector2(0, uH));

		// Right face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vFbr, vBbr, vBtr, vFtr,
			new Vector2(0, 0), new Vector2(uD, 0), new Vector2(uD, uH), new Vector2(0, uH));
	}



	private void DrawBeveledCurb(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, float _CenterX)
	{
		Vector3 up = Vector3.Up;

		float angleRad = SpotAngle * MathF.PI / 180.0f;
		float sinAngle = float.Sin(angleRad);
		float cosAngle = float.Cos(angleRad);

		float curbWidth = SpotWidth * CurbsFillRatio;
		float halfW = curbWidth * 0.5f;
		float halfD = CurbsDepth * 0.5f;
		float h = CurbsHeight;

		float spotX = _CenterX - (SpotWidth * 0.5f);
		float backOffsetFromFront = SpotLength - halfD - CurbsOffset;
		float curbCenterX = spotX + (SpotWidth * 0.5f * cosAngle) - (backOffsetFromFront * sinAngle);
		float curbCenterY = (SpotWidth * 0.5f * sinAngle) + (backOffsetFromFront * cosAngle);

		Vector3 perpDir = new Vector3(cosAngle, sinAngle, 0);
		Vector3 lengthDir = new Vector3(-sinAngle, cosAngle, 0);
		Vector3 center = new Vector3(curbCenterX, curbCenterY, 0);

		// Define 4 corners of a classic curb profile
		var anchors = new[]
		{
			new Vector2(-halfD, 0),          // Front bottom
			new Vector2(-halfD * 0.5f, h),   // Front top
			new Vector2(halfD * 0.5f, h),    // Back top
			new Vector2(halfD, 0)            // Back bottom
		};

		// Generate the profile based on segments
		var profile = new Vector2[CurbsSegments + 1];

		for (int i = 0; i <= CurbsSegments; i++)
		{
			float t = (float)i / CurbsSegments;

			if (t <= 0.333f)
				profile[i] = Vector2.Lerp(anchors[0], anchors[1], t / 0.333f);
			else if (t <= 0.666f)
				profile[i] = Vector2.Lerp(anchors[1], anchors[2], (t - 0.333f) / 0.333f);
			else
				profile[i] = Vector2.Lerp(anchors[2], anchors[3], (t - 0.666f) / 0.334f);
		}

		float uW = curbWidth / CurbsTextureRepeat;

		// Body — one quad per profile segment, shared vertices across adjacent segments
		for (int i = 0; i < CurbsSegments; i++)
		{
			Vector2 p1 = profile[i];
			Vector2 p2 = profile[i + 1];

			Vector3 bl = center - perpDir * halfW + lengthDir * p1.x + up * p1.y;
			Vector3 br = center + perpDir * halfW + lengthDir * p1.x + up * p1.y;
			Vector3 tr = center + perpDir * halfW + lengthDir * p2.x + up * p2.y;
			Vector3 tl = center - perpDir * halfW + lengthDir * p2.x + up * p2.y;

			float v1 = (p1.x + halfD) / CurbsTextureRepeat;
			float v2 = (p2.x + halfD) / CurbsTextureRepeat;

			var vBl = MeshUtility.GetOrAddVertex(_Mesh, _Cache, bl);
			var vBr = MeshUtility.GetOrAddVertex(_Mesh, _Cache, br);
			var vTr = MeshUtility.GetOrAddVertex(_Mesh, _Cache, tr);
			var vTl = MeshUtility.GetOrAddVertex(_Mesh, _Cache, tl);

			MeshUtility.AddTexturedQuad(_Mesh, _Material, vBl, vBr, vTr, vTl,
				new Vector2(0, v1), new Vector2(uW, v1), new Vector2(uW, v2), new Vector2(0, v2));
		}

		// End caps — fan of triangles from the center of each cap face
		Vector3 centerL = center - perpDir * halfW;
		Vector3 centerR = center + perpDir * halfW;

		for (int i = 0; i < CurbsSegments; i++)
		{
			Vector3 v1L = centerL + lengthDir * profile[i].x     + up * profile[i].y;
			Vector3 v2L = centerL + lengthDir * profile[i + 1].x + up * profile[i + 1].y;
			Vector3 v1R = centerR + lengthDir * profile[i].x     + up * profile[i].y;
			Vector3 v2R = centerR + lengthDir * profile[i + 1].x + up * profile[i + 1].y;

			Vector2 uvC  = new Vector2(0.5f, 0) * (CurbsDepth / CurbsTextureRepeat);
			Vector2 uv1  = new Vector2((profile[i].x     + halfD) / CurbsTextureRepeat, profile[i].y     / CurbsTextureRepeat);
			Vector2 uv2  = new Vector2((profile[i + 1].x + halfD) / CurbsTextureRepeat, profile[i + 1].y / CurbsTextureRepeat);

			var vCL  = MeshUtility.GetOrAddVertex(_Mesh, _Cache, centerL);
			var vV1L = MeshUtility.GetOrAddVertex(_Mesh, _Cache, v1L);
			var vV2L = MeshUtility.GetOrAddVertex(_Mesh, _Cache, v2L);
			var vCR  = MeshUtility.GetOrAddVertex(_Mesh, _Cache, centerR);
			var vV1R = MeshUtility.GetOrAddVertex(_Mesh, _Cache, v1R);
			var vV2R = MeshUtility.GetOrAddVertex(_Mesh, _Cache, v2R);

			MeshUtility.AddTexturedTriangle(_Mesh, _Material, vCL, vV1L, vV2L, uvC, uv1, uv2);
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, vCR, vV2R, vV1R, uvC, uv2, uv1);
		}
	}
}
