using System;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RailComponent
{
	[Property, FeatureEnabled("Sleepers", Icon = "horizontal_rule", Tint = EditorTint.Yellow)] private bool HasSleepers { get; set { field = value; IsDirty = true; } } = true;
	[Property(Title = "Material"), Feature("Sleepers")] private Material SleeperMaterial { get; set { field = value; IsDirty = true; } }

	/// <summary>Distance along the track between consecutive sleepers. The count is derived from the spline length.</summary>
	[Property(Title = "Spacing"), Feature("Sleepers"), Range(10.0f, 1000.0f)] private float SleeperSpacing { get; set { field = value; IsDirty = true; } } = 60.0f;

	/// <summary>How far each sleeper extends past the OUTER edge of the rails on each side.</summary>
	[Property(Title = "Overhang"), Feature("Sleepers"), Range(0.0f, 500.0f)] private float SleeperOverhang { get; set { field = value; IsDirty = true; } } = 30.0f;

	/// <summary>Sleeper thickness measured along the track.</summary>
	[Property(Title = "Width"), Feature("Sleepers"), Range(2.0f, 500.0f)] private float SleeperWidth { get; set { field = value; IsDirty = true; } } = 25.0f;

	/// <summary>Sleeper height (the rails sit on top of this).</summary>
	[Property(Title = "Height"), Feature("Sleepers"), Range(1.0f, 200.0f)] private float SleeperHeight { get; set { field = value; IsDirty = true; } } = 12.0f;

	/// <summary>World units per texture tile for the box (cube) projection — bigger means the texture covers more of the sleeper.</summary>
	[Property(Title = "Texture Repeat"), Feature("Sleepers")] private float SleeperTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 100.0f;



	private void BuildSleepers(Transform[] _Frames)
	{
		float length = Spline.Length;
		float spacing = Math.Max(1.0f, SleeperSpacing);

		if (length <= 0.0f)
			return;

		var material = SleeperMaterial ?? Material.Load("materials/dev/reflectivity_50.vmat");
		var polygonMesh = new PolygonMesh();

		// Reach from the track centre out past the outer edge of each rail by the overhang.
		float halfLength = RailGauge * 0.5f + RailWidth * 0.5f + SleeperOverhang;
		float halfWidth = SleeperWidth * 0.5f;
		float textureRepeat = Math.Max(1.0f, SleeperTextureRepeat);

		int count = Math.Max(1, (int)MathF.Floor(length / spacing)) + 1;

		for (int s = 0; s < count; s++)
		{
			float distance = MathF.Min(s * spacing, length);
			var frame = SampleFrameAtDistance(_Frames, distance, length);

			AddSleeperBox(polygonMesh, material, frame, halfLength, halfWidth, SleeperHeight, textureRepeat);
		}

		CreateSleeperChild("Sleepers", polygonMesh);
	}



	/// <summary>Adds one rectangular sleeper box to the shared mesh, oriented by the frame (right = across the track).</summary>
	private static void AddSleeperBox(PolygonMesh _Mesh, Material _Material, Transform _Frame, float _HalfLength, float _HalfWidth, float _Height, float _TextureRepeat)
	{
		Vector3 origin = _Frame.Position;
		Vector3 right = _Frame.Rotation.Right;
		Vector3 forward = _Frame.Rotation.Forward;
		Vector3 up = _Frame.Rotation.Up;

		Vector3 Corner(float r, float f, float u) => origin + right * r + forward * f + up * u;

		var c000 = Corner(-_HalfLength, -_HalfWidth, 0.0f);
		var c100 = Corner(_HalfLength, -_HalfWidth, 0.0f);
		var c110 = Corner(_HalfLength, _HalfWidth, 0.0f);
		var c010 = Corner(-_HalfLength, _HalfWidth, 0.0f);
		var c001 = Corner(-_HalfLength, -_HalfWidth, _Height);
		var c101 = Corner(_HalfLength, -_HalfWidth, _Height);
		var c111 = Corner(_HalfLength, _HalfWidth, _Height);
		var c011 = Corner(-_HalfLength, _HalfWidth, _Height);

		// Box (cube) projection: each face is textured from the two frame-space axes it spans, scaled by a
		// constant world size, so the texel density stays uniform instead of being stretched to fit each face.
		float r0 = -_HalfLength / _TextureRepeat, r1 = _HalfLength / _TextureRepeat;
		float f0 = -_HalfWidth / _TextureRepeat, f1 = _HalfWidth / _TextureRepeat;
		float u0 = 0.0f, u1 = _Height / _TextureRepeat;

		// ends span (forward, up)
		AddSleeperQuad(_Mesh, _Material, c100, c110, c111, c101, new Vector2(f0, u0), new Vector2(f1, u0), new Vector2(f1, u1), new Vector2(f0, u1)); // +right end
		AddSleeperQuad(_Mesh, _Material, c010, c000, c001, c011, new Vector2(f1, u0), new Vector2(f0, u0), new Vector2(f0, u1), new Vector2(f1, u1)); // -right end
		// sides span (right, up)
		AddSleeperQuad(_Mesh, _Material, c110, c010, c011, c111, new Vector2(r1, u0), new Vector2(r0, u0), new Vector2(r0, u1), new Vector2(r1, u1)); // +forward side
		AddSleeperQuad(_Mesh, _Material, c000, c100, c101, c001, new Vector2(r0, u0), new Vector2(r1, u0), new Vector2(r1, u1), new Vector2(r0, u1)); // -forward side
		// top spans (right, forward)
		AddSleeperQuad(_Mesh, _Material, c001, c101, c111, c011, new Vector2(r0, f0), new Vector2(r1, f0), new Vector2(r1, f1), new Vector2(r0, f1)); // top
	}



	// Own vertices per face so every box edge stays hard.
	private static void AddSleeperQuad(PolygonMesh _Mesh, Material _Material, Vector3 _A, Vector3 _B, Vector3 _C, Vector3 _D, Vector2 _UvA, Vector2 _UvB, Vector2 _UvC, Vector2 _UvD)
	{
		var v = _Mesh.AddVertices(_A, _B, _C, _D);

		MeshUtility.AddTexturedQuad(_Mesh, _Material, v[0], v[1], v[2], v[3], _UvA, _UvB, _UvC, _UvD);
	}



	private void CreateSleeperChild(string _Name, PolygonMesh _PolygonMesh)
	{
		var child = new GameObject(GameObject, true, _Name);
		child.Tags.Add(RailMeshTag);
		child.Tags.Add(SleeperSurfaceTag);

		var meshComponent = child.AddComponent<MeshComponent>();
		meshComponent.Mesh = _PolygonMesh;
	}
}
