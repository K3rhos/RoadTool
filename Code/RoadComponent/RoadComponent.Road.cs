using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	[Property(Title = "Material"), Feature("Road", Icon = "fork_left", Tint = EditorTint.Green)] private Material RoadMaterial { get; set { field = value; IsDirty = true; } }
	[Property(Title = "Width"), Feature("Road"), Range(10.0f, 1000.0f)] private float RoadWidth { get; set { field = value; IsDirty = true; } } = 500.0f;
	[Property(Title = "Precision"), Feature("Road"), Range(10.0f, 100.0f)] private float RoadPrecision { get; set { field = value.Clamp(1.0f, 10000.0f); IsDirty = true; } } = 40.0f;
	[Property(Title = "Texture Repeat"), Feature("Road")] private float RoadTextureInchesPerRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 500.0f;



	private void BuildRoad()
	{
		int segmentCount = Math.Max(2, (int)Math.Ceiling(Spline.Length / RoadPrecision));
		int frameCount = segmentCount + 1;

		var frames = UseRotationMinimizingFrames
		   ? CalculateRotationMinimizingTangentFrames(Spline, frameCount)
		   : CalculateTangentFramesUsingUpDir(Spline, frameCount);

		var segmentsToKeep = new List<int>();

		if (AutoSimplify)
		{
			segmentsToKeep = DetectImportantSegments(frames, segmentCount, MinSegmentsToMerge, StraightThreshold);
		}
		else
		{
			for (int i = 0; i <= segmentCount; i++)
			{
				segmentsToKeep.Add(i);
			}
		}

		const int vertsPerSegment = 4;
		const int indicesPerSegment = 6;

		int finalSegmentCount = segmentsToKeep.Count - 1;

		m_MeshBuilder.InitSubmesh
		(
			"road",
			finalSegmentCount * vertsPerSegment,
			finalSegmentCount * indicesPerSegment,
			RoadMaterial ?? Material.Load("materials/dev/reflectivity_30.vmat"),
			_HasCollision: true
		);

		float halfWidth = RoadWidth * 0.5f;

		for (int i = 0; i < finalSegmentCount; i++)
		{
			int idx0 = segmentsToKeep[i];
			int idx1 = segmentsToKeep[i + 1];

			float t0 = (float)idx0 / segmentCount;
			float t1 = (float)idx1 / segmentCount;

			float d0 = t0 * Spline.Length;
			float d1 = t1 * Spline.Length;

			float v0 = d0 / RoadTextureInchesPerRepeat;
			float v1 = d1 / RoadTextureInchesPerRepeat;

			Transform f0 = frames[idx0];
			Transform f1 = frames[idx1];

			Vector3 p0 = f0.Position;
			Vector3 p1 = f1.Position;

			Vector3 forward = (p1 - p0).Normal;

			Vector3 right0 = f0.Rotation.Right;
			Vector3 right1 = f1.Rotation.Right;

			Vector3 up0 = f0.Rotation.Up;
			Vector3 up1 = f1.Rotation.Up;

			Vector3 l0 = p0 - right0 * halfWidth;
			Vector3 r0 = p0 + right0 * halfWidth;

			Vector3 l1 = p1 - right1 * halfWidth;
			Vector3 r1 = p1 + right1 * halfWidth;

			Vector2 uv00 = new(0, v0);
			Vector2 uv01 = new(0, v1);
			Vector2 uv11 = new(1, v1);
			Vector2 uv10 = new(1, v0);

			m_MeshBuilder.AddQuad("road",
				l0, r0, r1, l1,
				up0, up1, up1, up0,
				forward,
				uv00, uv10, uv11, uv01
			);
		}
	}
}
