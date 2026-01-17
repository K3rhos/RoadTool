using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	[Property, FeatureEnabled("Sidewalk", Icon = "directions_walk", Tint = EditorTint.Blue)] private bool HasSidewalk { get; set { field = value; IsDirty = true; } } = true;
	[Property(Title = "Material"), Feature("Sidewalk")] private Material SidewalkMaterial { get; set { field = value; IsDirty = true; } }
	[Property(Title = "Width"), Feature("Sidewalk"), Range(10.0f, 500.0f)] private float SidewalkWidth { get; set { field = value; IsDirty = true; } } = 150.0f;
	[Property(Title = "Height"), Feature("Sidewalk"), Range(0.1f, 100.0f)] private float SidewalkHeight { get; set { field = value; IsDirty = true; } } = 5.0f;
	[Property(Title = "Texture Repeat"), Feature("Sidewalk")] private float SidewalkTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 200.0f;
	
	
	
	private void BuildSidewalk()
	{
		if (!HasSidewalk)
			return;

		int baseSegmentCount = Math.Max(2, (int)Math.Ceiling(Spline.Length / RoadPrecision));

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

		const int vertsPerSegment = 24;
		const int indicesPerSegment = 36;

		int totalVertices = finalSegmentCount * vertsPerSegment;
		int totalIndices = finalSegmentCount * indicesPerSegment;

		m_MeshBuilder.InitSubmesh
		(
			"sidewalk",
			totalVertices,
			totalIndices,
			SidewalkMaterial ?? Material.Load("materials/dev/reflectivity_70.vmat"),
			_HasCollision: true
		);

		float roadEdgeOffset = RoadWidth * 0.5f;

		float leftInnerEdge = -roadEdgeOffset;
		float leftOuterEdge = -(roadEdgeOffset + SidewalkWidth);

		float rightInnerEdge = roadEdgeOffset;
		float rightOuterEdge = roadEdgeOffset + SidewalkWidth;

		float leftAvgUVDist = 0f;
		float rightAvgUVDist = 0f;
		
		for (int i = 0; i < finalSegmentCount; i++)
		{
			int idx0 = segmentsToKeep[i];
			int idx1 = segmentsToKeep[i + 1];

			float v2 = SidewalkHeight / SidewalkTextureRepeat;

			Transform f0 = frames[idx0];
			Transform f1 = frames[idx1];

			Vector3 u0 = f0.Rotation.Up;
			Vector3 u1 = f1.Rotation.Up;
			Vector3 r0 = f0.Rotation.Right;
			Vector3 r1 = f1.Rotation.Right;

			Vector3 p0 = f0.Position;
			Vector3 p1 = f1.Position;

			Vector3 forward = (p1 - p0).Normal;

			Vector3 right0 = f0.Rotation.Right;
			Vector3 up0 = f0.Rotation.Up;

			Vector3 right1 = f1.Rotation.Right;
			Vector3 up1 = f1.Rotation.Up;

			// Left sidewalk positions
			Vector3 lb0 = p0 + right0 * leftInnerEdge;
			Vector3 lb1 = p1 + right1 * leftInnerEdge;

			Vector3 lo0 = p0 + right0 * leftOuterEdge;
			Vector3 lo1 = p1 + right1 * leftOuterEdge;

			Vector3 lt0 = lb0 + up0 * SidewalkHeight;
			Vector3 lt1 = lb1 + up1 * SidewalkHeight;

			Vector3 lto0 = lo0 + up0 * SidewalkHeight;
			Vector3 lto1 = lo1 + up1 * SidewalkHeight;

			// Right sidewalk positions
			Vector3 rb0 = p0 + right0 * rightInnerEdge;
			Vector3 rb1 = p1 + right1 * rightInnerEdge;

			Vector3 ro0 = p0 + right0 * rightOuterEdge;
			Vector3 ro1 = p1 + right1 * rightOuterEdge;

			Vector3 rt0 = rb0 + up0 * SidewalkHeight;
			Vector3 rt1 = rb1 + up1 * SidewalkHeight;

			Vector3 rto0 = ro0 + up0 * SidewalkHeight;
			Vector3 rto1 = ro1 + up1 * SidewalkHeight;
			
			// Calculate distances
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

			// Left top
			m_MeshBuilder.AddQuad("sidewalk",
				lt0, lt1, lto1, lto0,
				u0, u1, u1, u0,
				forward,
				new Vector2(0, leftAvgV0), new Vector2(0, leftAvgV1), new Vector2(1, leftAvgV1), new Vector2(1, leftAvgV0));

			// Left inner
			m_MeshBuilder.AddQuad("sidewalk",
				lb0, lb1, lt1, lt0,
				r0, r1, r1, r0,
				forward,
				new Vector2(v2, leftAvgV0), new Vector2(v2, leftAvgV1), new Vector2(0, leftAvgV1), new Vector2(0, leftAvgV0));

			// Left outer
			m_MeshBuilder.AddQuad("sidewalk",
				lo0, lto0, lto1, lo1,
				-r0, -r0, -r1, -r1,
				forward,
				new Vector2(1 - v2, 1 - leftAvgV0), new Vector2(1, 1 - leftAvgV0), new Vector2(1, 1 - leftAvgV1), new Vector2(1 - v2, 1 - leftAvgV1));

			// Right top
			m_MeshBuilder.AddQuad("sidewalk",
				rt0, rto0, rto1, rt1,
				u0, u0, u1, u1,
				forward,
				new Vector2(0, rightAvgV0), new Vector2(1, rightAvgV0), new Vector2(1, rightAvgV1), new Vector2(0, rightAvgV1));

			// Right inner
			m_MeshBuilder.AddQuad("sidewalk",
				rb0, rt0, rt1, rb1,
				-r0, -r0, -r1, -r1,
				forward,
				new Vector2(v2, rightAvgV0), new Vector2(0, rightAvgV0), new Vector2(0, rightAvgV1), new Vector2(v2, rightAvgV1));

			// Right outer
			m_MeshBuilder.AddQuad("sidewalk",
				ro0, ro1, rto1, rto0,
				r0, r1, r1, r0,
				forward,
				new Vector2(1 - v2, 1 - rightAvgV0), new Vector2(1 - v2, 1 - rightAvgV1), new Vector2(1, 1 - rightAvgV1), new Vector2(1, 1 - rightAvgV0));
		}
	}
}
