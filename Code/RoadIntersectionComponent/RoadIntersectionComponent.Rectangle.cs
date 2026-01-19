using System;
using System.Numerics;
using Sandbox;

namespace RedSnail.RoadTool;

[Flags]
public enum RectangleExit
{
	None = 0,
	North = 1 << 0, // +Forward
	East = 1 << 1, // +Right
	South = 1 << 2, // -Forward
	West = 1 << 3  // -Right
}

public partial class RoadIntersectionComponent
{
	private static readonly float QuarterTurnRad = MathF.PI * 0.5f;

	[Property, ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private float Width { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 500.0f;
	[Property, ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private float Length { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 500.0f;
	[Property, ShowIf(nameof(Shape), IntersectionShape.Rectangle), Range(0, 16), Step(2)] private int CornerSegments { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 8;
	[Property(Title = "Exits"), ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private RectangleExit RectangleExits { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = (RectangleExit)15;



	private void BuildRectangleRoad()
	{
		bool n = RectangleExits.HasFlag(RectangleExit.North);
		bool s = RectangleExits.HasFlag(RectangleExit.South);
		bool e = RectangleExits.HasFlag(RectangleExit.East);
		bool w = RectangleExits.HasFlag(RectangleExit.West);

		int exitCount = BitOperations.PopCount((uint)RectangleExits);

		int cornerQuads = 0;

		if (CornerSegments > 0)
		{
			if (n && e) cornerQuads += CornerSegments;
			if (n && w) cornerQuads += CornerSegments;
			if (s && e) cornerQuads += CornerSegments;
			if (s && w) cornerQuads += CornerSegments;
		}

		m_MeshBuilder.InitSubmesh
		(
			"intersection_road",
			(1 + exitCount + cornerQuads) * 4,
			(1 + exitCount + cornerQuads) * 6,
			RoadMaterial ?? Material.Load("materials/dev/reflectivity_30.vmat"),
			_HasCollision: true
		);

		Vector3 right = WorldRotation.Right;
		Vector3 forward = WorldRotation.Forward;
		Vector3 up = WorldRotation.Up;

		float hw = Width * 0.5f;
		float hl = Length * 0.5f;

		Vector3 pSW = -right * hw - forward * hl;
		Vector3 pNW = -right * hw + forward * hl;
		Vector3 pNE = right * hw + forward * hl;
		Vector3 pSE = right * hw - forward * hl;

		// Main center quad
		m_MeshBuilder.AddQuad
		(
			"intersection_road",
			pSE, pNE, pNW, pSW,
			up,
			forward,
			new Vector2(pSE.x, pSE.y) / RoadTextureRepeat,
			new Vector2(pNE.x, pNE.y) / RoadTextureRepeat,
			new Vector2(pNW.x, pNW.y) / RoadTextureRepeat,
			new Vector2(pSW.x, pSW.y) / RoadTextureRepeat
		);

		// Road extensions for exits
		if (n) AddRoadExtension(pNW, pNE, forward);
		if (s) AddRoadExtension(pSE, pSW, -forward);
		if (e) AddRoadExtension(pNE, pSE, right);
		if (w) AddRoadExtension(pSW, pNW, -right);

		// Corner road fillers
		if (CornerSegments > 0)
		{
			if (n && e) AddRoadCornerFiller(pNE, right, forward);
			if (n && w) AddRoadCornerFiller(pNW, -right, forward);
			if (s && e) AddRoadCornerFiller(pSE, right, -forward);
			if (s && w) AddRoadCornerFiller(pSW, -right, -forward);
		}
	}



	private void AddRoadExtension(Vector3 _CornerA, Vector3 _CornerB, Vector3 _Direction)
	{
		Vector3 up = WorldRotation.Up;

		Vector3 extA = _CornerA + _Direction * SidewalkWidth;
		Vector3 extB = _CornerB + _Direction * SidewalkWidth;

		m_MeshBuilder.AddQuad
		(
			"intersection_road",
			_CornerB, extB, extA, _CornerA,
			up,
			_Direction,
			new Vector2(_CornerB.x, _CornerB.y) / RoadTextureRepeat,
			new Vector2(extB.x, extB.y) / RoadTextureRepeat,
			new Vector2(extA.x, extA.y) / RoadTextureRepeat,
			new Vector2(_CornerA.x, _CornerA.y) / RoadTextureRepeat
		);
	}



	private void AddRoadCornerFiller(Vector3 _Corner, Vector3 _DirA, Vector3 _DirB)
	{
		Vector3 up = WorldRotation.Up;
		float w = SidewalkWidth;

		Vector3 arcCenter = _Corner + _DirA * w + _DirB * w;

		Vector3 cross = Vector3.Cross(_DirA, _DirB);
		bool flip = Vector3.Dot(cross, up) >= 0;

		Vector3 tangent = WorldRotation.Right;
		
		for (int i = 0; i < CornerSegments; i++)
		{
			float t0 = (float)i / CornerSegments;
			float t1 = (float)(i + 1) / CornerSegments;

			float angle0 = t0 * QuarterTurnRad;
			float angle1 = t1 * QuarterTurnRad;

			Vector3 roadEdge0 = arcCenter - _DirB * w * MathF.Cos(angle0) - _DirA * w * MathF.Sin(angle0);
			Vector3 roadEdge1 = arcCenter - _DirB * w * MathF.Cos(angle1) - _DirA * w * MathF.Sin(angle1);

			Vector2 uvCenter = new Vector2(_Corner.x, _Corner.y) / RoadTextureRepeat;
			Vector2 uvEdge0 = new Vector2(roadEdge0.x, roadEdge0.y) / RoadTextureRepeat;
			Vector2 uvEdge1 = new Vector2(roadEdge1.x, roadEdge1.y) / RoadTextureRepeat;

			if (flip)
			{
				m_MeshBuilder.AddTriangle
				(
					"intersection_road",
					_Corner, roadEdge0, roadEdge1,
					up, tangent,
					uvCenter, uvEdge0, uvEdge1
				);
			}
			else
			{
				m_MeshBuilder.AddTriangle
				(
					"intersection_road",
					_Corner, roadEdge1, roadEdge0,
					up, tangent,
					uvCenter, uvEdge1, uvEdge0
				);
			}
		}
	}



	private void BuildRectangleSidewalk()
	{
		bool n = RectangleExits.HasFlag(RectangleExit.North);
		bool s = RectangleExits.HasFlag(RectangleExit.South);
		bool e = RectangleExits.HasFlag(RectangleExit.East);
		bool w = RectangleExits.HasFlag(RectangleExit.West);

		int sideCount = 4 - BitOperations.PopCount((uint)RectangleExits);

		int endCapCount = 0;

		if (n) endCapCount += 2;
		if (s) endCapCount += 2;
		if (e) endCapCount += 2;
		if (w) endCapCount += 2;

		int cornerSegments = 0;

		if (CornerSegments > 0)
		{
			if (n && e) cornerSegments += CornerSegments;
			if (n && w) cornerSegments += CornerSegments;
			if (s && e) cornerSegments += CornerSegments;
			if (s && w) cornerSegments += CornerSegments;
		}

		int sideVerts = sideCount * 12;
		int sideIndices = sideCount * 18;

		int endCapVerts = endCapCount * 4;
		int endCapIndices = endCapCount * 6;

		int cornerVerts;
		int cornerIndices;

		if (CornerSegments > 0)
		{
			cornerVerts = cornerSegments * 12;
			cornerIndices = cornerSegments * 18;

			int remainingCorners = 4 - (cornerSegments / CornerSegments);
			cornerVerts += remainingCorners * 22;
			cornerIndices += remainingCorners * 30;
		}
		else
		{
			cornerVerts = 4 * 22;
			cornerIndices = 4 * 30;
		}

		int totalVertices = sideVerts + endCapVerts + cornerVerts;
		int totalIndices = sideIndices + endCapIndices + cornerIndices;

		m_MeshBuilder.InitSubmesh
		(
			"intersection_sidewalk",
			totalVertices,
			totalIndices,
			SidewalkMaterial ?? Material.Load("materials/dev/reflectivity_70.vmat"),
			true
		);

		Vector3 right = WorldRotation.Right;
		Vector3 forward = WorldRotation.Forward;
		float hw = Width * 0.5f;
		float hl = Length * 0.5f;

		Vector3 pSW = -right * hw - forward * hl;
		Vector3 pNW = -right * hw + forward * hl;
		Vector3 pNE = right * hw + forward * hl;
		Vector3 pSE = right * hw - forward * hl;

		// Build sidewalk strips
		if (!n) AddSidewalkStrip(pNE, pNW, forward);
		if (!s) AddSidewalkStrip(pSW, pSE, -forward);
		if (!e) AddSidewalkStrip(pSE, pNE, right);
		if (!w) AddSidewalkStrip(pNW, pSW, -right);

		// Build corner caps
		if (CornerSegments > 0)
		{
			if (n && e) AddRoundedSidewalkCorner(pNE, right, forward);
			else AddCornerCap(pNE, right, forward, e, n);

			if (n && w) AddRoundedSidewalkCorner(pNW, -right, forward);
			else AddCornerCap(pNW, -right, forward, w, n);

			if (s && e) AddRoundedSidewalkCorner(pSE, right, -forward);
			else AddCornerCap(pSE, right, -forward, e, s);

			if (s && w) AddRoundedSidewalkCorner(pSW, -right, -forward);
			else AddCornerCap(pSW, -right, -forward, w, s);
		}
		else
		{
			AddCornerCap(pNE, right, forward, e, n);
			AddCornerCap(pNW, -right, forward, w, n);
			AddCornerCap(pSE, right, -forward, e, s);
			AddCornerCap(pSW, -right, -forward, w, s);
		}
	}



	private void AddRoundedSidewalkCorner(Vector3 _Corner, Vector3 _DirA, Vector3 _DirB)
	{
		Vector3 up = WorldRotation.Up;
		float h = SidewalkHeight;
		float w = SidewalkWidth;

		Vector3 cross = Vector3.Cross(_DirA, _DirB);
		bool flip = Vector3.Dot(cross, up) >= 0;

		Vector3 arcCenter = _Corner + _DirA * w + _DirB * w;

		float totalArcLength = w * QuarterTurnRad;
		float uW = w / SidewalkTextureRepeat;
		float hH = h / SidewalkTextureRepeat;

		for (int i = 0; i < CornerSegments; i++)
		{
			float t0 = (float)i / CornerSegments;
			float t1 = (float)(i + 1) / CornerSegments;

			float angle0 = float.DegreesToRadians(t0 * 90.0f);
			float angle1 = float.DegreesToRadians(t1 * 90.0f);

			Vector3 inner0 = arcCenter - _DirB * w * MathF.Cos(angle0) - _DirA * w * MathF.Sin(angle0);
			Vector3 inner1 = arcCenter - _DirB * w * MathF.Cos(angle1) - _DirA * w * MathF.Sin(angle1);

			Vector3 outer0, outer1;

			if (t1 <= 0.5f)
			{
				outer0 = _Corner + _DirA * w + _DirB * w * (t0 * 2.0f);
				outer1 = _Corner + _DirA * w + _DirB * w * (t1 * 2.0f);
			}
			else if (t0 >= 0.5f)
			{
				outer0 = _Corner + _DirA * w * (1.0f - (t0 - 0.5f) * 2.0f) + _DirB * w;
				outer1 = _Corner + _DirA * w * (1.0f - (t1 - 0.5f) * 2.0f) + _DirB * w;
			}
			else
			{
				outer0 = _Corner + _DirA * w + _DirB * w * (t0 * 2.0f);
				outer1 = _Corner + _DirA * w * (1.0f - (t1 - 0.5f) * 2.0f) + _DirB * w;
			}

			Vector3 topInner0 = inner0 + up * h;
			Vector3 topInner1 = inner1 + up * h;
			Vector3 topOuter0 = outer0 + up * h;
			Vector3 topOuter1 = outer1 + up * h;

			Vector3 n0 = (inner0 - arcCenter).Normal;
			Vector3 n1 = (inner1 - arcCenter).Normal;

			Vector3 tangent = (inner1 - inner0).Normal;

			float v0 = (t0 * totalArcLength) / SidewalkTextureRepeat;
			float v1 = (t1 * totalArcLength) / SidewalkTextureRepeat;

			float distOuter0 = Vector3.DistanceBetween(inner0, outer0) / SidewalkTextureRepeat;
			float distOuter1 = Vector3.DistanceBetween(inner1, outer1) / SidewalkTextureRepeat;

			Vector3 outerTangent = (outer1 - outer0).Normal;
			Vector3 outerNormal = flip ? Vector3.Cross(outerTangent, up) : Vector3.Cross(up, outerTangent);

			Vector3 tangentTop = WorldRotation.Right;
			
			if (flip)
			{
				// Top face
				m_MeshBuilder.AddQuad
				(
					"intersection_sidewalk",
					topOuter0, topOuter1, topInner1, topInner0,
					up,
					tangentTop,
					new Vector2(distOuter0, v0), new Vector2(distOuter1, v1), new Vector2(0, v1), new Vector2(0, v0)
				);

				// Inner face
				m_MeshBuilder.AddQuad
				(
					"intersection_sidewalk",
					topInner0, topInner1, inner1, inner0,
					n0, n1, n1, n0,
					tangent,
					new Vector2(0, v0), new Vector2(0, v1), new Vector2(hH, v1), new Vector2(hH, v0)
				);

				// Outer face
				m_MeshBuilder.AddQuad
				(
					"intersection_sidewalk",
					topOuter1, topOuter0, outer0, outer1,
					outerNormal,
					-outerTangent,
					new Vector2(0, v1), new Vector2(0, v0), new Vector2(hH, v0), new Vector2(hH, v1)
				);
			}
			else
			{
				// Top face
				m_MeshBuilder.AddQuad
				(
					"intersection_sidewalk",
					topInner0, topInner1, topOuter1, topOuter0,
					up,
					tangentTop,
					new Vector2(0, v0), new Vector2(0, v1), new Vector2(distOuter1, v1), new Vector2(distOuter0, v0)
				);

				// Inner face
				m_MeshBuilder.AddQuad
				(
					"intersection_sidewalk",
					topInner1, topInner0, inner0, inner1,
					n1, n0, n0, n1,
					-tangent,
					new Vector2(0, v1), new Vector2(0, v0), new Vector2(hH, v0), new Vector2(hH, v1)
				);

				// Outer face
				m_MeshBuilder.AddQuad
				(
					"intersection_sidewalk",
					topOuter0, topOuter1, outer1, outer0,
					outerNormal,
					outerTangent,
					new Vector2(0, v1), new Vector2(0, v0), new Vector2(hH, v0), new Vector2(hH, v1)
				);
			}
		}
	}



	private void AddCornerCap(Vector3 _CornerPos, Vector3 _DirA, Vector3 _DirB, bool _SideAIsExit, bool _SideBIsExit)
	{
		Vector3 up = WorldRotation.Up;
		float h = SidewalkHeight;
		float w = SidewalkWidth;

		Vector3 pCenter = _CornerPos;
		Vector3 pA = _CornerPos + _DirA * w;
		Vector3 pB = _CornerPos + _DirB * w;
		Vector3 pOuter = _CornerPos + (_DirA * w) + (_DirB * w);

		Vector3 tCenter = pCenter + up * h;
		Vector3 tA = pA + up * h;
		Vector3 tB = pB + up * h;
		Vector3 tOuter = pOuter + up * h;

		Vector3 cross = Vector3.Cross(_DirA, _DirB);
		bool flip = Vector3.Dot(cross, up) >= 0;

		float uW = SidewalkWidth / SidewalkTextureRepeat;
		float hH = SidewalkHeight / SidewalkTextureRepeat;

		Vector2 uvA = !_SideAIsExit ? new Vector2(uW, 0) : new Vector2(0, uW);
		Vector2 uvB = !_SideBIsExit ? new Vector2(uW, 0) : new Vector2(0, uW);

		Vector3 tangentTop = WorldRotation.Right;

		if (flip)
		{
			// Top face
			m_MeshBuilder.AddTriangle("intersection_sidewalk", tOuter, tCenter, tA, up, tangentTop,
				new Vector2(uW, uW), new Vector2(0, 0), uvA);
			m_MeshBuilder.AddTriangle("intersection_sidewalk", tOuter, tB, tCenter, up, tangentTop,
				new Vector2(uW, uW), uvB, new Vector2(0, 0));

			// Outer faces
			m_MeshBuilder.AddQuad("intersection_sidewalk", tA, pA, pOuter, tOuter, _DirA, _DirB,
				new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));

			m_MeshBuilder.AddQuad("intersection_sidewalk", tOuter, pOuter, pB, tB, _DirB, -_DirA,
				new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));
		}
		else
		{
			// Top face
			m_MeshBuilder.AddTriangle("intersection_sidewalk", tOuter, tA, tCenter, up, tangentTop,
				new Vector2(uW, uW), uvA, new Vector2(0, 0));
			m_MeshBuilder.AddTriangle("intersection_sidewalk", tOuter, tCenter, tB, up, tangentTop,
				new Vector2(uW, uW), new Vector2(0, 0), uvB);

			// Outer faces
			m_MeshBuilder.AddQuad("intersection_sidewalk", tOuter, pOuter, pA, tA, _DirA, -_DirB,
				new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));

			m_MeshBuilder.AddQuad("intersection_sidewalk", tB, pB, pOuter, tOuter, _DirB, _DirA,
				new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));
		}

		if (_SideAIsExit)
		{
			if (flip)
				m_MeshBuilder.AddQuad("intersection_sidewalk", tA, tCenter, pCenter, pA, -_DirB, -_DirA,
					new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0));
			else
				m_MeshBuilder.AddQuad("intersection_sidewalk", tCenter, tA, pA, pCenter, -_DirB, _DirA,
					new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0));
		}

		if (_SideBIsExit)
		{
			if (flip)
				m_MeshBuilder.AddQuad("intersection_sidewalk", tCenter, tB, pB, pCenter, -_DirA, _DirB,
					new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0));
			else
				m_MeshBuilder.AddQuad("intersection_sidewalk", tB, tCenter, pCenter, pB, -_DirA, -_DirB,
					new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0));
		}
	}



	private void AddSidewalkStrip(Vector3 _Start, Vector3 _End, Vector3 _Outward)
	{
		Vector3 up = WorldRotation.Up;
		Vector3 forward = (_End - _Start).Normal;

		Vector3 s0 = _Start;
		Vector3 s1 = _End;
		Vector3 o0 = s0 + _Outward * SidewalkWidth;
		Vector3 o1 = s1 + _Outward * SidewalkWidth;

		Vector3 t0 = s0 + up * SidewalkHeight;
		Vector3 t1 = s1 + up * SidewalkHeight;
		Vector3 ot0 = o0 + up * SidewalkHeight;
		Vector3 ot1 = o1 + up * SidewalkHeight;

		float stripLen = (_End - _Start).Length;
		float uWidth = SidewalkWidth / SidewalkTextureRepeat;
		float vLen = stripLen / SidewalkTextureRepeat;
		float hHeight = SidewalkHeight / SidewalkTextureRepeat;

		Vector3 tangentTop = WorldRotation.Right;
		
		// Top face
		m_MeshBuilder.AddQuad("intersection_sidewalk", ot0, ot1, t1, t0, up, tangentTop,
			new Vector2(uWidth, 0), new Vector2(uWidth, vLen), new Vector2(0, vLen), new Vector2(0, 0));

		// Inner face
		Vector3 innerNormal = -_Outward;
		m_MeshBuilder.AddQuad("intersection_sidewalk", t0, t1, s1, s0, innerNormal, forward,
			new Vector2(0, 0), new Vector2(0, vLen), new Vector2(hHeight, vLen), new Vector2(hHeight, 0));

		// Outer face
		Vector3 outerNormal = _Outward;
		m_MeshBuilder.AddQuad("intersection_sidewalk", ot1, ot0, o0, o1, outerNormal, -forward,
			new Vector2(0, 0), new Vector2(0, vLen), new Vector2(hHeight, vLen), new Vector2(hHeight, 0));
	}



	private Transform GetRectangleExitLocalTransform(RectangleExit _Side, bool _IncludeSidewalk = false)
	{
		Vector3 pos = Vector3.Zero;
		Rotation rot = Rotation.Identity;

		switch (_Side)
		{
			case RectangleExit.North:
				pos += LocalRotation.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				break;
			case RectangleExit.South:
				pos -= LocalRotation.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(180);
				break;
			case RectangleExit.East:
				pos += LocalRotation.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(-90);
				break;
			case RectangleExit.West:
				pos -= LocalRotation.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(90);
				break;
		}

		return new Transform { Position = pos, Rotation = rot };
	}



	private Transform GetRectangleExitTransform(RectangleExit _Side, bool _IncludeSidewalk = false)
	{
		Vector3 pos = WorldPosition;
		Rotation rot = WorldRotation;

		switch (_Side)
		{
			case RectangleExit.North:
				pos += WorldRotation.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				break;
			case RectangleExit.South:
				pos -= WorldRotation.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(180);
				break;
			case RectangleExit.East:
				pos += WorldRotation.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(-90);
				break;
			case RectangleExit.West:
				pos -= WorldRotation.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(90);
				break;
		}

		return new Transform { Position = pos, Rotation = rot };
	}
}
