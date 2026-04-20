using System;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	[Property, FeatureEnabled("Lines", Icon = "show_chart", Tint = EditorTint.Yellow), Change] private bool HasLines { get; set; } = false;
	[Property(Title = "Lines"), Feature("Lines")] public RoadLineDefinition[] LineDefinitions { get; set { field = value; IsDirty = true; } }
	[Property(Title = "Offset"), Feature("Lines"), Range(0.01f, 1.0f)] private float LinesOffset { get; set { field = value; IsDirty = true; } } = 0.1f;
	[Property(Title = "Width"), Feature("Lines"), Range(1.0f, 50.0f)] private float LinesWidth { get; set { field = value; IsDirty = true; } } = 5.0f;
	[Property(Title = "Extra Spacing"), Feature("Lines"), Range(0.0f, 1000.0f)] private float LinesExtraSpacing { get; set { field = value; IsDirty = true; } } = 0.0f;
	[Property(Title = "Texture Repeat"), Feature("Lines")] private float LinesTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 10.0f;



	private void OnHasLinesChanged(bool _OldValue, bool _NewValue)
	{
		IsDirty = true;
	}



	private void CreateLines()
	{
		EnsureLinesMeshExist();
	}



	private void UpdateLines()
	{
	}



	private void RemoveLines()
	{
		RemoveGeneratedMeshChildren(LineSurfaceTag);
	}



	private void EnsureLinesMeshExist()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		if (HasGeneratedMeshChildren(LineSurfaceTag))
			return;

		BuildLinesMesh();
	}



	private void RebuildLinesMesh()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		RemoveGeneratedMeshChildren(LineSurfaceTag);
		BuildLinesMesh();
	}



	private void BuildLinesMesh()
	{
		if (!HasLines || LineDefinitions == null || LineDefinitions.Length == 0)
			return;

		GetSplineFrameData(out var frames, out var segmentsToKeep);

		int finalSegmentCount = segmentsToKeep.Count - 1;

		if (finalSegmentCount <= 0)
			return;

		float roadWidth = RoadWidth + LinesExtraSpacing;
		float lineSpacing = roadWidth / (LineDefinitions.Length + 1);

		var polygonMeshes = new PolygonMesh[LineDefinitions.Length];
		for (int i = 0; i < LineDefinitions.Length; i++)
			polygonMeshes[i] = new PolygonMesh();

		float[] lineDistances = new float[LineDefinitions.Length];

		for (int i = 0; i < finalSegmentCount; i++)
		{
			int idx0 = segmentsToKeep[i];
			int idx1 = segmentsToKeep[i + 1];

			Transform f0 = frames[idx0];
			Transform f1 = frames[idx1];

			Vector3 p0 = f0.Position;
			Vector3 p1 = f1.Position;

			Vector3 right0 = f0.Rotation.Right;

			for (int line = 0; line < LineDefinitions.Length; line++)
			{
				float offsetFromCenter = ((line + 1) * lineSpacing) - (roadWidth * 0.5f);

				Vector3 center0 =
					p0 +
					f0.Rotation.Right * offsetFromCenter +
					f0.Rotation.Up * LinesOffset;

				Vector3 center1 =
					p1 +
					f1.Rotation.Right * offsetFromCenter +
					f1.Rotation.Up * LinesOffset;

				float segmentLength = Vector3.DistanceBetween(center0, center1);
				Vector3 dir = (center1 - center0).Normal;

				float remaining = segmentLength;
				Vector3 curCenter = center0;

				float dashSpacing = LineDefinitions[line]?.DashSpacing ?? 0.0f;
				float dashFillRatio = LineDefinitions[line]?.DashFillRatio ?? 1.0f;
				float dashLength = dashSpacing * dashFillRatio;
				float halfWidth = LinesWidth * 0.5f;

				var polygonMesh = polygonMeshes[line];
				var material = LineDefinitions[line]?.Material ?? Material.Load("materials/default.vmat");

				while (remaining > 0.001f)
				{
					float linePos = lineDistances[line];
					float cyclePos = dashSpacing > 0 ? linePos % dashSpacing : 0;

					if (cyclePos < 0.0001f)
						cyclePos = 0.0f;

					if (dashSpacing > 0 && dashSpacing - cyclePos < 0.0001f)
						cyclePos = dashSpacing;

					bool inDash = dashSpacing <= 0 || cyclePos <= dashLength - 0.0001f;

					float step;

					if (dashSpacing <= 0)
						step = remaining;
					else if (inDash)
						step = dashLength - cyclePos;
					else
						step = dashSpacing - cyclePos;

					step = Math.Max(step, 0.01f);
					step = Math.Min(step, remaining);

					Vector3 nextCenter = curCenter + dir * step;

					if (inDash)
					{
						Vector3 l0 = curCenter - right0 * halfWidth;
						Vector3 r0 = curCenter + right0 * halfWidth;
						Vector3 l1 = nextCenter - right0 * halfWidth;
						Vector3 r1 = nextCenter + right0 * halfWidth;

						float v0 = linePos / LinesTextureRepeat;
						float v1 = (linePos + step) / LinesTextureRepeat;

						var verts = polygonMesh.AddVertices(l0, r0, r1, l1);
						MeshUtility.AddTexturedQuad(polygonMesh, material, verts[0], verts[1], verts[2], verts[3],
							new Vector2(0, v0), new Vector2(1, v0),
							new Vector2(1, v1), new Vector2(0, v1));
					}

					lineDistances[line] += step;
					curCenter = nextCenter;
					remaining -= step;
				}
			}
		}

		for (int line = 0; line < LineDefinitions.Length; line++)
		{
			var child = new GameObject(GameObject, true, $"Line_{line}");
			child.Tags.Add(LineSurfaceTag);

			var meshComponent = child.AddComponent<MeshComponent>();
			meshComponent.Mesh = polygonMeshes[line];
			meshComponent.Collision = MeshComponent.CollisionType.None;
			meshComponent.RenderType = ModelRenderer.ShadowRenderType.Off;
			meshComponent.SmoothingAngle = 40.0f;
			meshComponent.Static = true;
		}
	}
}
