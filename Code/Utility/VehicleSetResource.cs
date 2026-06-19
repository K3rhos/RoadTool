using Sandbox;

namespace RedSnail.RoadTool;

[AssetType(Name = "Vehicle Set", Extension = "vset", Category = "Road Tool")]
public sealed class VehicleSetResource : GameResource
{
	public GameObject[] Prefabs { get; set; }

	protected override Bitmap CreateAssetTypeIcon(int _Width, int _Height)
	{
		return CreateSimpleAssetTypeIcon("directions_car", _Width, _Height, "#00ccff", "black");
	}
}
