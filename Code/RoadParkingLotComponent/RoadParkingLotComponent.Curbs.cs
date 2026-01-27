using System;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadParkingLotComponent
{
	[Property, FeatureEnabled("Curbs", Icon = "block", Tint = EditorTint.Pink), Change] private bool HasCurbs { get; set; } = false;
	[Property, Feature("Curbs")] private Material CurbsMaterial { get; set { field = value; m_MeshBuilder?.IsDirty = true; } }
	[Property, Feature("Curbs"), Range(1, 5)] private int CurbsSegments { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 3;
	[Property, Feature("Curbs"), Range(0.1f, 1.0f)] private float CurbsFillRatio { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 0.667f; // 2/3 of the SpotWidth
	[Property, Feature("Curbs"), Range(1.0f, 100.0f)] private float CurbsHeight { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 8.0f;
	[Property, Feature("Curbs"), Range(1.0f, 100.0f)] private float CurbsDepth { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 12.0f;
	[Property, Feature("Curbs"), Range(-50.0f, 50.0f)] private float CurbsOffset { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 6.0f;
	[Property(Title = "Texture Repeat"), Feature("Curbs")] private float CurbsTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); m_MeshBuilder?.IsDirty = true; } } = 10.0f;



	private void OnHasCurbsChanged(bool _OldValue, bool _NewValue)
	{
		m_MeshBuilder?.IsDirty = true;
	}



	private void BuildCurbs()
	{
		if (!HasCurbs || SpotCount <= 0)
			return;

		Material material = CurbsMaterial ?? Material.Load("materials/dev/reflectivity_90.vmat");

		int vertsPerCurb;
		int indicesPerCurb;

		if (CurbsSegments <= 1)
		{
			vertsPerCurb = 20;
			indicesPerCurb = 30;
		}
		else
		{
			vertsPerCurb = (CurbsSegments * 4) + (CurbsSegments * 3 * 2);
			indicesPerCurb = (CurbsSegments * 6) + (CurbsSegments * 3 * 2);
		}

		m_MeshBuilder.InitSubmesh
		(
			"parking_curbs",
			vertsPerCurb * SpotCount,
			indicesPerCurb * SpotCount,
			material,
			_HasCollision: true
		);

		for (int i = 0; i < SpotCount; i++)
		{
			float spacing = CalculateSpacing();
			float xCenter = (i * spacing) + (SpotWidth * 0.5f);

			if (CurbsSegments <= 1)
				DrawSimpleCurb(xCenter);
			else
				DrawBeveledCurb(xCenter);
		}
	}



	private void DrawSimpleCurb(float _CenterX)
	{
		Vector3 up = Vector3.Up;

		float angleRad = SpotAngle.DegreeToRadian();
		float sinAngle = float.Sin(angleRad);
		float cosAngle = float.Cos(angleRad);

		float curbWidth = (SpotWidth * CurbsFillRatio);
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

		// Top face
		m_MeshBuilder.AddQuad
		(
			"parking_curbs",
			ftl, ftr, btr, btl,
			up, perpDir,
			new Vector2(0, 0), new Vector2(uW, 0), new Vector2(uW, uD), new Vector2(0, uD)
		);

		// Front face
		m_MeshBuilder.AddQuad
		(
			"parking_curbs",
			fbl, fbr, ftr, ftl,
			-lengthDir, up,
			new Vector2(0, 0), new Vector2(uW, 0), new Vector2(uW, uH), new Vector2(0, uH)
		);

		// Back face
		m_MeshBuilder.AddQuad
		(
			"parking_curbs",
			bbr, bbl, btl, btr,
			lengthDir, up,
			new Vector2(0, 0), new Vector2(uW, 0), new Vector2(uW, uH), new Vector2(0, uH)
		);

		// Left face
		m_MeshBuilder.AddQuad
		(
			"parking_curbs",
			bbl, fbl, ftl, btl,
			-perpDir, up,
			new Vector2(0, 0), new Vector2(uD, 0), new Vector2(uD, uH), new Vector2(0, uH)
		);

		// Right face
		m_MeshBuilder.AddQuad
		(
			"parking_curbs",
			fbr, bbr, btr, ftr,
			perpDir, up,
			new Vector2(0, 0), new Vector2(uD, 0), new Vector2(uD, uH), new Vector2(0, uH)
		);
	}



	private void DrawBeveledCurb(float _CenterX)
	{
		Vector3 up = Vector3.Up;

		float angleRad = SpotAngle * MathF.PI / 180.0f;
		float sinAngle = float.Sin(angleRad);
		float cosAngle = float.Cos(angleRad);

		float curbWidth = (SpotWidth * CurbsFillRatio);
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

		// Body
		for (int i = 0; i < CurbsSegments; i++)
		{
			Vector2 p1 = profile[i];
			Vector2 p2 = profile[i + 1];

			Vector3 bl = center - perpDir * halfW + lengthDir * p1.x + up * p1.y;
			Vector3 br = center + perpDir * halfW + lengthDir * p1.x + up * p1.y;
			Vector3 tr = center + perpDir * halfW + lengthDir * p2.x + up * p2.y;
			Vector3 tl = center - perpDir * halfW + lengthDir * p2.x + up * p2.y;

			Vector2 dir = (p2 - p1).Normal;
			Vector3 normal = (-lengthDir * dir.y + up * dir.x).Normal;

			// If the segment is flat, the normal is straight up
			if (MathF.Abs(p1.y - p2.y) < 0.01f)
				normal = up;

			float uW = curbWidth / CurbsTextureRepeat;
			float v1 = (p1.x + halfD) / CurbsTextureRepeat;
			float v2 = (p2.x + halfD) / CurbsTextureRepeat;

			m_MeshBuilder.AddQuad
			(
				"parking_curbs",
				bl, br, tr, tl,
				normal, perpDir,
				new Vector2(0, v1), new Vector2(uW, v1), new Vector2(uW, v2), new Vector2(0, v2)
			);
		}

		// End caps
		for (int i = 0; i < CurbsSegments; i++)
		{
			Vector3 centerL = center - perpDir * halfW;
			Vector3 v1L = center - perpDir * halfW + lengthDir * profile[i].x + up * profile[i].y;
			Vector3 v2L = center - perpDir * halfW + lengthDir * profile[i + 1].x + up * profile[i + 1].y;

			Vector3 centerR = center + perpDir * halfW;
			Vector3 v1R = center + perpDir * halfW + lengthDir * profile[i].x + up * profile[i].y;
			Vector3 v2R = center + perpDir * halfW + lengthDir * profile[i + 1].x + up * profile[i + 1].y;

			Vector2 uvC = new Vector2(0.5f, 0) * (CurbsDepth / CurbsTextureRepeat);
			Vector2 uv1 = new Vector2((profile[i].x + halfD) / CurbsTextureRepeat, profile[i].y / CurbsTextureRepeat);
			Vector2 uv2 = new Vector2((profile[i + 1].x + halfD) / CurbsTextureRepeat, profile[i + 1].y / CurbsTextureRepeat);

			m_MeshBuilder.AddTriangle("parking_curbs", centerL, v1L, v2L, -perpDir, lengthDir, uvC, uv1, uv2);
			m_MeshBuilder.AddTriangle("parking_curbs", centerR, v2R, v1R, perpDir, lengthDir, uvC, uv2, uv1);
		}
	}
}
