using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public static class MeshUtility
{
	public static HalfEdgeMesh.VertexHandle GetOrAddVertex(PolygonMesh _Mesh, Dictionary<Vector3, HalfEdgeMesh.VertexHandle> _Cache, Vector3 _Pos)
	{
		if (!_Cache.TryGetValue(_Pos, out var handle))
		{
			handle = _Mesh.AddVertices(_Pos)[0];
			_Cache[_Pos] = handle;
		}

		return handle;
	}



	public static void AddTexturedQuad(PolygonMesh _Mesh, Material _Material,
		HalfEdgeMesh.VertexHandle _A, HalfEdgeMesh.VertexHandle _B,
		HalfEdgeMesh.VertexHandle _C, HalfEdgeMesh.VertexHandle _D,
		Vector2 _UvA, Vector2 _UvB, Vector2 _UvC, Vector2 _UvD)
	{
		var face = _Mesh.AddFace(_A, _B, _C, _D);

		if (!face.IsValid)
			return;

		_Mesh.SetFaceMaterial(face, _Material);
		_Mesh.SetFaceTextureCoords(face, new List<Vector2> { _UvA, _UvB, _UvC, _UvD });
	}



	public static void AddTexturedTriangle(PolygonMesh _Mesh, Material _Material,
		HalfEdgeMesh.VertexHandle _A, HalfEdgeMesh.VertexHandle _B,
		HalfEdgeMesh.VertexHandle _C,
		Vector2 _UvA, Vector2 _UvB, Vector2 _UvC)
	{
		var face = _Mesh.AddFace(_A, _B, _C);

		if (!face.IsValid)
			return;

		_Mesh.SetFaceMaterial(face, _Material);
		_Mesh.SetFaceTextureCoords(face, new List<Vector2> { _UvA, _UvB, _UvC });
	}
}
