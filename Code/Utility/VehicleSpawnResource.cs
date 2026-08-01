using Sandbox;

namespace RedSnail.RoadTool;

[AssetType(Name = "Vehicle Spawn", Extension = "vspawn", Category = "Road Tool")]
public sealed class VehicleSpawnResource : GameResource
{
	[Property] public GameObject Prefab { get; set; }

	[Property, Range(0.0f, 10.0f)] public float Probability { get; set; } = 1.0f;
	
	
	
	protected override Bitmap CreateAssetTypeIcon(int _Width, int _Height)
	{
		return CreateSimpleAssetTypeIcon("car_rental", _Width, _Height, "#00ccff", "black");
	}
}
