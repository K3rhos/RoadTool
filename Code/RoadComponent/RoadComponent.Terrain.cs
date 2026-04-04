using System;
using System.Linq;
using Sandbox;

#if SANDBOX_EDITOR
using Editor;
using RedSnail.RoadTool.Editor; 
#endif

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	[Property, FeatureEnabled("Terrain", Icon = "landscape", Tint = EditorTint.Green)]
	private bool HasTerrainModification { get; set; } = false;

	[Property(Title = "Cible du Terrain"), Feature("Terrain")]
	public Terrain TerrainTarget { get; set; }

	[Property(Title = "Rayon de lissage"), Feature("Terrain"), Range(0f, 2000f)]
	public float TerrainFalloffRadius { get; set; } = 500f;

	[Property(Title = "Précision d'échantillonnage"), Feature("Terrain"), Range(1f, 500f)]
	public float TerrainStepPrecision { get; set; } = 50f;

	[Property(Title = "Décalage de hauteur"), Feature("Terrain"), Range(-100f, 100f)]
	public float TerrainHeightOffset { get; set; } = 0f;

#if SANDBOX_EDITOR
	[EditorEvent.Component.ContextMenu( "Générer la route sur le terrain" )]
	[Button( "Générer la route sur le terrain", "terrain" ), Feature( "Terrain" )]
#endif
	/// <summary>
	/// Adapte la géométrie du terrain à la forme de la spline.
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

		// 1. Paramètres du Terrain et de la Route
		int resolution = storage.Resolution;
		float terrainSize = storage.TerrainSize;
		float terrainMaxHeight = storage.TerrainHeight;
		float halfSize = terrainSize * 0.5f;
		float roadWidthHalf = RoadWidth * 0.5f;
		int sampleCount = Math.Max( 1, (int)MathF.Ceiling( Spline.Length / Math.Max( 5f, TerrainStepPrecision ) ) );

		// 2. Initialisation des buffers de calcul
		var heightMap = storage.HeightMap;

		// Capture de l'état initial pour le Undo
		var previousHeightMap = new ushort[heightMap.Length];
		Array.Copy( heightMap, previousHeightMap, heightMap.Length );

		var updatedHeights = new float[heightMap.Length];
		var bestDistance = new float[heightMap.Length];

		for ( int i = 0; i < heightMap.Length; i++ )
		{
			updatedHeights[i] = (heightMap[i] / (float)ushort.MaxValue) * terrainMaxHeight;
			bestDistance[i] = float.MaxValue;
		}

		// 3. Échantillonnage de la Spline
		var frames = UseRotationMinimizingFrames
			? CalculateRotationMinimizingTangentFrames( Spline, sampleCount + 1 )
			: CalculateTangentFramesUsingUpDir( Spline, sampleCount + 1 );

		bool hasModified = false;

		for ( int i = 0; i <= sampleCount; i++ )
		{
			var frame = frames[i];
			var worldPos = WorldTransform.PointToWorld( frame.Position );
			var worldRight = WorldRotation * frame.Rotation.Right;

			// Conversion en coordonnées locales terrain pour supporter la rotation/translation
			var localPos = TerrainTarget.Transform.World.PointToLocal( worldPos );
			var roadRightLocal = TerrainTarget.Transform.World.Rotation.Inverse * worldRight;

			// Détection adaptative du référentiel (Centre vs Coin)
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

			// Modification des pixels dans le rayon d'influence
			for ( int ix = gridX - pixelRadius; ix <= gridX + pixelRadius; ix++ )
			{
				for ( int iy = gridY - pixelRadius; iy <= gridY + pixelRadius; iy++ )
				{
					if ( ix < 0 || ix >= resolution || iy < 0 || iy >= resolution ) continue;
					
					// Indexation s&box : ix (X Monde) est l'axe majeur, iy (Y Monde) est l'axe mineur
					// On utilise iy * resolution + ix pour correspondre au stockage standard si ix*res ne marche pas
					int index = iy * resolution + ix;

					float nodeLocalX = (ix / (float)(resolution - 1)) * terrainSize - (localIsCentered ? halfSize : 0);
					float nodeLocalY = (iy / (float)(resolution - 1)) * terrainSize - (localIsCentered ? halfSize : 0);

					Vector3 nodeLocalPos = new Vector3( nodeLocalX, nodeLocalY, 0 );
					float distance = Vector3.DistanceBetween( roadCenter, nodeLocalPos );

					if ( distance > totalRadius ) continue;

					// Calcul de la hauteur avec Roll et Offset
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

		if (hasModified)
		{
			// 4. Encodage final vers ushort et synchronisation GPU
			for ( int i = 0; i < heightMap.Length; i++ )
			{
				heightMap[i] = (ushort)MathF.Round( Math.Clamp( updatedHeights[i], 0f, terrainMaxHeight ) / terrainMaxHeight * ushort.MaxValue );
			}

			storage.HeightMap = heightMap;
			storage.StateHasChanged();
			TerrainTarget.Create();
			TerrainTarget.SyncGPUTexture();

#if SANDBOX_EDITOR
			var newHeightMap = new ushort[heightMap.Length];
			Array.Copy( heightMap, newHeightMap, heightMap.Length );

			var targetTerrain = TerrainTarget;
			var targetStorage = storage;

			SceneEditorSession.Active.AddUndo( "Align Terrain to Road",
				undo: () =>
				{
					if ( !targetTerrain.IsValid() ) return;
					targetStorage.HeightMap = previousHeightMap;
					targetStorage.StateHasChanged();
					targetTerrain.Create();
					targetTerrain.SyncGPUTexture();
				},
				redo: () =>
				{
					if ( !targetTerrain.IsValid() ) return;
					targetStorage.HeightMap = newHeightMap;
					targetStorage.StateHasChanged();
					targetTerrain.Create();
					targetTerrain.SyncGPUTexture();
				} );
#endif

			Log.Info( "RoadTool: Terrain terraformé avec succès !" );
		}
	}
}