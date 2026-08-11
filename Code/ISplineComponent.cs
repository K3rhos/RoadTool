using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// A component that owns an editable <see cref="Sandbox.Spline"/>. Lets the spline editor tool/window drive any of
/// them (roads, rails, …) without being tied to a single component type.
/// </summary>
public interface ISplineComponent : IValid
{
	Spline Spline { get; }
	Transform WorldTransform { get; }
	GameObject GameObject { get; }
}
