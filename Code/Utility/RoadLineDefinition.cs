using Sandbox;

namespace RedSnail.RoadTool;

[AssetType(Name = "Road Line Definition", Extension = "roadline", Category = "Road Tool")]
public sealed class RoadLineDefinition : GameResource
{
	public Material Material { get; set; }

	public float DashSpacing { get; set { field = value.Clamp(0.0f, 10000.0f); } }

	[Range(0.0f, 1.0f)]
	public float DashFillRatio { get; set { field = value.Clamp(0.0f, 1.0f); } } = 1.0f;



	protected override Bitmap CreateAssetTypeIcon(int _Width, int _Height)
	{
		return CreateSimpleAssetTypeIcon("line_axis", _Width, _Height, "#00ccff", "black");
	}
}
