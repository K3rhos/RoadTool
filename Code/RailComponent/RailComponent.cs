using System;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// Generates a railway track along a spline — two I-beam rails plus evenly spaced wooden sleepers — mirroring the
/// spline/frame/mesh flow of <see cref="RoadComponent"/>.
/// </summary>
[Icon("train")]
public partial class RailComponent : Component, Component.ExecuteInEditor, Component.IHasBounds, ISplineComponent
{
	[Property, Feature("Rail"), Hide]
	public Spline Spline
	{
		get;
		set
		{
			field = value;

			SubscribeToSpline();
			UpdateData();
		}
	} = new();

	private Spline m_SubscribedSpline;

	private bool m_DoesRailMeshNeedRebuild;
	
	private const float RebuildThrottleSeconds = 0.25f; // ~4 rebuilds / second
	private RealTimeSince m_TimeSinceRebuild;

	private const string RailMeshTag = "rail_mesh";
	private const string RailSurfaceTag = "rail_surface";
	private const string SleeperSurfaceTag = "rail_sleepers";

	/// <summary>
	/// Prevents the rails from being rebuilt when a property changes or the component is re-enabled. Useful if you plan
	/// to hand-edit the baked mesh so you don't accidentally erase it.
	/// </summary>
	[Property(Title = "🔒 Locked"), Feature("Rail")] private bool IsRailLocked { get; set; } = false;
	[Property(Title = "Precision"), Feature("Rail"), Range(10.0f, 100.0f)] private float RailPrecision { get; set { field = value.Clamp(10.0f, 10000.0f); IsDirty = true; } } = 40.0f;
	[Property(Title = "Rotation Minimizing Frames"), Feature("Rail")] public bool UseRotationMinimizingFrames { get; set { field = value; IsDirty = true; } }

	private bool IsDirty
	{
		get => m_DoesRailMeshNeedRebuild;
		set => m_DoesRailMeshNeedRebuild = value;
	}

	public BBox LocalBounds => Spline.Bounds;



	public RailComponent()
	{
		Spline.InsertPoint(Spline.PointCount, new Spline.Point { Position = new Vector3(0, 0, 0) });
		Spline.InsertPoint(Spline.PointCount, new Spline.Point { Position = new Vector3(1000, 0, 0) });
		Spline.InsertPoint(Spline.PointCount, new Spline.Point { Position = new Vector3(1600, 1000, 0) });
	}



	protected override void OnEnabled()
	{
		SubscribeToSpline();

		EnsureRailMeshExist();
	}



	protected override void OnDisabled()
	{
		UnsubscribeFromSpline();

		RemoveRailMeshes();
	}



	/// <summary>
	/// Undo/redo restores the serialized spline data in place, which doesn't raise
	/// <see cref="Sandbox.Spline.SplineChanged"/>. Rebuilding from here is what makes the rails follow an undo.
	/// </summary>
	protected override void OnValidate()
	{
		SubscribeToSpline();
		UpdateData();
	}



	protected override void OnUpdate()
	{
		SyncSplineSubscription();

		UpdateRailMeshes();
	}



	private void UpdateData()
	{
		if (!GameObject.IsValid() || !Scene.IsEditor)
			return;

		IsDirty = true;
	}



	/// <summary>Safety net for editor state changes that swap the spline instance without hitting the setter or OnValidate.</summary>
	private void SyncSplineSubscription()
	{
		if (ReferenceEquals(m_SubscribedSpline, Spline))
			return;

		SubscribeToSpline();
		UpdateData();
	}



	private void SubscribeToSpline()
	{
		if (ReferenceEquals(m_SubscribedSpline, Spline))
			return;

		UnsubscribeFromSpline();

		m_SubscribedSpline = Spline;

		if (m_SubscribedSpline is not null)
			m_SubscribedSpline.SplineChanged += UpdateData;
	}



	private void UnsubscribeFromSpline()
	{
		if (m_SubscribedSpline is null)
			return;

		m_SubscribedSpline.SplineChanged -= UpdateData;
		m_SubscribedSpline = null;
	}



	private void UpdateRailMeshes()
	{
		if (!m_DoesRailMeshNeedRebuild)
			return;

		if (m_TimeSinceRebuild < RebuildThrottleSeconds)
			return; // inside the throttle window — stay dirty and rebuild on the next slot

		RebuildRailMeshes();

		m_TimeSinceRebuild = 0.0f;
		m_DoesRailMeshNeedRebuild = false;
	}



	private void RebuildRailMeshes()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		if (IsRailLocked)
			return;

		RemoveRailMeshes();
		BuildRailMeshes();
	}



	private void EnsureRailMeshExist()
	{
		if (SandboxUtility.IsInPlayMode)
			return;

		if (IsRailLocked)
			return;

		if (HasGeneratedMeshChildren(RailMeshTag))
			return;

		BuildRailMeshes();
	}



	private void RemoveRailMeshes()
	{
		if (IsRailLocked)
			return;

		RemoveGeneratedMeshChildren(RailMeshTag);
	}



	private void BuildRailMeshes()
	{
		var frames = GetRailFrames();

		if (frames.Length < 2)
			return;

		BuildRails(frames);

		if (HasSleepers)
			BuildSleepers(frames);
	}



	private Transform[] GetRailFrames()
	{
		int segmentCount = Math.Max(2, (int)Math.Ceiling(Spline.Length / RailPrecision));
		int frameCount = segmentCount + 1;

		return UseRotationMinimizingFrames
			? CalculateRotationMinimizingTangentFrames(Spline, frameCount)
			: CalculateTangentFramesUsingUpDir(Spline, frameCount);
	}



	/// <summary>
	/// Interpolates a frame at an arbitrary spline distance from the evenly spaced frame array (frame i sits at
	/// distance i · length / (count − 1)). Keeps the sleepers riding the same curve as the rails.
	/// </summary>
	private static Transform SampleFrameAtDistance(Transform[] _Frames, float _Distance, float _TotalLength)
	{
		if (_Frames.Length == 1 || _TotalLength <= 0.0f)
			return _Frames[0];

		float step = _TotalLength / (_Frames.Length - 1);
		float t = _Distance / step;

		int i0 = Math.Clamp((int)MathF.Floor(t), 0, _Frames.Length - 2);
		float frac = Math.Clamp(t - i0, 0.0f, 1.0f);

		Transform f0 = _Frames[i0];
		Transform f1 = _Frames[i0 + 1];

		return new Transform(Vector3.Lerp(f0.Position, f1.Position, frac), Rotation.Slerp(f0.Rotation, f1.Rotation, frac));
	}



	private void RemoveGeneratedMeshChildren(string _Tag)
	{
		var toRemove = GameObject.Children.Where(child => child.Tags.Has(_Tag)).ToList();

		foreach (var child in toRemove)
			child.Destroy();
	}



	private bool HasGeneratedMeshChildren(string _Tag)
	{
		return GameObject.Children.Any(child => child.Tags.Has(_Tag));
	}
}
