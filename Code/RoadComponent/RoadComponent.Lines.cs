using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	private MeshBuilder m_LinesBuilder;

	[Property, FeatureEnabled("Lines", Icon = "show_chart", Tint = EditorTint.Yellow), Change] private bool HasLines { get; set; } = false;
	[Property(Title = "Lines"), Feature("Lines")] public RoadLineDefinition[] LineDefinitions { get; set { field = value; IsDirty = true; } }
	[Property(Title = "Offset"), Feature("Lines"), Range(0.01f, 1.0f)] private float LinesOffset { get; set { field = value; IsDirty = true; } } = 0.1f;
	[Property(Title = "Width"), Feature("Lines"), Range(0.1f, 50.0f)] private float LinesWidth { get; set { field = value; IsDirty = true; } } = 1.0f;
	[Property(Title = "Extra Spacing"), Feature("Lines"), Range(0.0f, 1000.0f)] private float LinesExtraSpacing { get; set { field = value; IsDirty = true; } } = 0.0f;
	[Property(Title = "Texture Repeat"), Feature("Lines")] private float LinesTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 10.0f;



	private void OnHasLinesChanged(bool _OldValue, bool _NewValue)
	{
		m_LinesBuilder?.IsDirty = true;
	}



	private void CreateLines()
	{
		m_LinesBuilder = new MeshBuilder(GameObject);
		m_LinesBuilder.OnBuild += BuildLines;
		m_LinesBuilder.CastShadows = false;
		m_LinesBuilder.Rebuild();
	}



	private void UpdateLines()
	{
		m_LinesBuilder?.Update();
	}



	private void RemoveLines()
	{
		m_LinesBuilder?.OnBuild -= BuildLines;
		m_LinesBuilder?.Clear();
	}
	
	
	
	// TODO: Need some refactoring
	private void BuildLines()
	{
		if (!HasLines || LineDefinitions == null || LineDefinitions.Length == 0)
			return;

		float splineLength = Spline.Length;

		int baseSegmentCount = Math.Max(2, (int)Math.Ceiling(splineLength / RoadPrecision));
		int frameCount = baseSegmentCount + 1;

		var frames = UseRotationMinimizingFrames
			? CalculateRotationMinimizingTangentFrames(Spline, frameCount)
			: CalculateTangentFramesUsingUpDir(Spline, frameCount);

		var segmentsToKeep = new List<int>();

		if (AutoSimplify)
		{
			segmentsToKeep = DetectImportantSegments(
				frames,
				baseSegmentCount,
				MinSegmentsToMerge,
				StraightThreshold
			);
		}
		else
		{
			for (int i = 0; i <= baseSegmentCount; i++)
			{
				segmentsToKeep.Add(i);
			}
		}

		int finalSegmentCount = segmentsToKeep.Count - 1;

		float roadWidth = RoadWidth + LinesExtraSpacing;
		float lineSpacing = roadWidth / (LineDefinitions.Length + 1);

		// Pre pass to calculate properly the exact quads needed per line
		int[] quadCounts = new int[LineDefinitions.Length];
		float[] lineDistancesCount = new float[LineDefinitions.Length];

		for (int i = 0; i < finalSegmentCount; i++)
		{
			int idx0 = segmentsToKeep[i];
			int idx1 = segmentsToKeep[i + 1];

			Transform f0 = frames[idx0];
			Transform f1 = frames[idx1];

			Vector3 p0 = f0.Position;
			Vector3 p1 = f1.Position;

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

				float remaining = segmentLength;

				float dashSpacing = LineDefinitions[line]?.DashSpacing ?? 0.0f;
				float dashFillRatio = LineDefinitions[line]?.DashFillRatio ?? 1.0f;
				float dashLength = dashSpacing * dashFillRatio;

				while (remaining > 0.001f)
				{
					float linePos = lineDistancesCount[line];
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

					if (inDash)
						quadCounts[line]++;

					lineDistancesCount[line] += step;
					remaining -= step;
				}
			}
		}

		// Init submeshes
		for (int line = 0; line < LineDefinitions.Length; line++)
		{
			Material material = LineDefinitions[line]?.Material ?? Material.Load("materials/default.vmat");

			m_LinesBuilder.InitSubmesh(
				$"line_{line}",
				quadCounts[line] * 4,
				quadCounts[line] * 6,
				material,
				_HasCollision: false
			);
		}

		// Final pass to actually build the mesh
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
			Vector3 up0 = f0.Rotation.Up;

			Vector3 forward = (p1 - p0).Normal;

			for (int line = 0; line < LineDefinitions.Length; line++)
			{
				float offsetFromCenter =
					((line + 1) * lineSpacing) - (roadWidth * 0.5f);

				Vector3 center0 =
					p0 +
					right0 * offsetFromCenter +
					up0 * LinesOffset;

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

						m_LinesBuilder.AddQuad
						(
							$"line_{line}",
							l0, r0, r1, l1,
							up0, up0, up0, up0,
							forward,
							new Vector2(0, v0), new Vector2(1, v0),
							new Vector2(1, v1), new Vector2(0, v1)
						);
					}

					lineDistances[line] += step;
					curCenter = nextCenter;
					remaining -= step;
				}
			}
		}
	}
}
