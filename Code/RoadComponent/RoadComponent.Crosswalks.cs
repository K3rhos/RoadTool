using System;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

public enum CrosswalkConfig
{
	Start,
	End,
	Both
}

public partial class RoadComponent
{
	private bool m_DoesCrosswalksNeedsRebuild = false;

	[Property, FeatureEnabled("Crosswalks", Icon = "menu", Tint = EditorTint.Pink), Change] private bool HasCrosswalks { get; set; } = false;
	[Property(Title = "Config"), Feature("Crosswalks")] public CrosswalkConfig CrosswalkConfig { get; set { field = value; m_DoesCrosswalksNeedsRebuild = true; } } = CrosswalkConfig.Both;
	[Property(Title = "Decal Definition"), Feature("Crosswalks")] public DecalDefinition CrosswalkDefinition { get; set { field = value; m_DoesCrosswalksNeedsRebuild = true; } }
	[Property(Title = "Decal Size"), Feature("Crosswalks"), Range(0.1f, 10.0f)] private Vector2 CrosswalkSize { get; set { field = value; m_DoesCrosswalksNeedsRebuild = true; } } = Vector2.One;



	private void OnHasCrosswalksChanged(bool _OldValue, bool _NewValue)
	{
		m_DoesCrosswalksNeedsRebuild = true;
	}



	private void CreateCrosswalks()
	{
		RemoveCrosswalks();

		if (!HasCrosswalks || !CrosswalkDefinition.IsValid())
			return;

		BuildCrosswalks();
	}



	private void RemoveCrosswalks()
	{
		GameObject containerObject = GameObject.Children.FirstOrDefault(x => x.Name == "Crosswalks");

		if (containerObject.IsValid())
		{
			containerObject.Destroy();
		}
	}



	private void UpdateCrosswalks()
	{
		if (m_DoesCrosswalksNeedsRebuild)
		{
			CreateCrosswalks();

			m_DoesCrosswalksNeedsRebuild = false;
		}
	}



	private void BuildCrosswalks()
	{
		GameObject containerObject = new GameObject(GameObject, true, "Crosswalks");
		containerObject.Flags |= GameObjectFlags.NotSaved;
		
		float splineLength = Spline.Length;

		int sampleCount = Math.Max(2, (int)MathF.Ceiling(splineLength / DecalSpacing));

		var frames = UseRotationMinimizingFrames
			? CalculateRotationMinimizingTangentFrames(Spline, sampleCount)
			: CalculateTangentFramesUsingUpDir(Spline, sampleCount);

		if (CrosswalkConfig is CrosswalkConfig.Start or CrosswalkConfig.Both)
		{
			Transform roadStart = frames.FirstOrDefault();

			Vector3 position = roadStart.Position;
			Rotation rotation = Rotation.LookAt(-roadStart.Rotation.Up, roadStart.Rotation.Forward);

			CreateCrosswalk(containerObject, position, rotation);
		}

		if (CrosswalkConfig is CrosswalkConfig.End or CrosswalkConfig.Both)
		{
			Transform roadEnd = frames.LastOrDefault();

			Vector3 position = roadEnd.Position;
			Rotation rotation = Rotation.LookAt(-roadEnd.Rotation.Up, roadEnd.Rotation.Forward);

			CreateCrosswalk(containerObject, position, rotation);
		}
	}



	private void CreateCrosswalk(GameObject _GameObject, Vector3 _Position, Rotation _Rotation)
	{
		GameObject gameObject = new GameObject(_GameObject, true, "Crosswalk Decal")
		{
			LocalPosition = _Position,
			LocalRotation = _Rotation
		};

		gameObject.Flags |= GameObjectFlags.NotSaved;
		
		Decal decal = gameObject.AddComponent<Decal>();

		decal.Decals = [CrosswalkDefinition];
		decal.Rotation = new ParticleFloat(0.0f, 0.0f);
		decal.Size = CrosswalkSize;
		decal.Depth = 4.0f;
		decal.AttenuationAngle = 1.0f;
	}
}
