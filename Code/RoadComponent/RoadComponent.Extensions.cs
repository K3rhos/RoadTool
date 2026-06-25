using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public class RoadExtensionDefinition
{
	[Property] public Material Material { get; set; }
	[Property, Range(10.0f, 2000.0f)] public float Width { get; set; } = 300.0f;
	[Property, Range(0.0f, 2000.0f)] public float Height { get; set; } = 0.0f;
	[Property, Range(-100.0f, 100.0f)] public float HeightOffset { get; set; } = 0.0f;
	[Property] public float TextureRepeat { get; set; } = 500.0f;
	[Property] public bool LeftSide { get; set; } = true;
	[Property] public bool RightSide { get; set; } = true;
	[Property] public bool WallMode { get; set; } = false;
	[Property] public bool StraightEdge { get; set; } = false;
	[Property] public bool HasCollision { get; set; } = true;
}

public partial class RoadComponent
{
	private const string ExtensionTag = "road_extension";

	[Property, FeatureEnabled("Extensions", Icon = "dashboard", Tint = EditorTint.Yellow)] private bool HasExtensions { get; set { field = value; IsDirty = true; } } = false;
	
	/// <summary>
	/// This will prevent the extensions from being rebuilt if any property is edited or if the road component get disable and re-enabled.
	/// Really useful if you plan to edit the mesh with the mapping tool so you don't accidently erase/rebuild the extensions.
	/// </summary>
	[Property(Title = "🔒 Locked"), Feature("Extensions")]
	private bool AreExtensionsLocked { get; set; } = false;
	[Property, Feature("Extensions")] private List<RoadExtensionDefinition> ExtensionDefinitions { get; set { field = value; IsDirty = true; } } = new();



	[Button("Bake Extensions"), Feature("Extensions")]
	private void BakeExtensions()
	{
		if (AreExtensionsLocked)
			return;
		
		if (!Scene.IsEditor)
			return;

		if (!HasExtensions || ExtensionDefinitions == null || ExtensionDefinitions.Count == 0)
			return;

		ClearBakedExtensions();

		GetSplineFrameData(out var sampledFrames, out var segmentsToKeep);
		var frames = segmentsToKeep.Select(index => sampledFrames[index]).ToArray();
		int totalSegments = frames.Length - 1;

		if (totalSegments <= 0)
			return;

		float roadEdgeOffset = RoadWidth * 0.5f;
		float sidewalkOffset = HasSidewalk ? SidewalkWidth : 0.0f;
		float sidewalkUp = HasSidewalk ? SidewalkHeight : 0.0f;
		float baseInnerOffset = roadEdgeOffset + sidewalkOffset;

		// Segments already claimed by range-limited extensions, per side — full-range extensions fill only the gaps.
		var occupiedLeft = new bool[totalSegments];
		var occupiedRight = new bool[totalSegments];

		for (int d = 0; d < ExtensionDefinitions.Count; d++)
		{
			var def = ExtensionDefinitions[d];

			if (def == null)
				continue;

			// Build each side over its own gaps so two full-range defs don't overlap.
			if (def.LeftSide)
			{
				var runs = GetUnoccupiedRuns(occupiedLeft, totalSegments);
				for (int r = 0; r < runs.Count; r++)
					BakeExtensionRange($"Extension_{d}_r{r}_L", def, frames, runs[r].From, runs[r].To, baseInnerOffset, sidewalkUp, _ForceLeftOnly: true);
			}

			if (def.RightSide)
			{
				var runs = GetUnoccupiedRuns(occupiedRight, totalSegments);
				for (int r = 0; r < runs.Count; r++)
					BakeExtensionRange($"Extension_{d}_r{r}_R", def, frames, runs[r].From, runs[r].To, baseInnerOffset, sidewalkUp, _ForceRightOnly: true);
			}
		}
	}



	private static List<(int From, int To)> GetUnoccupiedRuns(bool[] _Occupied, int _TotalSegments)
	{
		var runs = new List<(int From, int To)>();
		int runStart = -1;

		for (int i = 0; i < _TotalSegments; i++)
		{
			if (!_Occupied[i])
			{
				if (runStart < 0)
					runStart = i;
			}
			else if (runStart >= 0)
			{
				runs.Add((runStart, i - 1));
				runStart = -1;
			}
		}

		if (runStart >= 0)
			runs.Add((runStart, _TotalSegments - 1));

		return runs;
	}



	private void BakeExtensionRange(string _NamePrefix, RoadExtensionDefinition _Def, Transform[] _AllFrames, int _SegFrom, int _SegTo, float _BaseInnerOffset, float _SidewalkUp, bool _ForceLeftOnly = false, bool _ForceRightOnly = false)
	{
		int rangeSegmentCount = _SegTo - _SegFrom + 1;
		int rangeFrameCount = rangeSegmentCount + 1;

		var rangeFrames = new Transform[rangeFrameCount];
		Array.Copy(_AllFrames, _SegFrom, rangeFrames, 0, rangeFrameCount);

		float innerOffset = _BaseInnerOffset;
		float textureRepeat = Math.Max(1.0f, _Def.TextureRepeat);
		float heightOffset = _SidewalkUp + _Def.HeightOffset;
		var material = _Def.Material ?? Material.Load("materials/dev/reflectivity_50.vmat");

		bool buildLeft = _Def.LeftSide && !_ForceRightOnly;
		bool buildRight = _Def.RightSide && !_ForceLeftOnly;

		if (_Def.WallMode)
		{
			float wallHeight = Math.Max(1.0f, _Def.Height > 0.0f ? _Def.Height : _Def.Width);

			if (buildLeft)
				CreateWallBakedMesh($"{_NamePrefix}_Wall_Left", _Def, material, rangeFrames, rangeSegmentCount, innerOffset, wallHeight, heightOffset, textureRepeat, _IsLeftSide: true);

			if (buildRight)
				CreateWallBakedMesh($"{_NamePrefix}_Wall_Right", _Def, material, rangeFrames, rangeSegmentCount, innerOffset, wallHeight, heightOffset, textureRepeat, _IsLeftSide: false);
		}
		else
		{
			float outerOffset = innerOffset + _Def.Width;

			if (buildLeft)
				CreateFlatBakedMesh($"{_NamePrefix}_Flat_Left", _Def, material, rangeFrames, rangeSegmentCount, innerOffset, outerOffset, heightOffset, textureRepeat, _IsLeftSide: true);

			if (buildRight)
				CreateFlatBakedMesh($"{_NamePrefix}_Flat_Right", _Def, material, rangeFrames, rangeSegmentCount, innerOffset, outerOffset, heightOffset, textureRepeat, _IsLeftSide: false);
		}
	}



	private void CreateFlatBakedMesh(string _Name, RoadExtensionDefinition _Def, Material _Material, Transform[] _Frames, int _SegmentCount, float _InnerOffset, float _OuterOffset, float _HeightOffset, float _TextureRepeat, bool _IsLeftSide)
	{
		var polygonMesh = new PolygonMesh();
		float sign = _IsLeftSide ? -1.0f : 1.0f;
		int frameCount = _SegmentCount + 1;

		var positions = new Vector3[frameCount * 2];

		for (int i = 0; i < frameCount; i++)
		{
			var frame = _Frames[i];
			var position = frame.Position;
			var right = frame.Rotation.Right;
			var up = frame.Rotation.Up;

			positions[i * 2] = position + right * (sign * _InnerOffset) + up * _HeightOffset;
			positions[i * 2 + 1] = position + right * (sign * _OuterOffset) + up * _HeightOffset;
		}

		if (_Def.StraightEdge && frameCount >= 2)
			ApplyStraightEdgeFlat(positions, frameCount);

		var vertices = polygonMesh.AddVertices(positions);

		float uvDistance = 0.0f;

		for (int i = 0; i < _SegmentCount; i++)
		{
			var inner0 = vertices[i * 2];
			var outer0 = vertices[i * 2 + 1];
			var inner1 = vertices[(i + 1) * 2];
			var outer1 = vertices[(i + 1) * 2 + 1];

			float segmentLength = (Vector3.DistanceBetween(positions[i * 2], positions[(i + 1) * 2]) + Vector3.DistanceBetween(positions[i * 2 + 1], positions[(i + 1) * 2 + 1])) * 0.5f;
			float v0 = uvDistance;
			uvDistance += segmentLength / _TextureRepeat;
			float v1 = uvDistance;

			if (_IsLeftSide)
				MeshUtility.AddTexturedQuad(polygonMesh, _Material, inner0, inner1, outer1, outer0,
					new Vector2(0, v0), new Vector2(0, v1), new Vector2(1, v1), new Vector2(1, v0));
			else
				MeshUtility.AddTexturedQuad(polygonMesh, _Material, inner0, outer0, outer1, inner1,
					new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1));
		}

		CreateExtensionChild(_Name, _Def, polygonMesh);
	}



	private void CreateWallBakedMesh(string _Name, RoadExtensionDefinition _Def, Material _Material, Transform[] _Frames, int _SegmentCount, float _EdgeOffset, float _WallHeight, float _HeightOffset, float _TextureRepeat, bool _IsLeftSide)
	{
		var polygonMesh = new PolygonMesh();
		float sign = _IsLeftSide ? -1.0f : 1.0f;
		float uvHeight = _WallHeight / _TextureRepeat;
		int frameCount = _SegmentCount + 1;

		var positions = new Vector3[frameCount * 2];

		for (int i = 0; i < frameCount; i++)
		{
			var frame = _Frames[i];
			var position = frame.Position;
			var right = frame.Rotation.Right;
			var up = frame.Rotation.Up;

			var bottom = position + right * (sign * _EdgeOffset) + up * _HeightOffset;

			positions[i * 2] = bottom;
			positions[i * 2 + 1] = bottom + up * _WallHeight;
		}

		if (_Def.StraightEdge && frameCount >= 2)
			ApplyStraightEdgeWall(positions, frameCount);

		var vertices = polygonMesh.AddVertices(positions);

		float uvDistance = 0.0f;

		for (int i = 0; i < _SegmentCount; i++)
		{
			var bottom0 = vertices[i * 2];
			var top0 = vertices[i * 2 + 1];
			var bottom1 = vertices[(i + 1) * 2];
			var top1 = vertices[(i + 1) * 2 + 1];

			float segmentLength = (Vector3.DistanceBetween(positions[i * 2], positions[(i + 1) * 2]) + Vector3.DistanceBetween(positions[i * 2 + 1], positions[(i + 1) * 2 + 1])) * 0.5f;
			float v0 = uvDistance;
			uvDistance += segmentLength / _TextureRepeat;
			float v1 = uvDistance;

			if (_IsLeftSide)
				MeshUtility.AddTexturedQuad(polygonMesh, _Material, bottom0, bottom1, top1, top0,
					new Vector2(0, v0), new Vector2(0, v1), new Vector2(uvHeight, v1), new Vector2(uvHeight, v0));
			else
				MeshUtility.AddTexturedQuad(polygonMesh, _Material, bottom0, top0, top1, bottom1,
					new Vector2(0, v0), new Vector2(uvHeight, v0), new Vector2(uvHeight, v1), new Vector2(0, v1));
		}

		CreateExtensionChild(_Name, _Def, polygonMesh);
	}



	/// <summary>
	/// Snaps the flat strip's outer edge to a single axis-aligned line at the farthest perpendicular distance, so the
	/// outer border reads as straight even though the road curves. Each outer point keeps its inner point's primary axis.
	/// </summary>
	private static void ApplyStraightEdgeFlat(Vector3[] _Positions, int _FrameCount)
	{
		var firstOuter = _Positions[1];
		var lastOuter = _Positions[(_FrameCount - 1) * 2 + 1];

		Vector3 lineDir = lastOuter - firstOuter;
		bool roadAlongX = MathF.Abs(lineDir.x) >= MathF.Abs(lineDir.y);

		float extremePerp = roadAlongX ? _Positions[1].y : _Positions[1].x;

		for (int i = 0; i < _FrameCount; i++)
		{
			float perp = roadAlongX ? _Positions[i * 2 + 1].y : _Positions[i * 2 + 1].x;
			float innerPerp = roadAlongX ? _Positions[i * 2].y : _Positions[i * 2].x;

			if (MathF.Abs(perp - innerPerp) > MathF.Abs(extremePerp - innerPerp))
				extremePerp = perp;
		}

		for (int i = 0; i < _FrameCount; i++)
		{
			var inner = _Positions[i * 2];
			_Positions[i * 2 + 1] = roadAlongX
				? new Vector3(inner.x, extremePerp, inner.z)
				: new Vector3(extremePerp, inner.y, inner.z);
		}
	}



	/// <summary>Levels the wall's top edge to a single height (the highest top), keeping each top above its own bottom.</summary>
	private static void ApplyStraightEdgeWall(Vector3[] _Positions, int _FrameCount)
	{
		float maxTopZ = float.MinValue;

		for (int i = 0; i < _FrameCount; i++)
			maxTopZ = MathF.Max(maxTopZ, _Positions[i * 2 + 1].z);

		for (int i = 0; i < _FrameCount; i++)
		{
			var bottom = _Positions[i * 2];
			_Positions[i * 2 + 1] = new Vector3(bottom.x, bottom.y, maxTopZ);
		}
	}



	private void CreateExtensionChild(string _Name, RoadExtensionDefinition _Def, PolygonMesh _PolygonMesh)
	{
		if (AreExtensionsLocked)
			return;
		
		var child = new GameObject(GameObject, true, _Name);
		child.Tags.Add(ExtensionTag);

		var meshComponent = child.AddComponent<MeshComponent>();
		meshComponent.Mesh = _PolygonMesh;
		meshComponent.SmoothingAngle = 40.0f;

		if (!_Def.HasCollision)
			meshComponent.Collision = MeshComponent.CollisionType.None;
	}



	[Button("Clear Extensions"), Feature("Extensions")]
	private void ClearBakedExtensions()
	{
		if (AreExtensionsLocked)
			return;
		
		if (!Scene.IsEditor)
			return;

		RemoveGeneratedMeshChildren(ExtensionTag);
	}
}
