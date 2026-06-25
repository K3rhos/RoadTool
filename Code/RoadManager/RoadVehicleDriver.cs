using System;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// A primitive control surface the traffic AI (<see cref="TrafficVehicle"/>) uses to drive ONE vehicle. It's a plain
/// bag of delegates — there is no interface for a vehicle controller to implement, and your vehicle code never needs
/// to reference this library.
///
/// The seam is filled in by whoever uses both this tool AND a vehicle controller — i.e. your GAME — via
/// <see cref="RoadManager.ResolveVehicleDriver"/>. The game maps whatever its controller looks like onto these few
/// delegates. Any field left null is simply skipped. The demo wires <see cref="DemoCarController"/> automatically.
/// </summary>
public sealed class RoadVehicleDriver
{
	/// <summary>True while a player is at the wheel — the AI then hands this car over for good and never reclaims it.</summary>
	public Func<bool> IsPlayerDriving;

	/// <summary>The vehicle body's world velocity. The brain reads it to chase a target speed and to detect being jammed.</summary>
	public Func<Vector3> Velocity;

	/// <summary>Tell the controller whether the AI is currently driving this vehicle (vs parked / player-driven). Pushed every frame.</summary>
	public Action<bool> SetAiControlled;

	/// <summary>Push the AI's per-frame inputs: throttle and steer in [-1, 1] (steer + = left), plus handbrake.</summary>
	public Action<float, float, bool> Drive;

	/// <summary>Optional: max steering angle in degrees, used to widen the entity look-ahead toward where the car is turning.</summary>
	public Func<float> MaxSteering;
}
