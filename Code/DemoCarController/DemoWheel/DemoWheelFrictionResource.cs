using Sandbox;

namespace RedSnail.RoadTool;

[AssetType(Name = "Demo - Wheel Friction", Extension = "dwhf", Category = "Road Tool")]
public sealed class DemoWheelFrictionResource : GameResource
{
	public float ExtremumSlip { get; set; } = 1.0f;
	public float ExtremumValue { get; set; } = 300.0f;
	public float AsymptoteSlip { get; set; } = 2.0f;
	public float AsymptoteValue { get; set; } = 150.0f;
	public float Stiffness { get; set; } = 1.0f;

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
	
	protected override Bitmap CreateAssetTypeIcon(int _Width, int _Height)
	{
		return CreateSimpleAssetTypeIcon("gesture", _Width, _Height, "#00ccff", "black");
	}
}
