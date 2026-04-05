using System;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public enum TerrainTextureLayer
{
	Base,
	Overlay
}

public partial class RoadComponent
{
	[Property, FeatureEnabled( "Terrain", Icon = "landscape", Tint = EditorTint.Green )]
	private bool HasTerrainModification { get; set; } = false;

	[Property, Feature( "Terrain" ), Hide]
	private Terrain TerrainTarget { get; set; }

	[Property, Feature( "Terrain" ), Range( 0f, 2000f )]
	public float TerrainFalloffRadius { get; set; } = 500f;

	[Property, Feature( "Terrain" ), Range( 10f, 500f )]
	public float TerrainStepPrecision { get; set; } = 50f;

	[Property, Feature( "Terrain" ), Range( -10f, 10f )]
	public float TerrainHeightOffset { get; set; } = 0f;

	[Property, Feature( "Terrain" ), Group( "Texture" ), Range( 100f, 1000f )]
	public float TerrainEdgeRadius { get; set; } = 500f;

	[Property, Feature( "Terrain" ), Group( "Texture" )]
	public TerrainTextureLayer TerrainTargetLayer { get; set; } = TerrainTextureLayer.Overlay;

	[Property, Feature( "Terrain" ), Group( "Texture" ), Range( 0f, 1f )]
	public float TerrainTextureNoise { get; set; } = 0.2f;

	[Property, Feature( "Terrain" ), Group( "Texture" )]
	public TerrainMaterial[] TerrainEdgeMaterials { get; set; } = Array.Empty<TerrainMaterial>();

	[Property, Feature( "Terrain" ), Group( "Texture" )]
	public Gradient TerrainEdgeBlendGradient = new Gradient(
		new Gradient.ColorFrame( 0, Color.White ),
		new Gradient.ColorFrame( 1, Color.White.WithAlpha( 0f ) )
	);


	/// <summary> 
	/// Adapts the terrain geometry to the spline shape.
	/// </summary>
	public void AdaptTerrainToRoad()
	{
		if ( !TerrainTarget.IsValid() )
		{
			TerrainTarget = Scene.GetAllComponents<Terrain>().FirstOrDefault();
		}

		if ( !TerrainTarget.IsValid() )
		{
			Log.Warning( "RoadTool: No valid TerrainTarget found in scene." );
			return;
		}

		if ( Spline == null || Spline.PointCount < 2 )
			return;

		var storage = TerrainTarget.Storage;
		if ( storage == null || storage.HeightMap == null )
		{
			Log.Warning( "RoadTool: Terrain Storage or HeightMap is missing." );
			return;
		}

		// 1. Terrain and Road Parameters 
		int resolution = storage.Resolution;
		float terrainSize = storage.TerrainSize;
		float terrainMaxHeight = storage.TerrainHeight;
		float halfSize = terrainSize * 0.5f;
		float roadWidthHalf = RoadWidth * 0.5f;
		int sampleCount = Math.Max( 1, (int)MathF.Ceiling( Spline.Length / Math.Max( 5f, TerrainStepPrecision ) ) );

		// 2. Initialization of calculation buffers
		var heightMap = storage.HeightMap;

		var updatedHeights = new float[heightMap.Length]; 
		var bestDistance = new float[heightMap.Length];

		for ( int i = 0; i < heightMap.Length; i++ )
		{
			updatedHeights[i] = (heightMap[i] / (float)ushort.MaxValue) * terrainMaxHeight;
			bestDistance[i] = float.MaxValue;
		}

		// 3. Spline Sampling 
		var frames = UseRotationMinimizingFrames
			? CalculateRotationMinimizingTangentFrames( Spline, sampleCount + 1 )
			: CalculateTangentFramesUsingUpDir( Spline, sampleCount + 1 );

		bool hasModified = false;

		for ( int i = 0; i <= sampleCount; i++ )
		{
			var frame = frames[i];
			var worldPos = WorldTransform.PointToWorld( frame.Position );
			var worldRight = WorldRotation * frame.Rotation.Right;

			// Conversion to local terrain coordinates to support rotation/translation 
			var localPos = TerrainTarget.Transform.World.PointToLocal( worldPos );
			var roadRightLocal = TerrainTarget.Transform.World.Rotation.Inverse * worldRight;

			// Adaptive coordinate system detection (Center vs Corner)
			float u = localPos.x / terrainSize;
			float v = localPos.y / terrainSize;
			bool localIsCentered = false;

			if ( u < 0f || u > 1f || v < 0f || v > 1f )
			{
				u = (localPos.x + halfSize) / terrainSize;
				v = (localPos.y + halfSize) / terrainSize;
				localIsCentered = true;
			}

			if ( u < 0f || u > 1f || v < 0f || v > 1f ) continue;

			int gridX = Math.Clamp( (int)MathF.Round( u * (resolution - 1) ), 0, resolution - 1 );
			int gridY = Math.Clamp( (int)MathF.Round( v * (resolution - 1) ), 0, resolution - 1 );

			float cellSize = terrainSize / (resolution - 1);
			float totalRadius = roadWidthHalf + TerrainFalloffRadius;
			int pixelRadius = (int)MathF.Ceiling( totalRadius / cellSize );

			var roadRight2D = new Vector2( roadRightLocal.x, roadRightLocal.y );
			if ( roadRight2D.LengthSquared > 0.0001f )
			{
				roadRight2D = roadRight2D.Normal;
			}
			else
			{
				roadRight2D = new Vector2( 1f, 0f );
			}
			Vector3 roadCenter = localPos.WithZ( 0 );

			// Modify pixels within influence radius 
			for ( int ix = gridX - pixelRadius; ix <= gridX + pixelRadius; ix++ )
			{
				for ( int iy = gridY - pixelRadius; iy <= gridY + pixelRadius; iy++ )
				{
					if ( ix < 0 || ix >= resolution || iy < 0 || iy >= resolution ) continue;

					// s&box indexing: ix (World X) is major axis, iy (World Y) is minor axis
					// Use iy * resolution + ix to match standard storage if ix*res doesn't work 
					int index = iy * resolution + ix;

					float nodeLocalX = (ix / (float)(resolution - 1)) * terrainSize - (localIsCentered ? halfSize : 0);
					float nodeLocalY = (iy / (float)(resolution - 1)) * terrainSize - (localIsCentered ? halfSize : 0);

					Vector3 nodeLocalPos = new Vector3( nodeLocalX, nodeLocalY, 0 );
					float distance = Vector3.DistanceBetween( roadCenter, nodeLocalPos );

					if ( distance > totalRadius ) continue;

					// Height calculation with Roll and Offset 
					var nodeLocal2D = new Vector2( nodeLocalX - localPos.x, nodeLocalY - localPos.y );
					float lateral = Vector2.Dot( nodeLocal2D, roadRight2D );
					float rollHeightOffset = roadRightLocal.z * lateral;
					float roadCoreHeight = Math.Clamp( localPos.z + TerrainHeightOffset + rollHeightOffset, 0f, terrainMaxHeight );

					float candidateHeight;
					if ( distance <= roadWidthHalf )
					{
						candidateHeight = roadCoreHeight;
					}
					else
					{
						float t = Math.Clamp( (distance - roadWidthHalf) / TerrainFalloffRadius, 0f, 1f );
						float smoothT = t * t * (3f - 2f * t);
						candidateHeight = MathX.Lerp( roadCoreHeight, (heightMap[index] / (float)ushort.MaxValue) * terrainMaxHeight, smoothT );
					}

					if ( distance < bestDistance[index] )
					{
						bestDistance[index] = distance;
						updatedHeights[index] = candidateHeight;
						hasModified = true;
					}
				}
			}
		}

		if ( hasModified )
		{ 
			// 4. Final encoding to ushort and GPU synchronization
			for ( int i = 0; i < heightMap.Length; i++ )
			{
				heightMap[i] = (ushort)MathF.Round( Math.Clamp( updatedHeights[i], 0f, terrainMaxHeight ) / terrainMaxHeight * ushort.MaxValue );
			}

			storage.HeightMap = heightMap;
			storage.StateHasChanged();
			TerrainTarget.Create();

			Log.Info( "RoadTool: Terrain terraformed successfully!" );
		}
	}

	/// <summary>
	/// Applies the materials to the terrain based on the road shape and the blend gradient.
	/// </summary>
	public void PaintTerrainToRoad()
	{
		if ( !TerrainTarget.IsValid() || TerrainEdgeMaterials == null || TerrainEdgeMaterials.Length == 0 ) return;
		
		var storage = TerrainTarget.Storage;
		if ( storage == null || storage.ControlMap == null ) return;

		// 1. Setup Parameters
		int resolution = storage.Resolution; 
		float terrainSize = storage.TerrainSize;
		float halfSize = terrainSize * 0.5f;
		float roadWidthHalf = RoadWidth * 0.5f;
		float totalRadius = roadWidthHalf + TerrainEdgeRadius;
		int sampleCount = Math.Max( 1, (int)MathF.Ceiling( Spline.Length / Math.Max( 5f, TerrainStepPrecision ) ) );

		// Identify all material indices in the terrain storage 
		bool materialsAdded = false;
		var materialIndices = new int[TerrainEdgeMaterials.Length];
		for ( int m = 0; m < TerrainEdgeMaterials.Length; m++ )
		{
			if ( TerrainEdgeMaterials[m] == null ) continue;

			int idx = storage.Materials.IndexOf( TerrainEdgeMaterials[m] );
			if ( idx == -1 )
			{
				storage.Materials.Add( TerrainEdgeMaterials[m] );
				idx = storage.Materials.Count - 1;
				materialsAdded = true;
			}

			// CompactTerrainMaterial only supports up to 32 materials (0-31)
			if ( idx > 31 )
			{
				Log.Error( $"RoadTool: Terrain has too many materials ({idx}). Material '{TerrainEdgeMaterials[m].ResourceName}' cannot be painted." );
				idx = 0; 
			}

			materialIndices[m] = idx;
		}

		if ( materialsAdded )
		{
			storage.StateHasChanged();
			// We call Create to ensure the renderer knows about the new material resources
			TerrainTarget.Create();
		}

		var frames = UseRotationMinimizingFrames ? CalculateRotationMinimizingTangentFrames( Spline, sampleCount + 1 ) : CalculateTangentFramesUsingUpDir( Spline, sampleCount + 1 );

		// 2. Conversion of spline points to local terrain coordinates (Z=0) 
		var localPoints = frames.Select( f => TerrainTarget.Transform.World.PointToLocal( WorldTransform.PointToWorld( f.Position ) ).WithZ( 0 ) ).ToArray();

		// 3. Coordinate system determination (Centered vs Corner) 
		var checkPos = TerrainTarget.Transform.World.PointToLocal( WorldPosition );
		bool localIsCentered = checkPos.x < 0f || checkPos.x > terrainSize || checkPos.y < 0f || checkPos.y > terrainSize;
		float coordOffset = localIsCentered ? halfSize : 0f;

		// 4. Definition of the working area (Road BBox + effect radius) 
		BBox localSplineBounds = BBox.FromPoints( localPoints );
		int ixMin = Math.Clamp( (int)MathF.Floor( (localSplineBounds.Mins.x + coordOffset - totalRadius) / terrainSize * (resolution - 1) ), 0, resolution - 1 );
		int ixMax = Math.Clamp( (int)MathF.Ceiling( (localSplineBounds.Maxs.x + coordOffset + totalRadius) / terrainSize * (resolution - 1) ), 0, resolution - 1 );
		int iyMin = Math.Clamp( (int)MathF.Floor( (localSplineBounds.Mins.y + coordOffset - totalRadius) / terrainSize * (resolution - 1) ), 0, resolution - 1 );
		int iyMax = Math.Clamp( (int)MathF.Ceiling( (localSplineBounds.Maxs.y + coordOffset + totalRadius) / terrainSize * (resolution - 1) ), 0, resolution - 1 );

		bool hasModified = false;
		var controlMap = storage.ControlMap;

		// 5. Parcours de la grille de pixels dans la zone de la route
		for ( int ix = ixMin; ix <= ixMax; ix++ ) 
		{
			for ( int iy = iyMin; iy <= iyMax; iy++ )
			{
				float px = (ix / (float)(resolution - 1)) * terrainSize - coordOffset;
				float py = (iy / (float)(resolution - 1)) * terrainSize - coordOffset;
				Vector3 pixelPos = new Vector3( px, py, 0 );
				
				// Trouver la distance la plus courte entre ce pixel et n'importe quel segment de la spline
				float minDist = float.MaxValue;
				for ( int s = 0; s < localPoints.Length - 1; s++ )
				{
					Vector3 pA = localPoints[s];
					Vector3 pB = localPoints[s + 1];
					
					Vector3 ab = pB - pA;
					Vector3 ap = pixelPos - pA;
					float t_seg = Math.Clamp( Vector3.Dot( ap, ab ) / ab.LengthSquared, 0f, 1f );
					float d = Vector3.DistanceBetween( pixelPos, pA + t_seg * ab );
					
					if ( d < minDist ) minDist = d;
				}

				if ( minDist > totalRadius ) continue;

				int index = iy * resolution + ix;
				float t = Math.Clamp( (minDist - roadWidthHalf) / TerrainEdgeRadius, 0f, 1f );
				if ( minDist <= roadWidthHalf ) t = 0f;

				float blendStrength = TerrainEdgeBlendGradient.Evaluate( t ).a;

				if ( blendStrength > 0.01f )
				{ 
					float pixelNoise = ((float)((index * 1103515245 + 12345) & 0x7FFFFFFF) / 0x7FFFFFFF) * TerrainTextureNoise - (TerrainTextureNoise * 0.5f);
					float noisyT = Math.Clamp( t + pixelNoise, 0f, 1f );
					float noisyDistance = minDist + (pixelNoise * TerrainEdgeRadius);

					int materialIndex;
					if ( noisyDistance <= roadWidthHalf )
					{
						materialIndex = materialIndices[0];
					}
					else
					{
						int edgeMatCount = materialIndices.Length - 1;
						int edgeIdx = edgeMatCount > 0 ? Math.Clamp( (int)(noisyT * edgeMatCount), 0, edgeMatCount - 1 ) + 1 : 0; 
						materialIndex = materialIndices[edgeIdx];
					}

					uint packed = controlMap[index];
					var mat = new CompactTerrainMaterial( packed );

					if ( TerrainTargetLayer == TerrainTextureLayer.Base )
					{ 
						mat.BaseTextureId = (byte)materialIndex;
						mat.BlendFactor = (byte)MathX.Lerp( mat.BlendFactor, 0, blendStrength );
					}
					else
					{
						mat.OverlayTextureId = (byte)materialIndex;
						mat.BlendFactor = (byte)MathX.Lerp( mat.BlendFactor, 255, blendStrength );
					}

					controlMap[index] = mat.Packed;
					hasModified = true;
				}
			}
		}

		if ( hasModified )
		{
			storage.ControlMap = controlMap;
			storage.StateHasChanged();
			TerrainTarget.SyncGPUTexture();
		}
	}
}
