using System.Linq;
using Sandbox;
using Sandbox.Movement;

namespace RedSnail.RoadTool;

/// <summary>
/// A vehicle driver seat built on s&amp;box's native <see cref="BaseChair"/> / sit system, so sitting, the seated
/// pose, the eye/camera transform and exiting (press use again while seated) all come for free.
///
/// On top of the native "aim exactly at the seat's collider and press use" entry — which is fiddly on a car with
/// several colliders — this adds a forgiving entry: press the use action while standing near the vehicle and
/// roughly facing it, and you'll be teleported into the seat. Assign this to <see cref="CarController.DriverSeat"/>.
/// </summary>
[Icon("airline_seat_recline_normal")]
public sealed class VehicleSeat : BaseChair
{
	/// <summary>Input action that enters the seat (same one the player uses to exit). Defaults to "use" (E).</summary>
	[Property, Group("Entry")] public string UseAction { get; set; } = "use";

	/// <summary>How close the player must be to the seat to enter.</summary>
	[Property, Group("Entry")] public float UseReach { get; set; } = 160.0f;



	private PlayerController m_Occupant;
	private float m_EntryCooldown;



	protected override void OnUpdate()
	{
		if (m_EntryCooldown > 0.0f)
			m_EntryCooldown -= Time.Delta;

		// Forget the occupant if it went away (destroyed, or exited some other way).
		if (m_Occupant is not null && !m_Occupant.IsValid())
			m_Occupant = null;

		// Adopt a player who got seated some other way (the native press, a save, etc.) so we can still eject them.
		if (!m_Occupant.IsValid() && m_EntryCooldown <= 0.0f)
		{
			PlayerController seated = GetOccupant();

			if (seated.IsValid())
				m_Occupant = seated;
		}

		// The same "use" action enters when standing near the car and exits when seated. We track the occupant
		// ourselves and eject them directly — exiting then works regardless of the player's move-mode setup.
		if (m_Occupant.IsValid())
			TryExit();
		else
			TryEnter();
	}



	private void TryExit()
	{
		if (string.IsNullOrEmpty(UseAction) || !Input.Pressed(UseAction))
			return;

		if (m_Occupant.IsProxy)
			return;

		PlayerController player = m_Occupant;
		m_Occupant = null;
		m_EntryCooldown = 0.4f; // so the same press that exits can't immediately re-enter

		// Unparent from the seat and undo what BaseChair.Sit did (it disabled the body + collider).
		player.GameObject.SetParent(null);
		player.Body.Enabled = true;
		player.ColliderObject.Enabled = true;

		if (player.Body.IsValid())
			player.Body.Velocity = Vector3.Zero;

		player.WorldPosition = FindBestExitPoint();
		player.GameObject.Transform.ClearInterpolation();
	}
	
	
	
	public override Transform CalculateEyeTransform( PlayerController controller )
	{
		ClampEyes(controller);
		
		Transform eyeTransform = GetEyeTransform();
		
		return new Transform()
		{
			Position = eyeTransform.Position,
			Rotation = controller.EyeAngles.ToRotation()
		};
	}
	
	
	
	// Seat the local player if they press the use action while near this seat and looking roughly toward it.
	private void TryEnter()
	{
		if (m_EntryCooldown > 0.0f)
			return;

		if (string.IsNullOrEmpty(UseAction) || !Input.Pressed(UseAction))
			return;

		PlayerController player = Scene.GetAll<PlayerController>().FirstOrDefault(p => p.IsValid() && !p.IsProxy);

		if (!player.IsValid())
			return;

		// Don't grab a player who's already sitting in something.
		if (player.GetComponentInParent<ISitTarget>() is not null)
			return;

		Vector3 seatPos = (SeatPosition ?? GameObject).WorldPosition;
		Vector3 toSeat = seatPos - player.EyeTransform.Position;

		if (toSeat.Length > UseReach)
			return;

		// Must be roughly facing the seat — so you can't enter a car that's behind you.
		if (Vector3.Dot(player.EyeTransform.Forward, toSeat.Normal) < 0.4f)
			return;

		// BaseChair.Sit parents the player to the seat (and the native SitMoveMode, if present, adds the pose/eyes).
		Sit(player);
		m_Occupant = player;
	}
}
