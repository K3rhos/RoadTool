using System;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadIntersectionComponent
{
	[Property, FeatureEnabled("Terrain", Icon = "landscape", Tint = EditorTint.Green)]
	private bool HasTerrainModification { get; set; } = false;

	[Property, Feature("Terrain"), Hide]
	private Terrain TerrainTarget { get; set; }

	[Property, Feature("Terrain"), Range(0f, 2000f)]
	public float TerrainFalloffRadius { get; set; } = 500f;

	[Property, Feature("Terrain"), Range(-10f, 10f)]
	public float TerrainHeightOffset { get; set; } = 0f;

	[Property, Feature("Terrain"), Group("Texture"), Range(100f, 1000f)]
	public float TerrainEdgeRadius { get; set; } = 500f;

	[Property, Feature("Terrain"), Group("Texture")]
	public TerrainTextureLayer TerrainTargetLayer { get; set; } = TerrainTextureLayer.Overlay;

	[Property, Feature("Terrain"), Group("Texture"), Range(0f, 1f)]
	public float TerrainTextureNoise { get; set; } = 0.2f;

	[Property, Feature("Terrain"), Group("Texture")]
	public TerrainMaterial[] TerrainEdgeMaterials { get; set; } = Array.Empty<TerrainMaterial>();

	[Property, Feature("Terrain"), Group("Texture")]
	public Gradient TerrainEdgeBlendGradient = new Gradient(
		new Gradient.ColorFrame(0, Color.White),
		new Gradient.ColorFrame(1, Color.White.WithAlpha(0f))
	);

	public void AdaptTerrainToIntersection()
	{
		if (!TerrainTarget.IsValid())
			TerrainTarget = Scene.GetAllComponents<Terrain>().FirstOrDefault();

		if (!TerrainTarget.IsValid())
		{
			Log.Warning("RoadTool: No Terrain found in scene.");
			return;
		}

		var storage = TerrainTarget.Storage;
		if (storage == null || storage.HeightMap == null) return;

		// 1. Setup Parameters 
		int resolution = storage.Resolution;
		float terrainSize = storage.TerrainSize;
		float terrainMaxHeight = storage.TerrainHeight;
		float halfSize = terrainSize * 0.5f;

		// Detect coordinate system (Center vs Corner)
		var centerLocal = TerrainTarget.Transform.World.PointToLocal(WorldPosition);
		bool localIsCentered = centerLocal.x < 0f || centerLocal.x > terrainSize || centerLocal.y < 0f || centerLocal.y > terrainSize;

		// Calculate bounds including falloff
		float boundSize = (Shape == IntersectionShape.Rectangle ? Math.Max(Width, Length) * 0.5f : Radius) + TerrainFalloffRadius;
		BBox worldBounds = new BBox(WorldPosition - new Vector3(boundSize), WorldPosition + new Vector3(boundSize));

		var heightMap = storage.HeightMap;

		// Capture initial state for Undo 
		var previousHeightMap = heightMap.ToArray();
		bool hasModified = false;

		// Initialize buffers for height calculation 
		var updatedHeights = new float[heightMap.Length];
		var bestDistance = new float[heightMap.Length];

		for (int i = 0; i < heightMap.Length; i++)
		{
			// Decode: Map [0..1] ushort to [0 .. MaxHeight] to match RoadComponent
			updatedHeights[i] = (heightMap[i] / (float)ushort.MaxValue) * terrainMaxHeight;
			bestDistance[i] = float.MaxValue;
		}

		// 2. Grid Traversal 
		for (int ix = 0; ix < resolution; ix++)
		{
			for (int iy = 0; iy < resolution; iy++)
			{
				// 1. Adaptive coordinate detection (Center vs Corner) matching RoadComponent
				float nodeLocalX_corner = (ix / (float)(resolution - 1)) * terrainSize;
				float nodeLocalY_corner = (iy / (float)(resolution - 1)) * terrainSize;

				float nodeLocalX = nodeLocalX_corner;
				float nodeLocalY = nodeLocalY_corner;

				// Check if the intersection is in the centered range
				var checkPos = TerrainTarget.Transform.World.PointToLocal(WorldPosition);
				if (checkPos.x < 0f || checkPos.x > terrainSize || checkPos.y < 0f || checkPos.y > terrainSize)
				{
					nodeLocalX = nodeLocalX_corner - halfSize;
					nodeLocalY = nodeLocalY_corner - halfSize;
				}

				Vector3 pixelWorldPos = TerrainTarget.Transform.World.PointToWorld(new Vector3(nodeLocalX, nodeLocalY, 0));

				if (!worldBounds.Contains(pixelWorldPos)) continue;

				int index = iy * resolution + ix;

				// 2. Distance to intersection shape 
				Vector3 relativePos = WorldTransform.PointToLocal(pixelWorldPos);
				float distance = GetDistanceToIntersectionShape(relativePos.WithZ(0));

				if (distance > TerrainFalloffRadius) continue;

				// 3. Target height matching RoadComponent (0 to MaxHeight range) 
				Vector3 intersectionLocalPos = TerrainTarget.Transform.World.PointToLocal(WorldPosition);
				float targetHeightFloat = Math.Clamp(intersectionLocalPos.z + TerrainHeightOffset, 0f, terrainMaxHeight);

				float candidateHeight;
				if (distance <= 0) // Inside the intersection
				{
					candidateHeight = targetHeightFloat;
				}
				else
				{
					float t = Math.Clamp(distance / TerrainFalloffRadius, 0f, 1f);
					float smoothT = t * t * (3f - 2f * t);
					float currentPixelHeight = (heightMap[index] / (float)ushort.MaxValue) * terrainMaxHeight;
					candidateHeight = MathX.Lerp(targetHeightFloat, currentPixelHeight, smoothT);
				}

				if (distance < bestDistance[index])
				{
					bestDistance[index] = distance;
					updatedHeights[index] = candidateHeight;
					hasModified = true;
				}
			}
		}

		if (hasModified)
		{
			// 4. Final encoding to ushort (Mapping back to 0..1 without the 0.5 offset)
			for (int i = 0; i < heightMap.Length; i++)
			{
				heightMap[i] = (ushort)MathF.Round(Math.Clamp(updatedHeights[i], 0f, terrainMaxHeight) / terrainMaxHeight * ushort.MaxValue);
			}

			storage.HeightMap = heightMap;
			storage.StateHasChanged();
			TerrainTarget.Create();
		}
	}

	public void PaintTerrainToIntersection()
	{
		if (!TerrainTarget.IsValid() || TerrainEdgeMaterials == null || TerrainEdgeMaterials.Length == 0) return;

		var storage = TerrainTarget.Storage;
		if (storage == null || storage.ControlMap == null) return;

		int resolution = storage.Resolution;
		float terrainSize = storage.TerrainSize;
		float halfSize = terrainSize * 0.5f;

		// Identify all material indices in the terrain storage 
		bool materialsAdded = false;
		var materialIndices = new int[TerrainEdgeMaterials.Length];
		for (int m = 0; m < TerrainEdgeMaterials.Length; m++)
		{
			if (TerrainEdgeMaterials[m] == null) continue;

			int idx = storage.Materials.IndexOf(TerrainEdgeMaterials[m]);
			if (idx == -1)
			{
				storage.Materials.Add(TerrainEdgeMaterials[m]);
				idx = storage.Materials.Count - 1;
				materialsAdded = true;
			}

			if (idx > 31)
			{
				Log.Error($"RoadTool: Terrain has too many materials ({idx}). Material '{TerrainEdgeMaterials[m].ResourceName}' cannot be painted.");
				idx = 0;
			}

			materialIndices[m] = idx;
		}

		if (materialsAdded)
		{
			storage.StateHasChanged();
			TerrainTarget.Create();
		}

		float boundSize = (Shape == IntersectionShape.Rectangle ? Math.Max(Width, Length) * 0.5f : Radius) + TerrainEdgeRadius; // This line is unchanged 
		BBox worldBounds = new BBox(WorldPosition - new Vector3(boundSize), WorldPosition + new Vector3(boundSize)); // This line is unchanged 

		var controlMap = storage.ControlMap;
		bool hasModified = false;

		for (int ix = 0; ix < resolution; ix++)
		{
			for (int iy = 0; iy < resolution; iy++)
			{
				float nodeLocalX = (ix / (float)(resolution - 1)) * terrainSize;
				float nodeLocalY = (iy / (float)(resolution - 1)) * terrainSize;

				var checkPos = TerrainTarget.Transform.World.PointToLocal(WorldPosition);
				if (checkPos.x < 0f || checkPos.x > terrainSize || checkPos.y < 0f || checkPos.y > terrainSize)
				{
					nodeLocalX -= halfSize;
					nodeLocalY -= halfSize;
				}

				Vector3 pixelWorldPos = TerrainTarget.Transform.World.PointToWorld(new Vector3(nodeLocalX, nodeLocalY, 0));
				if (!worldBounds.Contains(pixelWorldPos)) continue;

				Vector3 relativePos = WorldTransform.PointToLocal(pixelWorldPos);
				float distance = GetDistanceToIntersectionShape(relativePos.WithZ(0));

				if (distance > TerrainEdgeRadius) continue;

				int index = iy * resolution + ix;
				float t = Math.Clamp(distance / TerrainEdgeRadius, 0f, 1f);
				float blendStrength = TerrainEdgeBlendGradient.Evaluate(t).a;

				if (blendStrength > 0.01f)
				{
					// Add deterministic noise to blend textures together (Dithering)
					float pixelNoise = ((float)((index * 1103515245 + 12345) & 0x7FFFFFFF) / 0x7FFFFFFF) * TerrainTextureNoise - (TerrainTextureNoise * 0.5f);
					float noisyT = Math.Clamp(t + pixelNoise, 0f, 1f);
					float noisyDistance = distance + (pixelNoise * TerrainEdgeRadius);

					int materialIndex;
					if (noisyDistance <= 0)
					{
						materialIndex = materialIndices[0];
					}
					else
					{
						int edgeMatCount = materialIndices.Length - 1;
						// Using noisyT for index selection 
						int edgeIdx = edgeMatCount > 0 ? Math.Clamp((int)(noisyT * edgeMatCount), 0, edgeMatCount - 1) + 1 : 0;
						materialIndex = materialIndices[edgeIdx];
					}

					uint packed = controlMap[index];
					var mat = new CompactTerrainMaterial(packed);

					if (TerrainTargetLayer == TerrainTextureLayer.Base)
					{
						mat.BaseTextureId = (byte)materialIndex;
						mat.BlendFactor = (byte)MathX.Lerp(mat.BlendFactor, 0, blendStrength);
					}
					else
					{
						// Otherwise, we place it in Overlay and increase the BlendFactor to display it
						mat.OverlayTextureId = (byte)materialIndex;
						mat.BlendFactor = (byte)MathX.Lerp(mat.BlendFactor, 255, blendStrength);
					}

					controlMap[index] = mat.Packed;
					hasModified = true;
				}
			}
		}

		if (hasModified)
		{
			storage.ControlMap = controlMap;
			storage.StateHasChanged();
			TerrainTarget.SyncGPUTexture();
		}
	}

	private float GetDistanceToIntersectionShape(Vector3 localPixelPos)
	{
		if (Shape == IntersectionShape.Rectangle)
		{
			float dx = MathF.Max(MathF.Abs(localPixelPos.x) - Length * 0.5f, 0);
			float dy = MathF.Max(MathF.Abs(localPixelPos.y) - Width * 0.5f, 0);
			return MathF.Sqrt(dx * dx + dy * dy);
		}
		else // Circle
		{
			return MathF.Max(localPixelPos.Length - Radius, 0);
		}
	}
}
