using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// A deliberately simple arcade car controller for demo purpose in my Road Tool.
/// It's obviously recommended to either: make your own component
/// or use my Vehicle Controller library (Not yet available)
/// </summary>
[Icon("directions_car")]
[Title("Demo - Car Controller")]
[Category("Demo")]
public sealed class DemoCarController : Component
{
	[RequireComponent] public Rigidbody Rigidbody { get; set; }

	[Property, Group("Engine")] public float EnginePower { get; set; } = 25000.0f;
	[Property(Title = "Acceleration"), Group("Engine")] public float AccelerationRate { get; set; } = 10.0f;
	[Property(Title = "Coast Down"), Group("Engine")] public float DecelerationRate { get; set; } = 1.0f;
	[Property(Title = "Braking"), Group("Engine")] public float BrakingRate { get; set; } = 5.0f;
	[Property(Title = "Top Speed"), Group("Engine")] public float TopSpeed { get; set; } = 3000.0f;
	[Property(Title = "Reverse Top Speed"), Group("Engine")] public float ReverseTopSpeed { get; set; } = 800.0f;

	[Property, Group("Physics")] public float AngularDamping { get; set; } = 8.0f;
	[Property, Group("Physics")] public float HandbrakeAngularDamping { get; set; } = 0.1f;

	/// <summary>Input action held for the handbrake (locks the rear wheels). Defaults to Jump (space).</summary>
	[Property, Group("Input")] public string HandbrakeAction { get; set; } = "Jump";

	/// <summary>True while a traffic brain is driving this car (vs a seated player). It's the brain's call — pushed in by
	/// <see cref="TrafficVehicle.IsAiControlled"/> each frame — so a plain player car with no brain just leaves it false.
	/// When set, the car drives from the Ai* inputs below.</summary>
	public bool IsAiControlled { get; set; }
	public float AiThrottle { get; set; }
	public float AiSteer { get; set; }
	public bool AiHandbrake { get; set; }

	/// <summary>True while a player is seated in the driver seat.</summary>
	public bool IsDriven { get; private set; }
	public bool IsBraking { get; private set; }
	public bool IsReversing { get; private set; }

	/// <summary>Signed forward speed in km/h (negative while reversing).</summary>
	public float SpeedKmh => Rigidbody.IsValid() ? Vector3.Dot(Rigidbody.Velocity, WorldRotation.Forward) * 0.09144f : 0.0f;

	private List<DemoWheel> m_Wheels = new();
	private float m_CurrentTorque;



	protected override void OnAwake()
	{
		m_Wheels = GetComponentsInChildren<DemoWheel>().ToList();
	}



	protected override void OnFixedUpdate()
	{
		if (!Rigidbody.IsValid() || IsProxy)
			return;

		// The driver is the player seated in the driver seat (or any seated player if no seat is assigned).
		PlayerController driver = GetComponentInChildren<PlayerController>();
		IsDriven = driver.IsValid();

		float throttle;
		float steer;
		bool handbrake;

		if (IsDriven && driver is { IsProxy: false })
		{
			// Local seated player: read live controls. (Movement is consumed by the sit mode, so WASD is free for us.)
			throttle = Input.AnalogMove.x;
			steer = Input.AnalogMove.y;
			handbrake = !string.IsNullOrEmpty(HandbrakeAction) && Input.Down(HandbrakeAction);
		}
		else
		{
			// Nobody driving locally — fall back to the AI inputs (zero unless the traffic system is feeding them).
			throttle = AiThrottle;
			steer = AiSteer;
			handbrake = AiHandbrake;
		}

		// The vehicle is "live" when a player is in it or an NPC brain is driving it; otherwise it sits parked.
		bool live = IsDriven || IsAiControlled;
		
		ApplyDriving(throttle, steer, handbrake, live);
	}



	private void ApplyDriving(float _Throttle, float _Steer, bool _Handbrake, bool _Live)
	{
		float forwardSpeed = Vector3.Dot(Rigidbody.Velocity, WorldTransform.Forward);

		float targetTorque = 0.0f;
		float targetBrake = 0.0f;

		if (!_Live)
		{
			// Parked: hold it still, no steering, no power.
			targetBrake = 0.0f;
			IsBraking = false;
			IsReversing = false;
			_Steer = 0.0f;
		}
		else if (forwardSpeed > 10.0f && _Throttle < -0.02f)
		{
			// Rolling forward while pressing reverse → brake.
			IsBraking = true;
			IsReversing = false;
			targetBrake = MathF.Abs(_Throttle);
		}
		else if (forwardSpeed < -10.0f && _Throttle > 0.02f)
		{
			// Rolling backward while pressing forward → brake.
			IsBraking = true;
			IsReversing = true;
			targetBrake = _Throttle;
		}
		else
		{
			// Accelerate (forward or reverse) under engine power.
			IsBraking = false;
			IsReversing = forwardSpeed < -10.0f;
			targetTorque = _Throttle * EnginePower;
		}

		bool handbraking = _Handbrake && _Live;

		// Ease the torque toward its target — slower when coasting, snappier when accelerating or braking — for feel.
		float rate = MathF.Abs(_Throttle) > 0.02f ? AccelerationRate : DecelerationRate;

		if (IsBraking)
			rate = BrakingRate;

		if (handbraking)
			rate = BrakingRate * 0.5f;

		m_CurrentTorque = m_CurrentTorque.LerpTo(targetTorque, rate * Time.Delta);

		Rigidbody.AngularDamping = handbraking ? HandbrakeAngularDamping : AngularDamping;

		foreach (DemoWheel wheel in m_Wheels)
		{
			if (!wheel.IsValid())
				continue;

			wheel.ApplyMotorTorque(m_CurrentTorque);
			wheel.ApplySteeringInput(_Steer);
			wheel.ApplyBrakeTorque(targetBrake);

			// The handbrake locks the rear (non-steering) wheels for drifting / parking.
			if (handbraking && !wheel.CanSteer)
				wheel.ApplyBrakeTorque(1.0f);
		}

		// Cap top speed (lower in reverse).
		float topSpeed = IsReversing ? ReverseTopSpeed : TopSpeed;

		if (_Live && Rigidbody.Velocity.Length > topSpeed)
			Rigidbody.Velocity = Rigidbody.Velocity.Normal * topSpeed;
	}
	
	
	
	public float GetMaxSteering()
	{
		return m_Wheels.FirstOrDefault(x => x.CanSteer)!.MaxSteeringAngle;
	}
}
