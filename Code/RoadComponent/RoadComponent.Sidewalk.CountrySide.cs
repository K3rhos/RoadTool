using System;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Utility;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	/// <summary>
	/// Builds the "Country Side" sidewalk: a flat road-material shoulder that steps down and falls away on an
	/// undulating, organically irregular slope (sidewalk material) which merges into the terrain. The shoulder and the
	/// verge live in separate meshes because each carries a different material (matching the one-material-per-mesh flow
	/// the rest of the tool uses). Vertices are shared within each mesh so the rolling slope shades smoothly while the
	/// near-vertical drop and the road/verge material seam stay crisp.
	/// </summary>
	private void BuildCountrySideSidewalk(Transform[] _Frames, List<int> _SegmentsToKeep)
	{
		var roadMaterial = RoadMaterial ?? Material.Load("materials/dev/reflectivity_30.vmat");
		var sidewalkMaterial = SidewalkMaterial ?? Material.Load("materials/dev/reflectivity_70.vmat");

		var shoulderMesh = new PolygonMesh(); // ROAD material: the flat shoulder + the vertical drop edge (the road slab's thickness)
		var vergeMesh = new PolygonMesh();    // SIDEWALK material: the undulating slope down to the terrain

		int count = _SegmentsToKeep.Count;
		float halfRoad = RoadWidth * 0.5f;
		int slopeSegments = Math.Max(1, VergeSlopeSegments);
		int nodeCount = 2 + slopeSegments;  // 0 = drop top (road level), 1 = drop bottom, 2.. = slope down to the outer edge
		int vergeNodeCount = nodeCount - 1; // the slope reuses the drop-bottom node as its top

		// Along-road distance drives the V coordinate and the noise sampling, so the wobble is stable frame to frame.
		var dist = new float[count];
		for (int i = 1; i < count; i++)
			dist[i] = dist[i - 1] + Vector3.DistanceBetween(_Frames[_SegmentsToKeep[i - 1]].Position, _Frames[_SegmentsToKeep[i]].Position);

		// shoulder verts per frame = [roadEdge, dropTop (node0), dropBottom (node1)]; verge verts = [node1 .. node(nodeCount-1)]
		var shoulderVerts = new HalfEdgeMesh.VertexHandle[2][][];
		var shoulderTopUV = new Vector2[2][][];
		var vergeVerts = new HalfEdgeMesh.VertexHandle[2][][];
		var vergeU = new float[2][][];

		// side 0 = right (+1), side 1 = left (-1)
		for (int s = 0; s < 2; s++)
		{
			float sideSign = s == 0 ? 1.0f : -1.0f;

			shoulderVerts[s] = new HalfEdgeMesh.VertexHandle[count][];
			shoulderTopUV[s] = new Vector2[count][];
			vergeVerts[s] = new HalfEdgeMesh.VertexHandle[count][];
			vergeU[s] = new float[count][];

			for (int i = 0; i < count; i++)
			{
				var frameNodes = new Vector3[nodeCount];
				Vector3 roadEdge = ComputeCountrySideProfile(_Frames[_SegmentsToKeep[i]], dist[i], sideSign, halfRoad, slopeSegments, frameNodes);

				shoulderVerts[s][i] = new[]
				{
					shoulderMesh.AddVertices(roadEdge)[0],
					shoulderMesh.AddVertices(frameNodes[0])[0],
					shoulderMesh.AddVertices(frameNodes[1])[0],
				};
				shoulderTopUV[s][i] = new[] { PlanarRoadUV(roadEdge), PlanarRoadUV(frameNodes[0]) };

				// The slope shares the drop-bottom node (node1) as its top, so it starts at index 1.
				var slopeNodes = new Vector3[vergeNodeCount];
				Array.Copy(frameNodes, 1, slopeNodes, 0, vergeNodeCount);
				vergeVerts[s][i] = vergeMesh.AddVertices(slopeNodes);

				var u = new float[vergeNodeCount];
				for (int n = 1; n < vergeNodeCount; n++)
					u[n] = u[n - 1] + Vector3.DistanceBetween(slopeNodes[n - 1], slopeNodes[n]) / SidewalkTextureRepeat;
				vergeU[s][i] = u;
			}
		}

		float roadRepeat = RoadTextureInchesPerRepeat;
		float dropU = CountrySideDrop / roadRepeat;

		for (int s = 0; s < 2; s++)
		{
			bool leftSide = s == 1;

			for (int i = 0; i < count - 1; i++)
			{
				// Flat road-material shoulder (road edge -> shoulder outer edge), planar UVs so it tiles with the road.
				AddCountrySideQuad(shoulderMesh, roadMaterial, leftSide,
					shoulderVerts[s][i][0], shoulderVerts[s][i][1], shoulderVerts[s][i + 1][0], shoulderVerts[s][i + 1][1],
					shoulderTopUV[s][i][0], shoulderTopUV[s][i][1], shoulderTopUV[s][i + 1][0], shoulderTopUV[s][i + 1][1]);

				// Vertical drop = the exposed thickness of the road slab, so it keeps the ROAD material. Wrapped UVs
				// (U down the drop, V along the road) because a planar projection would smear on a vertical face.
				float dropV0 = dist[i] / roadRepeat;
				float dropV1 = dist[i + 1] / roadRepeat;
				AddCountrySideQuad(shoulderMesh, roadMaterial, leftSide,
					shoulderVerts[s][i][1], shoulderVerts[s][i][2], shoulderVerts[s][i + 1][1], shoulderVerts[s][i + 1][2],
					new Vector2(0.0f, dropV0), new Vector2(dropU, dropV0), new Vector2(0.0f, dropV1), new Vector2(dropU, dropV1));

				// Undulating slope (sidewalk material).
				float v0 = dist[i] / SidewalkTextureRepeat;
				float v1 = dist[i + 1] / SidewalkTextureRepeat;
				for (int n = 0; n < vergeNodeCount - 1; n++)
				{
					AddCountrySideQuad(vergeMesh, sidewalkMaterial, leftSide,
						vergeVerts[s][i][n], vergeVerts[s][i][n + 1], vergeVerts[s][i + 1][n], vergeVerts[s][i + 1][n + 1],
						new Vector2(vergeU[s][i][n], v0), new Vector2(vergeU[s][i][n + 1], v0),
						new Vector2(vergeU[s][i + 1][n], v1), new Vector2(vergeU[s][i + 1][n + 1], v1));
				}
			}
		}

		CreateCountrySideMeshChild("Sidewalk Shoulder", shoulderMesh);
		CreateCountrySideMeshChild("Sidewalk", vergeMesh);
	}



	/// <summary>
	/// Fills <paramref name="_Nodes"/> with the verge cross-section (node 0 = top of the drop at road level, node 1 =
	/// bottom of the drop, the rest stepping down the slope to the outer edge) and returns the road-edge point where the
	/// flat shoulder begins. Perlin/FBM noise meanders the shoulder and outer edges and rolls the slope depth so the
	/// verge reads like uneven natural ground; a per-side seed keeps the two sides from mirroring each other.
	/// </summary>
	private Vector3 ComputeCountrySideProfile(Transform _Frame, float _Dist, float _SideSign, float _HalfRoad, int _SlopeSegments, Vector3[] _Nodes)
	{
		Vector3 p = _Frame.Position;
		Vector3 outward = _Frame.Rotation.Right * _SideSign; // points away from the road centre for this side
		Vector3 up = _Frame.Rotation.Up;

		float scale = VergeChaosScale;
		float amp = VergeChaosAmount;
		float seed = _SideSign > 0.0f ? 0.0f : 1337.0f;

		// Noise is 0..1; centre it to roughly ±amp. Separate Y bands keep the three offsets from correlating.
		float meanderShoulder = (Noise.Perlin(_Dist * scale + seed, 11.0f) - 0.5f) * 2.0f * amp;
		float meanderOuter = (Noise.Perlin(_Dist * scale + seed, 71.0f) - 0.5f) * 2.0f * amp;

		// Floor at a small positive width so a strong inward meander can never collapse two nodes onto each other
		// (a zero-width strip would be a degenerate, dropped face).
		float shoulderWidth = MathF.Max(1.0f, SidewalkWidth + meanderShoulder);
		float vergeSpan = MathF.Max(1.0f, CountrySideVergeWidth + meanderOuter);
		float latShoulder = _HalfRoad + shoulderWidth;

		_Nodes[0] = p + outward * latShoulder;                        // drop top (road level)
		_Nodes[1] = p + outward * latShoulder - up * CountrySideDrop; // drop bottom

		for (int k = 1; k <= _SlopeSegments; k++)
		{
			float f = (float)k / _SlopeSegments;
			// Roll varies along the road AND across the slope (Y = nominal cross position) so the surface undulates in 3D.
			float roll = (Noise.Fbm(3, _Dist * scale + seed, 200.0f + f * CountrySideVergeWidth * scale) - 0.5f) * 2.0f * amp;
			float lat = latShoulder + f * vergeSpan;
			float h = -CountrySideDrop - f * CountrySideVergeDepth + roll;

			_Nodes[1 + k] = p + outward * lat + up * h;
		}

		return p + outward * _HalfRoad;
	}



	private Vector2 PlanarRoadUV(Vector3 _WorldPos)
	{
		return new Vector2(_WorldPos.x, _WorldPos.y) / RoadTextureInchesPerRepeat;
	}



	/// <summary>
	/// Emits one up/outward-facing quad of a verge strip. The left side is the mirror of the right, which flips the
	/// winding, so the vertex order is chosen per side to keep every face front-facing.
	/// </summary>
	private static void AddCountrySideQuad(PolygonMesh _Mesh, Material _Material, bool _LeftSide,
		HalfEdgeMesh.VertexHandle _Inner0, HalfEdgeMesh.VertexHandle _Outer0,
		HalfEdgeMesh.VertexHandle _Inner1, HalfEdgeMesh.VertexHandle _Outer1,
		Vector2 _UvInner0, Vector2 _UvOuter0, Vector2 _UvInner1, Vector2 _UvOuter1)
	{
		// Emit two triangles rather than a quad. The rolling slope and lateral meander make many of these faces
		// non-planar (and, at high chaos scale, slightly folded), which AddFace rejects outright — that is the holes
		// that appear when the chaos is pushed up. The two triangles a quad splits into are always planar, so they hold.
		if (!_LeftSide)
		{
			// quad winding: Inner0 -> Outer0 -> Outer1 -> Inner1
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, _Inner0, _Outer0, _Outer1, _UvInner0, _UvOuter0, _UvOuter1);
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, _Inner0, _Outer1, _Inner1, _UvInner0, _UvOuter1, _UvInner1);
		}
		else
		{
			// quad winding: Inner0 -> Inner1 -> Outer1 -> Outer0
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, _Inner0, _Inner1, _Outer1, _UvInner0, _UvInner1, _UvOuter1);
			MeshUtility.AddTexturedTriangle(_Mesh, _Material, _Inner0, _Outer1, _Outer0, _UvInner0, _UvOuter1, _UvOuter0);
		}
	}



	private void CreateCountrySideMeshChild(string _Name, PolygonMesh _PolygonMesh)
	{
		var child = new GameObject(GameObject, true, _Name);
		child.Tags.Add(RoadMeshTag);
		child.Tags.Add(SidewalkSurfaceTag);

		var meshComponent = child.AddComponent<MeshComponent>();
		meshComponent.Mesh = _PolygonMesh;
		meshComponent.SmoothingAngle = 40.0f;
	}
}
