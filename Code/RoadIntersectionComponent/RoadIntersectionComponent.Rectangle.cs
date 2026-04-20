using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

[Flags]
public enum RectangleExit
{
	None = 0,
	North = 1 << 0, // +Forward
	East = 1 << 1,  // +Right
	South = 1 << 2, // -Forward
	West = 1 << 3   // -Right
}

public partial class RoadIntersectionComponent
{
	private static readonly float QuarterTurnRad = MathF.PI * 0.5f;

	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private float Width { get; set { field = value; m_IsDirty = true; } } = 500.0f;
	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private float Length { get; set { field = value; m_IsDirty = true; } } = 500.0f;
	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle), Range(0, 16), Step(2)] private int CornerSegments { get; set { field = value; m_IsDirty = true; } } = 8;
	[Property(Title = "Exits"), Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private RectangleExit RectangleExits { get; set { field = value; m_IsDirty = true; } } = (RectangleExit)15;



	private void BuildRectangleRoad(PolygonMesh _Mesh, Material _Material)
	{
		var cache = new Dictionary<Vector3, HalfEdgeMesh.VertexHandle>();

		bool n = RectangleExits.HasFlag(RectangleExit.North);
		bool s = RectangleExits.HasFlag(RectangleExit.South);
		bool e = RectangleExits.HasFlag(RectangleExit.East);
		bool w = RectangleExits.HasFlag(RectangleExit.West);

		Vector3 right = Vector3.Right;
		Vector3 forward = Vector3.Forward;

		float hw = Width * 0.5f;
		float hl = Length * 0.5f;

		Vector3 pSW = -right * hw - forward * hl;
		Vector3 pNW = -right * hw + forward * hl;
		Vector3 pNE = right * hw + forward * hl;
		Vector3 pSE = right * hw - forward * hl;

		var vSW = MeshUtility.GetOrAddVertex(_Mesh, cache, pSW);
		var vNW = MeshUtility.GetOrAddVertex(_Mesh, cache, pNW);
		var vNE = MeshUtility.GetOrAddVertex(_Mesh, cache, pNE);
		var vSE = MeshUtility.GetOrAddVertex(_Mesh, cache, pSE);

		// Main center quad
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vSE, vNE, vNW, vSW,
			new Vector2(pSE.x, pSE.y) / RoadTextureRepeat,
			new Vector2(pNE.x, pNE.y) / RoadTextureRepeat,
			new Vector2(pNW.x, pNW.y) / RoadTextureRepeat,
			new Vector2(pSW.x, pSW.y) / RoadTextureRepeat);

		if (n) AddRoadExtension(_Mesh, _Material, cache, pNW, pNE, forward);
		if (s) AddRoadExtension(_Mesh, _Material, cache, pSE, pSW, -forward);
		if (e) AddRoadExtension(_Mesh, _Material, cache, pNE, pSE, right);
		if (w) AddRoadExtension(_Mesh, _Material, cache, pSW, pNW, -right);

		if (CornerSegments > 0)
		{
			if (n && e) AddRoadCornerFiller(_Mesh, _Material, cache, pNE, right, forward);
			if (n && w) AddRoadCornerFiller(_Mesh, _Material, cache, pNW, -right, forward);
			if (s && e) AddRoadCornerFiller(_Mesh, _Material, cache, pSE, right, -forward);
			if (s && w) AddRoadCornerFiller(_Mesh, _Material, cache, pSW, -right, -forward);
		}
	}



	private void AddRoadExtension(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _CornerA, Vector3 _CornerB, Vector3 _Direction)
	{
		Vector3 extA = _CornerA + _Direction * SidewalkWidth;
		Vector3 extB = _CornerB + _Direction * SidewalkWidth;

		var vCA = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _CornerA);
		var vCB = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _CornerB);
		var vExtA = MeshUtility.GetOrAddVertex(_Mesh, _Cache, extA);
		var vExtB = MeshUtility.GetOrAddVertex(_Mesh, _Cache, extB);

		MeshUtility.AddTexturedQuad(_Mesh, _Material, vCB, vExtB, vExtA, vCA,
			new Vector2(_CornerB.x, _CornerB.y) / RoadTextureRepeat,
			new Vector2(extB.x, extB.y) / RoadTextureRepeat,
			new Vector2(extA.x, extA.y) / RoadTextureRepeat,
			new Vector2(_CornerA.x, _CornerA.y) / RoadTextureRepeat);
	}



	private void AddRoadCornerFiller(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _Corner, Vector3 _DirA, Vector3 _DirB)
	{
		Vector3 up = Vector3.Up;
		float w = SidewalkWidth;

		Vector3 arcCenter = _Corner + _DirA * w + _DirB * w;

		Vector3 cross = Vector3.Cross(_DirA, _DirB);
		bool flip = Vector3.Dot(cross, up) >= 0;

		var vCorner = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _Corner);

		for (int i = 0; i < CornerSegments; i++)
		{
			float t0 = (float)i / CornerSegments;
			float t1 = (float)(i + 1) / CornerSegments;

			float angle0 = t0 * QuarterTurnRad;
			float angle1 = t1 * QuarterTurnRad;

			Vector3 roadEdge0 = arcCenter - _DirB * w * MathF.Cos(angle0) - _DirA * w * MathF.Sin(angle0);
			Vector3 roadEdge1 = arcCenter - _DirB * w * MathF.Cos(angle1) - _DirA * w * MathF.Sin(angle1);

			var vEdge0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, roadEdge0);
			var vEdge1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, roadEdge1);

			Vector2 uvCorner = new Vector2(_Corner.x, _Corner.y) / RoadTextureRepeat;
			Vector2 uvEdge0 = new Vector2(roadEdge0.x, roadEdge0.y) / RoadTextureRepeat;
			Vector2 uvEdge1 = new Vector2(roadEdge1.x, roadEdge1.y) / RoadTextureRepeat;

			if (flip)
				MeshUtility.AddTexturedTriangle(_Mesh, _Material, vCorner, vEdge0, vEdge1, uvCorner, uvEdge0, uvEdge1);
			else
				MeshUtility.AddTexturedTriangle(_Mesh, _Material, vCorner, vEdge1, vEdge0, uvCorner, uvEdge1, uvEdge0);
		}
	}



	private void BuildRectangleSidewalk(PolygonMesh _Mesh, Material _Material)
	{
		var cache = new Dictionary<Vector3, HalfEdgeMesh.VertexHandle>();

		bool n = RectangleExits.HasFlag(RectangleExit.North);
		bool s = RectangleExits.HasFlag(RectangleExit.South);
		bool e = RectangleExits.HasFlag(RectangleExit.East);
		bool w = RectangleExits.HasFlag(RectangleExit.West);

		Vector3 right = Vector3.Right;
		Vector3 forward = Vector3.Forward;
		float hw = Width * 0.5f;
		float hl = Length * 0.5f;

		Vector3 pSW = -right * hw - forward * hl;
		Vector3 pNW = -right * hw + forward * hl;
		Vector3 pNE = right * hw + forward * hl;
		Vector3 pSE = right * hw - forward * hl;

		if (!n) AddSidewalkStrip(_Mesh, _Material, cache, pNE, pNW, forward);
		if (!s) AddSidewalkStrip(_Mesh, _Material, cache, pSW, pSE, -forward);
		if (!e) AddSidewalkStrip(_Mesh, _Material, cache, pSE, pNE, right);
		if (!w) AddSidewalkStrip(_Mesh, _Material, cache, pNW, pSW, -right);

		if (CornerSegments > 0)
		{
			if (n && e) AddRoundedSidewalkCorner(_Mesh, _Material, cache, pNE, right, forward);
			else AddCornerCap(_Mesh, _Material, cache, pNE, right, forward, e, n);

			if (n && w) AddRoundedSidewalkCorner(_Mesh, _Material, cache, pNW, -right, forward);
			else AddCornerCap(_Mesh, _Material, cache, pNW, -right, forward, w, n);

			if (s && e) AddRoundedSidewalkCorner(_Mesh, _Material, cache, pSE, right, -forward);
			else AddCornerCap(_Mesh, _Material, cache, pSE, right, -forward, e, s);

			if (s && w) AddRoundedSidewalkCorner(_Mesh, _Material, cache, pSW, -right, -forward);
			else AddCornerCap(_Mesh, _Material, cache, pSW, -right, -forward, w, s);
		}
		else
		{
			AddCornerCap(_Mesh, _Material, cache, pNE, right, forward, e, n);
			AddCornerCap(_Mesh, _Material, cache, pNW, -right, forward, w, n);
			AddCornerCap(_Mesh, _Material, cache, pSE, right, -forward, e, s);
			AddCornerCap(_Mesh, _Material, cache, pSW, -right, -forward, w, s);
		}
	}



	private void AddRoundedSidewalkCorner(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _Corner, Vector3 _DirA, Vector3 _DirB)
	{
		Vector3 up = Vector3.Up;
		float h = SidewalkHeight;
		float w = SidewalkWidth;

		Vector3 cross = Vector3.Cross(_DirA, _DirB);
		bool flip = Vector3.Dot(cross, up) >= 0;

		Vector3 arcCenter = _Corner + _DirA * w + _DirB * w;

		float totalArcLength = w * QuarterTurnRad;
		float hH = h / SidewalkTextureRepeat;

		for (int i = 0; i < CornerSegments; i++)
		{
			float t0 = (float)i / CornerSegments;
			float t1 = (float)(i + 1) / CornerSegments;

			// At t=0 inner0==outer0, at t=1 inner1==outer1 — top face degenerates to triangle.
			bool startDeg = (i == 0);
			bool endDeg = (i == CornerSegments - 1);

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

			var vI0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, inner0);
			var vI1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, inner1);
			var vO0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, outer0);
			var vO1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, outer1);
			var vTI0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, topInner0);
			var vTI1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, topInner1);
			var vTO0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, topOuter0);
			var vTO1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, topOuter1);

			float v0 = (t0 * totalArcLength) / SidewalkTextureRepeat;
			float v1 = (t1 * totalArcLength) / SidewalkTextureRepeat;
			float distOuter0 = Vector3.DistanceBetween(inner0, outer0) / SidewalkTextureRepeat;
			float distOuter1 = Vector3.DistanceBetween(inner1, outer1) / SidewalkTextureRepeat;

			if (flip)
			{
				// Top face — triangle at arc endpoints where inner==outer
				if (startDeg)
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO0, vTO1, vTI1,
						new Vector2(distOuter0, v0), new Vector2(distOuter1, v1), new Vector2(0, v1));
				else if (endDeg)
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO0, vTO1, vTI0,
						new Vector2(distOuter0, v0), new Vector2(distOuter1, v1), new Vector2(0, v0));
				else
					MeshUtility.AddTexturedQuad(_Mesh, _Material, vTO0, vTO1, vTI1, vTI0,
						new Vector2(distOuter0, v0), new Vector2(distOuter1, v1), new Vector2(0, v1), new Vector2(0, v0));

				// Inner face
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTI0, vTI1, vI1, vI0,
					new Vector2(0, v0), new Vector2(0, v1), new Vector2(hH, v1), new Vector2(hH, v0));

				// Outer face
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTO1, vTO0, vO0, vO1,
					new Vector2(0, v1), new Vector2(0, v0), new Vector2(hH, v0), new Vector2(hH, v1));
			}
			else
			{
				// Top face — triangle at arc endpoints where inner==outer
				if (startDeg)
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTI0, vTI1, vTO1,
						new Vector2(0, v0), new Vector2(0, v1), new Vector2(distOuter1, v1));
				else if (endDeg)
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTI0, vTI1, vTO0,
						new Vector2(0, v0), new Vector2(0, v1), new Vector2(distOuter0, v0));
				else
					MeshUtility.AddTexturedQuad(_Mesh, _Material, vTI0, vTI1, vTO1, vTO0,
						new Vector2(0, v0), new Vector2(0, v1), new Vector2(distOuter1, v1), new Vector2(distOuter0, v0));

				// Inner face
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTI1, vTI0, vI0, vI1,
					new Vector2(0, v1), new Vector2(0, v0), new Vector2(hH, v0), new Vector2(hH, v1));

				// Outer face
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTO0, vTO1, vO1, vO0,
					new Vector2(0, v1), new Vector2(0, v0), new Vector2(hH, v0), new Vector2(hH, v1));
			}
		}
	}



	private void AddCornerCap(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _CornerPos, Vector3 _DirA, Vector3 _DirB, bool _SideAIsExit, bool _SideBIsExit)
	{
		Vector3 up = Vector3.Up;
		float h = SidewalkHeight;
		float w = SidewalkWidth;

		Vector3 pCenter = _CornerPos;
		Vector3 pA = _CornerPos + _DirA * w;
		Vector3 pB = _CornerPos + _DirB * w;
		Vector3 pOuter = _CornerPos + _DirA * w + _DirB * w;

		Vector3 tCenter = pCenter + up * h;
		Vector3 tA = pA + up * h;
		Vector3 tB = pB + up * h;
		Vector3 tOuter = pOuter + up * h;

		var vCenter = MeshUtility.GetOrAddVertex(_Mesh, _Cache, pCenter);
		var vA = MeshUtility.GetOrAddVertex(_Mesh, _Cache, pA);
		var vB = MeshUtility.GetOrAddVertex(_Mesh, _Cache, pB);
		var vOuter = MeshUtility.GetOrAddVertex(_Mesh, _Cache, pOuter);
		var vTC = MeshUtility.GetOrAddVertex(_Mesh, _Cache, tCenter);
		var vTA = MeshUtility.GetOrAddVertex(_Mesh, _Cache, tA);
		var vTB = MeshUtility.GetOrAddVertex(_Mesh, _Cache, tB);
		var vTO = MeshUtility.GetOrAddVertex(_Mesh, _Cache, tOuter);

		Vector3 cross = Vector3.Cross(_DirA, _DirB);
		bool flip = Vector3.Dot(cross, up) >= 0;

		float uW = SidewalkWidth / SidewalkTextureRepeat;
		float hH = SidewalkHeight / SidewalkTextureRepeat;

		Vector2 uvA = !_SideAIsExit ? new Vector2(uW, 0) : new Vector2(0, uW);
		Vector2 uvB = !_SideBIsExit ? new Vector2(uW, 0) : new Vector2(0, uW);

		if (flip)
		{
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO, vTC, vTA, new Vector2(uW, uW), new Vector2(0, 0), uvA);
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO, vTB, vTC, new Vector2(uW, uW), uvB, new Vector2(0, 0));

			MeshUtility.AddTexturedQuad(_Mesh, _Material, vTA, vA, vOuter, vTO,
				new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));
			MeshUtility.AddTexturedQuad(_Mesh, _Material, vTO, vOuter, vB, vTB,
				new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));
		}
		else
		{
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO, vTA, vTC, new Vector2(uW, uW), uvA, new Vector2(0, 0));
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO, vTC, vTB, new Vector2(uW, uW), new Vector2(0, 0), uvB);

			MeshUtility.AddTexturedQuad(_Mesh, _Material, vTO, vOuter, vA, vTA,
				new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));
			MeshUtility.AddTexturedQuad(_Mesh, _Material, vTB, vB, vOuter, vTO,
				new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));
		}

		if (_SideAIsExit)
		{
			if (flip)
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTA, vTC, vCenter, vA,
					new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0));
			else
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTC, vTA, vA, vCenter,
					new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0));
		}

		if (_SideBIsExit)
		{
			if (flip)
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTC, vTB, vB, vCenter,
					new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0));
			else
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTB, vTC, vCenter, vB,
					new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0));
		}
	}



	private void AddSidewalkStrip(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _Start, Vector3 _End, Vector3 _Outward)
	{
		Vector3 up = Vector3.Up;

		Vector3 s0 = _Start;
		Vector3 s1 = _End;
		Vector3 o0 = s0 + _Outward * SidewalkWidth;
		Vector3 o1 = s1 + _Outward * SidewalkWidth;
		Vector3 t0 = s0 + up * SidewalkHeight;
		Vector3 t1 = s1 + up * SidewalkHeight;
		Vector3 ot0 = o0 + up * SidewalkHeight;
		Vector3 ot1 = o1 + up * SidewalkHeight;

		var vS0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, s0);
		var vS1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, s1);
		var vO0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, o0);
		var vO1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, o1);
		var vT0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, t0);
		var vT1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, t1);
		var vOT0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, ot0);
		var vOT1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, ot1);

		float stripLen = (_End - _Start).Length;
		float uWidth = SidewalkWidth / SidewalkTextureRepeat;
		float vLen = stripLen / SidewalkTextureRepeat;
		float hHeight = SidewalkHeight / SidewalkTextureRepeat;

		// Top face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vOT0, vOT1, vT1, vT0,
			new Vector2(uWidth, 0), new Vector2(uWidth, vLen), new Vector2(0, vLen), new Vector2(0, 0));

		// Inner face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vT0, vT1, vS1, vS0,
			new Vector2(0, 0), new Vector2(0, vLen), new Vector2(hHeight, vLen), new Vector2(hHeight, 0));

		// Outer face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vOT1, vOT0, vO0, vO1,
			new Vector2(0, 0), new Vector2(0, vLen), new Vector2(hHeight, vLen), new Vector2(hHeight, 0));
	}



	private Transform GetRectangleExitLocalTransform(RectangleExit _Side, bool _IncludeSidewalk = false)
	{
		Vector3 pos = Vector3.Zero;
		Rotation rot = Rotation.Identity;

		switch (_Side)
		{
			case RectangleExit.North:
				pos += Vector3.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				break;
			case RectangleExit.South:
				pos -= Vector3.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(180);
				break;
			case RectangleExit.East:
				pos += Vector3.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(-90);
				break;
			case RectangleExit.West:
				pos -= Vector3.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
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
