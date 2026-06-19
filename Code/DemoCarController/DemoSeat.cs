using Sandbox;

namespace RedSnail.RoadTool;

[Icon("airline_seat_recline_normal")]
[Title("Demo - Seat")]
[Category("Demo")]
public sealed class DemoSeat : BaseChair
{
	public override Transform CalculateEyeTransform(PlayerController _Controller)
	{
		ClampEyes(_Controller);
		
		Transform eyeTransform = GetEyeTransform();
		
		return new Transform()
		{
			Position = eyeTransform.Position,
			Rotation = _Controller.EyeAngles.ToRotation()
		};
	}
}
