using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace RedSnail.RoadTool;

[Icon("local_parking")]
[EditorHandle(Icon = "local_parking")]
public sealed class ParkingSystem : Component
{
	private TimeSince m_LastSpawnAttempt;
	private const float EXECUTION_INTERVAL = 0.25f;
	
	private float m_Timer = float.MaxValue;
	private bool m_CanRespawn = true;
	private readonly List<GameObject> m_SpawnedVehicles = [];
	
	private TimeSince m_DespawnTimer;
	private const float DESPAWN_DELAY = 1.0f;
	
	/// <summary>
	/// The vehicles parking list we want to use
	/// </summary>
	[Property] private VehicleSetResource Vehicles { get; set; }
	
	/// <summary>
	/// This is useful especially for a car parking, it allows cars to sometimes be oriented in the opposite direction when parked
	/// </summary>
	[Property] private bool RandomOppositeYaw { get; set; } = false;
	
	/// <summary>
	/// This is the defined limit of the parking spot, if there is something inside this limit (any type of colliders) it will prevent the vehicle from spawning
	/// </summary>
	[Property] private BBox SpawnArea { get; set; } = BBox.FromPositionAndSize(Vector3.Zero, Vector3.One * 10);
	
	/// <summary>
	/// As the name imply, this is the minimum time for a vehicle to respawn
	/// </summary>
	[Property] private int RespawnMinTime { get; set; } = 60;
	
	/// <summary>
	/// And this one is the maximum value
	/// </summary>
	[Property] private int RespawnMaxTime { get; set; } = 120;
	
	/// <summary>
	/// Does the vehicle spawn after this component get enabled ?
	/// </summary>
	[Property] private bool SpawnOnEnable { get; set; } = true;
	
	/// <summary>
	/// This determines if this parking slot will be able to spawn a vehicle or not (This is defined once when this component get enabled)
	/// </summary>
	[Property, Range(0, 100)] private int SpawnChance { get; set; } = 100;
	
	
	
	protected override void OnEnabled()
	{
		if (IsProxy || Vehicles?.Prefabs == null || Vehicles.Prefabs.Length == 0)
			return;

		int chance = Random.Shared.Next(0, 100);
		
		m_CanRespawn = chance < SpawnChance;

		if (SpawnOnEnable)
		{
			if (!m_CanRespawn)
				return;

			m_Timer = 0.0f;
			
			TrySpawnVehicle();	
		}
		else
		{
			m_Timer = Random.Shared.Next(RespawnMinTime, RespawnMaxTime + 1);
		}
	}
	
	
	
	protected override void OnDisabled()
	{
		if (IsProxy)
			return;

		var toDelete = new List<GameObject>();
		toDelete.AddRange(m_SpawnedVehicles);

		foreach (var vehicle in toDelete)
		{
			m_SpawnedVehicles.Remove(vehicle);
			
			var entityFade = vehicle.GetComponent<EntityFade>();

			if (entityFade.IsValid())
			{
				entityFade.FadeOutAndDestroyBroadcasted();
			}
			else
			{
				vehicle.Destroy();
			}
		}
	}
	
	
	
	protected override void DrawGizmos()
	{
		if (Gizmo.CameraTransform.Position.Distance(WorldPosition) > Gizmo.Settings.GizmoRenderDistance)
			return;
		
		if (!Gizmo.IsSelected)
		{
			Gizmo.Draw.LineBBox(SpawnArea);
			
			return;
		}
		
		Gizmo.Draw.Color = Color.Cyan;
		Gizmo.Draw.LineBBox(SpawnArea);
		Gizmo.Draw.Arrow(new Vector3(0, SpawnArea.Maxs.y, SpawnArea.Center.z), new Vector3(0, SpawnArea.Maxs.y + 100.0f, SpawnArea.Center.z));
	}
	
	
	
	protected override void OnUpdate()
	{
		if (IsProxy || !m_CanRespawn)
			return;
		
		UpdateDespawnLogic();
		
		// We limit the execution of this function, bcs executing this check every frame would be completely overkill !
		if (m_LastSpawnAttempt >= EXECUTION_INTERVAL)
		{
			TrySpawnVehicle();
			
			m_LastSpawnAttempt = 0;
		}
	}
	
	
	
	private bool IsSpawnAreaBlocked()
	{
		SceneTraceResult result = Scene.Trace.Box(SpawnArea, WorldPosition, WorldPosition).Rotated(WorldRotation).WithAnyTags("player", "vehicle", "prop").Run();
		
		return result.Hit;
	}
	
	
	
	private void TrySpawnVehicle()
	{
		bool canSafelyRespawn = !IsSpawnAreaBlocked();
		
		// Debug overlay
		if (RoadManager.Current.ShowOverlays)
		{
			Color debugColor = canSafelyRespawn ? Color.Green : Color.Blue;
			
			DebugOverlay.Box(SpawnArea, debugColor, duration: EXECUTION_INTERVAL, transform: WorldTransform);
		}
		
		if (canSafelyRespawn)
		{
			// We engage an almost instant respawn when all players were previously far away from the parking spot,
			// this allows for a fast respawn in this specific case
			if (ArePlayersWithinParkingSpot(RoadManager.Current.ParkedVehicleRespawnDistance))
			{
				m_Timer -= EXECUTION_INTERVAL;
			}
			else
			{
				m_Timer = EXECUTION_INTERVAL;	
			}

			if (m_Timer <= 0.0f)
			{
				SpawnVehicle();
			}
		}
	}
	
	
	
	private void UpdateDespawnLogic()
	{
		if (m_DespawnTimer > DESPAWN_DELAY)
		{
			var toDelete = new List<GameObject>();
			
			foreach (var vehicle in m_SpawnedVehicles)
			{
				if (ArePlayersWithinParkingSpot(RoadManager.Current.ParkedVehicleDespawnDistance))
					continue;
			
				if (vehicle.Tags.Has("last_vehicle"))
					continue;

				toDelete.Add(vehicle);
			}

			foreach (var vehicle in toDelete)
			{
				m_SpawnedVehicles.Remove(vehicle);
				
				var entityFade = vehicle.GetComponent<EntityFade>();

				if (entityFade.IsValid())
				{
					entityFade.FadeOutAndDestroyBroadcasted();
				}
				else
				{
					vehicle.Destroy();
				}
			}
				
			m_DespawnTimer = 0;
		}
	}
	
	
	
	private bool ArePlayersWithinParkingSpot(float _Distance)
	{
		return RoadManager.ArePlayersWithin(WorldPosition, _Distance);
	}
	
	
	
	private void SpawnVehicle()
	{
		Angles angles = CalculateSpawnAngles();
		GameObject vehiclePrefab = GetRandomVehiclePrefab();
		
		GameObject vehicle = vehiclePrefab.Clone(WorldPosition, angles);
		vehicle.NetworkSpawn(Connection.Host);
		vehicle.Network.SetOrphanedMode(NetworkOrphaned.Host);
		vehicle.Network.SetOwnerTransfer(OwnerTransfer.Request);
		
		var renderer = vehicle.GetComponent<ModelRenderer>();
		
		if (renderer.IsValid())
		{
			// Give it a cool random tint
			{
				renderer.Tint = new Color(Game.Random.NextSingle(), Game.Random.NextSingle(), Game.Random.NextSingle(), renderer.Tint.a);
			
				Network.Refresh(renderer);
			}

			// Properly place the vehicle on the ground
			{
				// Bottom of the vehicle in world space
				float bottomZ = renderer.Bounds.Mins.z;
				
				// Target ground height
				float groundZ = SpawnArea.Transform(WorldTransform).Mins.z;

				// Offset required to place bottom on ground
				float offsetZ = groundZ - bottomZ;
				
				vehicle.WorldPosition += Vector3.Up * offsetZ;
			}
		}
		
		m_SpawnedVehicles.Add(vehicle);
		m_Timer = Random.Shared.Next(RespawnMinTime, RespawnMaxTime + 1);
	}
	
	
	
	private Angles CalculateSpawnAngles()
	{
		Angles angles = WorldRotation.Angles() * Rotation.FromYaw(90.0f);

		if (RandomOppositeYaw && Random.Shared.Next(0, 2) == 0)
		{
			angles.yaw -= 180.0f;
		}
		
		return angles;
	}
	
	
	
	private GameObject GetRandomVehiclePrefab()
	{
		int index = Random.Shared.Next(0, Vehicles.Prefabs.Length);
		
		return Vehicles.Prefabs[index];
	}
}
