using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public sealed class MeshBuilder
{
	private readonly GameObject m_GameObject;

	private SceneObject m_SceneObject;
	private CustomCollider m_Collider;

	private readonly List<Mesh> m_Meshes = [];
	private readonly Dictionary<string, SubMesh> m_SubMeshes = [];
	private Model m_Model = Model.Error;

	private class SubMesh
	{
		public Vertex[] Vertices;
		public int[] Indices;
		public Material Material;
		public bool HasCollision;
		public int TotalVertices;
		public int TotalIndices;
		public int CurrentVertex;
		public int CurrentIndex;
	}

	public bool IsDirty { get; set; }
	public bool CastShadows { get; set; } = true;
	public Action OnBuild { get; set; }



	public MeshBuilder(GameObject _GameObject)
	{
		m_GameObject = _GameObject;
	}



	public void InitSubmesh(string _Name, int _TotalVertices, int _TotalIndices, Material _Material, bool _HasCollision)
	{
		if (!m_SubMeshes.ContainsKey(_Name))
		{
			m_SubMeshes[_Name] = new SubMesh();
		}

		var submesh = m_SubMeshes[_Name];
		submesh.TotalVertices = _TotalVertices;
		submesh.TotalIndices = _TotalIndices;
		submesh.Vertices = new Vertex[_TotalVertices];
		submesh.Indices = new int[_TotalIndices];
		submesh.Material = _Material;
		submesh.HasCollision = _HasCollision;
		submesh.CurrentVertex = 0;
		submesh.CurrentIndex = 0;
	}



	public void AddTriangle
	(
		string _SubmeshName,
		Vector3 _A, Vector3 _B, Vector3 _C,
		Vector3 _Normal,
		Vector3 _Tangent,
		Vector2 _UVA,
		Vector2 _UVB,
		Vector2 _UVC
	)
	{
		if (!m_SubMeshes.TryGetValue(_SubmeshName, out var submesh))
			return;

		int start = submesh.CurrentVertex;
		int vert = submesh.CurrentVertex;
		int idx = submesh.CurrentIndex;

		submesh.Vertices[vert++] = new Vertex { Position = _A, Normal = _Normal, Tangent = new Vector4(_Tangent, 1), TexCoord0 = _UVA };
		submesh.Vertices[vert++] = new Vertex { Position = _B, Normal = _Normal, Tangent = new Vector4(_Tangent, 1), TexCoord0 = _UVB };
		submesh.Vertices[vert++] = new Vertex { Position = _C, Normal = _Normal, Tangent = new Vector4(_Tangent, 1), TexCoord0 = _UVC };

		submesh.Indices[idx++] = start + 0;
		submesh.Indices[idx++] = start + 1;
		submesh.Indices[idx++] = start + 2;

		submesh.CurrentVertex = vert;
		submesh.CurrentIndex = idx;
	}



	public void AddQuad
	(
		string _SubmeshName,
		Vector3 _A, Vector3 _B, Vector3 _C, Vector3 _D,
		Vector3 _NormalA, Vector3 _NormalB, Vector3 _NormalC, Vector3 _NormalD,
		Vector3 _Tangent,
		Vector2 _UVA, Vector2 _UVB, Vector2 _UVC, Vector2 _UVD
	)
	{
		if (!m_SubMeshes.TryGetValue(_SubmeshName, out var submesh))
			return;

		int start = submesh.CurrentVertex;
		int vert = submesh.CurrentVertex;
		int idx = submesh.CurrentIndex;

		submesh.Vertices[vert++] = new Vertex { Position = _A, Normal = _NormalA, Tangent = new Vector4(_Tangent, 1), TexCoord0 = _UVA };
		submesh.Vertices[vert++] = new Vertex { Position = _B, Normal = _NormalB, Tangent = new Vector4(_Tangent, 1), TexCoord0 = _UVB };
		submesh.Vertices[vert++] = new Vertex { Position = _C, Normal = _NormalC, Tangent = new Vector4(_Tangent, 1), TexCoord0 = _UVC };
		submesh.Vertices[vert++] = new Vertex { Position = _D, Normal = _NormalD, Tangent = new Vector4(_Tangent, 1), TexCoord0 = _UVD };

		submesh.Indices[idx++] = start + 0;
		submesh.Indices[idx++] = start + 1;
		submesh.Indices[idx++] = start + 2;

		submesh.Indices[idx++] = start + 0;
		submesh.Indices[idx++] = start + 2;
		submesh.Indices[idx++] = start + 3;

		submesh.CurrentVertex = vert;
		submesh.CurrentIndex = idx;
	}



	// Kept for retro compatibility (Single normal vector version)
	public void AddQuad
	(
		string _SubmeshName,
		Vector3 _A, Vector3 _B, Vector3 _C, Vector3 _D,
		Vector3 _Normal,
		Vector3 _Tangent,
		Vector2 _UVA, Vector2 _UVB, Vector2 _UVC, Vector2 _UVD
	)
	{
		AddQuad(_SubmeshName, _A, _B, _C, _D, _Normal, _Normal, _Normal, _Normal, _Tangent, _UVA, _UVB, _UVC, _UVD);
	}



	public void Update()
	{
		if (!m_GameObject.IsValid())
			return;

		if (m_SceneObject.IsValid())
			m_SceneObject.Transform = m_GameObject.WorldTransform;

		if (IsDirty)
		{
			Rebuild();

			IsDirty = false;
		}
	}



	public void Rebuild()
	{
		if (!m_GameObject.IsValid())
			return;

		Clear();
		BuildModel();
		BuildCollider();
	}



	public void Clear()
	{
		ClearCollider();
		ClearModel();

		m_SubMeshes.Clear();
	}



	private void BuildModel()
	{
		OnBuild?.Invoke();

		if (m_SubMeshes.Count == 0)
			return;

		m_Meshes.Clear();

		ModelBuilder modelBuilder = Model.Builder;

		foreach (var (_, submesh) in m_SubMeshes)
		{
			if (submesh.TotalVertices == 0 || submesh.TotalIndices == 0)
				continue;

			Mesh mesh = new Mesh
			{
				Material = submesh.Material ?? Material.Load("materials/default.vmat")
			};

			mesh.CreateVertexBuffer(submesh.TotalVertices, submesh.Vertices);
			mesh.CreateIndexBuffer(submesh.TotalIndices, submesh.Indices);
			mesh.Bounds = BBox.FromPoints(submesh.Vertices.Select(v => v.Position));

			m_Meshes.Add(mesh);
			modelBuilder.AddMesh(mesh);
		}

		m_Model = modelBuilder.Create();

		if (m_SceneObject.IsValid())
		{
			m_SceneObject.Model = m_Model;
		}
		else
		{
			m_SceneObject = new SceneObject(m_GameObject.Scene.SceneWorld, m_Model, m_GameObject.WorldTransform)
			{
				Batchable = false,
				Flags =
				{
					CastShadows = CastShadows
				}
			};
		}
	}



	private void BuildCollider()
	{
		// Combine all collision-enabled submeshes into one collider
		var collisionVertices = new List<Vector3>();
		var collisionIndices = new List<int>();

		int vertexOffset = 0;

		foreach (var (_, submesh) in m_SubMeshes)
		{
			if (!submesh.HasCollision || submesh.TotalVertices == 0)
				continue;

			collisionVertices.AddRange(submesh.Vertices.Select(v => v.Position));
			collisionIndices.AddRange(submesh.Indices.Select(index => index + vertexOffset));

			vertexOffset += submesh.TotalVertices;
		}

		if (collisionVertices.Count == 0)
			return;

		m_Collider = m_GameObject.AddComponent<CustomCollider>();
		m_Collider.Flags |= ComponentFlags.Hidden;
		m_Collider.Flags |= ComponentFlags.NotSaved;
		m_Collider.Flags |= ComponentFlags.NotCloned;
		m_Collider.Static = true;

		m_Collider.SetMeshShape(collisionVertices, collisionIndices);
	}



	private void ClearModel()
	{
		m_SceneObject?.Delete();
		m_SceneObject = null;

		m_Meshes.Clear();
	}



	private void ClearCollider()
	{
		m_Collider?.Destroy();
		m_Collider = null;
	}
}
