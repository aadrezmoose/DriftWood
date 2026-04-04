using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed class TankSpawner : Component
{
	private bool IsAuthoritativeInstance() => !Networking.IsActive || Connection.Local?.IsHost == true;

	[Property] public float SpawnRadius { get; set; } = 500f; // Distance from player to spawn Tank
	[Property] public GameObject TankPrefab { get; set; } // Prefab to spawn
	[Property] public GameObject PlayerReference { get; set; } // Optional: manually assign the player
	[Property] public bool Enabled { get; set; } = true; // Toggle spawner on/off

	private GameObject spawnedTank = null;
	private GameObject player;

	// Cache original Tank properties from the scene
	private float cachedMaxHealth = 1500f;
	private float cachedWalkSpeed = 150f;
	private float cachedChargeSpeed = 600f;
	private float cachedPunchDamage = 40f;
	private float cachedPunchKnockback = 800f;
	private float cachedPunchKnockbackUp = 300f;
	private float cachedChargeRange = 500f;
	private float cachedPunchRange = 200f;
	private Color cachedFlashColor = Color.White;
	private float cachedFlashDuration = 0.2f;
	private SoundEvent cachedDeathSound;
	private float cachedHeadshotStaggerMultiplier = 1.2f;

	protected override void OnAwake()
	{
		Log.Info( "TankSpawner: OnAwake called" );

		// Use manual reference if provided
		if ( PlayerReference is not null )
		{
			player = PlayerReference;
			Log.Info( "TankSpawner: Using manually assigned player reference" );
		}
		else
		{
			// Try to find the player by looking for a PlayerMovement component
			var playerMovement = Scene.Components.Get<PlayerMovement>();
			if ( playerMovement is not null )
			{
				player = playerMovement.GameObject;
				Log.Info( "TankSpawner: Player found via Scene.Components" );
			}
			else
			{
				// Fallback: search all GameObjects for PlayerMovement
				foreach ( var go in Scene.GetAllComponents<PlayerMovement>() )
				{
					player = go.GameObject;
					Log.Info( "TankSpawner: Player found via GetAllComponents fallback" );
					break;
				}
			}
		}

		if ( player is null )
		{
			Log.Warning( "TankSpawner: Could not find player! Try manually assigning PlayerReference property." );
		}

		// Cache original Tank properties from the scene
		var originalTank = Scene.Components.Get<Tank>();
		if ( originalTank is not null )
		{
			cachedMaxHealth = originalTank.MaxHealth;
			cachedWalkSpeed = originalTank.WalkSpeed;
			cachedChargeSpeed = originalTank.ChargeSpeed;
			cachedPunchDamage = originalTank.PunchDamage;
			cachedPunchKnockback = originalTank.PunchKnockback;
			cachedPunchKnockbackUp = originalTank.PunchKnockbackUp;
			cachedChargeRange = originalTank.ChargeRange;
			cachedPunchRange = originalTank.PunchRange;
			cachedFlashColor = originalTank.FlashColor;
			cachedFlashDuration = originalTank.FlashDuration;
			cachedDeathSound = originalTank.DeathSound;
			cachedHeadshotStaggerMultiplier = originalTank.HeadshotStaggerMultiplier;
			Log.Info( $"TankSpawner: Cached original Tank properties" );
		}
	}

	protected override void OnUpdate()
	{
		if ( !IsAuthoritativeInstance() )
			return;

		if ( !Enabled )
		{
			return;
		}

		if ( player is null )
		{
			Log.Warning( "TankSpawner: Player is null" );
			return;
		}

		if ( TankPrefab is null )
		{
			Log.Warning( "TankSpawner: TankPrefab is null" );
			return;
		}

		// Cleanup dead Tank
		if ( spawnedTank is not null )
		{
			bool isDead = false;

			if ( !spawnedTank.Active )
			{
				isDead = true;
			}
			else
			{
				var tankComponent = spawnedTank.Components.Get<Tank>();
				if ( tankComponent is null || !tankComponent.Enabled )
				{
					isDead = true;
				}
			}

			if ( isDead )
			{
				Log.Info( "TankSpawner: Tank died, clearing reference" );
				spawnedTank = null;
			}
		}
	}

	/// <summary>
	/// Attempt to spawn a Tank. Only spawns if no Tank is currently alive.
	/// </summary>
	public bool TrySpawnTank()
	{
		if ( !IsAuthoritativeInstance() )
			return false;

		if ( !Enabled )
		{
			Log.Warning( "TankSpawner: Spawner is disabled" );
			return false;
		}

		if ( player is null )
		{
			Log.Error( "TankSpawner: Player is null during spawn attempt!" );
			return false;
		}

		if ( TankPrefab is null )
		{
			Log.Error( "TankSpawner: No Tank prefab assigned!" );
			return false;
		}

		// Check if Tank already exists
		if ( spawnedTank is not null )
		{
			var tankComponent = spawnedTank.Components.Get<Tank>();
			if ( tankComponent is not null && tankComponent.Enabled )
			{
				Log.Warning( "TankSpawner: BLOCKED SPAWN - Tank already alive" );
				return false;
			}
		}

		// Random spawn position around the player (farther than regular enemies)
		float angle = Game.Random.Float( 0f, 360f );
		float distance = Game.Random.Float( SpawnRadius * 0.8f, SpawnRadius );

		// Ensure minimum distance from player
		if ( distance < 300f )
		{
			distance = 300f;
		}

		try
		{
			Log.Info( "TankSpawner: Starting Tank spawn..." );

			// Start raycast from high above to find ground level
			Vector3 raycastStart = player.WorldPosition + new Vector3(
				(float)System.Math.Cos( angle * System.Math.PI / 180f ) * distance,
				(float)System.Math.Sin( angle * System.Math.PI / 180f ) * distance,
				500f  // Raycast from high in the air
			);

			// Raycast down to find ground level
			var trace = Scene.Trace
				.Ray( raycastStart, raycastStart + Vector3.Down * 1000f )
				.Run();

			// Reject spawn if trace doesn't hit (no ground found)
			if ( !trace.Hit )
			{
				Log.Warning( $"TankSpawner: No ground found at spawn position, rejecting spawn" );
				return false;
			}

			Vector3 spawnPos = trace.EndPosition + Vector3.Up * 5f;  // Just above ground

			// Validate spawn position - reject if it's below a certain threshold
			if ( spawnPos.z < 20f )
			{
				Log.Warning( $"TankSpawner: Spawn position too low ({spawnPos.z:F1}), rejecting spawn" );
				return false;
			}

			// Instantiate the Tank
			spawnedTank = TankPrefab.Clone( spawnPos );
			Log.Info( "TankSpawner: Tank cloned" );

			if ( spawnedTank is null )
			{
				Log.Error( "TankSpawner: Failed to clone prefab!" );
				return false;
			}

			// 1. Set color to red FIRST (before Tank.OnAwake runs)
			var modelRenderer = spawnedTank.Components.Get<SkinnedModelRenderer>() as ModelRenderer
			                 ?? spawnedTank.Components.Get<ModelRenderer>();
			if ( modelRenderer is not null )
			{
				modelRenderer.Tint = Color.Red;
			}

			// 2. Ensure CharacterController exists (BEFORE creating Tank)
			var characterController = spawnedTank.Components.Get<CharacterController>();
			if ( characterController is null )
			{
				characterController = spawnedTank.Components.Create<CharacterController>();
				characterController.Radius = 40f; // Larger than regular enemy
				characterController.Height = 120f;
			}
			characterController.Enabled = true;
			characterController.Velocity = Vector3.Zero;
			characterController.IsOnGround = true;

			// 3. Setup Rigidbody (create if needed for ragdoll when Tank dies)
			try
			{
				var rigidbody = spawnedTank.Components.Get<Rigidbody>();
				if ( rigidbody is null )
				{
					rigidbody = spawnedTank.Components.Create<Rigidbody>();
				}
				rigidbody.Enabled = true;
			}
			catch ( System.Exception rbEx )
			{
				Log.Warning( $"TankSpawner: Could not setup rigidbody: {rbEx.Message}" );
			}

			// 4. Ensure HealthComponent exists (required by Tank)
			var healthComponent = spawnedTank.Components.Get<HealthComponent>();
			if ( healthComponent is null )
			{
				healthComponent = spawnedTank.Components.Create<HealthComponent>();
				healthComponent.MaxHealth = cachedMaxHealth;
				healthComponent.CurrentHealth = cachedMaxHealth;
				healthComponent.IsPlayer = false;
			}
			else
			{
				// Reset health if component exists
				healthComponent.MaxHealth = cachedMaxHealth;
				healthComponent.CurrentHealth = cachedMaxHealth;
			}

			// 5. Now enable or create Tank component
			var allTanks = spawnedTank.Components.GetAll<Tank>().ToList();
			Log.Info( $"TankSpawner: Found {allTanks.Count} Tank components on clone" );

			Tank spawnedTankComponent = null;

			if ( allTanks.Count > 0 )
			{
				spawnedTankComponent = allTanks.First();
				spawnedTankComponent.Enabled = true;
				Log.Info( "TankSpawner: Enabled existing Tank component" );
			}
			else
			{
				spawnedTankComponent = spawnedTank.Components.Create<Tank>();
				Log.Info( "TankSpawner: Created new Tank component" );
			}

			// 6. Apply cached properties to the spawned Tank
			if ( spawnedTankComponent is not null )
			{
				spawnedTankComponent.PlayerReference = player;
				spawnedTankComponent.MaxHealth = cachedMaxHealth;
				spawnedTankComponent.WalkSpeed = cachedWalkSpeed;
				spawnedTankComponent.ChargeSpeed = cachedChargeSpeed;
				spawnedTankComponent.PunchDamage = cachedPunchDamage;
				spawnedTankComponent.PunchKnockback = cachedPunchKnockback;
				spawnedTankComponent.PunchKnockbackUp = cachedPunchKnockbackUp;
				spawnedTankComponent.ChargeRange = cachedChargeRange;
				spawnedTankComponent.PunchRange = cachedPunchRange;
				spawnedTankComponent.FlashColor = cachedFlashColor;
				spawnedTankComponent.FlashDuration = cachedFlashDuration;
				spawnedTankComponent.DeathSound = cachedDeathSound;
				spawnedTankComponent.HeadshotStaggerMultiplier = cachedHeadshotStaggerMultiplier;
				Log.Info( $"TankSpawner: Applied cached properties to spawned Tank" );
			}

			if ( Networking.IsActive )
				spawnedTank.NetworkSpawn();

			Log.Info( $"TankSpawner: Successfully spawned Tank at distance {distance:F0}" );
			return true;
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"TankSpawner: Exception in spawn logic: {ex.Message}\n{ex.StackTrace}" );
			return false;
		}
	}

	/// <summary>
	/// Check if a Tank is currently alive
	/// </summary>
	public bool HasAliveTank()
	{
		if ( spawnedTank is null )
			return false;

		if ( !spawnedTank.Active )
			return false;

		var tankComponent = spawnedTank.Components.Get<Tank>();
		if ( tankComponent is null || !tankComponent.Enabled )
			return false;

		return true;
	}

	/// <summary>
	/// Get the currently alive Tank GameObject
	/// </summary>
	public GameObject GetAliveTank()
	{
		if ( HasAliveTank() )
			return spawnedTank;

		return null;
	}
}
