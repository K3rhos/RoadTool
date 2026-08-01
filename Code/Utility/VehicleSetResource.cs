using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

/// <summary>
/// A pool of vehicles to spawn for the traffic on a road/parking system.
/// </summary>
[AssetType(Name = "Vehicle Set", Extension = "vset", Category = "Road Tool")]
public sealed class VehicleSetResource : GameResource
{
	[Property] public List<VehicleSpawnResource> Vehicles { get; set; } = new();
	
	public bool HasVehicles => Vehicles is not null && Vehicles.Any(IsSpawnable);



	/// <summary>
	/// A prefab from the set, chosen by weight. Null when the set is empty, unassigned, or every entry sits at
	/// probability 0.
	///
	/// Sum the weights, roll a point in that range, walk until it's covered. Weights
	/// are relative and needn't total anything in particular, so a set stays balanced when a car is added or
	/// removed, which a percentage-based list wouldn't.
	/// </summary>
	public GameObject PickRandomPrefab()
	{
		if (Vehicles is null)
			return null;

		float total = 0.0f;

		foreach (VehicleSpawnResource entry in Vehicles)
		{
			if (IsSpawnable(entry))
				total += entry.Probability;
		}

		if (total <= 0.0f)
			return null;

		float roll = Game.Random.Float(0.0f, total);

		foreach (VehicleSpawnResource entry in Vehicles)
		{
			if (!IsSpawnable(entry))
				continue;

			roll -= entry.Probability;

			if (roll <= 0.0f)
				return entry.Prefab;
		}
		
		return Vehicles.LastOrDefault(IsSpawnable)?.Prefab;
	}



	private static bool IsSpawnable(VehicleSpawnResource _Entry)
	{
		return _Entry is not null && _Entry.Prefab.IsValid() && _Entry.Probability > 0.0f;
	}



	protected override Bitmap CreateAssetTypeIcon(int _Width, int _Height)
	{
		return CreateSimpleAssetTypeIcon("directions_car", _Width, _Height, "#00ccff", "black");
	}
}
