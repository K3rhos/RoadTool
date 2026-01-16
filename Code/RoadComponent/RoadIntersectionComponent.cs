using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public enum IntersectionShape
{
	/// <summary>
	/// (WIP) Rectangle mode is work in progress and is gonna get reworked.
	/// </summary>
	Rectangle,
	
	/// <summary>
	/// Circle mode is almost finished, just need to create the system to link the intersection and the road.
	/// </summary>
	Circle
}

public class IntersectionExit
{
	[Property] public float AngleDegrees { get; set; } = 0f;
	[Property] public float RoadWidth { get; set; } = 500f;
	[Property] public bool HasSidewalk { get; set; } = true;
}

public partial class RoadIntersectionComponent : Component, Component.ExecuteInEditor
{
	private MeshBuilder m_MeshBuilder;

	[Property(Title = "Road Material")]
	public Material RoadMaterial { get; set { field = value; m_MeshBuilder?.IsDirty = true; } }

	[Property(Title = "Sidewalk Material")]
	public Material SidewalkMaterial { get; set { field = value; m_MeshBuilder?.IsDirty = true; } }

	[Property]
	public IntersectionShape Shape { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = IntersectionShape.Rectangle;

	[Property, ShowIf(nameof(Shape), IntersectionShape.Rectangle)]
	public float Width { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 500f;

	[Property, ShowIf(nameof(Shape), IntersectionShape.Rectangle)]
	public float Length { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 500f;

	[Property, ShowIf(nameof(Shape), IntersectionShape.Circle)]
	public float Radius { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 600f;

	[Property, ShowIf(nameof(Shape), IntersectionShape.Circle)]
	public float Precision { get; set { field = value.Clamp(10.0f, 10000.0f); m_MeshBuilder?.IsDirty = true; } } = 40.0f;

	[Property]
	public float SidewalkWidth { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 50f;

	[Property]
	public float SidewalkHeight { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 5f;

	[Property]
	public float SidewalkTextureRepeat { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 100.0f;

	[Property]
	public List<IntersectionExit> Exits { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = new();



	protected override void OnEnabled()
	{
		m_MeshBuilder = new MeshBuilder(GameObject);
		m_MeshBuilder.OnBuild += BuildAllMeshes;
		m_MeshBuilder.Rebuild();
	}



	protected override void OnDisabled()
	{
		m_MeshBuilder?.OnBuild -= BuildAllMeshes;
		m_MeshBuilder?.Clear();
	}



	protected override void OnUpdate()
	{
		m_MeshBuilder?.Update();
	}



	private void BuildAllMeshes()
	{
		BuildRoadSurface();
		BuildSidewalk();
	}



	private void BuildRoadSurface()
	{
		if (Shape == IntersectionShape.Rectangle)
			BuildRectangleRoad();
		else
			BuildCircleRoad();
	}



	private void BuildRectangleRoad()
	{
		m_MeshBuilder.InitSubmesh(
			"intersection_road",
			4,
			6,
			RoadMaterial ?? Material.Load("materials/dev/reflectivity_30.vmat"),
			_HasCollision: true
		);

		Vector3 right = WorldRotation.Right;
		Vector3 forward = WorldRotation.Forward;
		Vector3 up = WorldRotation.Up;

		Vector3 p0 = -right * Width * 0.5f - forward * Length * 0.5f;
		Vector3 p1 = -right * Width * 0.5f + forward * Length * 0.5f;
		Vector3 p2 = right * Width * 0.5f + forward * Length * 0.5f;
		Vector3 p3 = right * Width * 0.5f - forward * Length * 0.5f;

		Vector3 tangent = (p1 - p0).Normal;

		m_MeshBuilder.AddQuad(
			"intersection_road",
			p3, p2, p1, p0,
			up, tangent,
			new Vector2(0, 0),
			new Vector2(0, 1),
			new Vector2(1, 1),
			new Vector2(1, 0)
		);
	}



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
			if (ArcBlockedByExit(a0, a1)) continue;

			Vector3 d0 = Rotation.FromYaw(a0).Forward;
			Vector3 d1 = Rotation.FromYaw(a1).Forward;

			m_MeshBuilder.AddTriangle("intersection_road", Vector3.Zero, d0 * Radius, d1 * Radius,
				up, d0, Vector2.Zero, new Vector2(0, 1), new Vector2(1, 1));
		}
	}



	private void BuildSidewalk()
	{
		if (SidewalkWidth <= 0 || SidewalkHeight <= 0)
			return;

		if (Shape == IntersectionShape.Rectangle)
			BuildRectangleSidewalk();
		else
			BuildCircleSidewalk();
	}



	private void BuildRectangleSidewalk()
	{
		m_MeshBuilder.InitSubmesh(
			"intersection_sidewalk",
			32,
			48,
			SidewalkMaterial ?? Material.Load("materials/dev/reflectivity_70.vmat"),
			_HasCollision: true
		);

		Vector3 right = WorldRotation.Right;
		Vector3 forward = WorldRotation.Forward;

		float hw = Width * 0.5f;
		float hl = Length * 0.5f;

		AddSidewalkStrip(
			-right * hw - forward * hl,
			right * hw - forward * hl,
			-forward
		);

		AddSidewalkStrip(
			right * hw - forward * hl,
			right * hw + forward * hl,
			right
		);

		AddSidewalkStrip(
			right * hw + forward * hl,
			-right * hw + forward * hl,
			forward
		);

		AddSidewalkStrip(
			-right * hw + forward * hl,
			-right * hw - forward * hl,
			-right
		);
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
	
	
	
	private void AddSidewalkStrip(Vector3 _Start, Vector3 _End, Vector3 _Outward)
	{
		Vector3 up = WorldRotation.Up;
		Vector3 forward = (_End - _Start).Normal;

		// Inner points (at road level)
		Vector3 s0 = _Start;
		Vector3 s1 = _End;

		// Outer points (at road level)
		Vector3 o0 = s0 + _Outward * SidewalkWidth;
		Vector3 o1 = s1 + _Outward * SidewalkWidth;

		// Top points (inner edge)
		Vector3 t0 = s0 + up * SidewalkHeight;
		Vector3 t1 = s1 + up * SidewalkHeight;

		// Top points (outer edge)
		Vector3 ot0 = o0 + up * SidewalkHeight;
		Vector3 ot1 = o1 + up * SidewalkHeight;

		// Top face
		m_MeshBuilder.AddQuad("intersection_sidewalk", ot0, ot1, t1, t0, up, forward,
			new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));

		// Curb face (The vertical side facing the road)
		Vector3 curbNormal = -_Outward;
		m_MeshBuilder.AddQuad("intersection_sidewalk", t0, t1, s1, s0, curbNormal, forward,
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
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
		foreach (var exit in Exits)
		{
			float halfAngle = float.Atan(exit.RoadWidth / Radius).RadianToDegree();
			float ea = exit.AngleDegrees;

			if (AngleDelta(_A0, ea) < halfAngle ||
				AngleDelta(_A1, ea) < halfAngle)
				return true;
		}

		return false;
	}
	
	
	
	public Transform GetExitTransform(int _Index)
	{
		var exit = Exits[_Index];

		Vector3 dir = Rotation.FromYaw(exit.AngleDegrees).Forward;

		float dist = Shape == IntersectionShape.Circle ? Radius : Math.Max(Width, Length) * 0.5f;

		return new Transform
		{
			Position = WorldPosition + dir * dist,
			Rotation = Rotation.LookAt(dir, WorldRotation.Up)
		};
	}
}
