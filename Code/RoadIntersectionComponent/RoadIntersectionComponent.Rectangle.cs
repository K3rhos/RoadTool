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
	[Property(Title = "Inner Corners UV Correction"), Feature("General"), Category("Sidewalk"), Range(0, 1), Step(0.01f), Order(3)] private float InnerCornersCorrection { get; set { field = value; m_IsDirty = true; } } = 0.0f;


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
		float outsideTextureRepeat = SidewalkTextureRepeat * (1.0f + MathF.Max(0.0f, InnerCornersCorrection));
		float hH = h / outsideTextureRepeat;

		float GetTopV(float _T)
		{
			return (_T * totalArcLength) / outsideTextureRepeat;
		}

		void AddOuterFace(Vector3 _Outer0, Vector3 _Outer1, Vector3 _TopOuter0, Vector3 _TopOuter1, float _V0, float _V1)
		{
			var vO0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _Outer0);
			var vO1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _Outer1);
			var vTO0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _TopOuter0);
			var vTO1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _TopOuter1);

			if (flip)
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTO1, vTO0, vO0, vO1,
					new Vector2(0, _V1), new Vector2(0, _V0), new Vector2(hH, _V0), new Vector2(hH, _V1));
			else
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTO0, vTO1, vO1, vO0,
					new Vector2(0, _V1), new Vector2(0, _V0), new Vector2(hH, _V0), new Vector2(hH, _V1));
		}

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

			// Inner-top pushed toward the arc centre (outward from the curb face) by the bevel → sloped curb face.
			Vector3 topInner0 = inner0 + up * h + (arcCenter - inner0).Normal * CurbBevel;
			Vector3 topInner1 = inner1 + up * h + (arcCenter - inner1).Normal * CurbBevel;
			Vector3 topOuter0 = outer0 + up * h;
			Vector3 topOuter1 = outer1 + up * h;

			if (i == 0)
				topOuter0 = topInner0;

			if (i == CornerSegments - 1)
				topOuter1 = topInner1;

			var vI0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, inner0);
			var vI1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, inner1);
			var vTI0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, topInner0);
			var vTI1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, topInner1);
			var vTO0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, topOuter0);
			var vTO1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, topOuter1);

			float sideV0 = (t0 * totalArcLength) / SidewalkTextureRepeat;
			float sideV1 = (t1 * totalArcLength) / SidewalkTextureRepeat;
			float topV0 = GetTopV(t0);
			float topV1 = GetTopV(t1);
			// Outer-top U spans the real top-edge width. The inner-top is pushed back by the bevel, so measuring
			// between the two top vertices (not the ground inner→outer span) keeps U from collapsing to zero — and
			// the texture from pinching — where the corner narrows to a point at the arc ends.
			float distOuter0 = Vector3.DistanceBetween(topInner0, topOuter0) / outsideTextureRepeat;
			float distOuter1 = Vector3.DistanceBetween(topInner1, topOuter1) / outsideTextureRepeat;

			// Outer face
			if (t0 < 0.5f && t1 > 0.5f)
			{
				Vector3 outerTip = _Corner + _DirA * w + _DirB * w;
				Vector3 topOuterTip = outerTip + up * h;
				float topVTip = GetTopV(0.5f);

				AddOuterFace(outer0, outerTip, topOuter0, topOuterTip, topV0, topVTip);
				AddOuterFace(outerTip, outer1, topOuterTip, topOuter1, topVTip, topV1);
			}
			else
			{
				AddOuterFace(outer0, outer1, topOuter0, topOuter1, topV0, topV1);
			}

			if (flip)
			{
				// Top face
				if (i == 0)
				{
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO0, vTO1, vTI0, new Vector2(distOuter0, topV0), new Vector2(distOuter1, topV1), new Vector2(0, topV0));
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO1, vTI1, vTI0, new Vector2(distOuter1, topV1), new Vector2(0, topV1), new Vector2(0, topV0));
				}
				else
				{
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO0, vTO1, vTI1, new Vector2(distOuter0, topV0), new Vector2(distOuter1, topV1), new Vector2(0, topV1));
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTO0, vTI1, vTI0, new Vector2(distOuter0, topV0), new Vector2(0, topV1), new Vector2(0, topV0));
				}

				// Inner face
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTI0, vTI1, vI1, vI0,
					new Vector2(0, sideV0), new Vector2(0, sideV1), new Vector2(CurbBevelV, sideV1), new Vector2(CurbBevelV, sideV0));
			}
			else
			{
				// Top face
				if (i == 0)
				{
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTI0, vTO1, vTO0, new Vector2(0, topV0), new Vector2(distOuter1, topV1), new Vector2(distOuter0, topV0));
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTI0, vTI1, vTO1, new Vector2(0, topV0), new Vector2(0, topV1), new Vector2(distOuter1, topV1));
				}
				else
				{
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTI1, vTO1, vTO0, new Vector2(0, topV1), new Vector2(distOuter1, topV1), new Vector2(distOuter0, topV0));
					MeshUtility.AddTexturedTriangle(_Mesh, _Material, vTI0, vTI1, vTO0, new Vector2(0, topV0), new Vector2(0, topV1), new Vector2(distOuter0, topV0));
				}

				// Inner face
				MeshUtility.AddTexturedQuad(_Mesh, _Material, vTI1, vTI0, vI0, vI1,
					new Vector2(0, sideV1), new Vector2(0, sideV0), new Vector2(CurbBevelV, sideV0), new Vector2(CurbBevelV, sideV1));
			}
		}
	}



	private void AddCornerCap(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _CornerPos, Vector3 _DirA, Vector3 _DirB, bool _SideAIsExit, bool _SideBIsExit)
	{
		// A beveled inner edge turns the square corner into a mitred/inset top with a sloped curb, so it's built
		// by a dedicated path. bevel == 0 falls through to the original vertical-curb square cap below, unchanged.
		if (CurbBevel > 0.0f)
		{
			AddBeveledCornerCap(_Mesh, _Material, _Cache, _CornerPos, _DirA, _DirB, _SideAIsExit, _SideBIsExit);
			return;
		}

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



	private void AddBeveledCornerCap(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _CornerPos, Vector3 _DirA, Vector3 _DirB, bool _SideAIsExit, bool _SideBIsExit)
	{
		Vector3 up = Vector3.Up;
		float h = SidewalkHeight;
		float w = SidewalkWidth;
		float b = CurbBevel;

		Vector3 pCenter = _CornerPos;
		Vector3 pA = _CornerPos + _DirA * w;
		Vector3 pB = _CornerPos + _DirB * w;
		Vector3 pOuter = _CornerPos + _DirA * w + _DirB * w;

		Vector3 tA = pA + up * h;
		Vector3 tB = pB + up * h;
		Vector3 tOuter = pOuter + up * h;

		// Inner-top vertices, inset by the bevel. eEnd/nEnd sit on the two inner edges and weld to the abutting
		// strip's inset top end when that side has no exit (AddSidewalkStrip uses the same pCenter + dir * bevel);
		// seEnd/nwEnd are the outer ends of a side's own curb when it IS an exit. The inner corner is chamfered
		// straight from eEnd to nEnd, and a small ramp drops from that chamfer down to the road corner.
		Vector3 eEnd = pCenter + _DirA * b + up * h;
		Vector3 nEnd = pCenter + _DirB * b + up * h;
		Vector3 seEnd = pA + _DirB * b + up * h;
		Vector3 nwEnd = pB + _DirA * b + up * h;

		bool flip = Vector3.Dot(Vector3.Cross(_DirA, _DirB), up) >= 0;

		float uW = w / SidewalkTextureRepeat;
		float hH = h / SidewalkTextureRepeat;
		float bV = CurbBevelV;

		// Top-face UV. The inner edges are pushed back by the bevel, so the projection is offset by the bevel to keep
		// the texture pinned to those receding edges — it slides back with the bevel like the straight strips do,
		// instead of staying anchored to the fixed road corner. (The single-exit cases below pin their one beveled
		// edge to U=0 explicitly so it lines up exactly with the abutting strip's top.)
		Vector2 TopUV(Vector3 p) => new Vector2(Vector3.Dot(p - pCenter, _DirA) - b, Vector3.Dot(p - pCenter, _DirB) - b) / SidewalkTextureRepeat;
		// Folded top UV for the neither-exit corner: (max, min) of the two edge distances mirrors the texture across
		// the 45-degree corner diagonal, matching the un-beveled cap. Used per-half so no triangle straddles the crease.
		Vector2 FoldTopUV(Vector3 p)
		{
			float da = Vector3.Dot(p - pCenter, _DirA);
			float db = Vector3.Dot(p - pCenter, _DirB);
			// Pin the inner (chamfer) edge to U=0 and the outer corner to U=uW so the texture slides back with the
			// bevel (matching the abutting strips / single-exit caps) instead of staying anchored to the road corner.
			float denom = w - b;
			float u = denom > 0.001f ? MathF.Max(0.0f, MathF.Max(da, db) - b) / denom * uW : 0.0f;
			return new Vector2(u, MathF.Min(da, db) / SidewalkTextureRepeat);
		}

		// Geometry below is authored for the flip == true corner (CCW seen from above); the two mirror corners
		// emit the same faces with the vertex order reversed so their winding stays correct.
		void Quad(Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3)
		{
			var v0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, q0);
			var v1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, q1);
			var v2 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, q2);
			var v3 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, q3);
			if (flip) MeshUtility.AddTexturedQuad(_Mesh, _Material, v0, v1, v2, v3, u0, u1, u2, u3);
			else MeshUtility.AddTexturedQuad(_Mesh, _Material, v3, v2, v1, v0, u3, u2, u1, u0);
		}

		void Tri(Vector3 q0, Vector3 q1, Vector3 q2, Vector2 u0, Vector2 u1, Vector2 u2)
		{
			var v0 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, q0);
			var v1 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, q1);
			var v2 = MeshUtility.GetOrAddVertex(_Mesh, _Cache, q2);
			if (flip) MeshUtility.AddTexturedTriangle(_Mesh, _Material, v0, v1, v2, u0, u1, u2);
			else MeshUtility.AddTexturedTriangle(_Mesh, _Material, v2, v1, v0, u2, u1, u0);
		}

		void AddMiteredInnerCorner()
		{
			Vector3 miterTop = pCenter + (_DirA + _DirB) * b + up * h;
			float miterV = Vector3.DistanceBetween(eEnd, miterTop) / SidewalkTextureRepeat;

			Tri(pCenter, eEnd, miterTop,
				new Vector2(bV, 0),
				new Vector2(0, 0),
				new Vector2(0, miterV));

			Tri(pCenter, miterTop, nEnd,
				new Vector2(bV, 0),
				new Vector2(0, miterV),
				new Vector2(0, 0));
		}

		// Outer (away-from-road) curb faces never bevel; their inner-top corner just follows the side's own curb
		// when that side is an exit (seEnd / nwEnd), otherwise it stays at the square top corner (tA / tB).
		Vector3 outerAInner = _SideAIsExit ? seEnd : tA;
		Vector3 outerBInner = _SideBIsExit ? nwEnd : tB;
		Quad(outerAInner, pA, pOuter, tOuter, new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));
		Quad(tOuter, pOuter, pB, outerBInner, new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));

		// An exit side has no abutting strip, so the cap draws that side's own sloped curb along the inner edge.
		if (_SideAIsExit)
			Quad(pCenter, pA, seEnd, nEnd, new Vector2(bV, 0), new Vector2(bV, uW), new Vector2(0, uW), new Vector2(0, 0));

		if (_SideBIsExit)
			Quad(pCenter, eEnd, nwEnd, pB, new Vector2(bV, 0), new Vector2(0, 0), new Vector2(0, uW), new Vector2(bV, uW));

		// Top surface (the square minus the bevel inset). A strip side insets to eEnd/nEnd; an exit side insets
		// straight across to seEnd/nwEnd.
		if (!_SideAIsExit && !_SideBIsExit)
		{
			// Mitre the inner corner: extend both beveled inner edges to a single sharp 90-degree point instead of
			// cutting straight across between them (which read as a chamfer/step that grew with the bevel). Split
			// along the diagonal (tOuter to the mitre point) so each half stays on one side of the fold.
			Vector3 miterPoint = pCenter + (_DirA + _DirB) * b + up * h;
			Quad(eEnd, tA, tOuter, miterPoint, FoldTopUV(eEnd), FoldTopUV(tA), FoldTopUV(tOuter), FoldTopUV(miterPoint));
			Quad(miterPoint, tOuter, tB, nEnd, FoldTopUV(miterPoint), FoldTopUV(tOuter), FoldTopUV(tB), FoldTopUV(nEnd));
			
			AddMiteredInnerCorner();
		}
		else if (_SideAIsExit && !_SideBIsExit)
		{
			// Inner edge nEnd→seEnd pinned to U=0 so the top texture recedes with the bevel and matches the B strip.
			Quad(nEnd, seEnd, tOuter, tB, new Vector2(0, 0), new Vector2(0, uW), new Vector2(uW, uW), new Vector2(uW, 0));
		}
		else if (!_SideAIsExit && _SideBIsExit)
		{
			// Inner edge eEnd→nwEnd pinned to U=0 so the top texture recedes with the bevel and matches the A strip.
			Quad(eEnd, tA, tOuter, nwEnd, new Vector2(0, 0), new Vector2(uW, 0), new Vector2(uW, uW), new Vector2(0, uW));
		}
		else
		{
			Quad(eEnd, seEnd, tOuter, nwEnd, TopUV(eEnd), TopUV(seEnd), TopUV(tOuter), TopUV(nwEnd));
			Tri(eEnd, nwEnd, nEnd, TopUV(eEnd), TopUV(nwEnd), TopUV(nEnd));
		}
		
		// Corner ramp: a small sloped face bridging the two inner insets down to the road corner. Only needed when
		// both inner edges inset diagonally and leave a notch — i.e. when neither side is an exit, or both are.
		if (_SideAIsExit && _SideBIsExit)
		{
			float chamferV = Vector3.DistanceBetween(eEnd, nEnd) / SidewalkTextureRepeat;
			Tri(pCenter, eEnd, nEnd, new Vector2(bV, chamferV * 0.5f), new Vector2(0, 0), new Vector2(0, chamferV));
		}
	}



	private void AddSidewalkStrip(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _Start, Vector3 _End, Vector3 _Outward)
	{
		Vector3 up = Vector3.Up;

		Vector3 s0 = _Start;
		Vector3 s1 = _End;
		Vector3 o0 = s0 + _Outward * SidewalkWidth;
		Vector3 o1 = s1 + _Outward * SidewalkWidth;
		Vector3 t0 = s0 + up * SidewalkHeight + _Outward * CurbBevel;  // inner-top pushed outward → sloped curb face
		Vector3 t1 = s1 + up * SidewalkHeight + _Outward * CurbBevel;
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

		// Inner face (sloped when beveled — U follows the slope length)
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vT0, vT1, vS1, vS0,
			new Vector2(0, 0), new Vector2(0, vLen), new Vector2(CurbBevelV, vLen), new Vector2(CurbBevelV, 0));

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
