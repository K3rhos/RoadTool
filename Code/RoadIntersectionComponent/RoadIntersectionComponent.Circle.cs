using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public class CircleExit
{
	[Property, Hide] public RoadIntersectionComponent Reference { get; set; }
	[Property] public float AngleDegrees { get; set { field = value; Reference?.m_IsDirty = true; } } = 0.0f;
	[Property] public float RoadWidth { get; set { field = value;  Reference?.m_IsDirty = true; } } = 500.0f;
}

public partial class RoadIntersectionComponent
{
	private const float SegmentsMultiplier = 3.0f / 32.0f;
	
	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1)] private float Radius { get; set { field = value; m_IsDirty = true; } } = 1000.0f;
	[Property, Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1), Range(4, 8)] private int DiscSegmentsPower { get; set { field = value.Clamp(4, 8); m_IsDirty = true; } } = 6;
	[Property(Title = "Exits"), Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1), Change] private CircleExit[] CircleExits { get; set; } = [];

	/// <summary>When enabled, traffic circulates around a ring (roundabout) instead of crossing straight through — so paths never cross.</summary>
	[Property(Title = "Roundabout Mode"), Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1)] private bool RoundaboutMode { get; set; } = false;
	[Property(Title = "Ring Scale"), Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1), Range(0.2f, 0.95f)] private float RoundaboutRingScale { get; set; } = 0.65f;

	/// <summary>Widens the entry/exit "arms" where each road meets the ring (1 = natural lane offset). Clamped so arms never reach a neighbouring leg.</summary>
	[Property(Title = "Arm Spread"), Feature("General"), ShowIf(nameof(Shape), IntersectionShape.Circle), Order(1), Range(0.5f, 4.0f)] public float RoundaboutArmSpread { get; set; } = 1.5f;

	/// <summary>True when this is a circle intersection routing traffic around a ring rather than straight across.</summary>
	public bool IsRoundabout => Shape == IntersectionShape.Circle && RoundaboutMode;

	/// <summary>World-units radius of the drivable ring vehicles follow in roundabout mode.</summary>
	public float RoundaboutRingRadius => Radius * RoundaboutRingScale;

	private int DiscSegments => 1 << DiscSegmentsPower;
	
	
	
	private void OnCircleExitsChanged(CircleExit[] _OldValue, CircleExit[] _NewValue)
	{
		if (_NewValue.Length > 0)
		{
			_NewValue[^1] = new CircleExit
			{
				Reference = this
			};
		}
		
		if (_NewValue.Length > 1 && _NewValue.Length > _OldValue.Length)
		{
			float prevAngle = _OldValue.Length > 0 && _OldValue[^1] is not null ? _OldValue[^1].AngleDegrees : 0.0f;
		
			_NewValue[^1].AngleDegrees = prevAngle + 90.0f;	
		}
		
		m_IsDirty = true;
	}
	
	
	
	// Samples a curved wall from inner (on disc) to outer (at corridor outer corner) using a quadratic Bezier
	// tangent to the disc at the inner end and tangent to the exit direction at the outer end.
	// Returns N+1 points where N = ExitCornerSegments.
	private List<Vector3> BuildExitWallVerts(Vector3 _Inner, Vector3 _Outer, Vector3 _DiscTangent, Vector3 _ExitDir)
	{
		var verts = new List<Vector3>();
		int n = (int)Math.Max(2, DiscSegments * SegmentsMultiplier);

		if (n == 1)
		{
			verts.Add(_Inner);
			verts.Add(_Outer);
			return verts;
		}

		TryBezierControl(_Inner, _DiscTangent, _Outer, _ExitDir, out Vector3 b1);

		for (int i = 0; i <= n; i++)
		{
			float t = (float)i / n;
			verts.Add(SampleQuadBezier(_Inner, b1, _Outer, t));
		}

		return verts;
	}



	private void BuildCircleRoad(PolygonMesh _Mesh, Material _Material)
	{
		var cache = new Dictionary<Vector3, HalfEdgeMesh.VertexHandle>();

		int segments = DiscSegments;
		float step = 360.0f / segments;

		var vCenter = MeshUtility.GetOrAddVertex(_Mesh, cache, Vector3.Zero);

		// Exits are handled by the corridor mesh extending outward from the arc.
		for (int i = 0; i < segments; i++)
		{
			float a0 = i * step;
			float a1 = (i + 1) * step;

			Vector3 d0 = Rotation.FromYaw(a0).Forward * Radius;
			Vector3 d1 = Rotation.FromYaw(a1).Forward * Radius;

			var vD0 = MeshUtility.GetOrAddVertex(_Mesh, cache, d0);
			var vD1 = MeshUtility.GetOrAddVertex(_Mesh, cache, d1);

			MeshUtility.AddTexturedTriangle(_Mesh, _Material, vCenter, vD0, vD1,
				Vector2.Zero, new Vector2(0, 1), new Vector2(1, 1));
		}

		// Flat exit corridor extending from the disc's arc out into the exterior
		foreach (var exit in CircleExits ?? Array.Empty<CircleExit>())
			AddCircleExitCorridor(_Mesh, _Material, cache, exit);
	}



	// Snap target sits SW beyond the ring's outer boundary, clearly exterior to the intersection
	private float GetCircleExitSnapDistance() => Radius + SidewalkWidth * 2.0f;



	// Returns the disc grid angles bounding the exit gap.
	// The gap span is fixed at (2 * CornerSegments) disc grid steps centered on the exit angle, so the arc
	// vertex count matches the wall vertex count (CornerSegments + 1 per side), required for the zigzag strip
	// triangulation. The exit road's physical width should fit inside this angular gap; if it doesn't, increase
	// DiscSegments or decrease CornerSegments.
	private void GetCircleExitGridAngles(CircleExit _Exit, out float _GridAngleCw, out float _GridAngleCcw)
	{
		int segments = DiscSegments;
		float step = 360.0f / segments;

		// Snap exit angle to nearest grid index, then offset by ±CornerSegments grid steps.
		int n = (int)Math.Max(2, DiscSegments * SegmentsMultiplier);
		int iCenter = (int)MathF.Round(_Exit.AngleDegrees / step);
		int iCw  = iCenter - n;
		int iCcw = iCenter + n;

		// Normalize to [0, segments) so we use the same grid angles the disc loop uses
		int iCwNorm  = ((iCw  % segments) + segments) % segments;
		int iCcwNorm = ((iCcw % segments) + segments) % segments;

		_GridAngleCcw = iCcwNorm * step;
		_GridAngleCw  = iCwNorm  * step;
	}



	private void AddCircleExitCorridor(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, CircleExit _Exit)
	{
		float halfRoadWidth = _Exit.RoadWidth * 0.5f;
		float snapDistance = GetCircleExitSnapDistance();

		int segments = DiscSegments;
		float step = 360.0f / segments;

		Vector3 exitDir = Rotation.FromYaw(_Exit.AngleDegrees).Forward;
		Vector3 perpDir = Vector3.Cross(exitDir, Vector3.Up).Normal;

		Vector3 outerMinus = exitDir * snapDistance + perpDir * halfRoadWidth;
		Vector3 outerPlus  = exitDir * snapDistance - perpDir * halfRoadWidth;

		// Inner corners on the disc (grid-aligned so they weld with disc vertices)
		GetCircleExitGridAngles(_Exit, out float gridAngleCw, out float gridAngleCcw);
		Vector3 innerMinus = Rotation.FromYaw(gridAngleCw).Forward  * Radius;
		Vector3 innerPlus  = Rotation.FromYaw(gridAngleCcw).Forward * Radius;

		// Curved walls: tangent to the disc at the inner end, tangent to exitDir at the outer end.
		Vector3 tangentMinus = Rotation.FromYaw(gridAngleCw  + 90.0f).Forward;
		Vector3 tangentPlus  = Rotation.FromYaw(gridAngleCcw - 90.0f).Forward;

		var wallMinus = BuildExitWallVerts(innerMinus, outerMinus, tangentMinus, exitDir);
		var wallPlus  = BuildExitWallVerts(innerPlus,  outerPlus,  tangentPlus,  exitDir);

		// Arc vertices: gap span is exactly (2 * CornerSegments) disc steps, so arc has (2n + 1) vertices.
		// arc[0] = innerMinus, arc[n] = arcMidpoint, arc[2n] = innerPlus. All weld with disc through the cache.
		int n = (int)Math.Max(2, DiscSegments * SegmentsMultiplier);
		int iCenter = (int)MathF.Round(_Exit.AngleDegrees / step);
		var arcPositions = new List<Vector3>(2 * n + 1);
		for (int k = -n; k <= n; k++)
		{
			int idx = ((iCenter + k) % segments + segments) % segments;
			arcPositions.Add(Rotation.FromYaw(idx * step).Forward * Radius);
		}
		Vector3 arcMidpoint   = arcPositions[n];
		Vector3 outerMidpoint = exitDir * snapDistance;

		// Helper: add a CCW triangle (viewed from +Z = above) with planar UVs scaled by RoadTextureRepeat.
		void AddTri(Vector3 a, Vector3 b, Vector3 c)
		{
			var vA = MeshUtility.GetOrAddVertex(_Mesh, _Cache, a);
			var vB = MeshUtility.GetOrAddVertex(_Mesh, _Cache, b);
			var vC = MeshUtility.GetOrAddVertex(_Mesh, _Cache, c);
			Vector2 uvA = new Vector2(a.x, a.y) / RoadTextureRepeat;
			Vector2 uvB = new Vector2(b.x, b.y) / RoadTextureRepeat;
			Vector2 uvC = new Vector2(c.x, c.y) / RoadTextureRepeat;
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, vA, vB, vC, uvA, uvB, uvC);
		}

		// ── LEFT zigzag strip ──
		// Rails: arc[0..n] (innerMinus → arcMidpoint) ←→ wallMinus[0..n] (innerMinus → outerMinus).
		// Both rails START at innerMinus (i = 0) — they share that vertex, so iteration 0 produces only ONE
		// triangle (the wedge tip), not two. The would-be first triangle (a0, w0, w1) is degenerate because
		// a0 = w0 = innerMinus.
		// CCW winding (interior on the left, arc is on the disc side which is CCW around the corridor interior):
		//   triangle (arc[i],   wall[i],   wall[i+1])    — skipped at i = 0 (degenerate)
		//   triangle (arc[i],   wall[i+1], arc[i+1])
		for (int i = 0; i < n; i++)
		{
			Vector3 a0 = arcPositions[i];
			Vector3 a1 = arcPositions[i + 1];
			Vector3 w0 = wallMinus[i];
			Vector3 w1 = wallMinus[i + 1];

			if (i > 0)
				AddTri(a0, w0, w1);
			AddTri(a0, w1, a1);
		}

		// ── RIGHT zigzag strip ──
		// Mirror of the left strip. Rails: arc[2n..n] (innerPlus → arcMidpoint, walking arc in REVERSE) ←→
		// wallPlus[0..n] (innerPlus → outerPlus). Both rails START at innerPlus (i = 0) — same wedge-tip
		// degeneracy as the left strip, so the first (a0, w1, w0) triangle is skipped at i = 0.
		// CCW winding is the mirror of the left strip (arc is now on the CW side relative to the wall):
		//   triangle (arc[i],   wall[i+1], wall[i])      — skipped at i = 0 (degenerate)
		//   triangle (arc[i],   arc[i+1],  wall[i+1])
		// where arc[i] means arcPositions[2n - i] (so arc[0] = innerPlus, arc[n] = arcMidpoint).
		for (int i = 0; i < n; i++)
		{
			Vector3 a0 = arcPositions[2 * n - i];
			Vector3 a1 = arcPositions[2 * n - i - 1];
			Vector3 w0 = wallPlus[i];
			Vector3 w1 = wallPlus[i + 1];

			if (i > 0)
				AddTri(a0, w1, w0);
			AddTri(a0, a1, w1);
		}

		// ── Bottom closure ──
		// At the bottom-middle, the two strips have reached (outerMinus on the left wall, arcMidpoint at the apex,
		// outerPlus on the right wall). Close the bottom with two triangles meeting at outerMidpoint:
		//   (arcMidpoint, outerMinus,    outerMidpoint)  ← left closure
		//   (arcMidpoint, outerMidpoint, outerPlus)      ← right closure
		// Both are CCW from above. arcMidpoint is the shared "top" of the closure region; outerMidpoint splits the
		// outer edge so each side gets its own triangle (no degenerate collinear edges).
		AddTri(arcMidpoint, outerMinus,    outerMidpoint);
		AddTri(arcMidpoint, outerMidpoint, outerPlus);
	}



	private void BuildCircleSidewalk(PolygonMesh _Mesh, Material _Material)
	{
		var cache = new Dictionary<Vector3, HalfEdgeMesh.VertexHandle>();

		int segments = DiscSegments;
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

		// Sidewalk strips alongside each exit corridor, curb walls on both sides of the exit road
		foreach (var exit in CircleExits ?? [])
			AddCircleExitSidewalk(_Mesh, _Material, cache, exit);
	}



	private void AddCircleExitSidewalk(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, CircleExit _Exit)
	{
		float halfRoadWidth = _Exit.RoadWidth * 0.5f;
		float snapDistance = GetCircleExitSnapDistance();

		GetCircleExitGridAngles(_Exit, out float gridAngleCw, out float gridAngleCcw);

		Vector3 exitDir = Rotation.FromYaw(_Exit.AngleDegrees).Forward;
		Vector3 perpDir = Vector3.Cross(exitDir, Vector3.Up).Normal;

		Vector3 innerMinus = Rotation.FromYaw(gridAngleCw).Forward  * Radius;
		Vector3 innerPlus  = Rotation.FromYaw(gridAngleCcw).Forward * Radius;
		Vector3 outerMinus = exitDir * snapDistance + perpDir * halfRoadWidth;
		Vector3 outerPlus  = exitDir * snapDistance - perpDir * halfRoadWidth;

		Vector3 radialMinus = Rotation.FromYaw(gridAngleCw).Forward;
		Vector3 radialPlus  = Rotation.FromYaw(gridAngleCcw).Forward;

		Vector3 tangentMinus = Rotation.FromYaw(gridAngleCw  + 90.0f).Forward;
		Vector3 tangentPlus  = Rotation.FromYaw(gridAngleCcw - 90.0f).Forward;

		// Curved inner edges (matches the corridor walls) and outer edges (offset outward).
		// Inner-edge tangents start with the disc tangent, end with exitDir → smooth blend.
		// Outer-edge tangents start with the disc radial (so it lands on ring outer corner)
		// and end with perpDir (so it lands on road sidewalk outer corner) → no spike.
		var innerMinusVerts = BuildExitWallVerts(innerMinus, outerMinus, tangentMinus, exitDir);
		var outerMinusVerts = BuildExitWallVerts(
			innerMinus + radialMinus * SidewalkWidth,
			outerMinus + perpDir     * SidewalkWidth,
			tangentMinus, exitDir);

		var innerPlusVerts = BuildExitWallVerts(innerPlus, outerPlus, tangentPlus, exitDir);
		var outerPlusVerts = BuildExitWallVerts(
			innerPlus + radialPlus * SidewalkWidth,
			outerPlus - perpDir    * SidewalkWidth,
			tangentPlus, exitDir);

		// Accumulate V coordinate along each inner curve (= cumulative arc length / SidewalkTextureRepeat).
		// Both inner and outer edges share the same V at each curve parameter, so the texture flows along the curve
		// (same scheme as the rectangle's rounded sidewalk corners).
		float[] vMinus = new float[innerMinusVerts.Count];
		for (int i = 1; i < innerMinusVerts.Count; i++)
			vMinus[i] = vMinus[i - 1] + Vector3.DistanceBetween(innerMinusVerts[i - 1], innerMinusVerts[i]) / SidewalkTextureRepeat;

		float[] vPlus = new float[innerPlusVerts.Count];
		for (int i = 1; i < innerPlusVerts.Count; i++)
			vPlus[i] = vPlus[i - 1] + Vector3.DistanceBetween(innerPlusVerts[i - 1], innerPlusVerts[i]) / SidewalkTextureRepeat;

		int n = innerMinusVerts.Count - 1;

		// Minus side: walk inner edge outer→inner so A→B→C→D is CCW for top-up normal.
		for (int i = n - 1; i >= 0; i--)
			AddCircleExitSidewalkSegment(_Mesh, _Material, _Cache,
				innerMinusVerts[i + 1], innerMinusVerts[i],
				outerMinusVerts[i],     outerMinusVerts[i + 1],
				vMinus[i + 1],          vMinus[i]);

		// Plus side: walk inner edge inner→outer (opposite direction to the minus side).
		for (int i = 0; i < n; i++)
			AddCircleExitSidewalkSegment(_Mesh, _Material, _Cache,
				innerPlusVerts[i],     innerPlusVerts[i + 1],
				outerPlusVerts[i + 1], outerPlusVerts[i],
				vPlus[i],              vPlus[i + 1]);
	}



	// Builds one trapezoidal sidewalk segment given 4 ground-level corners in CCW order (top face up-facing).
	// A-B = corridor wall edge (curb face faces road); C-D = outer edge (outer face faces away from road).
	// A and D share V_A (one end of the segment along the curve); B and C share V_B (other end).
	// U = perpendicular width from inner edge to outer edge (variable along the curve since the strip isn't strictly parallel).
	// This matches the rectangle's rounded-corner UV scheme so the texture flows along the curve without stretching.
	private void AddCircleExitSidewalkSegment(PolygonMesh _Mesh, Material _Material, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache,
		Vector3 _A, Vector3 _B, Vector3 _C, Vector3 _D, float _VA, float _VB)
	{
		Vector3 up = Vector3.Up;
		float h = SidewalkHeight;
		float hHeight = h / SidewalkTextureRepeat;
		float uA = Vector3.DistanceBetween(_A, _D) / SidewalkTextureRepeat;
		float uB = Vector3.DistanceBetween(_B, _C) / SidewalkTextureRepeat;

		Vector3 aT = _A + up * h;
		Vector3 bT = _B + up * h;
		Vector3 cT = _C + up * h;
		Vector3 dT = _D + up * h;

		var vA  = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _A);
		var vB  = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _B);
		var vC  = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _C);
		var vD  = MeshUtility.GetOrAddVertex(_Mesh, _Cache, _D);
		var vAT = MeshUtility.GetOrAddVertex(_Mesh, _Cache, aT);
		var vBT = MeshUtility.GetOrAddVertex(_Mesh, _Cache, bT);
		var vCT = MeshUtility.GetOrAddVertex(_Mesh, _Cache, cT);
		var vDT = MeshUtility.GetOrAddVertex(_Mesh, _Cache, dT);

		// Top face — U = 0 on inner edge (A, B), U = uA/uB on outer edge (D, C); V follows the curve.
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vAT, vBT, vCT, vDT,
			new Vector2(0, _VA), new Vector2(0, _VB), new Vector2(uB, _VB), new Vector2(uA, _VA));

		// Curb face along A-B edge — U is the wall height (0 at top, hHeight at ground), V follows the curve.
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vAT, vA, vB, vBT,
			new Vector2(0, _VA), new Vector2(hHeight, _VA), new Vector2(hHeight, _VB), new Vector2(0, _VB));

		// Outer face along C-D edge.
		MeshUtility.AddTexturedQuad(_Mesh, _Material, vCT, vC, vD, vDT,
			new Vector2(0, _VB), new Vector2(hHeight, _VB), new Vector2(hHeight, _VA), new Vector2(0, _VA));
	}



	private static float AngleDelta(float _A, float _B)
	{
		float d = (_A - _B) % 360.0f;

		if (d > 180.0f) d -= 360.0f;
		if (d < -180.0f) d += 360.0f;

		return MathF.Abs(d);
	}



	// True if either endpoint of arc segment [_A0, _A1] falls inside an exit's grid-aligned gap.
	// The gap span is the same (2 * CornerSegments * step) used by GetCircleExitGridAngles, so disc segments are
	// skipped exactly where the corridor takes over — vertices weld cleanly at the gap boundary.
	private bool ArcBlockedByExit(float _A0, float _A1)
	{
		int segments = DiscSegments;
		float step = 360.0f / segments;
		int n = (int)Math.Max(2, DiscSegments * SegmentsMultiplier);
		float halfGap = n * step;

		foreach (var exit in CircleExits ?? Array.Empty<CircleExit>())
		{
			// Use the same grid-snapped center that GetCircleExitGridAngles uses so the blocked window
			// aligns exactly with the corridor bounds even when the exit angle isn't on a grid line.
			int iCenter = (int)MathF.Round(exit.AngleDegrees / step);
			float snappedCenter = iCenter * step;

			if (AngleDelta(_A0, snappedCenter) < halfGap || AngleDelta(_A1, snappedCenter) < halfGap)
				return true;
		}

		return false;
	}



	public Transform GetCircleExitTransform(int _Index, bool _IncludeSidewalk = false)
	{
		var exit = CircleExits[_Index];

		Vector3 dir = WorldRotation * Rotation.FromYaw(exit.AngleDegrees).Forward;
		// When sidewalk is included, snap target sits at the corridor's outer edge — well beyond R+SW
		float dist = _IncludeSidewalk ? GetCircleExitSnapDistance() : Radius;

		return new Transform
		{
			Position = WorldPosition + dir * dist,
			Rotation = Rotation.LookAt(dir, WorldRotation.Up)
		};
	}
}
