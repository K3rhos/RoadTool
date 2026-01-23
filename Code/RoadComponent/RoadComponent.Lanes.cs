using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	private MeshBuilder m_LanesBuilder;

	[Property, FeatureEnabled("Lanes", Icon = "show_chart", Tint = EditorTint.Yellow), Change] private bool HasLanes { get; set; } = false;
	[Property(Title = "Lanes"), Feature("Lanes")] public LaneDefinition[] LaneDefinitions { get; set { field = value; IsDirty = true; } }
	[Property(Title = "Offset"), Feature("Lanes"), Range(0.01f, 1.0f)] private float LanesOffset { get; set { field = value; IsDirty = true; } } = 0.1f;
	[Property(Title = "Width"), Feature("Lanes"), Range(0.1f, 50.0f)] private float LaneWidth { get; set { field = value; IsDirty = true; } } = 1.0f;
	[Property(Title = "Extra Spacing"), Feature("Lanes"), Range(0.0f, 1000.0f)] private float LaneExtraSpacing { get; set { field = value; IsDirty = true; } } = 0.0f;
	[Property(Title = "Texture Repeat"), Feature("Lanes")] private float LaneTextureInchesPerRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 10.0f;



	private void OnHasLanesChanged(bool _OldValue, bool _NewValue)
	{
		m_LanesBuilder?.IsDirty = true;
	}



	private void CreateLanes()
	{
		m_LanesBuilder = new MeshBuilder(GameObject);
		m_LanesBuilder.OnBuild += BuildLanes;
		m_LanesBuilder.CastShadows = false;
		m_LanesBuilder.Rebuild();
	}



	private void UpdateLanes()
	{
		m_LanesBuilder?.Update();
	}



	private void RemoveLanes()
	{
		m_LanesBuilder?.OnBuild -= BuildLanes;
		m_LanesBuilder?.Clear();
	}



	private void BuildLanes()
	{
		if (!HasLanes || LaneDefinitions == null || LaneDefinitions.Length == 0)
			return;

		float splineLength = Spline.Length;

		int baseSegmentCount = Math.Max(2, (int)Math.Ceiling(splineLength / RoadPrecision));
		int frameCount = baseSegmentCount + 1;

		var frames = UseRotationMinimizingFrames
			? CalculateRotationMinimizingTangentFrames(Spline, frameCount)
			: CalculateTangentFramesUsingUpDir(Spline, frameCount);

		List<int> segmentsToKeep;

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
			segmentsToKeep = new List<int>();
			for (int i = 0; i <= baseSegmentCount; i++)
				segmentsToKeep.Add(i);
		}

		int finalSegmentCount = segmentsToKeep.Count - 1;

		float roadWidth = RoadWidth + LaneExtraSpacing;
		float laneSpacing = roadWidth / (LaneDefinitions.Length + 1);

		// Pre pass to calculate properly the exact quads needed per lane
		int[] quadCounts = new int[LaneDefinitions.Length];
		float[] laneDistancesCount = new float[LaneDefinitions.Length];

		for (int i = 0; i < finalSegmentCount; i++)
		{
			int idx0 = segmentsToKeep[i];
			int idx1 = segmentsToKeep[i + 1];

			Transform f0 = frames[idx0];
			Transform f1 = frames[idx1];

			Vector3 p0 = f0.Position;
			Vector3 p1 = f1.Position;

			for (int lane = 0; lane < LaneDefinitions.Length; lane++)
			{
				float offsetFromCenter =
					((lane + 1) * laneSpacing) - (roadWidth * 0.5f);

				Vector3 center0 =
					p0 +
					f0.Rotation.Right * offsetFromCenter +
					f0.Rotation.Up * LanesOffset;

				Vector3 center1 =
					p1 +
					f1.Rotation.Right * offsetFromCenter +
					f1.Rotation.Up * LanesOffset;

				float segmentLength = Vector3.DistanceBetween(center0, center1);
				Vector3 dir = (center1 - center0).Normal;

				float remaining = segmentLength;

				float dashSpacing = LaneDefinitions[lane].DashSpacing;
				float dashFillRatio = LaneDefinitions[lane].DashFillRatio;
				float dashLength = dashSpacing * dashFillRatio;

				while (remaining > 0.001f)
				{
					float lanePos = laneDistancesCount[lane];
					float cyclePos = dashSpacing > 0 ? lanePos % dashSpacing : 0;

					bool inDash = dashSpacing <= 0 || cyclePos < dashLength;

					float step;

					if (dashSpacing <= 0)
						step = remaining;
					else if (inDash)
						step = Math.Min(dashLength - cyclePos, remaining);
					else
						step = Math.Min(dashSpacing - cyclePos, remaining);

					if (inDash)
						quadCounts[lane]++;

					laneDistancesCount[lane] += step;
					remaining -= step;
				}
			}
		}

		// Init submeshes
		for (int lane = 0; lane < LaneDefinitions.Length; lane++)
		{
			Material material =
				LaneDefinitions[lane]?.Material ??
				Material.Load("materials/default.vmat");

			m_LanesBuilder.InitSubmesh(
				$"lane_{lane}",
				quadCounts[lane] * 4,
				quadCounts[lane] * 6,
				material,
				_HasCollision: false
			);
		}

		// Final pass to actually build the mesh
		float[] laneDistances = new float[LaneDefinitions.Length];

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

			for (int lane = 0; lane < LaneDefinitions.Length; lane++)
			{
				float offsetFromCenter =
					((lane + 1) * laneSpacing) - (roadWidth * 0.5f);

				Vector3 center0 =
					p0 +
					right0 * offsetFromCenter +
					up0 * LanesOffset;

				Vector3 center1 =
					p1 +
					f1.Rotation.Right * offsetFromCenter +
					f1.Rotation.Up * LanesOffset;

				float segmentLength = Vector3.DistanceBetween(center0, center1);
				Vector3 dir = (center1 - center0).Normal;

				float remaining = segmentLength;
				Vector3 curCenter = center0;

				float dashSpacing = LaneDefinitions[lane].DashSpacing;
				float dashFillRatio = LaneDefinitions[lane].DashFillRatio;
				float dashLength = dashSpacing * dashFillRatio;

				float halfWidth = LaneWidth * 0.5f;

				while (remaining > 0.001f)
				{
					float lanePos = laneDistances[lane];
					float cyclePos = dashSpacing > 0 ? lanePos % dashSpacing : 0;

					bool inDash = dashSpacing <= 0 || cyclePos < dashLength;

					float step;

					if (dashSpacing <= 0)
						step = remaining;
					else if (inDash)
						step = Math.Min(dashLength - cyclePos, remaining);
					else
						step = Math.Min(dashSpacing - cyclePos, remaining);

					Vector3 nextCenter = curCenter + dir * step;

					if (inDash)
					{
						Vector3 l0 = curCenter - right0 * halfWidth;
						Vector3 r0 = curCenter + right0 * halfWidth;
						Vector3 l1 = nextCenter - right0 * halfWidth;
						Vector3 r1 = nextCenter + right0 * halfWidth;

						float v0 = lanePos / LaneTextureInchesPerRepeat;
						float v1 = (lanePos + step) / LaneTextureInchesPerRepeat;

						m_LanesBuilder.AddQuad(
							$"lane_{lane}",
							l0, r0, r1, l1,
							up0, up0, up0, up0,
							forward,
							new Vector2(0, v0), new Vector2(1, v0),
							new Vector2(1, v1), new Vector2(0, v1)
						);
					}

					laneDistances[lane] += step;
					curCenter = nextCenter;
					remaining -= step;
				}
			}
		}
	}
}
