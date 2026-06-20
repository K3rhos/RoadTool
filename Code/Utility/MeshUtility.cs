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
		if (HasDuplicateVertex(_A, _B, _C, _D))
			return;

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
		if (HasDuplicateVertex(_A, _B, _C))
			return;

		var face = _Mesh.AddFace(_A, _B, _C);

		if (!face.IsValid)
			return;

		_Mesh.SetFaceMaterial(face, _Material);
		_Mesh.SetFaceTextureCoords(face, new List<Vector2> { _UvA, _UvB, _UvC });
	}



	private static bool HasDuplicateVertex(HalfEdgeMesh.VertexHandle _A, HalfEdgeMesh.VertexHandle _B, HalfEdgeMesh.VertexHandle _C)
	{
		return _A.Equals(_B) || _A.Equals(_C) || _B.Equals(_C);
	}



	private static bool HasDuplicateVertex(HalfEdgeMesh.VertexHandle _A, HalfEdgeMesh.VertexHandle _B, HalfEdgeMesh.VertexHandle _C, HalfEdgeMesh.VertexHandle _D)
	{
		return _A.Equals(_B) || _A.Equals(_C) || _A.Equals(_D) ||
			_B.Equals(_C) || _B.Equals(_D) ||
			_C.Equals(_D);
	}
}
