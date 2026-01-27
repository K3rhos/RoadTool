using System;
using System.Linq;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

public partial class RoadComponent
{
	private bool m_DoesLamppostsNeedRebuild = false;

	[Property, FeatureEnabled("Lampposts", Icon = "light_mode", Tint = EditorTint.Red), Change] private bool HasLampposts { get; set; } = false;
	[Property, Feature("Lampposts")] public GameObject LamppostPrefab { get; set { field = value; m_DoesLamppostsNeedRebuild = true; } }
	[Property, Feature("Lampposts"), Range(50.0f, 2000.0f)] private float LamppostSpacing { get; set { field = value.Clamp(10.0f, 100000.0f); m_DoesLamppostsNeedRebuild = true; } } = 50.0f;
	[Property, Feature("Lampposts"), Range(-200.0f, 200.0f)] private float LamppostOffsetFromSidewalk { get; set { field = value; m_DoesLamppostsNeedRebuild = true; } } = 10.0f;
	[Property, Feature("Lampposts"), Range(0.0f, 10.0f)] private float LamppostHeightOffset { get; set { field = value; m_DoesLamppostsNeedRebuild = true; } } = 0.0f;
	[Property, Feature("Lampposts")] private LamppostSide LamppostPlacement { get; set { field = value; m_DoesLamppostsNeedRebuild = true; } } = LamppostSide.Both;
	[Property, Feature("Lampposts")] private bool AlignToSplineRotation { get; set { field = value; m_DoesLamppostsNeedRebuild = true; } } = true;
	[Property(Title = "Keep Vertical (World Up)"), Feature("Lampposts")] private bool KeepVertical { get; set { field = value; m_DoesLamppostsNeedRebuild = true; } } = true;
	[Property, Feature("Lampposts"), Range(0.0f, 360.0f)] private float LamppostRotationOffset { get; set { field = value; m_DoesLamppostsNeedRebuild = true; } } = 0.0f;
	[Property, Feature("Lampposts"), Range(0.0f, 100.0f)] private float StartOffset { get; set { field = value; m_DoesLamppostsNeedRebuild = true; } } = 0.0f;
	[Property, Feature("Lampposts"), Range(0.0f, 100.0f)] private float EndOffset { get; set { field = value; m_DoesLamppostsNeedRebuild = true; } } = 0.0f;

	public enum LamppostSide
	{
		Left,
		Right,
		Both,
		Alternating
	}



	private void OnHasLamppostsChanged(bool _OldValue, bool _NewValue)
	{
		m_DoesLamppostsNeedRebuild = true;
	}



	private void CreateLampposts()
	{
		RemoveLampposts();

		if (!HasLampposts || !LamppostPrefab.IsValid())
			return;

		BuildLampposts();
	}



	private void RemoveLampposts()
	{
		// If we're in play mode, do not clear them
		if (LoadingScreen.IsVisible || Game.IsPlaying)
			return;

		GameObject containerObject = GameObject.Children.FirstOrDefault(x => x.Name == "Lampposts");

		if (containerObject.IsValid())
		{
			foreach (var gameObject in containerObject.Children.Where(x => x.IsValid()))
			{
				gameObject.Destroy();
			}
		}
	}



	private void UpdateLampposts()
	{
		if (m_DoesLamppostsNeedRebuild)
		{
			CreateLampposts();

			m_DoesLamppostsNeedRebuild = false;
		}
	}



	private void BuildLampposts()
	{
		// If we're in play mode, do not rebuild them
		if (LoadingScreen.IsVisible || Game.IsPlaying)
			return;

		GameObject containerObject = GameObject.Children.FirstOrDefault(x => x.Name == "Lampposts");

		if (!containerObject.IsValid())
			containerObject = new GameObject(GameObject, true, "Lampposts");

		float splineLength = Spline.Length;
		float effectiveLength = splineLength - StartOffset - EndOffset;

		if (effectiveLength <= 0)
			return;

		// Calculate base segments using the same logic as road/sidewalk
		int baseSegmentCount = Math.Max(2, (int)Math.Ceiling(Spline.Length / RoadPrecision));
		int baseFrameCount = baseSegmentCount + 1;

		var frames = UseRotationMinimizingFrames
			? CalculateRotationMinimizingTangentFrames(Spline, baseFrameCount)
			: CalculateTangentFramesUsingUpDir(Spline, baseFrameCount);

		var segmentsToKeep = new List<int>();

		if (AutoSimplify)
		{
			segmentsToKeep = DetectImportantSegments(frames, baseSegmentCount, MinSegmentsToMerge, StraightThreshold);
		}
		else
		{
			for (int i = 0; i <= baseSegmentCount; i++)
			{
				segmentsToKeep.Add(i);
			}
		}

		var simplifiedPositions = new List<(Transform _Frame, float _Distance)>();

		foreach (int index in segmentsToKeep)
		{
			float t = (float)index / (baseFrameCount - 1);
			float distance = t * splineLength;

			simplifiedPositions.Add((frames[index], distance));
		}

		int lamppostCount = Math.Max(1, (int)MathF.Ceiling(effectiveLength / LamppostSpacing));
		int frameCount = lamppostCount + 1;

		float roadEdgeOffset = RoadWidth * 0.5f;
		float sidewalkOffset = HasSidewalk ? SidewalkWidth : 0.0f;
		float totalOffset = roadEdgeOffset + sidewalkOffset + LamppostOffsetFromSidewalk;

		for (int i = 0; i < frameCount; i++)
		{
			float t = (float)i / (frameCount - 1);
			float distance = t * splineLength;

			// Skip if outside the start/end offset range
			if (distance < StartOffset || distance > splineLength - EndOffset)
				continue;

			// Interpolate frame at this distance along the simplified spline
			Transform frame = InterpolateFrameAtDistance(simplifiedPositions, distance);

			Vector3 basePosition = frame.Position;
			Vector3 forward = frame.Rotation.Forward;
			Vector3 up = frame.Rotation.Up;
			Vector3 right = frame.Rotation.Right;

			bool placeLeft = false;
			bool placeRight = false;

			switch (LamppostPlacement)
			{
				case LamppostSide.Left:
					placeLeft = true;
					break;
				case LamppostSide.Right:
					placeRight = true;
					break;
				case LamppostSide.Both:
					placeLeft = true;
					placeRight = true;
					break;
				case LamppostSide.Alternating:
					if (i % 2 == 0)
						placeLeft = true;
					else
						placeRight = true;
					break;
				default:
					placeLeft = true;
					placeRight = true;
					break;
			}

			if (placeLeft)
			{
				Vector3 leftPosition = basePosition - right * totalOffset + up * (LamppostHeightOffset + (HasSidewalk ? SidewalkHeight : 0.0f));
				Rotation leftRotation = CalculateLamppostRotation(forward, up, LamppostRotationOffset);

				CreateLamppost(containerObject, leftPosition, leftRotation);
			}

			if (placeRight)
			{
				Vector3 rightPosition = basePosition + right * totalOffset + up * (LamppostHeightOffset + (HasSidewalk ? SidewalkHeight : 0.0f));
				Rotation rightRotation = CalculateLamppostRotation(forward, up, LamppostRotationOffset + 180.0f);

				CreateLamppost(containerObject, rightPosition, rightRotation);
			}
		}
	}



	private void CreateLamppost(GameObject _Parent, Vector3 _Position, Rotation _Rotation)
	{
		if (!LamppostPrefab.IsValid())
			return;

		GameObject lamppostObject = LamppostPrefab.Clone(_Parent, _Position, _Rotation, Vector3.One);

		if (!lamppostObject.IsValid())
			return;

		// lamppostObject.Name = "Lamppost";
		lamppostObject.LocalPosition = _Position;
		lamppostObject.LocalRotation = _Rotation;
	}



	private Rotation CalculateLamppostRotation(Vector3 _Forward, Vector3 _SplineUp, float _YawOffset)
	{
		if (!AlignToSplineRotation)
			return Rotation.FromYaw(_YawOffset);

		Rotation finalRotation;

		if (KeepVertical)
		{
			Vector3 flatForward = _Forward.WithZ(0).Normal;

			if (flatForward.Length > 0.001f)
			{
				finalRotation = Rotation.LookAt(flatForward, Vector3.Up);
			}
			else
			{
				finalRotation = Rotation.FromYaw(_YawOffset);
			}
		}
		else
		{
			finalRotation = Rotation.LookAt(_Forward, _SplineUp);
		}

		return finalRotation * Rotation.FromYaw(_YawOffset);
	}
}
