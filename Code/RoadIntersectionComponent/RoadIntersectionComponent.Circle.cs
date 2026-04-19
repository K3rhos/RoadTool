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
	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1)] private float Radius { get; set { field = value; m_IsDirty = true; } } = 600.0f;
	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1)] private float Precision { get; set { field = value.Clamp(10.0f, 10000.0f); m_IsDirty = true; } } = 40.0f;
	[Property(Title = "Exits"), Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1)] private List<CircleExit> CircleExits { get; set { field = value; m_IsDirty = true; } } = new();



	private int GetCircleSegmentCount()
	{
		float circumference = 2.0f * MathF.PI * Radius;

		// Ensure at least 8 segments so it doesn't turn into a square/triangle
		return Math.Max(8, (int)Math.Ceiling(circumference / Precision));
	}



	private void BuildCircleRoad(PolygonMesh _Mesh, Material _Material)
	{
		var cache = new Dictionary<Vector3, HalfEdgeMesh.VertexHandle>();

		int segments = GetCircleSegmentCount();
		float step = 360.0f / segments;

		var vCenter = MeshUtility.GetOrAddVertex(_Mesh, cache, Vector3.Zero);

		for (int i = 0; i < segments; i++)
		{
			float a0 = i * step;
			float a1 = (i + 1) * step;

			if (ArcBlockedByExit(a0, a1))
				continue;

			Vector3 d0 = Rotation.FromYaw(a0).Forward * Radius;
			Vector3 d1 = Rotation.FromYaw(a1).Forward * Radius;

			var vD0 = MeshUtility.GetOrAddVertex(_Mesh, cache, d0);
			var vD1 = MeshUtility.GetOrAddVertex(_Mesh, cache, d1);

			MeshUtility.AddTexturedTriangle(_Mesh, _Material, vCenter, vD0, vD1,
				Vector2.Zero, new Vector2(0, 1), new Vector2(1, 1));
		}
	}



	private void BuildCircleSidewalk(PolygonMesh _Mesh, Material _Material)
	{
		var cache = new Dictionary<Vector3, HalfEdgeMesh.VertexHandle>();

		int segments = GetCircleSegmentCount();
		float step = 360f / segments;
		Vector3 up = Vector3.Up;

		float innerR = Radius;
		float outerR = Radius + SidewalkWidth;
		float heightUV = SidewalkHeight / SidewalkTextureRepeat;

		for (int i = 0; i < segments; i++)
		{
			float a0 = i * step;
			float a1 = (i + 1) * step;

			if (ArcBlockedByExit(a0, a1))
				continue;

			float arcDist0 = innerR * (a0 * MathF.PI / 180f);
			float arcDist1 = innerR * (a1 * MathF.PI / 180f);

			float v0 = arcDist0 / SidewalkTextureRepeat;
			float v1 = arcDist1 / SidewalkTextureRepeat;

			Vector3 d0V = Rotation.FromYaw(a0).Forward;
			Vector3 d1V = Rotation.FromYaw(a1).Forward;

			Vector3 i0 = d0V * innerR;
			Vector3 i1 = d1V * innerR;
			Vector3 o0 = d0V * outerR;
			Vector3 o1 = d1V * outerR;

			var vI0 = MeshUtility.GetOrAddVertex(_Mesh, cache, i0);
			var vI1 = MeshUtility.GetOrAddVertex(_Mesh, cache, i1);
			var vO0 = MeshUtility.GetOrAddVertex(_Mesh, cache, o0);
			var vO1 = MeshUtility.GetOrAddVertex(_Mesh, cache, o1);
			var vTI0 = MeshUtility.GetOrAddVertex(_Mesh, cache, i0 + up * SidewalkHeight);
			var vTI1 = MeshUtility.GetOrAddVertex(_Mesh, cache, i1 + up * SidewalkHeight);
			var vTO0 = MeshUtility.GetOrAddVertex(_Mesh, cache, o0 + up * SidewalkHeight);
			var vTO1 = MeshUtility.GetOrAddVertex(_Mesh, cache, o1 + up * SidewalkHeight);

			// Top face
			MeshUtility.AddTexturedQuad(_Mesh, _Material, vTO0, vTO1, vTI1, vTI0,
				new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1), new Vector2(0, v0));

			// Curb face
			MeshUtility.AddTexturedQuad(_Mesh, _Material, vTI0, vTI1, vI1, vI0,
				new Vector2(0, v0), new Vector2(0, v1), new Vector2(heightUV, v1), new Vector2(heightUV, v0));
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

			if (AngleDelta(_A0, ea) < halfAngle || AngleDelta(_A1, ea) < halfAngle)
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
