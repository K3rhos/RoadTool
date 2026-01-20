using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	private MeshBuilder m_LanesBuilder;

	[Property, FeatureEnabled("Lanes", Icon = "show_chart", Tint = EditorTint.Yellow), Change] private bool HasLanes { get; set; } = false;
	[Property(Title = "Materials"), Feature("Lanes")] public Material[] LaneMaterials { get; set { field = value; IsDirty = true; } }
	[Property(Title = "Count"), Feature("Lanes"), Range(1, 10)] private int LaneCount { get; set { field = value; IsDirty = true; } } = 1;
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
		if (!HasLanes || LaneCount <= 0)
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
			segmentsToKeep = DetectImportantSegments(frames, baseSegmentCount, MinSegmentsToMerge, StraightThreshold);
		}
		else
		{
			for (int i = 0; i <= baseSegmentCount; i++)
			{
				segmentsToKeep.Add(i);
			}
		}

		int finalSegmentCount = segmentsToKeep.Count - 1;

		int quadsPerSegment = LaneCount;
		int vertsPerSegment = quadsPerSegment * 4;
		int indicesPerSegment = quadsPerSegment * 6;
		
		for (int lane = 0; lane < LaneCount; lane++)
		{
			// Use the material at the index, or the last available, or default
			Material material = (LaneMaterials != null && LaneMaterials.Length > lane) ? LaneMaterials[lane] : (LaneMaterials?.FirstOrDefault() ?? Material.Load("materials/default.vmat"));
			
			m_LanesBuilder.InitSubmesh
			(
				$"lane_{lane}",
				finalSegmentCount * vertsPerSegment,
				finalSegmentCount * indicesPerSegment,
				material,
				_HasCollision: false
			);
		}

		float roadWidth = RoadWidth + LaneExtraSpacing;
		float laneSpacing = roadWidth / (LaneCount + 1);

		float[] laneDistances = new float[LaneCount];

		for (int i = 0; i < finalSegmentCount; i++)
		{
			int idx0 = segmentsToKeep[i];
			int idx1 = segmentsToKeep[i + 1];

			Transform f0 = frames[idx0];
			Transform f1 = frames[idx1];

			Vector3 p0 = f0.Position;
			Vector3 p1 = f1.Position;

			Vector3 forward = (p1 - p0).Normal;

			Vector3 right0 = f0.Rotation.Right;
			Vector3 right1 = f1.Rotation.Right;

			Vector3 up0 = f0.Rotation.Up;
			Vector3 up1 = f1.Rotation.Up;

			for (int lane = 0; lane < LaneCount; lane++)
			{
				float offsetFromCenter = ((lane + 1) * laneSpacing) - (roadWidth * 0.5f);

				Vector3 center0 =
				   p0 +
				   right0 * offsetFromCenter +
				   up0 * LanesOffset;

				Vector3 center1 =
				   p1 +
				   right1 * offsetFromCenter +
				   up1 * LanesOffset;

				float halfWidth = LaneWidth * 0.5f;

				Vector3 l0 = center0 - right0 * halfWidth;
				Vector3 r0 = center0 + right0 * halfWidth;

				Vector3 l1 = center1 - right1 * halfWidth;
				Vector3 r1 = center1 + right1 * halfWidth;

				float segmentLength = Vector3.DistanceBetween(center0, center1);

				float v0 = laneDistances[lane] / LaneTextureInchesPerRepeat;
				laneDistances[lane] += segmentLength;
				float v1 = laneDistances[lane] / LaneTextureInchesPerRepeat;

				m_LanesBuilder.AddQuad($"lane_{lane}",
					l0, r0, r1, l1,
					up0, up1, up1, up0,
					forward,
					new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1)
				);
			}
		}
	}
}
