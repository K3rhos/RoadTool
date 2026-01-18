using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public class CircleExit
{
	[Property] public float AngleDegrees { get; set; } = 0.0f;
	[Property] public float RoadWidth { get; set; } = 500.0f;
}

public partial class RoadIntersectionComponent
{
	[Property, ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1)] private float Radius { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 600.0f;
	[Property, ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1)] private float Precision { get; set { field = value.Clamp(10.0f, 10000.0f); m_MeshBuilder?.IsDirty = true; } } = 40.0f;
	[Property(Title = "Exits"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1)] private List<CircleExit> CircleExits { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = new();



	private int GetCircleSegmentCount()
	{
		float circumference = 2.0f * MathF.PI * Radius;

		// Ensure at least 8 segments so it doesn't turn into a square/triangle
		return Math.Max(8, (int)Math.Ceiling(circumference / Precision));
	}



	private void BuildCircleRoad()
	{
		Vector3 up = WorldRotation.Up;
		int segments = GetCircleSegmentCount();
		float step = 360.0f / segments;

		int activeSegments = 0;

		for (int i = 0; i < segments; i++)
		{
			if (!ArcBlockedByExit(i * step, (i + 1) * step)) activeSegments++;
		}

		m_MeshBuilder.InitSubmesh("intersection_road", activeSegments * 3, activeSegments * 3, RoadMaterial ?? Material.Load("materials/dev/reflectivity_30.vmat"), true);

		for (int i = 0; i < segments; i++)
		{
			float a0 = i * step;
			float a1 = (i + 1) * step;

			if (ArcBlockedByExit(a0, a1))
				continue;

			Vector3 d0 = Rotation.FromYaw(a0).Forward;
			Vector3 d1 = Rotation.FromYaw(a1).Forward;

			m_MeshBuilder.AddTriangle("intersection_road", Vector3.Zero, d0 * Radius, d1 * Radius,
				up, d0, Vector2.Zero, new Vector2(0, 1), new Vector2(1, 1));
		}
	}



	private void BuildCircleSidewalk()
	{
		Vector3 up = WorldRotation.Up;
		int segments = GetCircleSegmentCount();
		float step = 360f / segments;

		int activeSegments = 0;

		for (int i = 0; i < segments; i++)
		{
			if (!ArcBlockedByExit(i * step, (i + 1) * step)) activeSegments++;
		}

		m_MeshBuilder.InitSubmesh("intersection_sidewalk", activeSegments * 8, activeSegments * 12, SidewalkMaterial ?? Material.Load("materials/dev/reflectivity_70.vmat"), true);

		float innerR = Radius;
		float outerR = Radius + SidewalkWidth;

		for (int i = 0; i < segments; i++)
		{
			float a0 = i * step;
			float a1 = (i + 1) * step;

			if (ArcBlockedByExit(a0, a1))
				continue;

			float d0 = innerR * (a0 * MathF.PI / 180f);
			float d1 = innerR * (a1 * MathF.PI / 180f);

			float v0 = d0 / SidewalkTextureRepeat;
			float v1 = d1 / SidewalkTextureRepeat;

			Vector3 d0V = Rotation.FromYaw(a0).Forward;
			Vector3 d1V = Rotation.FromYaw(a1).Forward;

			Vector3 n0 = -d0V; // Normal at angle a0
			Vector3 n1 = -d1V; // Normal at angle a1

			Vector3 i0 = d0V * innerR;
			Vector3 i1 = d1V * innerR;
			Vector3 o0 = d0V * outerR;
			Vector3 o1 = d1V * outerR;

			Vector3 segmentTangent = (i1 - i0).Normal;

			// Top face
			m_MeshBuilder.AddQuad(
			   "intersection_sidewalk",
			   o0 + up * SidewalkHeight,
			   o1 + up * SidewalkHeight,
			   i1 + up * SidewalkHeight,
			   i0 + up * SidewalkHeight,
			   up,
			   segmentTangent,
			   new Vector2(1, v0), // Outer Start
			   new Vector2(1, v1), // Outer End
			   new Vector2(0, v1), // Inner End
			   new Vector2(0, v0)  // Inner Start
			);

			// Curb face
			float heightUV = SidewalkHeight / SidewalkTextureRepeat;

			m_MeshBuilder.AddQuad(
			   "intersection_sidewalk",
			   i0 + up * SidewalkHeight,
			   i1 + up * SidewalkHeight,
			   i1,
			   i0,
			   n0, n1, n1, n0,
			   segmentTangent,
			   new Vector2(0, v0),        // Top Start
			   new Vector2(0, v1),        // Top End
			   new Vector2(heightUV, v1), // Bottom End
			   new Vector2(heightUV, v0)  // Bottom Start
			);
		}
	}



	private static float AngleDelta(float _A, float _B)
	{
		float d = (_A - _B) % 360.0f;

		if (d > 180.0f) d -= 360.0f;
		if (d < -180.0f) d += 360.0f;

		return MathF.Abs(d);
	}



	private bool ArcBlockedByExit(float _A0, float _A1)
	{
		foreach (var exit in CircleExits)
		{
			float halfAngle = float.Atan(exit.RoadWidth / Radius).RadianToDegree();
			float ea = exit.AngleDegrees;

			if (AngleDelta(_A0, ea) < halfAngle ||
				AngleDelta(_A1, ea) < halfAngle)
				return true;
		}

		return false;
	}



	public Transform GetCircleExitTransform(int _Index)
	{
		var exit = CircleExits[_Index];

		Vector3 dir = Rotation.FromYaw(exit.AngleDegrees).Forward;

		float dist = Shape == IntersectionShape.Circle ? Radius : Math.Max(Width, Length) * 0.5f;

		return new Transform
		{
			Position = WorldPosition + dir * dist,
			Rotation = Rotation.LookAt(dir, WorldRotation.Up)
		};
	}
}
