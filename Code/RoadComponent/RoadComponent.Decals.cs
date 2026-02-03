using System;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	private bool m_DoesDecalsNeedsRebuild = false;
	
	[Property, FeatureEnabled("Decals", Icon = "layers", Tint = EditorTint.Pink), Change] private bool HasDecals { get; set; } = false;
	[Property, Feature("Decals")] public DecalDefinition[] DecalDefinitions { get; set; }
	[Property, Feature("Decals"), Range(0.1f, 20f)] private float DecalSpacing { get; set { field = value; m_DoesDecalsNeedsRebuild = true; } } = 4.0f;
	[Property, Feature("Decals"), Range(0.0f, 1.0f)] private float DecalSpawnChance { get; set { field = value; m_DoesDecalsNeedsRebuild = true; } } = 0.1f;
	[Property, Feature("Decals"), Range(0.0f, 10.0f)] private float DecalEdgeMargin { get; set { field = value; m_DoesDecalsNeedsRebuild = true; } } = 0.5f;
	[Property, Feature("Decals"), Range(0.0f, 1.0f)] private float DecalWidthUsage { get; set { field = value; m_DoesDecalsNeedsRebuild = true; } } = 1.0f;
	[Property, Feature("Decals")] private ParticleFloat DecalSize { get; set { field = value; m_DoesDecalsNeedsRebuild = true; } } = new ParticleFloat(1.0f, 3.0f);
	[Property, Feature("Decals")] private ParticleFloat DecalRotation { get; set { field = value; m_DoesDecalsNeedsRebuild = true; } } = new ParticleFloat(0.0f, 360.0f);
	[Property, Feature("Decals")] private int Seed { get; set { field = value; m_DoesDecalsNeedsRebuild = true; } } = 0;
	
	
	
	private void OnHasDecalsChanged(bool _OldValue, bool _NewValue)
	{
		m_DoesDecalsNeedsRebuild = true;
	}



	private void CreateDecals()
	{
		RemoveDecals();

		if (!HasDecals || DecalDefinitions == null || DecalDefinitions.Length == 0)
			return;

		BuildDecals();
	}



	private void RemoveDecals()
	{
		GameObject containerObject = GameObject.Children.FirstOrDefault(x => x.Name == "Decals");
		
		if (containerObject.IsValid())
		{
			containerObject.Destroy();
		}
	}



	private void UpdateDecals()
	{
		if (m_DoesDecalsNeedsRebuild)
		{
			CreateDecals();

			m_DoesDecalsNeedsRebuild = false;
		}
	}



	private void BuildDecals()
	{
		Game.SetRandomSeed(Seed);
		
		GameObject containerObject = new GameObject(GameObject, true, "Decals");
		containerObject.Flags |= GameObjectFlags.NotSaved;
		
		float splineLength = Spline.Length;

		int sampleCount = Math.Max(2, (int)MathF.Ceiling(splineLength / DecalSpacing));

		var frames = UseRotationMinimizingFrames
			? CalculateRotationMinimizingTangentFrames(Spline, sampleCount)
			: CalculateTangentFramesUsingUpDir(Spline, sampleCount);

		float halfRoadWidth = RoadWidth * 0.5f;

		foreach (var frame in frames)
		{
			if (Game.Random.Float(0.0f, 1.0f) > DecalSpawnChance)
				continue;

			float usableHalfWidth = MathF.Max(0.0f, halfRoadWidth - DecalEdgeMargin) * DecalWidthUsage;

			float lateralOffset = Game.Random.Float(-usableHalfWidth, usableHalfWidth);

			Vector3 position = frame.Position + frame.Rotation.Right * lateralOffset + frame.Rotation.Up;
			Rotation rotation = Rotation.LookAt(-frame.Rotation.Up, frame.Rotation.Forward);

			CreateDecal(containerObject, position, rotation);
		}
	}



	private void CreateDecal(GameObject _GameObject, Vector3 _Position, Rotation _Rotation)
	{
		GameObject gameObject = new GameObject(_GameObject, true, "Road Decal")
		{
			LocalPosition = _Position,
			LocalRotation = _Rotation
		};
		
		gameObject.Flags |= GameObjectFlags.NotSaved;
		
		Decal decal = gameObject.AddComponent<Decal>();

		DecalDefinition decalDefinition = DecalDefinitions[Game.Random.Next(0, DecalDefinitions.Length)];

		decal.Decals = [decalDefinition];
		decal.Scale = DecalSize.Evaluate(0.0f, Seed);
		decal.Rotation = DecalRotation.Evaluate(0.0f, Seed);
	}
}
