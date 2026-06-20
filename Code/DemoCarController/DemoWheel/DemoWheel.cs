using System;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// A deliberately simple custom wheel system for demo purpose in my Road Tool.
/// It's obviously recommended to either: make your own component
/// or use my Vehicle Controller library (Not yet available)
/// </summary>
[Icon("tire_repair")]
[Title("Demo - Wheel")]
[Category("Demo")]
public sealed class DemoWheel : Component
{
	public enum WheelPhysic
	{
		[Title("1D"), Description("Better performance with a huge amount of vehicles, but its less accurate")]
		Unidimensional,
		
		[Title("3D"), Description("More accurate but performance can be slighly worse than using 1D")]
		ThreeDimensions
	}
	
	private const float LOW_SPEED_THRESHOLD = 32.0f;
	private const float SLIDE_THRESHOLD = 0.05f; // Slip threshold to detect sliding
	
	private ModelRenderer m_ModelRenderer;
	private Rigidbody m_Rigidbody;
	private DemoCarController m_CarController;
	
	private SceneTraceResult m_GroundTrace;
	private SceneTraceResult m_BackupGroundTrace;
	
	private float m_MotorTorque;
	private float m_BrakeTorque;
	private float m_SpinAngle;
	private float m_PreviousSuspensionCompression;
	
	private float m_CurrentForwardSlip;
	private float m_CurrentSideSlip;

	private bool m_WantsToUse3DWheelColliders = false;
	
	[Sync] private float TotalSlip { get; set; }

	[Sync] private float SteeringInput { get; set; }
	
	public bool IsGrounded => m_GroundTrace.Hit;
	
	/// <summary>
	/// Returns true if the wheel is sliding/skidding on the ground
	/// </summary>
	public bool IsSliding => TotalSlip > SLIDE_THRESHOLD && IsGrounded;
	
	/// <summary>
	/// Returns the forward slip component (for acceleration/braking skids)
	/// </summary>
	public float ForwardSlip => m_CurrentForwardSlip;
	
	/// <summary>
	/// Returns the side slip component (for drifting)
	/// </summary>
	public float SideSlip => m_CurrentSideSlip;
	
	[Property, Group("General"), Order(0)] private WheelPhysic PhysicMethod { get; set; } = WheelPhysic.Unidimensional;
	[Property, Group("General"), Order(0)] public bool IsPowered { get; set; } = false;
	[Property, Group("General"), Order(0)] private float WheelRadius { get; set; } = 14.0f;
	[Property, Group("General"), Order(0)] private float WheelWidth { get; set; } = 7.0f;
	[Property, Group("General"), Order(0)] private float ExtraWheelDetectionDistance { get; set; } = 0.0f;
	
	[Property, Group("Steering"), Order(1)] public bool CanSteer { get; set; } = false;
	[Property, Group("Steering"), Order(1)] private float SteeringSmoothness { get; set; } = 10.0f;
	[Property, Group("Steering"), Order(1)] public float MaxSteeringAngle { get; set; } = 20.0f;
	[Property, Group("Steering"), Order(1)] private GameObject SteeringWheel { get; set; }
	[Property, Group("Steering"), Order(1)] private bool Inverted { get; set; } = false;
	
	[Property, Group("Suspension"), Order(2)] private float MinSuspensionLength { get; set; } = 0.0f;
	[Property, Group("Suspension"), Order(2)] private float MaxSuspensionLength { get; set; } = 8.0f;
	[Property, Group("Suspension"), Order(2)] private float SuspensionStiffness { get; set; } = 300.0f;
	[Property, Group("Suspension"), Order(2)] private float SuspensionDamping { get; set; } = 15.0f;
	
	[Property, Range(0.1f, 1.5f), Group("Grip"), Order(3)] private float GripMultiplier { get; set; } = 1.0f;
	[Property, Range(0.1f, 2.0f), Group("Grip"), Order(3)] private float MaxGripAngle { get; set; } = 0.3f;
	[Property, Range(0.1f, 2.0f), Group("Grip"), Order(3)] private float NoGripAngle { get; set; } = 0.8f;

	[Property, Group("Friction"), Order(4)] private DemoWheelFrictionResource ForwardFriction { get; set; }
	[Property, Group("Friction"), Order(4)] private DemoWheelFrictionResource SideFriction { get; set; }
	
	[Property, Group("Braking"), Order(5)] private float MaxBrakeForce { get; set; } = 5000.0f;
	
	
	
	protected override void OnEnabled()
	{
		m_ModelRenderer = GetComponentInChildren<ModelRenderer>();
		m_Rigidbody = GetComponentInParent<Rigidbody>();
		m_CarController = GetComponentInParent<DemoCarController>();
	}
	
	
	
	protected override void OnFixedUpdate()
	{
		if (!m_Rigidbody.IsValid())
			return;

		if (m_CarController.IsValid())
		{
			m_WantsToUse3DWheelColliders = PhysicMethod == WheelPhysic.ThreeDimensions && !m_CarController.IsAiControlled && m_CarController.IsDriven;	
		}
		else
		{
			m_WantsToUse3DWheelColliders = PhysicMethod == WheelPhysic.ThreeDimensions;
		}
		
		DoTrace();
		UpdateModelRender();
		UpdateSteering();
		
		if (IsProxy)
			return;
		
		UpdateSuspension();
		UpdateForces();
	}
	
	
	
	protected override void DrawGizmos()
	{
		if (!Gizmo.IsSelected)
			return;
		
		Gizmo.Draw.IgnoreDepth = true;

		{
			Vector3 suspensionStart = Vector3.Zero - Vector3.Down * MinSuspensionLength;
			Vector3 suspensionEnd = Vector3.Zero + Vector3.Down * MaxSuspensionLength;
			
			Gizmo.Draw.Color = Color.Cyan;
			Gizmo.Draw.LineThickness = 0.25f;

			Gizmo.Draw.Line(suspensionStart, suspensionEnd);

			Gizmo.Draw.Line(suspensionStart + Vector3.Forward, suspensionStart + Vector3.Backward);
			Gizmo.Draw.Line(suspensionEnd + Vector3.Forward, suspensionEnd + Vector3.Backward);
		}

		{
			Vector3 circleAxis = Vector3.Right * WheelWidth * 0.5f;
			Vector3 circlePosition = WorldTransform.PointToLocal(WorldPosition);
			
			Gizmo.Draw.LineThickness = 1.0f;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineCylinder(circlePosition + circleAxis, circlePosition - circleAxis, WheelRadius, WheelRadius, 16);
		}

		{
			Vector3 arrowStart = Vector3.Forward * WheelRadius;
			Vector3 arrowEnd = arrowStart + Vector3.Forward * 8.0f;
			
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.Arrow(arrowStart, arrowEnd, 4.0f, 1.0f);
		}
	}
	
	
	
	private static float CalculateSlip(Vector3 _Velocity, Vector3 _Direction, float _Speed)
	{
		const float EPSILON = 0.01f;
		
		return Vector3.Dot(_Velocity, _Direction) / (_Speed + EPSILON);
	}
	
	
	
	private static Vector3 CalculateFrictionForce(DemoWheelFrictionResource _Friction, float _Mass, float _Slip, Vector3 _Direction)
	{
		return -_Friction.Evaluate(MathF.Abs(_Slip), _Mass) * MathF.Sign(_Slip) * _Direction;
	}
	
	
	
	private Vector3 GetCenterLocalPosition()
	{
		Vector3 up = m_Rigidbody.WorldRotation.Up;
		
		if (m_WantsToUse3DWheelColliders)
			return WorldTransform.PointToLocal(m_GroundTrace.EndPosition);
		
		return WorldTransform.PointToLocal(m_GroundTrace.EndPosition + up * WheelRadius);
	}
	
	
	
	private void UpdateForces()
	{
		if (!IsGrounded)
		{
			// Reset slip values when not grounded
			m_CurrentForwardSlip = 0.0f;
			m_CurrentSideSlip = 0.0f;
			TotalSlip = 0.0f;
			
			return;
		}
	    
	    Vector3 forwardDir = WorldRotation.Forward;
	    Vector3 sideDir = WorldRotation.Right;
	    Vector3 surfaceNormal = m_GroundTrace.Normal;
	    // Vector3 surfaceNormal = m_WantsToUse3DWheelColliders && m_BackupGroundTrace.Hit ? m_BackupGroundTrace.Normal : m_GroundTrace.Normal;
	    
	    Vector3 wheelVelocity = m_Rigidbody.GetVelocityAtPoint(WorldPosition);
	    float wheelSpeed = wheelVelocity.Length;

	    Vector3 surfaceForward = (forwardDir - Vector3.Dot(forwardDir, surfaceNormal) * surfaceNormal).Normal;
	    Vector3 surfaceSide = (sideDir - Vector3.Dot(sideDir, surfaceNormal) * surfaceNormal).Normal;

	    float surfaceAngle = 1.0f - Math.Abs(Vector3.Dot(surfaceNormal, Vector3.Up));
	    
	    float gripFactor = (1.0f - surfaceAngle.LerpInverse(MaxGripAngle, NoGripAngle)) * GripMultiplier;
	    gripFactor = Math.Max(0.0f, gripFactor);
	    gripFactor *= gripFactor;
	    
	    /*
	    if (gripFactor <= 0.001f)
	    {
		    TotalSlip = 2.0f; // Maximum slip on no-grip surfaces
		    
		    return;
	    }
		*/
	    
	    float sideSlip = CalculateSlip(wheelVelocity, surfaceSide, wheelSpeed);
	    float forwardSlip = CalculateSlip(wheelVelocity, surfaceForward, wheelSpeed);

	    // Store slip values for external access
	    m_CurrentForwardSlip = MathF.Abs(forwardSlip);
	    m_CurrentSideSlip = MathF.Abs(sideSlip);
	    
	    // Total slip combines both directions
	    TotalSlip = MathF.Sqrt(m_CurrentForwardSlip * m_CurrentForwardSlip + m_CurrentSideSlip * m_CurrentSideSlip);
	    TotalSlip += 1.0f / gripFactor;
	    TotalSlip -= 2.0f;
	    
	    Vector3 sideForce = CalculateFrictionForce(SideFriction, m_Rigidbody.Mass, sideSlip, surfaceSide);
	    Vector3 forwardForce = CalculateFrictionForce(ForwardFriction, m_Rigidbody.Mass, forwardSlip, surfaceForward);

	    // Apply braking force
	    if (m_BrakeTorque > 0.0f)
	    {
		    // Calculate the direction we need to brake in (opposite to forward velocity)
		    float forwardVelocity = Vector3.Dot(wheelVelocity, surfaceForward);
	        
		    // Brake force opposes the wheel's forward motion
		    float brakeForce = -MathF.Sign(forwardVelocity) * m_BrakeTorque * MaxBrakeForce * MaxBrakeForce * gripFactor;
	        
		    // Blend brake force smoothly based on speed to prevent jittering at low speeds
		    float speedBlendFactor = Math.Min(1.0f, wheelSpeed / LOW_SPEED_THRESHOLD);
		    brakeForce *= speedBlendFactor;
	        
		    forwardForce += surfaceForward * brakeForce;
		    
		    // Increase slip when braking hard
		    TotalSlip += m_BrakeTorque * 0.1f;
	    }
	    else if (!IsPowered)
	    {
		    // Only zero out forward force if not braking and not powered
		    forwardForce = Vector3.Zero;
	    }
	    
	    float factor = wheelSpeed.LerpInverse(0.0f, LOW_SPEED_THRESHOLD);
	    float groundFriction = m_GroundTrace.Surface.Friction;
	    
	    Vector3 targetAcceleration = (sideForce + forwardForce) * factor * groundFriction * gripFactor;

	    // Motor torque is only applied when not braking (or at reduced strength)
	    if (IsPowered && m_BrakeTorque < 0.5f)
	    {
		    // Reduce motor torque influence when light braking
		    float motorInfluence = 1.0f - (m_BrakeTorque * 2.0f);
		    targetAcceleration += m_MotorTorque * surfaceForward * gripFactor * motorInfluence;
	    }
	    
	    /*
	    if (m_PreviousSuspensionCompression < -20.0f && wheelSpeed > 500.0f)
	    {
		    float positiveSuspensionCompressionRate = -m_PreviousSuspensionCompression;
			
		    // Increase slip when suspensions are highly compressed and the speed is significant enough
		    TotalSlip += positiveSuspensionCompressionRate * 0.1f;
	    }
		*/
	    
	    TotalSlip = TotalSlip.Clamp(0.0f, 1.0f);

	    Vector3 force = targetAcceleration;

	    m_Rigidbody.ApplyForceAt(WorldPosition, force);
	}
	
	
	
	private void UpdateModelRender()
	{
		if (!m_ModelRenderer.IsValid())
			return;
		
		Vector3 center = GetCenterLocalPosition();
		
		m_ModelRenderer.LocalPosition = new Vector3(0.0f, 0.0f, center.z);
		
		Vector3 groundVel = m_Rigidbody.Velocity;
		Vector3 forward = WorldTransform.Forward;
		float forwardSpeed = Vector3.Dot(groundVel, forward);
		
		Rotation relativeRotation = Rotation.Identity;
		
		if (SteeringWheel.IsValid())
		{
			// Get the rotation of the steering wheel relative to its parent
			// This removes the parent's contribution
			Rotation parentWorldRotation = WorldRotation;
			Rotation steeringWorldRotation = SteeringWheel.WorldRotation;

			relativeRotation = parentWorldRotation.Inverse * steeringWorldRotation;
		}
		
		m_SpinAngle += forwardSpeed * Time.Delta * MathF.PI;

		if (Inverted)
		{
			m_ModelRenderer.LocalRotation = relativeRotation * Rotation.FromAxis(Vector3.Left, m_SpinAngle) * Rotation.FromYaw(180.0f);	
		}
		else
		{
			m_ModelRenderer.LocalRotation = relativeRotation * Rotation.FromAxis(Vector3.Left, m_SpinAngle);
		}
	}
	
	
	
	private void UpdateSteering()
	{
		if (!CanSteer)
			return;
		
		Vector3 groundVelocity = m_Rigidbody.Velocity.WithZ(0.0f);
		
		const float TERMINAL_VELOCITY = 500.0f;
		float ratio = TERMINAL_VELOCITY / (groundVelocity.Length + 0.001f);
		float inverseRatio = Math.Clamp(ratio, 0.0f, 1.0f);

		if (!IsGrounded)
			inverseRatio = 1.0f;
		
		Rotation steeringRotation = Rotation.FromYaw(inverseRatio * MaxSteeringAngle * SteeringInput);
		
		LocalRotation = Rotation.Lerp(LocalRotation, steeringRotation, Time.Delta * SteeringSmoothness);
	}
	
	
	
	private void UpdateSuspension()
	{
		if (!IsGrounded)
			return;
		
		// Both the 1D ray and the (lifted) 3D cylinder now measure the distance straight down from the wheel centre to
		// the surface, so they share one rest length: max droop + the wheel radius, minus the min suspension length.
		float suspensionTotalLength = (MaxSuspensionLength + WheelRadius) - MinSuspensionLength;

		float suspensionCompression = -float.Abs(m_GroundTrace.Distance - suspensionTotalLength);
		
		// By default we use vehicle transform up for the suspension calculation
		// Less precise but feel better at high speed (and avoid some jittering in the suspension bcs ground normal on the other side can change abruptly)
		Vector3 suspensionDir = m_Rigidbody.WorldRotation.Up;

		// At low speed we're using ground normal calculation for the suspension
		// It's more precise at low speed and avoid pushing the vehicle slighly forward
		if (m_Rigidbody.Velocity.Length < 100.0f)
		{
			suspensionDir = m_GroundTrace.Normal;
		}
		
		// Old methods
		// Vector3 worldVelocity = _rigidbody.GetVelocityAtPoint(WorldPosition);
		// float velocityAlongSuspension = worldVelocity.z;
		// float velocityAlongSuspension = Vector3.Dot(worldVelocity, suspensionDir);

		// New method
		float suspensionCompressionRate = (suspensionCompression - m_PreviousSuspensionCompression) / Time.Delta;
		m_PreviousSuspensionCompression = suspensionCompression;
		
		float dampingForce = -SuspensionDamping * suspensionCompressionRate * m_Rigidbody.Mass * 0.1f;
		float springForce = -SuspensionStiffness * suspensionCompression * m_Rigidbody.Mass * 0.1f;
		float totalForce = (dampingForce + springForce);

		Vector3 suspensionForce = suspensionDir * totalForce;

		m_Rigidbody.ApplyForceAt(WorldPosition, suspensionForce);
	}
	
	
	
	private void DoTrace()
	{
	    Vector3 down = m_Rigidbody.WorldRotation.Down;
	    Vector3 startPos = WorldPosition + WorldTransform.Up * ExtraWheelDetectionDistance;
	    
	    if (m_WantsToUse3DWheelColliders)
	    {
		    // More expansive but way more accurate (Used when we actually drive a vehicle)
	        Perform3DWheelTrace(startPos, down);
	    }
	    else
	    {
		    // Really basic 1D ray trace physics (used for non-owned/non-driven/ai traffic vehicles)
		    Perform1DWheelTrace(startPos, down);
	    }
	    
	    DrawDebugTraces();
	}
	
	
	
	private void Perform3DWheelTrace(Vector3 _StartPos, Vector3 _Down)
	{
		// Start the wheel-shaped cylinder a wheel-radius ABOVE the wheel centre, then sweep down through the whole
		// suspension range. Starting AT the centre (the old way) buried the cylinder's lower half in the ground as soon
		// as the suspension travel was shorter than the wheel radius — so on a small car, or over any bump, the cast
		// "started solid" and returned a useless zero-distance hit (or none). Lifting the start keeps the cast clean:
		// the first contact is the bottom of the wheel touching the ground, and the distance then measures straight
		// down from the centre to the surface — exactly like the 1D ray, so UpdateSuspension uses one length for both.
		Vector3 cylinderStartPos = _StartPos - _Down * WheelRadius;
		Vector3 cylinderEndPos = _StartPos + _Down * MaxSuspensionLength;

		SceneTrace cylinderTrace = CreateCylinderTrace(cylinderStartPos, cylinderEndPos);

		// Backup ray trace uses extended length when driven or grounded
		// (since this is a 1D ray trace the wheel radius need to be taken into account here)
		bool needsExtendedTrace = IsGrounded;
		float backupDistance = needsExtendedTrace ? MaxSuspensionLength + WheelRadius : MaxSuspensionLength;
		Vector3 backupEndPos = _StartPos + _Down * backupDistance;
		
		m_BackupGroundTrace = CreateRayTrace(_StartPos, backupEndPos).Run();

		// Start with backup trace result
		m_GroundTrace = cylinderTrace.Run();

		// Check all cylinder trace hits to find the closest valid one
		float closestValidDistance = float.MaxValue;

		foreach (var result in cylinderTrace.RunAll())
		{
			if (!result.Hit || result.Distance < MinSuspensionLength)
				continue;
    
			if (result.Distance < closestValidDistance)
			{
				closestValidDistance = result.Distance;
				m_GroundTrace = result;
			}
		}
	}
	
	
	
	private void Perform1DWheelTrace(Vector3 _StartPos, Vector3 _Down)
	{
	    Vector3 endPos = _StartPos + _Down * (MaxSuspensionLength + WheelRadius);
	    
	    m_GroundTrace = CreateRayTrace(_StartPos, endPos).Run();
	    
	    m_BackupGroundTrace = m_GroundTrace;
	}
	
	
	
	private SceneTrace CreateRayTrace(Vector3 _Start, Vector3 _End)
	{
	    return Scene.Trace
	        .Ray(_Start, _End)
	        .IgnoreGameObjectHierarchy(GameObject)
	        .WithoutTags("vehicle");
	}



	private SceneTrace CreateCylinderTrace(Vector3 _Start, Vector3 _End)
	{
	    return Scene.Trace
	        .Cylinder(WheelWidth, WheelRadius, _Start, _End)
	        .Rotated(WorldRotation * Rotation.FromRoll(90.0f))
	        .IgnoreGameObjectHierarchy(GameObject)
	        .WithoutTags("vehicle");
	}
	
	
	
	private void DrawDebugTraces()
	{
		if (!Game.IsPlaying)
			return;
		
		if (!RoadManager.Current.ShowOverlays)
			return;
		
		if (Scene.Camera.IsValid()
		    && Scene.Camera.WorldPosition.DistanceSquared(WorldPosition) > 1000000.0f
		    || !Scene.Camera.GetFrustum(Scene.Camera.ScreenRect).IsInside(WorldPosition))
			return;
		
		DebugOverlay.Trace(m_GroundTrace, overlay: true);
		DebugOverlay.Trace(m_BackupGroundTrace, overlay: true);
	}
	
	
	
	public float CalculateRPM()
	{
		Vector3 groundVelocity = m_Rigidbody.Velocity.WithZ(0.0f);
		
		float radiusMeters = WheelRadius * 0.0254f;
		float wheelCircumference = 2.0f * MathF.PI * radiusMeters;
		
		float wheelRevsPerSecond = groundVelocity.Length / wheelCircumference;
		
		return wheelRevsPerSecond * 60.0f;
	}
	
	
	
	public void ApplyMotorTorque(float _Value)
	{
		m_MotorTorque = _Value;
	}
	
	
	
	public void ApplyBrakeTorque(float _Value)
	{
		m_BrakeTorque = Math.Clamp(_Value, 0.0f, 1.0f);
	}
	
	
	
	public void ApplySteeringInput(float _Value)
	{
		SteeringInput = _Value;
	}
}
