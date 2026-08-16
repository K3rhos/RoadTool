using System;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RailComponent
{
	[Property, FeatureEnabled("Fishplates", Icon = "link", Tint = EditorTint.Green)] private bool HasFishplates { get; set { field = value; IsDirty = true; } } = true;
	[Property(Title = "Material"), Feature("Fishplates")] private Material FishplateMaterial { get; set { field = value; IsDirty = true; } }

	/// <summary>Distance along the track between joints. Real rail comes in long sections, so this is much larger than the sleeper spacing.</summary>
	[Property(Title = "Spacing"), Feature("Fishplates"), Range(100.0f, 5000.0f)] private float FishplateSpacing { get; set { field = value; IsDirty = true; } } = 1200.0f;

	/// <summary>Plate length along the track (it straddles the joint between two rail sections).</summary>
	[Property(Title = "Length"), Feature("Fishplates"), Range(10.0f, 500.0f)] private float FishplateLength { get; set { field = value; IsDirty = true; } } = 80.0f;

	/// <summary>Plate height — sized to sit against the rail web, between the foot and the head.</summary>
	[Property(Title = "Height"), Feature("Fishplates"), Range(2.0f, 100.0f)] private float FishplateHeight { get; set { field = value; IsDirty = true; } } = 14.0f;

	/// <summary>How far the plate stands out from the rail web.</summary>
	[Property(Title = "Thickness"), Feature("Fishplates"), Range(1.0f, 50.0f)] private float FishplateThickness { get; set { field = value; IsDirty = true; } } = 6.0f;

	/// <summary>Radius of the hexagonal bolt heads.</summary>
	[Property(Title = "Bolt Radius"), Feature("Fishplates"), Range(0.5f, 20.0f)] private float FishplateBoltRadius { get; set { field = value; IsDirty = true; } } = 3.5f;

	/// <summary>How far the bolt heads stand out from the plate.</summary>
	[Property(Title = "Bolt Depth"), Feature("Fishplates"), Range(0.5f, 20.0f)] private float FishplateBoltDepth { get; set { field = value; IsDirty = true; } } = 3.0f;

	[Property(Title = "Texture Repeat"), Feature("Fishplates")] private float FishplateTextureRepeat { get; set { field = value.Clamp(1.0f, 100000.0f); IsDirty = true; } } = 100.0f;



	private void BuildFishplates(Transform[] _Frames)
	{
		float length = Spline.Length;

		if (length <= 0.0f)
			return;

		float spacing = Math.Max(1.0f, FishplateSpacing);

		int count = (int)MathF.Floor(length / spacing);

		if (count < 1)
			return; // track is shorter than one rail section — no joints

		var material = FishplateMaterial ?? Material.Load("materials/dev/reflectivity_50.vmat");
		var polygonMesh = new PolygonMesh();

		float textureRepeat = Math.Max(1.0f, FishplateTextureRepeat);
		float baseHeight = HasSleepers ? SleeperHeight : 0.0f;
		float halfGauge = RailGauge * 0.5f;

		int placed = 0;

		for (int s = 1; s <= count; s++)
		{
			float distance = s * spacing;

			if (distance >= length)
				break; // don't drop a joint right on the end of the track

			var frame = SampleFrameAtDistance(_Frames, distance, length);

			// One plate on the OUTER side of each rail (the outward sign flips the side for the left rail).
			AddFishplate(polygonMesh, material, frame, halfGauge, 1.0f, baseHeight, textureRepeat);
			AddFishplate(polygonMesh, material, frame, halfGauge, -1.0f, baseHeight, textureRepeat);

			placed++;
		}

		if (placed == 0)
			return;

		CreateFishplateChild("Fishplates", polygonMesh);
	}



	/// <summary>Adds one bolted joint plate against the outer face of a single rail's web.</summary>
	private void AddFishplate(PolygonMesh _Mesh, Material _Material, Transform _Frame, float _HalfGauge, float _OutwardSign, float _BaseHeight, float _TextureRepeat)
	{
		Vector3 origin = _Frame.Position;
		Vector3 right = _Frame.Rotation.Right;
		Vector3 forward = _Frame.Rotation.Forward;
		Vector3 up = _Frame.Rotation.Up;

		Vector3 outward = right * _OutwardSign; // points away from the track centre, toward the outer side of this rail

		float halfWeb = RailWidth * RailWebScale * 0.5f;
		float webMid = _BaseHeight + RailHeight * 0.5f;

		float embed = MathF.Min(1.0f, halfWeb * 0.5f); // bite into the web so the hidden inner face never z-fights it
		float innerDist = halfWeb - embed;
		float outerDist = halfWeb + FishplateThickness;
		float centreDist = (innerDist + outerDist) * 0.5f;
		float halfOut = (outerDist - innerDist) * 0.5f;

		float halfLength = FishplateLength * 0.5f;
		float halfHeight = FishplateHeight * 0.5f;

		Vector3 railCentre = origin + right * (_HalfGauge * _OutwardSign);
		Vector3 plateCentre = railCentre + outward * centreDist + up * webMid;

		AddBox(_Mesh, _Material, plateCentre, outward, forward, up, halfOut, halfLength, halfHeight, _TextureRepeat);

		// Two hex bolts standing out of the plate's outer face, spread along the joint.
		Vector3 outerFace = plateCentre + outward * halfOut;
		float boltOffset = FishplateLength * 0.25f;

		AddHexBolt(_Mesh, _Material, outerFace + forward * boltOffset, outward, forward, FishplateBoltRadius, FishplateBoltDepth, _TextureRepeat);
		AddHexBolt(_Mesh, _Material, outerFace - forward * boltOffset, outward, forward, FishplateBoltRadius, FishplateBoltDepth, _TextureRepeat);
	}



	/// <summary>A closed box built from three frame axes, textured with a uniform box (cube) projection.</summary>
	private static void AddBox(PolygonMesh _Mesh, Material _Material, Vector3 _Centre, Vector3 _AxisR, Vector3 _AxisF, Vector3 _AxisU, float _HalfR, float _HalfF, float _HalfU, float _TextureRepeat)
	{
		// The faces below wind outward only for a right-handed basis. The outward axis flips for the left rail, which
		// would make it left-handed and flip every face to a backface — mirror one axis to keep it right-handed (the
		// box is symmetric, so this changes nothing but the winding).
		if (Vector3.Dot(Vector3.Cross(_AxisR, _AxisF), _AxisU) < 0.0f)
			_AxisF = -_AxisF;

		Vector3 Corner(float r, float f, float u) => _Centre + _AxisR * r + _AxisF * f + _AxisU * u;

		var c000 = Corner(-_HalfR, -_HalfF, -_HalfU);
		var c100 = Corner(_HalfR, -_HalfF, -_HalfU);
		var c110 = Corner(_HalfR, _HalfF, -_HalfU);
		var c010 = Corner(-_HalfR, _HalfF, -_HalfU);
		var c001 = Corner(-_HalfR, -_HalfF, _HalfU);
		var c101 = Corner(_HalfR, -_HalfF, _HalfU);
		var c111 = Corner(_HalfR, _HalfF, _HalfU);
		var c011 = Corner(-_HalfR, _HalfF, _HalfU);

		float r0 = -_HalfR / _TextureRepeat, r1 = _HalfR / _TextureRepeat;
		float f0 = -_HalfF / _TextureRepeat, f1 = _HalfF / _TextureRepeat;
		float u0 = -_HalfU / _TextureRepeat, u1 = _HalfU / _TextureRepeat;

		AddQuad(_Mesh, _Material, c100, c110, c111, c101, new Vector2(f0, u0), new Vector2(f1, u0), new Vector2(f1, u1), new Vector2(f0, u1)); // +R
		AddQuad(_Mesh, _Material, c010, c000, c001, c011, new Vector2(f1, u0), new Vector2(f0, u0), new Vector2(f0, u1), new Vector2(f1, u1)); // -R
		AddQuad(_Mesh, _Material, c110, c010, c011, c111, new Vector2(r1, u0), new Vector2(r0, u0), new Vector2(r0, u1), new Vector2(r1, u1)); // +F
		AddQuad(_Mesh, _Material, c000, c100, c101, c001, new Vector2(r0, u0), new Vector2(r1, u0), new Vector2(r1, u1), new Vector2(r0, u1)); // -F
		AddQuad(_Mesh, _Material, c001, c101, c111, c011, new Vector2(r0, f0), new Vector2(r1, f0), new Vector2(r1, f1), new Vector2(r0, f1)); // +U
		AddQuad(_Mesh, _Material, c000, c010, c110, c100, new Vector2(r0, f0), new Vector2(r0, f1), new Vector2(r1, f1), new Vector2(r1, f0)); // -U
	}



	/// <summary>A hexagonal bolt head — a 6-sided prism standing out of the plate along <paramref name="_AxisOut"/>, capped on the outer end.</summary>
	private static void AddHexBolt(PolygonMesh _Mesh, Material _Material, Vector3 _Base, Vector3 _AxisOut, Vector3 _Forward, float _Radius, float _Depth, float _TextureRepeat)
	{
		const int sides = 6;

		// t2 completes a right-handed (out, t1, t2) basis so the winding below stays outward-facing on either rail.
		Vector3 t1 = _Forward;
		Vector3 t2 = Vector3.Cross(_AxisOut, t1);

		var baseRing = new Vector3[sides];
		var topRing = new Vector3[sides];

		for (int k = 0; k < sides; k++)
		{
			float angle = MathF.PI * 2.0f * k / sides;
			Vector3 radial = t1 * MathF.Cos(angle) + t2 * MathF.Sin(angle);

			baseRing[k] = _Base + radial * _Radius;
			topRing[k] = baseRing[k] + _AxisOut * _Depth;
		}

		float edge = _Radius; // a regular hexagon's side length equals its radius
		float v1 = _Depth / _TextureRepeat;

		for (int k = 0; k < sides; k++)
		{
			int next = (k + 1) % sides;
			float u0 = edge * k / _TextureRepeat;
			float u1 = edge * (k + 1) / _TextureRepeat;

			AddQuad(_Mesh, _Material, baseRing[k], baseRing[next], topRing[next], topRing[k],
				new Vector2(u0, 0.0f), new Vector2(u1, 0.0f), new Vector2(u1, v1), new Vector2(u0, v1));
		}

		Vector2 CapUv(Vector3 _P) => new Vector2(Vector3.Dot(_P - _Base, t1), Vector3.Dot(_P - _Base, t2)) / _TextureRepeat;

		for (int k = 1; k < sides - 1; k++)
		{
			AddTri(_Mesh, _Material, topRing[0], topRing[k], topRing[k + 1],
				CapUv(topRing[0]), CapUv(topRing[k]), CapUv(topRing[k + 1]));
		}
	}



	// Own vertices per face so every plate edge and bolt facet stays hard.
	private static void AddQuad(PolygonMesh _Mesh, Material _Material, Vector3 _A, Vector3 _B, Vector3 _C, Vector3 _D, Vector2 _UvA, Vector2 _UvB, Vector2 _UvC, Vector2 _UvD)
	{
		var v = _Mesh.AddVertices(_A, _B, _C, _D);

		MeshUtility.AddTexturedQuad(_Mesh, _Material, v[0], v[1], v[2], v[3], _UvA, _UvB, _UvC, _UvD);
	}



	private static void AddTri(PolygonMesh _Mesh, Material _Material, Vector3 _A, Vector3 _B, Vector3 _C, Vector2 _UvA, Vector2 _UvB, Vector2 _UvC)
	{
		var v = _Mesh.AddVertices(_A, _B, _C);

		MeshUtility.AddTexturedTriangle(_Mesh, _Material, v[0], v[1], v[2], _UvA, _UvB, _UvC);
	}



	private void CreateFishplateChild(string _Name, PolygonMesh _PolygonMesh)
	{
		var child = new GameObject(GameObject, true, _Name);
		child.Tags.Add(RailMeshTag);
		child.Tags.Add(FishplateSurfaceTag);

		var meshComponent = child.AddComponent<MeshComponent>();
		meshComponent.Mesh = _PolygonMesh;
	}
}
