using System;
using System.Collections.Generic;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>How a traffic vehicle is behaving right now.</summary>
public enum AIState
{
	/// <summary>Following the rules — lanes, lights, gaps, yielding.</summary>
	Normal,

	/// <summary>Lost patience behind a player/prop: reverses and tries to barge around the obstacle for a while.</summary>
	RoadRage
}

/// <summary>
/// A single AI vehicle. It follows its current lane's waypoints, and when it reaches the end it picks a random
/// successor lane (or U-turns at a dead end) and smoothly connects onto it. Spawned and configured by <see cref="RoadManager"/>.
/// </summary>
public sealed class TrafficVehicle : Component
{
	public RoadTrafficGraph Graph { get; set; }
	
	/// <summary>Shared list of all live vehicles (owned by the manager) used for collision awareness.</summary>
	public List<TrafficVehicle> Neighbors { get; set; }
	
	[Property] public float HeightOffset { get; set; } = 0.0f;
	
	/// <summary>Only used for the 'DriveOnRails' approach</summary>
	[Property] public float TurnRate { get; set; } = 6.0f;

	/// <summary>Speed (s&amp;box units/s) used on connectors and dead-end U-turns — anything that isn't a road or intersection lane.</summary>
	[Property] public float DefaultSpeed { get; set; } = 200.0f;

	/// <summary>Desired following gap; the vehicle brakes for anything ahead within this distance.</summary>
	[Property] public float Spacing { get; set { field = value.Clamp(10.0f, 1000.0f); } } = 180.0f;

	/// <summary>Entity tags (players, NPCs, …) the vehicle brakes for when sensed directly ahead.</summary>
	[Property] public TagSet AwareTags { get; set; }

	/// <summary>Radius of the forward sweep used to sense tagged entities.</summary>
	[Property] public float DetectRadius { get; set; } = 50.0f;

	/// <summary>How far before an intersection's stop line the vehicle aims to halt for a red light / yield, so
	/// leftover braking momentum doesn't carry it over the line (and the crosswalk) into the junction.</summary>
	[Property] public float StopMargin { get; set; } = 150.0f;

	/// <summary>Seconds the driver will sit blocked by a tagged entity (player/prop) before losing patience and entering road rage.</summary>
	[Property] public float LoosePatience { get; set; } = 20.0f;

	/// <summary>Seconds road rage lasts once triggered — it stays aggressive this long even after the obstacle clears.</summary>
	[Property] public float RoadRageDuration { get; set; } = 20.0f;

	/// <summary>
	/// True while this brain is actually driving the car as an NPC: the component is active and no player has taken
	/// the wheel. This is the single authority for "AI controlled" — the vehicle's controller just mirrors it,
	/// and other vehicles read it to tell a managed NPC apart from a player's (stolen) car. An on-rails vehicle with
	/// no controller is AI-controlled whenever it's active.
	/// </summary>
	public bool IsAiControlled => Active && !m_PlayerTookOver;

	/// <summary>True while driving a road lane; false while crossing an intersection or connector.</summary>
	public bool IsOnRoadLane => m_Lane?.IsRoadLane ?? true;

	/// <summary>Current normalized travel direction — used by other vehicles to tell same-way from crossing traffic.</summary>
	public Vector3 Heading => m_Heading;

	/// <summary>The intersection this vehicle is currently crossing (or about to enter), else null.</summary>
	public RoadIntersectionComponent CurrentIntersection => (!IsOnRoadLane && m_Lane?.Owner is RoadIntersectionComponent i) ? i : null;

	/// <summary>True while crossing an intersection on a lane that turns left (and so must yield to oncoming traffic).</summary>
	public bool IsLeftTurningNow => CurrentIntersection is not null && IsLeftTurnLane(m_Lane);

	/// <summary>True if this vehicle is turning left through the junction, or has committed to do so on entry.</summary>
	public bool IsLeftTurnPlannedOrActive => IsLeftTurningNow || (m_PlannedNext is not null && IsLeftTurnLane(m_PlannedNext));

	/// <summary>Direction this vehicle entered its current lane heading — used to tell oncoming from crossing traffic.</summary>
	public Vector3 LaneEntryDir => m_Lane?.StartDir ?? m_Heading;

	/// <summary>False when effectively stopped (red light, queued). Lets others ignore us as a non-threat.</summary>
	public bool IsMoving => m_SpeedScale > 0.05f;

	/// <summary>The lane this vehicle is currently driving (road, intersection cross-lane, or return lane mid-U-turn).</summary>
	public TrafficLane ActiveLane => m_Lane;

	/// <summary>True while physically turning around on a dead-end U-turn arc (i.e. passing through the merge).</summary>
	public bool IsTurningAround => m_UsingUTurnArc && m_Target <= m_ConnectorEnd;

	/// <summary>The return lane this vehicle is about to U-turn onto while still approaching the dead end; else null.</summary>
	public TrafficLane PendingUTurnReturn =>
		(m_Lane?.UTurnArc is { Count: >= 2 } && m_Target > m_ConnectorEnd && m_Lane.Successors.Count > 0)
			? m_Lane.Successors[0]
			: null;

	/// <summary>Distance from the pivot to the back of the car body, along its forward axis (0 if it has no model).</summary>
	public float RearExtent => m_RearExtent;

	/// <summary>The intersection cross-lane we're driving (if in the box) or committed to (if approaching); else null.</summary>
	public TrafficLane CurrentOrPlannedCrossLane =>
		CurrentIntersection is not null ? m_Lane
		: (m_PlannedNext is { IsRoadLane: false } ? m_PlannedNext : null);

	private TrafficLane m_Lane;
	private List<Vector3> m_Path = new();
	private int m_Target;
	private int m_ConnectorEnd;
	private Vector3 m_Pos;
	private Vector3 m_Heading = Vector3.Forward;
	private float m_SpeedScale = 1.0f;
	private TrafficLane m_PlannedNext;
	private bool m_UsingUTurnArc;
	private int m_Id;
	private float m_FrontExtent;
	private float m_RearExtent;
	private RoadVehicleDriver m_Driver; // resolved control surface for this vehicle's controller (null ⇒ kinematic on-rails)
	private float m_LastSteer;          // last AI steer we pushed; reused to swing the entity look-ahead
	private bool m_PlayerTookOver;   // a player has driven this car → it's theirs now; the AI never reclaims it
	private AIState m_State = AIState.Normal;
	private float m_BlockedTime;     // seconds blocked by an entity (patience timer)
	private float m_RoadRageTime;    // seconds of road rage left
	private float m_EntityAhead = float.MaxValue; // cached distance to the nearest tagged entity this frame
	private float m_RageSwerve = 1.0f;            // which way (+1/-1) we lean while raging
	private bool m_RageReversing;
	private bool m_RageGiveUp;        // stopped trying to dodge a pushable entity — now barging over it
	private float m_RageBlockedTime;  // cumulative time thwarted by the entity this rage (→ give up and barge)
	private float m_StuckTime;        // time spent shoving forward but barely moving (→ reverse to get unstuck)
	private float m_ReverseTime;      // how long we've been reversing this attempt
	private bool m_RageGoAround;      // steering wide around an obstacle (latched while blocked + a short hold after)
	private float m_GoAroundHold;     // remaining hold time for the go-around after the obstacle clears
	private float m_RelocateCooldown; // throttles the nearest-road search while the car is displaced off its route
	private Random m_Rng;



	/// <summary>Places the vehicle on a lane at a given waypoint index and seeds its successor picking.</summary>
	public void Initialize(RoadTrafficGraph _Graph, TrafficLane _Lane, int _StartIndex, int _Seed)
	{
		Graph = _Graph;
		m_Rng = new Random(_Seed);
		m_Id = _Seed;

		SetPath(new List<Vector3>(_Lane.Waypoints), _Lane, 0);

		int startIndex = Math.Clamp(_StartIndex, 0, m_Path.Count - 1);
		m_Pos = m_Path[startIndex];
		m_Target = Math.Min(startIndex + 1, m_Path.Count - 1);
		m_Heading = (m_Path[m_Target] - m_Pos).Normal;

		if (m_Heading.IsNearZeroLength)
			m_Heading = m_Lane.StartDir;

		WorldPosition = m_Pos + Vector3.Up * HeightOffset;
		WorldRotation = Rotation.LookAt(m_Heading, Vector3.Up);

		ComputeForwardExtents();

		// If the spawned prefab has a drivable controller (resolved via the manager's hook), drive it through that;
		// otherwise (null) fall back to lightweight kinematic on-rails movement.
		m_Driver = RoadManager.GetVehicleDriver(GameObject);
	}



	// Measures how far the car body reaches ahead of and behind its pivot, along its own forward axis, from the
	// model bounds. Lets the following gap be bumper-to-bumper instead of pivot-to-pivot. Stays 0 with no model,
	// which falls back to the original pivot-based behaviour.
	private void ComputeForwardExtents()
	{
		m_FrontExtent = 0.0f;
		m_RearExtent = 0.0f;

		float minX = float.MaxValue;
		float maxX = float.MinValue;

		foreach (var renderer in GetComponentsInChildren<ModelRenderer>(true))
		{
			if (renderer.Model is null)
				continue;

			BBox bounds = renderer.Model.Bounds;

			// Project every corner of the model bounds into our local frame (forward = +X) so the result is
			// correct even if the renderer is an offset/rotated child of the vehicle root.
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = new Vector3(
					(i & 1) == 0 ? bounds.Mins.x : bounds.Maxs.x,
					(i & 2) == 0 ? bounds.Mins.y : bounds.Maxs.y,
					(i & 4) == 0 ? bounds.Mins.z : bounds.Maxs.z);

				Vector3 local = WorldTransform.PointToLocal(renderer.WorldTransform.PointToWorld(corner));
				minX = MathF.Min(minX, local.x);
				maxX = MathF.Max(maxX, local.x);
			}
		}

		if (maxX < minX)
			return; // no model renderer found — keep pivot-based behaviour

		m_FrontExtent = MathF.Max(0.0f, maxX);
		m_RearExtent = MathF.Max(0.0f, -minX);
	}



	private const float SteerFullLock = 0.5f;       // heading error (radians) that calls for full steering lock
	private const float ThrottleGain = 0.02f;       // throttle/brake per (unit/s) of speed error
	private const float StopSpeed = 20.0f;          // desired speed below this ⇒ come to a full stop
	private const float RageAvoidWindow = 5.0f;     // seconds spent thwarted by a (pushable) entity while raging before giving up dodging
	private const float RageStuckSpeed = 15.0f;     // below this while shoving forward ⇒ we're jammed on something immovable
	private const float RageStuckTime = 1.0f;       // …for this long ⇒ reverse and try another angle
	private const float RageReverseDuration = 1.2f;  // seconds to back up before trying forward again
	private const float RageAroundClearance = 260.0f; // how far to the side we aim when steering around an obstacle
	private const float MaxCorneringAccel = 250.0f; // lateral-grip budget (units/s²) → how fast we dare take a curve (raise = more daring)
	private const float BrakeDecel = 350.0f;        // assumed braking deceleration (units/s²) for the slow-in-time profile (lower = brakes earlier)
	private const float RelocateOffThreshold = 80.0f;  // altitude gap (units) from the current lane that means we've been knocked off the route (e.g. off a bridge) and should look for another road
	private const float RelocateAltitudeBand = 60.0f;  // when re-entering, roads within this height of the closest-altitude road count as "the same level" and compete on proximity
	private const float RelocateInterval = 0.25f;       // seconds between nearest-road searches while displaced (the search is the expensive bit)



	protected override void OnUpdate()
	{
		if (Graph is null || m_Path.Count < 2)
			return;

		// Once a player takes the wheel of this car, it's theirs for good — latch it so the AI never reclaims it.
		if (m_Driver is not null && (m_Driver.IsPlayerDriving?.Invoke() ?? false))
			m_PlayerTookOver = true;

		// With a drivable controller, feed it engine/brake/steer and let physics move it; otherwise fall back to the
		// lightweight on-rails movement (kinematic placeholder vehicles).
		if (m_Driver is not null)
		{
			// We own the "is the AI driving?" decision; mirror it onto the controller, then drive if it's ours.
			m_Driver.SetAiControlled?.Invoke(IsAiControlled);

			if (IsAiControlled)
				DriveCarController();
		}
		else
		{
			DriveOnRails();
		}
	}



	// Pushes the AI's per-frame inputs to the vehicle controller, remembering the steer for the look-ahead swing.
	private void PushDrive(float _Throttle, float _Steer, bool _Handbrake)
	{
		m_LastSteer = _Steer;
		m_Driver?.Drive?.Invoke(_Throttle, _Steer, _Handbrake);
	}



	// The vehicle body's velocity via the driver hook (zero if the controller doesn't expose one).
	private Vector3 BodyVelocity() => m_Driver?.Velocity?.Invoke() ?? Vector3.Zero;



	// Max steering angle (degrees) for the entity look-ahead swing; sensible fallback if the controller omits it.
	private float MaxSteer() => m_Driver?.MaxSteering?.Invoke() ?? 35.0f;



	private void DriveOnRails()
	{
		// Speed comes from the current segment: the road/intersection lane's limit, or DefaultSpeed while crossing
		// a connector or dead-end U-turn. ComputeSpeedScale then brakes for vehicles / tagged entities ahead.
		m_EntityAhead = NearestEntityAhead();
		float segmentSpeed = m_Target <= m_ConnectorEnd ? DefaultSpeed : m_Lane.SpeedLimit;
		m_SpeedScale = ComputeSpeedScale();
		float remaining = segmentSpeed * m_SpeedScale * Time.Delta;
		int guard = 0;

		// Advance along the polyline, consuming as many short segments as this frame's travel allows.
		while (remaining > 0.0f && guard++ < 256)
		{
			Vector3 toTarget = m_Path[m_Target] - m_Pos;
			float distance = toTarget.Length;

			if (distance <= remaining)
			{
				m_Pos = m_Path[m_Target];
				remaining -= distance;

				if (m_Target >= m_Path.Count - 1)
				{
					AdvanceToNextLane();

					if (m_Path.Count < 2)
						return;
				}
				else
				{
					m_Target++;
				}
			}
			else
			{
				Vector3 dir = toTarget / distance;
				m_Pos += dir * remaining;
				m_Heading = dir;
				remaining = 0.0f;
			}
		}

		// Keep facing the next waypoint even while crossing a long segment.
		Vector3 desired = m_Path[m_Target] - m_Pos;

		if (!desired.IsNearZeroLength)
			m_Heading = desired.Normal;

		WorldPosition = m_Pos + Vector3.Up * HeightOffset;

		if (!m_Heading.IsNearZeroLength)
			WorldRotation = Rotation.Slerp(WorldRotation, Rotation.LookAt(m_Heading, Vector3.Up), Time.Delta * TurnRate);
	}



	// Drives a physics CarController along the lane path: pure-pursuit steering toward the look-ahead waypoint, plus
	// a throttle/brake that chases the desired speed produced by the SAME rules the on-rails vehicle uses — so the
	// AI car still obeys every gap / intersection / light / conflict rule, it just gets there via engine and brakes.
	private void DriveCarController()
	{
		m_Pos = WorldPosition;

		Vector3 fwd = WorldRotation.Forward;

		if (!fwd.IsNearZeroLength)
			m_Heading = fwd.Normal;

		float planarSpeed = BodyVelocity().WithZ(0.0f).Length;

		// If we've been knocked off our route (e.g. rammed off a bridge), re-localize onto the nearest road AT OUR
		// HEIGHT rather than blindly steering toward the old waypoints — which, being a 2-D chase, would drive us to
		// the spot directly under the bridge. Throttled, since the search scans the whole lane network.
		m_RelocateCooldown -= Time.Delta;

		if (m_RelocateCooldown <= 0.0f && IsOffPath())
		{
			RelocateToNearestLane();
			m_RelocateCooldown = RelocateInterval;
		}

		AdvancePathProgress(planarSpeed);

		if (m_Path.Count < 2)
			return;

		// Patience / road-rage state machine, then the aggressive driver takes over for the whole rage.
		m_EntityAhead = NearestEntityAhead();
		UpdateAIState(planarSpeed);

		if (m_State == AIState.RoadRage)
		{
			DriveRoadRage();
			return;
		}

		float segmentSpeed = m_Target <= m_ConnectorEnd ? DefaultSpeed : m_Lane.SpeedLimit;
		m_SpeedScale = ComputeSpeedScale();
		float desiredSpeed = segmentSpeed * m_SpeedScale;

		// Self-preservation: never carry more speed into the upcoming curve than grip can hold (slowing in time to
		// make it), and back off further if we've already drifted off our lane so we recover the line.
		desiredSpeed = MathF.Min(desiredSpeed, CorneringSpeedLimit(planarSpeed));
		desiredSpeed *= LaneDeviationSpeedFactor();

		// Pure-pursuit steering toward the look-ahead waypoint.
		float steer = SteerToward(m_Path[m_Target]);

		// Throttle chases the desired speed; a negative value brakes (the CarController reads reverse-while-rolling-
		// forward as a brake). To STOP we brake only while still rolling, then cut throttle and hold with the
		// handbrake — pushing negative throttle once stopped would make the controller drive in reverse instead.
		float forwardSpeed = Vector3.Dot(BodyVelocity(), WorldRotation.Forward);
		bool wantStop = desiredSpeed < StopSpeed;

		float throttle;
		bool handbrake = false;

		if (wantStop)
		{
			if (forwardSpeed > 10.0f)
			{
				throttle = -1.0f;
			}
			else
			{
				throttle = 0.0f;
				handbrake = true;
			}
		}
		else
		{
			throttle = ((desiredSpeed - forwardSpeed) * ThrottleGain).Clamp(-1.0f, 1.0f);
		}
		
		PushDrive(throttle, steer, handbrake);
	}



	// Walks m_Target forward as the car physically reaches or passes each waypoint, rolling onto the next lane at
	// the end. The capture radius grows a little with speed so faster cars look slightly further ahead.
	private void AdvancePathProgress(float _Speed)
	{
		float capture = (_Speed * 0.3f).Clamp(100.0f, 220.0f);
		int guard = 0;

		while (guard++ < 64)
		{
			Vector3 toTarget = (m_Path[m_Target] - m_Pos).WithZ(0.0f);
			Vector3 segDir = (m_Target > 0 ? (m_Path[m_Target] - m_Path[m_Target - 1]) : m_Heading).WithZ(0.0f);

			bool passed = !segDir.IsNearZeroLength && Vector3.Dot(toTarget, segDir.Normal) < 0.0f;
			bool captured = toTarget.Length < capture;

			if (!passed && !captured)
				break;

			if (m_Target >= m_Path.Count - 1)
			{
				AdvanceToNextLane();

				if (m_Path.Count < 2)
					return;
			}
			else
			{
				m_Target++;
			}
		}
	}



	// True when the car has drifted far from its lane vertically — i.e. it's been knocked to a different level (off a
	// bridge, into a ditch) and is no longer on the road its waypoints describe. We compare the car's height to the
	// road surface height directly beneath it (interpolated along the current segment), so slopes don't count as "off".
	private bool IsOffPath()
	{
		if (m_Target <= 0 || m_Target >= m_Path.Count)
			return false;

		Vector3 a = m_Path[m_Target - 1];
		Vector3 b = m_Path[m_Target];
		Vector3 abH = (b - a).WithZ(0.0f);
		float lenSq = abH.LengthSquared;
		float t = lenSq > 0.01f ? (Vector3.Dot((WorldPosition - a).WithZ(0.0f), abH) / lenSq).Clamp(0.0f, 1.0f) : 0.0f;

		float surfaceZ = a.z + (b.z - a.z) * t; // road height at our position along the segment

		return MathF.Abs(WorldPosition.z - surfaceZ) > RelocateOffThreshold;
	}



	// Re-localizes the car onto a road it can actually reach from where it is now, used after it's been displaced off
	// its route. The road is chosen by ALTITUDE FIRST: we find the closest any road comes to our current height, then
	// among the roads at (roughly) that height we pick the nearest one horizontally. So a ground road under a bridge —
	// matching our height — always wins over the bridge deck overhead, which we couldn't drive up onto anyway.
	private void RelocateToNearestLane()
	{
		if (Graph is null)
			return;

		// Pass 1: the smallest height difference any road has to us — i.e. "ground level" from the car's point of view.
		float minHeightGap = float.MaxValue;

		foreach (var lane in Graph.Lanes)
		{
			if (!lane.IsRoadLane || lane.Waypoints.Count < 2)
				continue;

			foreach (var wp in lane.Waypoints)
				minHeightGap = MathF.Min(minHeightGap, MathF.Abs(wp.z - WorldPosition.z));
		}

		if (minHeightGap == float.MaxValue)
			return;

		// Pass 2: among only the roads near that height (everything higher/lower — like the bridge — is off-limits),
		// take the nearest waypoint horizontally.
		float heightCeiling = minHeightGap + RelocateAltitudeBand;
		TrafficLane bestLane = null;
		int bestIndex = 0;
		float bestDistSq = float.MaxValue;

		foreach (var lane in Graph.Lanes)
		{
			if (!lane.IsRoadLane || lane.Waypoints.Count < 2)
				continue;

			for (int i = 0; i < lane.Waypoints.Count; i++)
			{
				Vector3 wp = lane.Waypoints[i];

				if (MathF.Abs(wp.z - WorldPosition.z) > heightCeiling)
					continue;

				float distSq = (wp - WorldPosition).WithZ(0.0f).LengthSquared;

				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					bestLane = lane;
					bestIndex = i;
				}
			}
		}

		// Nothing to move to, or we're already on the best lane — leave the route untouched.
		if (bestLane is null || ReferenceEquals(bestLane, m_Lane))
			return;

		// Re-target onto that lane at the nearest waypoint (driving in the lane's direction) WITHOUT teleporting the
		// physics body — the car drives itself back onto the road. Drop any stale turn / rage state from the old route.
		SetPath(new List<Vector3>(bestLane.Waypoints), bestLane, 0);
		m_Target = Math.Min(bestIndex + 1, m_Path.Count - 1);
		m_PlannedNext = null;
		m_UsingUTurnArc = false;
		m_State = AIState.Normal;
		m_BlockedTime = 0.0f;
		m_RoadRageTime = 0.0f;
		m_RageGiveUp = false;
		m_RageReversing = false;
		m_RageGoAround = false;
	}



	// Steering input [-1, 1] that turns the car toward a world point (pure pursuit). Positive = left.
	private float SteerToward(Vector3 _WorldTarget)
	{
		Vector3 to = (_WorldTarget - m_Pos).WithZ(0.0f);

		if (to.IsNearZeroLength)
			return 0.0f;

		to = to.Normal;
		float crossZ = m_Heading.x * to.y - m_Heading.y * to.x;
		float dot = Vector3.Dot(m_Heading, to).Clamp(-1.0f, 1.0f);
		float angle = MathF.Atan2(crossZ, dot);                 // signed heading error (radians)

		return (angle / SteerFullLock).Clamp(-1.0f, 1.0f);
	}



	// True when the car is sitting in a road lane that feeds a RED traffic light — the one place a car is supposed to
	// wait, so it must never lose patience there (otherwise a whole queue behind a red light would rage). Everywhere
	// else, staying stuck eventually trips road rage.
	private bool IsWaitingAtRedLight()
	{
		if (m_Lane is null || !m_Lane.IsRoadLane || m_Target <= m_ConnectorEnd)
			return false;

		foreach (var successor in m_Lane.Successors)
		{
			if (successor.Owner is RoadIntersectionComponent intersection
				&& intersection.HasTrafficLights
				&& !intersection.IsApproachGreen(m_Lane.EndDir))
			{
				return true;
			}
		}

		return false;
	}



	// Patience → road-rage state machine. A driver that stays stuck (barely moving) loses patience and, past
	// LoosePatience, snaps into RoadRage for RoadRageDuration seconds — staying aggressive the whole time even after it
	// frees up — then reverts to Normal. The ONE place stopping is legitimate is queued behind a red light, so that
	// never counts as stuck; everything else (jammed in a junction, blocked by a player or a car) eventually rages.
	private void UpdateAIState(float _PlanarSpeed)
	{
		bool stuck = _PlanarSpeed < RageStuckSpeed && !IsWaitingAtRedLight(); // barely moving, and not waiting at a light

		if (m_State == AIState.Normal)
		{
			m_BlockedTime = stuck ? m_BlockedTime + Time.Delta : 0.0f;

			if (m_BlockedTime >= LoosePatience)
			{
				m_State = AIState.RoadRage;
				m_RoadRageTime = RoadRageDuration;
				m_BlockedTime = 0.0f;
				m_RageBlockedTime = 0.0f;
				m_StuckTime = 0.0f;
				m_ReverseTime = 0.0f;
				m_RageGiveUp = false;
				m_RageReversing = false;
				m_RageGoAround = false;
				m_RageSwerve = m_Rng.Next(2) == 0 ? 1.0f : -1.0f; // pick a side to lean toward
			}
		}
		else // RoadRage
		{
			// Stay enraged as long as we're still stuck on something. The cool-down only runs once we're moving freely
			// again; getting stuck again tops it straight back up — so the rage lasts until we're genuinely clear, then
			// fades RoadRageDuration seconds after recovering.
			if (stuck)
			{
				m_RoadRageTime = RoadRageDuration;
			}
			else
			{
				m_RoadRageTime -= Time.Delta;

				if (m_RoadRageTime <= 0.0f)
				{
					m_State = AIState.Normal;
					m_BlockedTime = 0.0f;
					m_RageGiveUp = false;
					m_RageReversing = false;
					m_RageGoAround = false;
				}
			}

			// While still raging, escalate to barging if a (pushable) entity keeps thwarting the dodge.
			if (m_State == AIState.RoadRage && !m_RageGiveUp)
			{
				if (m_EntityAhead < Spacing)
					m_RageBlockedTime += Time.Delta;

				if (m_RageBlockedTime >= RageAvoidWindow)
					m_RageGiveUp = true;
			}
		}
	}



	// The aggressive driver. It shoves forward, and when something is in the way it steers WIDE around it — aiming
	// at a point pushed RageAroundClearance out to one side so it commits to a real arc, not a token wiggle. If it
	// jams (shoving forward but barely moving — a wall / heavy prop) it backs up to make room and flips to the other
	// side, latching the go-around until it's moving freely again. WHAT it dodges depends on the phase:
	//   • Dodging (not given up): a tagged entity in front (avoid it) OR a physical jam.
	//   • Barging (given up): only a physical jam — so it rolls over anything it can push (the player) but still
	//     backs out and goes around anything it truly can't move.
	private void DriveRoadRage()
	{
		float forwardSpeed = Vector3.Dot(BodyVelocity(), WorldRotation.Forward);
		float planarSpeed = BodyVelocity().WithZ(0.0f).Length;

		bool entityBlocking = !m_RageGiveUp && m_EntityAhead < Spacing;

		// Jam = shoving forward but barely moving. (Only meaningful while we're actually going forward.)
		bool jammed = false;

		if (!m_RageReversing)
		{
			m_StuckTime = planarSpeed < RageStuckSpeed ? m_StuckTime + Time.Delta : 0.0f;
			jammed = m_StuckTime >= RageStuckTime;
		}

		// Latch "go around" when we hit something, and hold it briefly after it clears so the arc finishes before we
		// cut back to the path (otherwise we'd straighten too early and drive straight back into it).
		if (jammed || entityBlocking)
		{
			m_RageGoAround = true;
			m_GoAroundHold = 0.3f;
		}
		else if (m_RageGoAround)
		{
			m_GoAroundHold -= Time.Delta;

			if (m_GoAroundHold <= 0.0f)
				m_RageGoAround = false;
		}

		// On a fresh jam, reverse to make room and flip to probe the other side next.
		if (jammed && !m_RageReversing)
		{
			m_RageReversing = true;
			m_ReverseTime = 0.0f;
			m_StuckTime = 0.0f;
			m_RageSwerve = -m_RageSwerve;
		}

		if (m_RageReversing)
		{
			m_ReverseTime += Time.Delta;

			if (m_ReverseTime >= RageReverseDuration)
				m_RageReversing = false;

			PushDrive(-1.0f, 0.0f, false);   // back straight to open up room; the forward arc does the going-around
			m_SpeedScale = 0.0f;
			return;
		}

		// Forward: steer toward the path — but when avoiding/clearing something, push that target hard out to our
		// chosen side so we arc around it rather than straight into it.
		Vector3 target = m_Path[m_Target];

		if (m_RageGoAround)
		{
			Vector3 side = (Rotation.FromYaw(90.0f) * m_Heading).WithZ(0.0f);

			if (!side.IsNearZeroLength)
				target += side.Normal * (m_RageSwerve * RageAroundClearance);
		}

		PushDrive(((m_Lane.SpeedLimit - forwardSpeed) * ThrottleGain).Clamp(0.3f, 1.0f), SteerToward(target), false);
		m_SpeedScale = 1.0f;
	}



	// Highest speed we can safely be doing right now given the curvature coming up on the path. For each bend ahead
	// we work out a corner speed from the grip budget (v = √(grip·radius)) and the speed we could still be at and
	// brake down to it in time (v = √(corner² + 2·decel·distance)); the lowest of those is our cap. Straights don't
	// limit anything. The look-ahead is sized to our own braking distance so we always spot a bend early enough.
	private float CorneringSpeedLimit(float _Speed)
	{
		float lookahead = (_Speed * _Speed / (2.0f * BrakeDecel) + 100.0f).Clamp(150.0f, 2000.0f);
		float limit = float.MaxValue;
		float dist = 0.0f;

		Vector3 prev = m_Pos;

		for (int i = m_Target; i < m_Path.Count && dist < lookahead; i++)
		{
			Vector3 cur = m_Path[i];
			dist += Vector3.DistanceBetween(prev.WithZ(0.0f), cur.WithZ(0.0f));

			if (i + 1 < m_Path.Count)
			{
				Vector3 d1 = (cur - prev).WithZ(0.0f);
				Vector3 d2 = (m_Path[i + 1] - cur).WithZ(0.0f);

				if (!d1.IsNearZeroLength && !d2.IsNearZeroLength)
				{
					float angle = MathF.Acos(Vector3.Dot(d1.Normal, d2.Normal).Clamp(-1.0f, 1.0f));
					float arc = (d1.Length + d2.Length) * 0.5f;
					float curvature = arc > 1.0f ? angle / arc : 0.0f;

					if (curvature > 0.0001f)
					{
						float cornerSpeed = MathF.Sqrt(MaxCorneringAccel / curvature);
						float allowed = MathF.Sqrt(cornerSpeed * cornerSpeed + 2.0f * BrakeDecel * dist);
						limit = MathF.Min(limit, allowed);
					}
				}
			}

			prev = cur;
		}

		return limit;
	}



	// 1.0 when we're tracking our lane; ramps down (to 0.4) as the car drifts off the current path segment, so an
	// already-sliding car eases off to recover its line rather than carrying the mistake further.
	private float LaneDeviationSpeedFactor()
	{
		if (m_Target <= 0 || m_Target >= m_Path.Count)
			return 1.0f;

		Vector3 pa = m_Path[m_Target - 1].WithZ(0.0f);
		Vector3 pb = m_Path[m_Target].WithZ(0.0f);
		Vector3 ab = pb - pa;

		if (ab.IsNearZeroLength)
			return 1.0f;

		Vector3 ap = m_Pos.WithZ(0.0f) - pa;
		float t = (Vector3.Dot(ap, ab) / ab.LengthSquared).Clamp(0.0f, 1.0f);
		float deviation = (m_Pos.WithZ(0.0f) - (pa + ab * t)).Length;

		float laneWidth = m_Lane?.LaneWidth ?? 100.0f;
		float start = laneWidth;
		float full = laneWidth * 3.0f;

		if (deviation <= start)
			return 1.0f;

		float k = ((deviation - start) / MathF.Max(1.0f, full - start)).Clamp(0.0f, 1.0f);
		return float.Lerp(1.0f, 0.4f, k);
	}



	// Returns 1 (full speed) when the road ahead is clear, ramping down to 0 (stopped) as the nearest obstacle —
	// another vehicle in the same lane, tagged entity, or a red traffic light — closes within range.
	private float ComputeSpeedScale()
	{
		// Two brakes only: the gap to whatever's ahead on our own path, and the intersection-entry decision.
		// There is deliberately NO in-junction yielding — once a vehicle is engaged it always drives through,
		// so it can never stop dead in the middle. All conflicts are resolved at the entry line instead.
		float gapScale = ComputeGapScale(MathF.Min(NearestVehicleAhead(), m_EntityAhead));
		float scale = MathF.Min(gapScale, IntersectionApproachScale());
		return MathF.Min(scale, UTurnApproachScale());
	}



	private float ComputeGapScale(float _Nearest)
	{
		if (_Nearest == float.MaxValue)
			return 1.0f;

		float stopGap = Spacing * 0.45f;

		if (_Nearest <= stopGap)
			return 0.0f;

		return ((_Nearest - stopGap) / (Spacing - stopGap)).Clamp(0.0f, 1.0f);
	}



	// Decides whether to slow/stop before the upcoming intersection. Conflicts are resolved HERE (at the
	// entry), so that once a vehicle commits it can cross without stopping. A vehicle yields if:
	//   • the light is red for its approach (lit intersections), or
	//   • a closer vehicle is approaching from its right (priority-to-right, unlit intersections), or
	//   • a crossing vehicle is still inside the intersection (clear it before entering — also covers the
	//     tail end of the previous green phase at a lit intersection).
	private float IntersectionApproachScale()
	{
		// Skip while on a connector (just left an intersection) so we don't brake for the next one too early.
		if (!m_Lane.IsRoadLane || m_Lane.Successors.Count == 0 || m_Target <= m_ConnectorEnd)
			return 1.0f;

		RoadIntersectionComponent upcoming = null;

		foreach (var successor in m_Lane.Successors)
		{
			if (successor.Owner is RoadIntersectionComponent intr)
			{
				upcoming = intr;
				break;
			}
		}

		if (upcoming is null)
			return 1.0f;

		// A roundabout has no crossings — entry is purely a merge onto the ring, which the in-box right-of-way
		// rules handle as the vehicle reaches the merge point. So skip the crossing-intersection approach gate.
		if (upcoming.IsRoundabout)
			return 1.0f;

		// Stop this far short of the lane end (the junction edge) so momentum doesn't carry us over the line/crosswalk;
		// the braking ramp then runs over a fixed zone ending at that stop point.
		float stopDist = StopMargin;
		float approachDist = stopDist + Spacing * 3.5f;
		float distToEnd = DistanceToLaneEnd();

		if (distToEnd > approachDist)
			return 1.0f;

		// Commit to a route through the junction now and keep it stable, so we know our turn before entering.
		if (m_PlannedNext is null || !m_Lane.Successors.Contains(m_PlannedNext))
			m_PlannedNext = PickSuccessor(m_Lane);

		bool shouldYield = upcoming.HasTrafficLights
			? !upcoming.IsApproachGreen(m_Heading)
			: MustYieldToRight(upcoming, approachDist);

		// Near the line, also wait for the intersection to clear of crossing (perpendicular) traffic.
		if (!shouldYield && distToEnd < approachDist * 0.5f)
			shouldYield = IntersectionHasCrossingTraffic(upcoming);

		// Unprotected left turn: hold at the line until oncoming has a gap, then commit and complete the turn
		// in one motion. Resolving it here — not mid-junction — is what guarantees an engaged car never stops.
		if (!shouldYield && IsLeftTurnLane(m_PlannedNext))
			shouldYield = HasMovingOncoming(upcoming);

		if (!shouldYield)
			return 1.0f;

		if (distToEnd <= stopDist)
			return 0.0f;

		return ((distToEnd - stopDist) / (approachDist - stopDist)).Clamp(0.0f, 1.0f);
	}



	// Dead-end turn-around merge gate. Where two same-direction lanes both U-turn onto a single return lane, they
	// converge to one point — so they must take turns. Hold at the dead end until the merge is clear and no other
	// turner has priority; then commit and complete the arc in one motion (never stall halfway through it).
	private float UTurnApproachScale()
	{
		// Only while still approaching the dead end on a lane that turns around (not once committed to the arc).
		if (m_Lane is null || m_Lane.UTurnArc is not { Count: >= 2 } || m_Lane.Successors.Count == 0)
			return 1.0f;

		if (m_Target <= m_ConnectorEnd || Neighbors is null)
			return 1.0f;

		TrafficLane returnLane = m_Lane.Successors[0];
		Vector3 mergePoint = m_Lane.UTurnArc[m_Lane.UTurnArc.Count - 1];

		float approachDist = Spacing * 2.0f;
		float distToEnd = DistanceToLaneEnd();

		if (distToEnd > approachDist)
			return 1.0f;

		if (!UTurnMergeBusy(returnLane, mergePoint))
			return 1.0f;

		float stopDist = Spacing * 0.3f;

		if (distToEnd <= stopDist)
			return 0.0f;

		return ((distToEnd - stopDist) / (approachDist - stopDist)).Clamp(0.0f, 1.0f);
	}



	private bool UTurnMergeBusy(TrafficLane _ReturnLane, Vector3 _MergePoint)
	{
		float mergeRadius = MathF.Max(m_Lane.LaneWidth, m_Lane.RoadHalfWidth * 0.6f);
		float myDist = WorldPosition.Distance(_MergePoint);

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid())
				continue;

			float otherDist = other.WorldPosition.Distance(_MergePoint);

			// Someone already committed to turning around onto this return lane occupies the merge for the whole
			// arc — wait, no matter where on the arc they are, so we don't both converge on the same point.
			if (other.IsTurningAround && other.ActiveLane == _ReturnLane)
				return true;

			// …or has just merged and is still sitting near the start of the return lane.
			if (other.ActiveLane == _ReturnLane && otherDist < mergeRadius)
				return true;

			// Another car waiting to make the SAME U-turn that's nearer the merge goes first (ties break by id).
			// Strict ordering ⇒ two simultaneous arrivals never both yield, so they can't deadlock.
			if (other.PendingUTurnReturn == _ReturnLane)
			{
				if (otherDist < myDist - 1.0f || (MathF.Abs(otherDist - myDist) <= 1.0f && other.m_Id > m_Id))
					return true;
			}
		}

		return false;
	}



	// Priority-to-right: yield to a perpendicular vehicle approaching from our right that is closer to the
	// intersection than we are. The "closer wins" tie-break is a strict ordering, so two vehicles can never
	// each yield to the other — circular deadlocks are impossible.
	private bool MustYieldToRight(RoadIntersectionComponent _Intersection, float _Range)
	{
		if (Neighbors is null)
			return false;

		Vector3 rightDir = Rotation.LookAt(m_Heading, Vector3.Up).Right;
		float myDist = Vector3.DistanceBetween(WorldPosition, _Intersection.WorldPosition);

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid() || !other.IsOnRoadLane)
				continue;

			// Only crossing traffic counts — not parallel cars in a neighbouring lane.
			if (MathF.Abs(Vector3.Dot(other.Heading, m_Heading)) > 0.5f)
				continue;

			float otherDist = Vector3.DistanceBetween(other.WorldPosition, _Intersection.WorldPosition);

			if (otherDist > _Range || otherDist >= myDist)
				continue;

			Vector3 toOther = (other.WorldPosition - WorldPosition).Normal;

			if (Vector3.Dot(toOther, rightDir) > 0.5f)
				return true;
		}

		return false;
	}



	// True if genuine cross traffic is currently inside the intersection.
	//
	// The conflict is judged by the AXIS a car ENTERED on, never its instantaneous heading. A car turning
	// through the junction sweeps its heading across 90°, so heading-based tests wrongly flag same-road traffic
	// (e.g. a car that came off our own road and is now turning off it) as crossing. A car that entered on our
	// own axis shares the road with us and never crosses our path; only perpendicular-entry traffic does.
	// This also subsumes the left-turner case: an oncoming left-turner entered on our axis, so it's not flagged.
	private bool IntersectionHasCrossingTraffic(RoadIntersectionComponent _Intersection)
	{
		if (Neighbors is null)
			return false;

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid())
				continue;

			if (other.CurrentIntersection != _Intersection)
				continue;

			// Same entry axis (parallel or anti-parallel) → shares our road, never crosses. Perpendicular → cross.
			if (MathF.Abs(Vector3.Dot(other.LaneEntryDir, m_Heading)) < 0.5f)
				return true;
		}

		return false;
	}



	// True if oncoming traffic (same axis, opposite direction) is moving toward or through the junction within
	// turning range. A left-turner holds at the line while this is true, then completes its turn in one motion.
	// Cars stopped at a red light or queued (not moving) are no threat, and two opposing left-turns don't cross.
	private bool HasMovingOncoming(RoadIntersectionComponent _Intersection)
	{
		if (Neighbors is null)
			return false;

		Vector3 center = _Intersection.WorldPosition;
		float rangeSqr = (Spacing * 2.0f) * (Spacing * 2.0f);

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid())
				continue;

			// Oncoming = heading roughly opposite to our approach direction.
			if (Vector3.Dot(other.Heading, m_Heading) > -0.5f)
				continue;

			// Two opposing left turns don't cross — ignore oncoming that is itself turning left.
			if (other.IsLeftTurnPlannedOrActive)
				continue;

			// Stopped at a red light or queued — not a threat, we can turn in front of it.
			if (!other.IsMoving)
				continue;

			bool inside = other.CurrentIntersection == _Intersection;
			bool approaching = Vector3.Dot(center - other.WorldPosition, other.Heading) > 0.0f
				&& other.WorldPosition.DistanceSquared(center) < rangeSqr;

			if (inside || approaching)
				return true;
		}

		return false;
	}



	// A cross-lane is a left turn when its heading rotates left (counter-clockwise) from entry to exit.
	// s&box is right-handed Z-up, so a left turn gives a positive up-component on the heading cross product.
	private static bool IsLeftTurnLane(TrafficLane _Lane)
	{
		if (_Lane is null || _Lane.IsRoadLane)
			return false;

		Vector3 a = _Lane.StartDir;
		Vector3 b = _Lane.EndDir;

		if (Vector3.Dot(a, b) > 0.7f)
			return false; // going straight, not a turn

		return Vector3.Cross(a, b).Dot(Vector3.Up) > 0.0f;
	}



	// Remaining path length from the current position through all remaining waypoints.
	private float DistanceToLaneEnd()
	{
		if (m_Path.Count < 2 || m_Target >= m_Path.Count)
			return 0.0f;

		float dist = Vector3.DistanceBetween(m_Pos, m_Path[m_Target]);

		for (int i = m_Target; i < m_Path.Count - 1; i++)
			dist += Vector3.DistanceBetween(m_Path[i], m_Path[i + 1]);

		return dist;
	}



	// Nearest vehicle ahead that we must brake for. A car is "ahead" when it sits in a tight corridor in front
	// of us (so cars in other lanes are ignored). For a car going the same way it's normal following. For a
	// crossing/opposing car it's a conflict, and only the lower-priority side yields (ShouldYieldTo): priority
	// goes to whoever is deeper into the junction, who is therefore always *ahead* — so the yielder (behind) is
	// the only one that brakes, the priority car drives clear, and the two can neither freeze nor phase through.
	private float NearestVehicleAhead()
	{
		if (Neighbors is null)
			return float.MaxValue;

		float laneWidth = m_Lane?.LaneWidth ?? 100.0f;
		float lateralLimit = MathF.Min(Spacing * 0.45f, laneWidth * 0.6f);
		float nearest = float.MaxValue;

		foreach (var other in Neighbors)
		{
			if (other == this || !other.IsValid())
				continue;

			Vector3 toOther = other.WorldPosition - WorldPosition;
			float forward = Vector3.Dot(toOther, m_Heading);

			if (forward <= 0.0f)
				continue;

			float lateral = (toOther - m_Heading * forward).Length;

			if (lateral > lateralLimit)
				continue;

			// Real bumper-to-bumper gap: pivot distance minus our front overhang and the other car's rear
			// overhang, so a long car is kept clear instead of being driven through. Extents are 0 with no model.
			float gap = MathF.Max(0.0f, forward - m_FrontExtent - other.RearExtent);

			if (gap > Spacing)
				continue;

			// Following the leader on our path → always brake. That's the leader on our exact lane, or one that's
			// already moved onto the lane we're about to enter (our successor) — e.g. the next arc of a roundabout
			// ring, so following continues seamlessly across lane boundaries. A different lane that ISN'T our
			// successor is a conflict (crossing OR merging): brake only when we must yield.
			bool following = ReferenceEquals(other.ActiveLane, m_Lane)
				|| (m_Lane is not null && m_Lane.Successors.Contains(other.ActiveLane));

			if (!following && !ShouldYieldTo(other))
				continue;

			if (gap < nearest)
				nearest = gap;
		}

		return nearest;
	}



	// Deterministic right-of-way between two vehicles on conflicting paths: exactly one yields, so they never
	// deadlock. Both rules pick the car that reaches the conflict first — which is always the one *ahead* — so
	// the yielder is always behind it and can actually stop (never a freeze, never a clip).
	private bool ShouldYieldTo(TrafficVehicle _Other)
	{
		TrafficLane mine = CurrentOrPlannedCrossLane;
		TrafficLane theirs = _Other.CurrentOrPlannedCrossLane;

		if (mine is not null && theirs is not null && !ReferenceEquals(mine, theirs))
		{
			// Merging onto a shared exit (e.g. a right turn and a left turn both feeding the same lane): whoever
			// is nearer that exit takes it first. The shorter movement reaches it first, as expected.
			Vector3 exit = mine.EndPos;

			if (exit.DistanceSquared(theirs.EndPos) < 3600.0f) // ~60u apart → same exit lane
			{
				float myToExit = WorldPosition.DistanceSquared(exit);
				float otherToExit = _Other.WorldPosition.DistanceSquared(exit);

				if (MathF.Abs(myToExit - otherToExit) > 1.0f)
					return otherToExit < myToExit;
			}
			// Crossing paths (e.g. a left turn cutting across oncoming straight traffic): whoever is nearer the
			// actual point where the two paths cross goes first; the other, being behind it, yields and can stop.
			else if (mine.Conflicts.TryGetValue(theirs, out Vector3 crossing))
			{
				float myToCross = WorldPosition.DistanceSquared(crossing);
				float otherToCross = _Other.WorldPosition.DistanceSquared(crossing);

				if (MathF.Abs(myToCross - otherToCross) > 1.0f)
					return otherToCross < myToCross;
			}
		}

		// Fallback: whoever is deeper into the junction (nearer its centre) clears first.
		RoadIntersectionComponent junction = CurrentIntersection ?? _Other.CurrentIntersection;

		if (junction is not null)
		{
			float myDist = WorldPosition.DistanceSquared(junction.WorldPosition);
			float otherDist = _Other.WorldPosition.DistanceSquared(junction.WorldPosition);

			if (MathF.Abs(myDist - otherDist) > 1.0f)
				return otherDist < myDist;
		}

		return _Other.m_Id > m_Id;
	}



	// Nearest UNMANAGED tagged entity ahead, via a forward sphere sweep filtered by the manager's tag set (so the flat
	// road/sidewalk meshes are never hit). Other AI traffic cars are deliberately skipped here — car-to-car is owned by
	// the deterministic right-of-way system instead (see the hit handling below). What's left is players, their cars and props.
	private float NearestEntityAhead()
	{
		if (AwareTags == null || AwareTags.IsEmpty)
			return float.MaxValue;
		
		// Swing the look direction toward where we're steering, so the sweep covers the turn we're taking.
		Vector3 lookDir = m_Heading;

		if (m_Driver is not null && IsAiControlled)
			lookDir = Rotation.FromYaw(m_LastSteer * MaxSteer()) * lookDir;
		
		Vector3 from = WorldPosition + m_Heading * m_FrontExtent; // start at the front bumper
		Vector3 to = from + lookDir * Spacing * 0.5f;

		SceneTraceResult trace = Scene.Trace
			.Sphere(m_FrontExtent * 0.5f, from, to)
			.WithAnyTags(AwareTags)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();

		if (Game.IsPlaying && RoadManager.Current.ShowOverlays)
			DebugOverlay.Trace(trace);

		if (!trace.Hit)
			return float.MaxValue;

		// Another AI-DRIVEN traffic car? Ignore it here. Car-to-car right-of-way is owned by the deterministic system
		// (NearestVehicleAhead + ShouldYieldTo): it gives exactly one of any two conflicting cars priority and never
		// stalls them mid-junction. If this symmetric sphere ALSO braked for them, two cars whose spheres overlap at an
		// angle would each read the other as a wall and both freeze — that's the intersection deadlock. We check the
		// brain's IsAiControlled, NOT just its presence, so a player who STOLE a traffic car (brain dormant) is still
		// seen as the obstacle it now is. The sphere's job is everything the deterministic system can't see: players,
		// their cars (stolen or own) and props.
		var managed = trace.GameObject.GetComponentInParent<TrafficVehicle>();

		if (managed.IsValid() && managed.IsAiControlled)
			return float.MaxValue;

		return trace.Distance;
	}



	private void AdvanceToNextLane()
	{
		// Use the route we committed to on approach (so we take the turn we yielded for); otherwise pick now.
		TrafficLane next = (m_PlannedNext is not null && m_Lane.Successors.Contains(m_PlannedNext))
			? m_PlannedNext
			: PickSuccessor(m_Lane);

		m_PlannedNext = null;

		if (next is null || next.Waypoints.Count < 2)
		{
			// No way forward at all — reverse the current lane so the vehicle never gets stuck.
			next = MakeReversed(m_Lane);
		}

		List<Vector3> connector;
		bool usingUTurnArc = m_Lane.UTurnArc is { Count: >= 2 };

		if (usingUTurnArc)
		{
			// Dead-end turn-around: follow the contained on-road arc instead of looping past the road end.
			connector = m_Lane.UTurnArc;
		}
		else
		{
			// Bridge the gap between where we are now and the start of the next lane (smooths the offset
			// mismatch where a road lane meets an intersection cross-lane).
			Vector3 nextDir = (next.Waypoints[1] - next.Waypoints[0]).Normal;
			connector = TrafficMath.BuildConnector(m_Pos, m_Heading, next.Waypoints[0], nextDir);
		}

		var path = new List<Vector3>(connector.Count + next.Waypoints.Count);
		path.AddRange(connector);

		// connector ends at next.Waypoints[0], so skip the duplicate.
		for (int i = 1; i < next.Waypoints.Count; i++)
			path.Add(next.Waypoints[i]);

		// Segments up to the connector's last point run at DefaultSpeed; the rest run at next.SpeedLimit.
		SetPath(path, next, connector.Count - 1);

		// Remember whether we're mid turn-around so others can tell we're occupying the merge.
		m_UsingUTurnArc = usingUTurnArc;
	}



	private TrafficLane PickSuccessor(TrafficLane _Lane)
	{
		var successors = _Lane.Successors;

		if (successors.Count == 0)
			return null;

		return successors[m_Rng.Next(successors.Count)];
	}



	// Emergency only: a one-way lane that dead-ends with no opposing lane to turn onto. Drive back along itself.
	private static TrafficLane MakeReversed(TrafficLane _Lane)
	{
		var reversed = new TrafficLane
		{
			Owner = _Lane.Owner,
			IsRoadLane = _Lane.IsRoadLane,
			LaneWidth = _Lane.LaneWidth,
			RoadHalfWidth = _Lane.RoadHalfWidth,
			SpeedLimit = _Lane.SpeedLimit
		};

		for (int i = _Lane.Waypoints.Count - 1; i >= 0; i--)
			reversed.Waypoints.Add(_Lane.Waypoints[i]);

		reversed.Successors.Add(_Lane);
		return reversed;
	}



	private void SetPath(List<Vector3> _Path, TrafficLane _Lane, int _ConnectorEnd)
	{
		m_Path = _Path;
		m_Lane = _Lane;
		m_ConnectorEnd = _ConnectorEnd;
		m_Target = 1;
	}
}
