using System;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RailComponent
{
	[Property(Title = "Material"), Feature("Rail", Icon = "train", Tint = EditorTint.Blue)] private Material RailMaterial { get; set { field = value; IsDirty = true; } }

	/// <summary>Centre-to-centre distance between the two rails (the track gauge).</summary>
	[Property(Title = "Gauge"), Feature("Rail"), Range(20.0f, 1000.0f)] private float RailGauge { get; set { field = value; IsDirty = true; } } = 150.0f;

	/// <summary>Width of a single rail's base flange (the widest part of the 工 profile).</summary>
	[Property(Title = "Rail Width"), Feature("Rail"), Range(2.0f, 200.0f)] private float RailWidth { get; set { field = value; IsDirty = true; } } = 16.0f;

	/// <summary>Total height of a single rail's 工 profile.</summary>
	[Property(Title = "Rail Height"), Feature("Rail"), Range(2.0f, 200.0f)] private float RailHeight { get; set { field = value; IsDirty = true; } } = 22.0f;

	/// <summary>Head (top flange) width as a fraction of the base width — smaller than the base, as on a real rail.</summary>
	[Property(Title = "Head Scale"), Feature("Rail"), Range(0.1f, 1.0f)] private float RailHeadScale { get; set { field = value; IsDirty = true; } } = 0.6f;

	/// <summary>Web (middle vertical) width as a fraction of the base width.</summary>
	[Property(Title = "Web Scale"), Feature("Rail"), Range(0.1f, 0.9f)] private float RailWebScale { get; set { field = value; IsDirty = true; } } = 0.35f;

	[Property(Title = "Texture Repeat"), Feature("Rail")] private float RailTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 100.0f;



	private void BuildRails(Transform[] _Frames)
	{
		var material = RailMaterial ?? Material.Load("materials/dev/reflectivity_50.vmat");
		float textureRepeat = Math.Max(1.0f, RailTextureRepeat);
		float baseHeight = HasSleepers ? SleeperHeight : 0.0f; // rails ride on top of the sleepers
		float halfGauge = RailGauge * 0.5f;

		var profile = BuildRailProfile();

		var polygonMesh = new PolygonMesh();

		CreateRailExtrusion(polygonMesh, material, _Frames, profile, halfGauge, baseHeight, textureRepeat);
		CreateRailExtrusion(polygonMesh, material, _Frames, profile, -halfGauge, baseHeight, textureRepeat);

		CreateRailChild("Rails", polygonMesh);
	}



	/// <summary>
	/// The 工 (I-beam) cross-section as a CLOSED outline in (right, up) offsets from the rail centre, traced
	/// counter-clockwise: wide base flange, narrow web, smaller head flange.
	/// </summary>
	private Vector2[] BuildRailProfile()
	{
		float halfBase = RailWidth * 0.5f;
		float halfHead = RailWidth * RailHeadScale * 0.5f;
		float halfWeb = RailWidth * RailWebScale * 0.5f;
		float height = RailHeight;
		float flangeThickness = height * 0.25f;
		float headBottom = height - flangeThickness;

		return new[]
		{
			new Vector2(-halfBase, 0.0f),          // base bottom-left
			new Vector2(halfBase, 0.0f),           // base bottom-right
			new Vector2(halfBase, flangeThickness),// base top-right
			new Vector2(halfWeb, flangeThickness), // web bottom-right
			new Vector2(halfWeb, headBottom),      // web top-right
			new Vector2(halfHead, headBottom),     // head bottom-right
			new Vector2(halfHead, height),         // head top-right
			new Vector2(-halfHead, height),        // head top-left
			new Vector2(-halfHead, headBottom),    // head bottom-left
			new Vector2(-halfWeb, headBottom),     // web top-left
			new Vector2(-halfWeb, flangeThickness),// web bottom-left
			new Vector2(-halfBase, flangeThickness)// base top-left
		};
	}



	private void CreateRailExtrusion(PolygonMesh _Mesh, Material _Material, Transform[] _Frames, Vector2[] _Profile, float _CentreOffset, float _BaseHeight, float _TextureRepeat)
	{
		int frameCount = _Frames.Length;
		int pointCount = _Profile.Length;

		var positions = new Vector3[frameCount * pointCount];

		for (int i = 0; i < frameCount; i++)
		{
			var frame = _Frames[i];
			var position = frame.Position;
			var right = frame.Rotation.Right;
			var up = frame.Rotation.Up;

			for (int j = 0; j < pointCount; j++)
				positions[i * pointCount + j] = position + right * (_CentreOffset + _Profile[j].x) + up * (_BaseHeight + _Profile[j].y);
		}

		var vertices = _Mesh.AddVertices(positions);

		// U spans the profile perimeter (closed, so the last edge wraps to point 0).
		var profileU = new float[pointCount + 1];
		for (int j = 0; j < pointCount; j++)
			profileU[j + 1] = profileU[j] + (_Profile[(j + 1) % pointCount] - _Profile[j]).Length / _TextureRepeat;

		float splineDistance = 0.0f;

		for (int i = 0; i < frameCount - 1; i++)
		{
			float travel = Vector3.DistanceBetween(_Frames[i].Position, _Frames[i + 1].Position);
			float v0 = splineDistance / _TextureRepeat;
			float v1 = (splineDistance + travel) / _TextureRepeat;

			for (int j = 0; j < pointCount; j++)
			{
				int jn = (j + 1) % pointCount;

				var current0 = vertices[i * pointCount + j];
				var current1 = vertices[(i + 1) * pointCount + j];
				var next1 = vertices[(i + 1) * pointCount + jn];
				var next0 = vertices[i * pointCount + jn];

				float u0 = profileU[j];
				float u1 = profileU[j + 1];

				MeshUtility.AddTexturedQuad(_Mesh, _Material, current0, current1, next1, next0,
					new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
			}

			splineDistance += travel;
		}
	}



	private void CreateRailChild(string _Name, PolygonMesh _PolygonMesh)
	{
		var child = new GameObject(GameObject, true, _Name);
		child.Tags.Add(RailMeshTag);
		child.Tags.Add(RailSurfaceTag);

		var meshComponent = child.AddComponent<MeshComponent>();
		meshComponent.Mesh = _PolygonMesh;
		meshComponent.SmoothingAngle = 30.0f; // smooth along the curve, keep the sharp 90° profile corners
	}
}
