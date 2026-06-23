using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public enum RectangleExit
{
	None = 0,
	North = 1 << 0, // +Forward
	East = 1 << 1,  // +Right
	South = 1 << 2, // -Forward
	West = 1 << 3   // -Right
}

/// <summary>
/// One opening of a rectangle intersection: which side it sits on, its lateral position along that side (world units,
/// 0 = centred), and the width of the road that connects there. A side may carry any number of these.
/// </summary>
public class RectangleExitDef
{
	[Property, Hide] public RoadIntersectionComponent Reference { get; set; }
	[Property(Title = "Side")] public RectangleExit Side { get; set { field = value; Reference?.m_IsDirty = true; } } = RectangleExit.North;
	[Property(Title = "Offset")] public float Offset { get; set { field = value; Reference?.m_IsDirty = true; } } = 0.0f;
	[Property(Title = "Width")] public float Width { get; set { field = value; Reference?.m_IsDirty = true; } } = 500.0f;
}

public partial class RoadIntersectionComponent
{
	private static readonly float QuarterTurnRad = MathF.PI * 0.5f;

	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private float Width { get; set { field = value; m_IsDirty = true; } } = 500.0f;
	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private float Length { get; set { field = value; m_IsDirty = true; } } = 500.0f;
	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle), Range(0, 16), Step(2)] private int CornerSegments { get; set { field = value; m_IsDirty = true; } } = 8;
	[Property, Hide] private RectangleExit RectangleExits { get; set; } = (RectangleExit)15; // legacy flags, now derived from Exits — kept so the mesh/lights/terrain/gizmos can still ask "any opening on this side?"
	[Property(Title = "Exits"), Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Rectangle), Change] private RectangleExitDef[] Exits { get; set; } = [];

	private void OnExitsChanged(RectangleExitDef[] _OldValue, RectangleExitDef[] _NewValue)
	{
		if (_NewValue.Length > 0 && _NewValue[^1] is null)
			_NewValue[^1] = new RectangleExitDef { Reference = this };

		SyncRectangleExitFlags();
		m_IsDirty = true;
	}

	/// <summary>
	/// Seeds <see cref="Exits"/> the first time it's needed: a rectangle saved under the old flags model migrates to one
	/// centred, full-width opening per set side, so a fresh intersection comes up as a 4-way (the old default). Authored
	/// openings are never overwritten. Also re-points each opening's back-reference and refreshes the legacy side flags.
	/// </summary>
	public void EnsureRectangleExits()
	{
		if (Exits is null || Exits.Length == 0)
		{
			var seeded = new List<RectangleExitDef>();

			foreach (RectangleExit side in new[] { RectangleExit.North, RectangleExit.East, RectangleExit.South, RectangleExit.West })
				if (RectangleExits.HasFlag(side))
					seeded.Add(new RectangleExitDef { Reference = this, Side = side, Offset = 0.0f, Width = GetExitRoadWidth(side) });

			Exits = seeded.ToArray();
		}

		foreach (var exit in Exits)
			if (exit is not null)
				exit.Reference = this;

		SyncRectangleExitFlags();
	}

	/// <summary>Mirrors which sides currently carry at least one opening into the legacy <see cref="RectangleExits"/> flags.</summary>
	private void SyncRectangleExitFlags()
	{
		RectangleExit flags = RectangleExit.None;

		if (Exits is not null)
			foreach (var exit in Exits)
				if (exit is not null)
					flags |= exit.Side;

		RectangleExits = flags;
	}
	
	[Property(Title = "Inner Corners UV Correction"), Feature("General"), Category("Sidewalk"), Range(0, 1), Step(0.01f), Order(3)] private float InnerCornersCorrection { get; set { field = value; m_IsDirty = true; } } = 0.0f;


	private void BuildRectangleRoad(PolygonMesh _Mesh, Material _Material)
	{
		var cache = new Dictionary<Vector3, HalfEdgeMesh.VertexHandle>();

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

		AddSideRoadExtensions(_Mesh, _Material, cache, RectangleExit.North, forward * hl, right, forward, hw);
		AddSideRoadExtensions(_Mesh, _Material, cache, RectangleExit.South, -forward * hl, -right, -forward, hw);
		AddSideRoadExtensions(_Mesh, _Material, cache, RectangleExit.East, right * hw, -forward, right, hl);
		AddSideRoadExtensions(_Mesh, _Material, cache, RectangleExit.West, -right * hw, forward, -right, hl);

		var reaches = GetCornerReaches(hw, hl);

		if (CornerSegments > 0)
		{
			if (reaches.NorthHigh && reaches.EastLow) AddRoadCornerFiller(_Mesh, _Material, cache, pNE, right, forward);
			if (reaches.NorthLow && reaches.WestHigh) AddRoadCornerFiller(_Mesh, _Material, cache, pNW, -right, forward);
			if (reaches.EastHigh && reaches.SouthLow) AddRoadCornerFiller(_Mesh, _Material, cache, pSE, right, -forward);
			if (reaches.WestLow && reaches.SouthHigh) AddRoadCornerFiller(_Mesh, _Material, cache, pSW, -right, -forward);
		}
	}



	/// <summary>
	/// This side's openings as [Lo, Hi] ranges in the side's lateral coordinate ([-span, span], where the lateral axis
	/// matches the exit transform's right vector), clamped to the side and merged where they overlap. Sorted ascending.
	/// </summary>
	private List<(float Lo, float Hi)> CollectSideOpenings(RectangleExit _Side, float _Span)
	{
		var ranges = new List<(float Lo, float Hi)>();

		if (Exits is not null)
			foreach (var exit in Exits)
			{
				if (exit is null || exit.Side != _Side)
					continue;

				float lo = (exit.Offset - exit.Width * 0.5f).Clamp(-_Span, _Span);
				float hi = (exit.Offset + exit.Width * 0.5f).Clamp(-_Span, _Span);

				if (hi - lo > 0.1f)
					ranges.Add((lo, hi));
			}

		ranges.Sort((a, b) => a.Lo.CompareTo(b.Lo));

		var merged = new List<(float Lo, float Hi)>();

		foreach (var r in ranges)
			if (merged.Count > 0 && r.Lo <= merged[^1].Hi + 0.1f)
				merged[^1] = (merged[^1].Lo, MathF.Max(merged[^1].Hi, r.Hi));
			else
				merged.Add(r);

		return merged;
	}

	/// <summary>Whether any opening on this side reaches the corner end of the side (its +lateral end if
	/// <paramref name="_HighEnd"/>, else its -lateral end) — i.e. the road, not the sidewalk, meets that corner.</summary>
	private bool SideOpeningReachesEnd(RectangleExit _Side, float _Span, bool _HighEnd)
	{
		foreach (var (lo, hi) in CollectSideOpenings(_Side, _Span))
			if (_HighEnd ? hi >= _Span - 0.1f : lo <= -_Span + 0.1f)
				return true;

		return false;
	}

	/// <summary>
	/// Per-corner "does an opening reach here?" flags. Each side meets two corners — its +lateral (High) end and its
	/// -lateral (low) end — and the names below say which corner each reaches:
	/// NE = NorthHigh &amp; EastLow, NW = NorthLow &amp; WestHigh, SE = EastHigh &amp; SouthLow, SW = WestLow &amp; SouthHigh.
	/// </summary>
	private readonly struct CornerReaches
	{
		public bool NorthHigh { get; init; }
		public bool NorthLow { get; init; }
		public bool SouthHigh { get; init; }
		public bool SouthLow { get; init; }
		public bool EastHigh { get; init; }
		public bool EastLow { get; init; }
		public bool WestHigh { get; init; }
		public bool WestLow { get; init; }
	}

	private CornerReaches GetCornerReaches(float _Hw, float _Hl) => new()
	{
		NorthHigh = SideOpeningReachesEnd(RectangleExit.North, _Hw, true),
		NorthLow = SideOpeningReachesEnd(RectangleExit.North, _Hw, false),
		SouthHigh = SideOpeningReachesEnd(RectangleExit.South, _Hw, true),
		SouthLow = SideOpeningReachesEnd(RectangleExit.South, _Hw, false),
		EastHigh = SideOpeningReachesEnd(RectangleExit.East, _Hl, true),
		EastLow = SideOpeningReachesEnd(RectangleExit.East, _Hl, false),
		WestHigh = SideOpeningReachesEnd(RectangleExit.West, _Hl, true),
		WestLow = SideOpeningReachesEnd(RectangleExit.West, _Hl, false),
	};

	/// <summary>Extends the road surface outward under each opening on a side; the gaps between openings stay road-less so
	/// the sidewalk strips fill them. center/lateral/outward are local-space; lateral matches the exit's right vector.</summary>
	private void AddSideRoadExtensions(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, RectangleExit _Side, Vector3 _Center, Vector3 _Lateral, Vector3 _Outward, float _Span)
	{
		foreach (var (lo, hi) in CollectSideOpenings(_Side, _Span))
		{
			AddRoadExtension(_Mesh, _Material, _Cache, _Center + _Lateral * lo, _Center + _Lateral * hi, _Outward);

			// Tarmac under each inset curb return's rounded cutout (pairs with the sidewalk curb return, exactly as a
			// geometric corner pairs a road filler with its rounded sidewalk). Empty when CornerSegments is 0.
			if (lo > -_Span + 0.1f)
				AddRoadCornerFiller(_Mesh, _Material, _Cache, _Center + _Lateral * lo, -_Lateral, _Outward);

			if (hi < _Span - 0.1f)
				AddRoadCornerFiller(_Mesh, _Material, _Cache, _Center + _Lateral * hi, _Lateral, _Outward);
		}
	}

	/// <summary>
	/// Builds the sidewalk along a side: a rounded curb return at every INSET opening edge (the rounded corner you see
	/// where a road meets the kerb), then straight strips filling the runs between those returns and the geometric
	/// corners. An edge that sits exactly on a geometric corner is left to the corner dispatch instead, so a full-width
	/// opening adds nothing here and migrated 4-ways are untouched. A curb return reuses the corner primitive, oriented
	/// between the side (lateral, toward the adjacent sidewalk) and the road (outward).
	/// </summary>
	private void AddSideSidewalkStrips(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, RectangleExit _Side, Vector3 _Center, Vector3 _Lateral, Vector3 _Outward, float _Span)
	{
		var openings = CollectSideOpenings(_Side, _Span);
		bool rounded = CornerSegments > 0;
		float w = SidewalkWidth;

		// Rounded curb return at each inset opening edge — its arc flares the kerb out from the side into the road.
		// (With no corner segments there is no rounding to do; the strips below just butt the opening with a flat cap.)
		if (rounded)
		{
			foreach (var (lo, hi) in openings)
			{
				if (lo > -_Span + 0.1f)
					AddRoundedSidewalkCorner(_Mesh, _Material, _Cache, _Center + _Lateral * lo, -_Lateral, _Outward, false);

				if (hi < _Span - 0.1f)
					AddRoundedSidewalkCorner(_Mesh, _Material, _Cache, _Center + _Lateral * hi, _Lateral, _Outward, false);
			}
		}

		// Straight strips fill the runs between features. When rounded, pull an opening-facing end back by w to make
		// room for its curb return; otherwise leave it flush and flat-cap it. Corner-facing ends stay flush for the
		// corner cap to close.
		float cursor = -_Span;

		for (int i = 0; i <= openings.Count; i++)
		{
			float gapLo = cursor;
			float gapHi = i < openings.Count ? openings[i].Lo : _Span;

			bool lowIsEdge = gapLo > -_Span + 0.1f;
			bool highIsEdge = gapHi < _Span - 0.1f;

			float stripLo = gapLo + (rounded && lowIsEdge ? w : 0.0f);
			float stripHi = gapHi - (rounded && highIsEdge ? w : 0.0f);

			if (stripHi - stripLo > 0.1f)
				AddSidewalkStrip(_Mesh, _Material, _Cache, _Center + _Lateral * stripHi, _Center + _Lateral * stripLo, _Outward,
					!rounded && highIsEdge, !rounded && lowIsEdge);

			if (i < openings.Count)
				cursor = openings[i].Hi;
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

		Vector3 right = Vector3.Right;
		Vector3 forward = Vector3.Forward;
		float hw = Width * 0.5f;
		float hl = Length * 0.5f;

		Vector3 pSW = -right * hw - forward * hl;
		Vector3 pNW = -right * hw + forward * hl;
		Vector3 pNE = right * hw + forward * hl;
		Vector3 pSE = right * hw - forward * hl;

		AddSideSidewalkStrips(_Mesh, _Material, cache, RectangleExit.North, forward * hl, right, forward, hw);
		AddSideSidewalkStrips(_Mesh, _Material, cache, RectangleExit.South, -forward * hl, -right, -forward, hw);
		AddSideSidewalkStrips(_Mesh, _Material, cache, RectangleExit.East, right * hw, -forward, right, hl);
		AddSideSidewalkStrips(_Mesh, _Material, cache, RectangleExit.West, -right * hw, forward, -right, hl);

		var reaches = GetCornerReaches(hw, hl);

		if (CornerSegments > 0)
		{
			if (reaches.EastLow && reaches.NorthHigh) AddRoundedSidewalkCorner(_Mesh, _Material, cache, pNE, right, forward);
			else AddCornerCap(_Mesh, _Material, cache, pNE, right, forward, reaches.EastLow, reaches.NorthHigh);

			if (reaches.WestHigh && reaches.NorthLow) AddRoundedSidewalkCorner(_Mesh, _Material, cache, pNW, -right, forward);
			else AddCornerCap(_Mesh, _Material, cache, pNW, -right, forward, reaches.WestHigh, reaches.NorthLow);

			if (reaches.EastHigh && reaches.SouthLow) AddRoundedSidewalkCorner(_Mesh, _Material, cache, pSE, right, -forward);
			else AddCornerCap(_Mesh, _Material, cache, pSE, right, -forward, reaches.EastHigh, reaches.SouthLow);

			if (reaches.WestLow && reaches.SouthHigh) AddRoundedSidewalkCorner(_Mesh, _Material, cache, pSW, -right, -forward);
			else AddCornerCap(_Mesh, _Material, cache, pSW, -right, -forward, reaches.WestLow, reaches.SouthHigh);
		}
		else
		{
			AddCornerCap(_Mesh, _Material, cache, pNE, right, forward, reaches.EastLow, reaches.NorthHigh);
			AddCornerCap(_Mesh, _Material, cache, pNW, -right, forward, reaches.WestHigh, reaches.NorthLow);
			AddCornerCap(_Mesh, _Material, cache, pSE, right, -forward, reaches.EastHigh, reaches.SouthLow);
			AddCornerCap(_Mesh, _Material, cache, pSW, -right, -forward, reaches.WestLow, reaches.SouthHigh);
		}
	}



	private void AddRoundedSidewalkCorner(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _Corner, Vector3 _DirA, Vector3 _DirB, bool _OuterCorners = true)
	{
		Vector3 up = Vector3.Up;
		float h = SidewalkHeight;
		float w = SidewalkWidth;

		Vector3 cross = Vector3.Cross(_DirA, _DirB);
		bool flip = Vector3.Dot(cross, up) >= 0;

		Vector3 arcCenter = _Corner + _DirA * w + _DirB * w;

		float totalArcLength = w * QuarterTurnRad;
		float outsideTextureRepeat = SidewalkTextureRepeat * (1.0f + InnerCornersCorrection);
		float correctedWidth = w * (1.0f + InnerCornersCorrection);
		float hH = h / outsideTextureRepeat;

		float GetTopV(float _T)
		{
			return (_T * totalArcLength) / correctedWidth;
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
			float distOuter0 = Vector3.DistanceBetween(topInner0, topOuter0) / correctedWidth;
			float distOuter1 = Vector3.DistanceBetween(topInner1, topOuter1) / correctedWidth;

			// Outer face
			if (_OuterCorners)
			{
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
			float denom = w - b;
			float u = denom > 0.001f ? MathF.Max(0.0f, MathF.Max(da, db) - b) / denom : 0.0f;
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
		Quad(outerAInner, pA, pOuter, tOuter, new Vector2(1, 0), new Vector2(1 - hH, 0), new Vector2(1 - hH, uW), new Vector2(1, uW));
		Quad(tOuter, pOuter, pB, outerBInner, new Vector2(1, 0), new Vector2(1 - hH, 0), new Vector2(1 - hH, uW), new Vector2(1, uW));

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



	private void AddSidewalkStrip(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _Start, Vector3 _End, Vector3 _Outward, bool _CapStart = false, bool _CapEnd = false)
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
		float vLen = stripLen / SidewalkTextureRepeat;
		float hHeight = SidewalkHeight / SidewalkTextureRepeat;

		// Top face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vOT0, vOT1, vT1, vT0,
			new Vector2(1, 0), new Vector2(1, vLen), new Vector2(0, vLen), new Vector2(0, 0));

		// Inner face (sloped when beveled — U follows the slope length)
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vT0, vT1, vS1, vS0,
			new Vector2(0, 0), new Vector2(0, vLen), new Vector2(CurbBevelV, vLen), new Vector2(CurbBevelV, 0));

		// Outer face
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vOT1, vOT0, vO0, vO1,
			new Vector2(1, vLen), new Vector2(1, 0), new Vector2(1 - hHeight, 0), new Vector2(1 - hHeight, vLen));

		// Curb caps where the strip ends at an opening (rather than a corner), so you don't see into the hollow curb.
		if (_CapStart)
			AddStripEndCap(_Mesh, _Material, s0, t0, ot0, o0);

		if (_CapEnd)
			AddStripEndCap(_Mesh, _Material, s1, t1, ot1, o1);
	}

	/// <summary>
	/// Closes one end of a sidewalk strip with its curb cross-section (inner-bottom → inner-top → outer-top →
	/// outer-bottom). Uses its own vertices — an isolated face the half-edge mesh always accepts — and is double-sided
	/// so winding can't drop it.
	/// </summary>
	private void AddStripEndCap(PolygonMesh _Mesh, Material _Material, Vector3 _InnerBottom, Vector3 _InnerTop, Vector3 _OuterTop, Vector3 _OuterBottom)
	{
		float uW = SidewalkWidth / SidewalkTextureRepeat;
		float hH = SidewalkHeight / SidewalkTextureRepeat;

		var a = _Mesh.AddVertices(_InnerBottom)[0];
		var b = _Mesh.AddVertices(_InnerTop)[0];
		var c = _Mesh.AddVertices(_OuterTop)[0];
		var d = _Mesh.AddVertices(_OuterBottom)[0];

		MeshUtility.AddTexturedQuad(_Mesh, _Material, a, b, c, d,
			new Vector2(0, 0), new Vector2(0, hH), new Vector2(uW, hH), new Vector2(uW, 0));
		MeshUtility.AddTexturedQuad(_Mesh, _Material, d, c, b, a,
			new Vector2(uW, 0), new Vector2(uW, hH), new Vector2(0, hH), new Vector2(0, 0));
	}



	private Transform GetRectangleExitLocalTransform(RectangleExit _Side, bool _IncludeSidewalk = false, float _Offset = 0.0f)
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

		pos += rot.Right * _Offset; // lateral shift along the side to this opening's position
		return new Transform { Position = pos, Rotation = rot };
	}



	private Transform GetRectangleExitTransform(RectangleExit _Side, bool _IncludeSidewalk = false, float _Offset = 0.0f)
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

		pos += rot.Right * _Offset; // lateral shift along the side to this opening's position
		return new Transform { Position = pos, Rotation = rot };
	}
}
