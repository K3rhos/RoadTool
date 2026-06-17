using Sandbox;

namespace RedSnail.RoadTool;

public struct WheelFrictionInfo
{
	public float ExtremumSlip { get; set; }
	public float ExtremumValue { get; set; }
	public float AsymptoteSlip { get; set; }
	public float AsymptoteValue { get; set; }
	public float Stiffness { get; set; }

	public WheelFrictionInfo()
	{
		ExtremumSlip = 1.0f;
		ExtremumValue = 300.0f;
		AsymptoteSlip = 2.0f;
		AsymptoteValue = 150.0f;
		Stiffness = 1.0f;
	}

	public float Evaluate(float _Slip, float _Mass)
	{
		float value;
		
		float extremumValue = ExtremumValue * _Mass;
		float asymptoteValue = AsymptoteValue * _Mass;

		if (_Slip <= ExtremumSlip)
		{
			value = (_Slip / ExtremumSlip) * extremumValue;
		}
		else
		{
			value = extremumValue - ((_Slip - ExtremumSlip) / (AsymptoteSlip - ExtremumSlip)) * (extremumValue - asymptoteValue);
		}

		return (value * Stiffness).Clamp(0.0f, float.MaxValue);
	}
}
