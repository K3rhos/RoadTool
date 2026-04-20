using System;
using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using Sandbox;

namespace RedSnail.RoadTool;

public enum BridgePillarShape
{
	Square,
	Cylinder
}

public partial class RoadComponent
{
	private bool m_DoesBridgeNeedRebuild = false;

	private const string BridgeTag = "road_bridge";

	[Property, FeatureEnabled("Bridge", Icon = "architecture", Tint = EditorTint.Blue)]
	private bool HasBridge { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = false;

	/// <summary>
	/// This will prevent the bridge from being rebuilt if any property is edited or if the road component get disable and re-enabled.
	/// Really useful if you plan to edit the mesh with the mapping tool so you don't accidently erase/rebuild the bridge.
	/// </summary>
	[Property(Title = "🔒 Locked"), Feature("Bridge")]
	private bool IsLocked { get; set; } = false;

	/// <summary>
	/// This is your bridge material you wanna use.
	/// I recommend using a tileable texture for better result.
	/// </summary>
	[Property(Title = "Material"), Feature("Bridge"), Group("Texturing"), Order(1)]
	private Material BridgeMaterial { get; set { field = value; m_DoesBridgeNeedRebuild = true; } }

	[Property(Title = "Texture Repeat"), Feature("Bridge"), Group("Texturing"), Order(1), Step(1)]
	private float BridgeTextureRepeat { get; set { field = value.Clamp(10.0f, 10000.0f); m_DoesBridgeNeedRebuild = true; } } = 500.0f;

	[Property(Title = "Border Width"), Feature("Bridge"), Group("Shape"), Order(0), Range(10.0f, 500.0f)]
	private float BridgeBorderWidth { get; set { field = value.Clamp(10.0f, 500.0f); m_DoesBridgeNeedRebuild = true; } } = 80.0f;

	[Property(Title = "Border Height"), Feature("Bridge"), Group("Shape"), Range(10.0f, 500.0f)]
	private float BridgeBorderHeight { get; set { field = value.Clamp(10.0f, 500.0f); m_DoesBridgeNeedRebuild = true; } } = 80.0f;

	[Property(Title = "Bottom Depth"), Feature("Bridge"), Group("Shape"), Range(10.0f, 500.0f)]
	private float BridgeBottomDepth { get; set { field = value.Clamp(10.0f, 500.0f); m_DoesBridgeNeedRebuild = true; } } = 80.0f;

	[Property(Title = "Close Caps"), Feature("Bridge"), Group("Shape")]
	private bool BridgeCloseCaps { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = true;

	[Property(Title = "Pillars"), Feature("Bridge"), ToggleGroup("Pillars"), Order(2)]
	private bool Pillars { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = true;

	[Property(Title = "Shape"), Feature("Bridge"), ToggleGroup("Pillars")]
	private BridgePillarShape BridgePillarType { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = BridgePillarShape.Square;

	[Property(Title = "Size"), Feature("Bridge"), ToggleGroup("Pillars"), Range(10.0f, 1000.0f)]
	private float BridgePillarSize { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = 200.0f;

	[Property(Title = "Height"), Feature("Bridge"), ToggleGroup("Pillars"), Range(10.0f, 5000.0f)]
	private float BridgePillarHeight { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = 600.0f;

	[Property(Title = "Spacing"), Feature("Bridge"), ToggleGroup("Pillars"), Range(100.0f, 10000.0f)]
	private float BridgePillarSpacing { get; set { field = value.Clamp(100.0f, 100000.0f); m_DoesBridgeNeedRebuild = true; } } = 1200.0f;

	[Property(Title = "Inset"), Feature("Bridge"), ToggleGroup("Pillars"), Range(0.0f, 200.0f)]
	private float BridgePillarInset { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = 20.0f;

	[Property(Title = "Segments"), Feature("Bridge"), ToggleGroup("Pillars"), Range(3, 24), ShowIf(nameof(BridgePillarType), BridgePillarShape.Cylinder)]
	private int BridgePillarRoundSegments { get; set { field = value.Clamp(3, 64); m_DoesBridgeNeedRebuild = true; } } = 12;

	/// <summary>
	/// Does the pillars follow world up vector or follow the road shape ?
	/// </summary>
	[Property(Title = "Keep Vertical (World Up)"), Feature("Bridge"), ToggleGroup("Pillars")]
	private bool BridgePillarsKeepVertical { get; set { field = value; m_DoesBridgeNeedRebuild = true; } } = true;



	private void CreateBridgeMesh(string _Name, Material _Material, Transform[] _Frames, int _SegmentCount, float _InnerOffset, float _HeightOffset, float _TextureRepeat)
	{
		var polygonMesh = new PolygonMesh();
		int frameCount = _SegmentCount + 1;
		const int verticesPerFrame = 10;
		var positions = new Vector3[frameCount * verticesPerFrame];

		float outerOffset = _InnerOffset + BridgeBorderWidth;
		float topHeight = _HeightOffset + BridgeBorderHeight;
		float bottomHeight = _HeightOffset - BridgeBottomDepth;
		float underRoadOffset = MathF.Max(0.0f, _InnerOffset);

		for (int i = 0; i < frameCount; i++)
		{
			var frame = _Frames[i];
			var position = frame.Position;
			var right = frame.Rotation.Right;
			var up = frame.Rotation.Up;

			positions[i * verticesPerFrame] = position - right * _InnerOffset;
			positions[i * verticesPerFrame + 1] = position - right * _InnerOffset + up * topHeight;
			positions[i * verticesPerFrame + 2] = position - right * outerOffset + up * topHeight;
			positions[i * verticesPerFrame + 3] = position - right * outerOffset + up * bottomHeight;
			positions[i * verticesPerFrame + 4] = position - right * underRoadOffset + up * bottomHeight;
			positions[i * verticesPerFrame + 5] = position + right * underRoadOffset + up * bottomHeight;
			positions[i * verticesPerFrame + 6] = position + right * outerOffset + up * bottomHeight;
			positions[i * verticesPerFrame + 7] = position + right * outerOffset + up * topHeight;
			positions[i * verticesPerFrame + 8] = position + right * _InnerOffset + up * topHeight;
			positions[i * verticesPerFrame + 9] = position + right * _InnerOffset;
		}

		var vertices = polygonMesh.AddVertices(positions);

		int profileEdgeCount = verticesPerFrame - 1;
		var profileDistances = new float[verticesPerFrame];
		for (int i = 0; i < profileEdgeCount; i++)
		{
			var current = positions[i];
			var next = positions[i + 1];
			profileDistances[i + 1] = profileDistances[i] + Vector3.DistanceBetween(current, next) / _TextureRepeat;
		}

		float splineDistance = 0f;

		for (int segmentIndex = 0; segmentIndex < _SegmentCount; segmentIndex++)
		{
			float segmentTravel = Vector3.DistanceBetween(_Frames[segmentIndex].Position, _Frames[segmentIndex + 1].Position);
			float v0 = splineDistance / _TextureRepeat;
			float v1 = (splineDistance + segmentTravel) / _TextureRepeat;

			for (int edgeIndex = 0; edgeIndex < profileEdgeCount; edgeIndex++)
			{
				int currentIndex0 = segmentIndex * verticesPerFrame + edgeIndex;
				int nextIndex0 = currentIndex0 + 1;
				int currentIndex1 = (segmentIndex + 1) * verticesPerFrame + edgeIndex;
				int nextIndex1 = currentIndex1 + 1;
				float u0 = profileDistances[edgeIndex];
				float u1 = profileDistances[edgeIndex + 1];

				MeshUtility.AddTexturedQuad(
					polygonMesh,
					_Material,
					vertices[currentIndex0],
					vertices[currentIndex1],
					vertices[nextIndex1],
					vertices[nextIndex0],
					new Vector2(u0, v0),
					new Vector2(u0, v1),
					new Vector2(u1, v1),
					new Vector2(u1, v0)
				);
			}

			splineDistance += segmentTravel;
		}

		if (BridgeCloseCaps)
		{
			AddBridgeCap(polygonMesh, vertices, positions, 0, verticesPerFrame, _Material, _TextureRepeat, _Frames[0].Rotation.Right, _Frames[0].Rotation.Up, _IsStartCap: true);
			AddBridgeCap(polygonMesh, vertices, positions, (frameCount - 1) * verticesPerFrame, verticesPerFrame, _Material, _TextureRepeat, _Frames[frameCount - 1].Rotation.Right, _Frames[frameCount - 1].Rotation.Up, _IsStartCap: false);
		}

		CreateBridgeChild(_Name, polygonMesh);

		if (Pillars)
			CreateBridgePillars($"{_Name}_Pillars", _Material, _Frames, bottomHeight, _TextureRepeat);
	}



	private void CreateBridgePillars(string _Name, Material _Material, Transform[] _Frames, float _BridgeBottomHeight, float _TextureRepeat)
	{
		if (_Frames.Length < 2)
			return;

		float pillarHeight = Math.Max(1.0f, BridgePillarHeight);
		float halfSize = Math.Max(1.0f, BridgePillarSize) * 0.5f;
		float spacing = Math.Max(1.0f, BridgePillarSpacing);

		var sampledFrames = new List<(Transform frame, float distance)>(_Frames.Length);
		float totalDistance = 0.0f;
		sampledFrames.Add((_Frames[0], 0.0f));

		for (int i = 1; i < _Frames.Length; i++)
		{
			totalDistance += Vector3.DistanceBetween(_Frames[i - 1].Position, _Frames[i].Position);
			sampledFrames.Add((_Frames[i], totalDistance));
		}

		if (totalDistance <= 0.0f)
			return;

		var polygonMesh = new PolygonMesh();
		var pillarDistances = new List<float>();

		if (totalDistance <= spacing)
		{
			pillarDistances.Add(totalDistance * 0.5f);
		}
		else
		{
			for (float distance = spacing * 0.5f; distance < totalDistance; distance += spacing)
				pillarDistances.Add(distance);
		}

		if (pillarDistances.Count == 0)
			return;

		foreach (float distance in pillarDistances)
		{
			Transform frame = InterpolateFrameAtDistance(sampledFrames, distance);
			Vector3 pillarUp = BridgePillarsKeepVertical ? Vector3.Up : frame.Rotation.Up;
			Vector3 pillarForward = BridgePillarsKeepVertical ? frame.Rotation.Forward.WithZ(0).Normal : frame.Rotation.Forward;

			if (pillarForward.IsNearZeroLength)
				pillarForward = Vector3.Forward;

			Vector3 pillarRight = Vector3.Cross(pillarForward, pillarUp).Normal;

			if (pillarRight.IsNearZeroLength)
				pillarRight = Vector3.Right;

			pillarForward = Vector3.Cross(pillarUp, pillarRight).Normal;

			float pillarTopOffset = _BridgeBottomHeight + (BridgePillarsKeepVertical ? BridgePillarInset : 0.0f);
			Vector3 topCenter = frame.Position + pillarUp * pillarTopOffset;

			if (BridgePillarType == BridgePillarShape.Cylinder)
				AddRoundBridgePillar(polygonMesh, _Material, topCenter, pillarRight, pillarForward, pillarUp, halfSize, pillarHeight, _TextureRepeat);
			else
				AddSquareBridgePillar(polygonMesh, _Material, topCenter, pillarRight, pillarForward, pillarUp, halfSize, pillarHeight, _TextureRepeat);
		}

		CreateBridgeChild(_Name, polygonMesh);
	}



	private static void AddSquareBridgePillar(PolygonMesh _PolygonMesh, Material _Material, Vector3 _TopCenter, Vector3 _Right, Vector3 _Forward, Vector3 _Up, float _HalfSize, float _Height, float _TextureRepeat)
	{
		var topCorners = new[]
		{
			_TopCenter - _Right * _HalfSize - _Forward * _HalfSize,
			_TopCenter + _Right * _HalfSize - _Forward * _HalfSize,
			_TopCenter + _Right * _HalfSize + _Forward * _HalfSize,
			_TopCenter - _Right * _HalfSize + _Forward * _HalfSize
		};

		var bottomCorners = new Vector3[4];

		for (int i = 0; i < 4; i++)
			bottomCorners[i] = topCorners[i] - _Up * _Height;

		var positions = new Vector3[8];
		Array.Copy(topCorners, 0, positions, 0, 4);
		Array.Copy(bottomCorners, 0, positions, 4, 4);
		var vertices = _PolygonMesh.AddVertices(positions);

		for (int side = 0; side < 4; side++)
		{
			int next = (side + 1) % 4;
			float edgeLength = Vector3.DistanceBetween(topCorners[side], topCorners[next]) / _TextureRepeat;
			float vLength = _Height / _TextureRepeat;

			MeshUtility.AddTexturedQuad(
				_PolygonMesh,
				_Material,
				vertices[side],
				vertices[side + 4],
				vertices[next + 4],
				vertices[next],
				new Vector2(0, 0),
				new Vector2(0, vLength),
				new Vector2(edgeLength, vLength),
				new Vector2(edgeLength, 0)
			);
		}
	}



	private void AddRoundBridgePillar(PolygonMesh _PolygonMesh, Material _Material, Vector3 _TopCenter, Vector3 _Right, Vector3 _Forward, Vector3 _Up, float _Radius, float _Height, float _TextureRepeat)
	{
		int segmentCount = Math.Max(3, BridgePillarRoundSegments);
		var positions = new Vector3[segmentCount * 2];

		for (int i = 0; i < segmentCount; i++)
		{
			float angle = (MathF.PI * 2.0f * i) / segmentCount;
			Vector3 radial = _Right * MathF.Cos(angle) + _Forward * MathF.Sin(angle);
			Vector3 top = _TopCenter + radial * _Radius;

			positions[i] = top;
			positions[i + segmentCount] = top - _Up * _Height;
		}

		var vertices = _PolygonMesh.AddVertices(positions);
		float circumference = MathF.PI * 2.0f * _Radius;

		for (int side = 0; side < segmentCount; side++)
		{
			int next = (side + 1) % segmentCount;
			float u0 = (circumference * side / segmentCount) / _TextureRepeat;
			float u1 = (circumference * (side + 1) / segmentCount) / _TextureRepeat;
			float v1 = _Height / _TextureRepeat;

			MeshUtility.AddTexturedQuad(
				_PolygonMesh,
				_Material,
				vertices[side],
				vertices[side + segmentCount],
				vertices[next + segmentCount],
				vertices[next],
				new Vector2(u0, 0),
				new Vector2(u0, v1),
				new Vector2(u1, v1),
				new Vector2(u1, 0)
			);
		}
	}



	private static void AddBridgeCap(PolygonMesh _PolygonMesh, VertexHandle[] _Vertices, Vector3[] _Positions, int _StartIndex, int _VerticesPerFrame, Material _Material, float _TextureRepeat, Vector3 _CapRight, Vector3 _CapUp, bool _IsStartCap)
	{
		var remainingIndices = new List<int>(_VerticesPerFrame);
		var capPoints = new Vector2[_VerticesPerFrame];
		Vector3 origin = _Positions[_StartIndex];

		for (int i = 0; i < _VerticesPerFrame; i++)
		{
			remainingIndices.Add(i);
			Vector3 position = _Positions[_StartIndex + i];
			Vector3 local = position - origin;
			capPoints[i] = new Vector2(Vector3.Dot(local, _CapRight), Vector3.Dot(local, _CapUp));
		}

		if (GetSignedArea(capPoints) < 0.0f)
			remainingIndices.Reverse();

		while (remainingIndices.Count > 2)
		{
			bool foundEar = false;

			for (int i = 0; i < remainingIndices.Count; i++)
			{
				int previous = remainingIndices[(i - 1 + remainingIndices.Count) % remainingIndices.Count];
				int current = remainingIndices[i];
				int next = remainingIndices[(i + 1) % remainingIndices.Count];

				if (!IsBridgeEar(previous, current, next, remainingIndices, capPoints))
					continue;

				var face = _IsStartCap
					? _PolygonMesh.AddFace(_Vertices[_StartIndex + previous], _Vertices[_StartIndex + current], _Vertices[_StartIndex + next])
					: _PolygonMesh.AddFace(_Vertices[_StartIndex + previous], _Vertices[_StartIndex + next], _Vertices[_StartIndex + current]);

				if (face.IsValid)
				{
					_PolygonMesh.SetFaceMaterial(face, _Material);

					var uvA = capPoints[previous] / _TextureRepeat;
					var uvB = capPoints[current] / _TextureRepeat;
					var uvC = capPoints[next] / _TextureRepeat;

					var uvs = _IsStartCap
						? new List<Vector2> { uvA, uvB, uvC }
						: new List<Vector2> { uvA, uvC, uvB };

					_PolygonMesh.SetFaceTextureCoords(face, uvs);
				}

				remainingIndices.RemoveAt(i);
				foundEar = true;
				break;
			}

			if (foundEar)
				continue;

			break;
		}
	}



	private static float GetSignedArea(Vector2[] _Points)
	{
		float area = 0.0f;

		for (int i = 0; i < _Points.Length; i++)
		{
			Vector2 a = _Points[i];
			Vector2 b = _Points[(i + 1) % _Points.Length];

			area += (a.x * b.y) - (b.x * a.y);
		}

		return area * 0.5f;
	}



	private static bool IsBridgeEar(int _Previous, int _Current, int _Next, List<int> _Indices, Vector2[] _Points)
	{
		Vector2 a = _Points[_Previous];
		Vector2 b = _Points[_Current];
		Vector2 c = _Points[_Next];

		if (CrossBridge2D(b - a, c - b) <= 0.0f)
			return false;

		foreach (var testIndex in _Indices)
		{
			if (testIndex == _Previous || testIndex == _Current || testIndex == _Next)
				continue;

			if (IsPointInBridgeTriangle(_Points[testIndex], a, b, c))
				return false;
		}

		return true;
	}



	private static float CrossBridge2D(Vector2 _A, Vector2 _B) => _A.x * _B.y - _A.y * _B.x;



	private static bool IsPointInBridgeTriangle(Vector2 _Point, Vector2 _A, Vector2 _B, Vector2 _C)
	{
		float ab = CrossBridge2D(_B - _A, _Point - _A);
		float bc = CrossBridge2D(_C - _B, _Point - _B);
		float ca = CrossBridge2D(_A - _C, _Point - _C);

		bool hasNegative = ab < 0.0f || bc < 0.0f || ca < 0.0f;
		bool hasPositive = ab > 0.0f || bc > 0.0f || ca > 0.0f;

		return !(hasNegative && hasPositive);
	}



	private void CreateBridgeChild(string _Name, PolygonMesh _PolygonMesh)
	{
		var child = new GameObject(GameObject, true, _Name);
		child.Tags.Add(BridgeTag);

		var meshComponent = child.AddComponent<MeshComponent>();
		meshComponent.Mesh = _PolygonMesh;
		meshComponent.SmoothingAngle = 40.0f;
	}



	private void UpdateBridge()
	{
		if (m_DoesBridgeNeedRebuild)
		{
			CreateBridge();

			m_DoesBridgeNeedRebuild = false;
		}
	}



	private void EnsureBridgeMeshExist()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		if (IsLocked)
			return;

		if (HasGeneratedMeshChildren(BridgeTag))
			return;

		CreateBridge();
	}



	private void CreateBridge()
	{
		if (IsLocked)
			return;

		if (!Scene.IsEditor)
			return;

		RemoveBridge();

		if (!HasBridge)
			return;

		GetSplineFrameData(out var sampledFrames, out var segmentsToKeep);
		var frames = segmentsToKeep.Select(index => sampledFrames[index]).ToArray();
		int totalSegments = frames.Length - 1;

		if (totalSegments <= 0)
			return;

		float roadEdgeOffset = RoadWidth * 0.5f;
		float sidewalkOffset = HasSidewalk ? SidewalkWidth : 0.0f;
		float sidewalkUp = HasSidewalk ? SidewalkHeight : 0.0f;
		float baseInnerOffset = roadEdgeOffset + sidewalkOffset;

		float textureRepeat = Math.Max(1.0f, BridgeTextureRepeat);
		var material = BridgeMaterial ?? Material.Load("materials/dev/reflectivity_50.vmat");

		CreateBridgeMesh("Bridge", material, frames, totalSegments, baseInnerOffset, sidewalkUp, textureRepeat);
	}



	private void RemoveBridge()
	{
		if (IsLocked)
			return;

		if (!Scene.IsEditor)
			return;

		RemoveGeneratedMeshChildren(BridgeTag);
	}
}
