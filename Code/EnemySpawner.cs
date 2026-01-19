using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed class EnemySpawner : Component
{
	[Property] public float SpawnInterval { get; set; } = 3f; // Seconds between spawns
	[Property] public int MaxEnemies { get; set; } = 5; // Max enemies alive at once
	[Property] public float SpawnDuration { get; set; } = 60f; // Seconds to spawn for
	[Property] public float SpawnPauseTime { get; set; } = 60f; // Seconds to pause after spawn duration ends
	[Property] public float SpawnRadius { get; set; } = 300f; // Distance from player to spawn enemies
	[Property] public GameObject EnemyPrefab { get; set; } // Prefab to spawn
	[Property] public GameObject PlayerReference { get; set; } // Optional: manually assign the player
	[Property] public bool Enabled { get; set; } = true; // Toggle spawner on/off

	private float spawnTimer = 0f;
	private float cycleTimer = 0f;
	private bool isSpawning = true; // Start spawning immediately
	private List<GameObject> spawnedEnemies = new List<GameObject>();
	private GameObject player;
	
	// Cache original enemy properties from the scene
	private float cachedPatrolSpeed = 100f;
	private float cachedChaseSpeed = 200f;
	private float cachedDetectionRange = 500f;
	private float cachedAttackRange = 150f;
	private float cachedAttackDamage = 15f;
	private float cachedKnockbackForce = 300f;
	private float cachedKnockbackUpForce = 200f;
	private Color cachedFlashColor = Color.White;
	private float cachedFlashDuration = 0.2f;
	private SoundEvent cachedDeathSound;
	private float cachedHeadshotStaggerMultiplier = 1.5f;
	private List<Transform> cachedPatrolPoints = new();

	protected override void OnAwake()
	{
		Log.Info( "EnemySpawner: OnAwake called" );

		// Use manual reference if provided
		if ( PlayerReference is not null )
		{
			player = PlayerReference;
			Log.Info( "EnemySpawner: Using manually assigned player reference" );
		}
		else
		{
			// Try to find the player by looking for a PlayerMovement component
			var playerMovement = Scene.Components.Get<PlayerMovement>();
			if ( playerMovement is not null )
			{
				player = playerMovement.GameObject;
				Log.Info( "EnemySpawner: Player found via Scene.Components" );
			}
			else
			{
				// Fallback: search all GameObjects for PlayerMovement
				foreach ( var go in Scene.GetAllComponents<PlayerMovement>() )
				{
					player = go.GameObject;
					Log.Info( "EnemySpawner: Player found via GetAllComponents fallback" );
					break;
				}
			}
		}

		if ( player is null )
		{
			Log.Warning( "EnemySpawner: Could not find player! Try manually assigning PlayerReference property." );
		}

		// Cache original enemy properties from the scene
		var originalEnemy = Scene.Components.Get<Enemy>();
		if ( originalEnemy is not null )
		{
			cachedPatrolSpeed = originalEnemy.PatrolSpeed;
			cachedChaseSpeed = originalEnemy.ChaseSpeed;
			cachedDetectionRange = originalEnemy.DetectionRange;
			cachedAttackRange = originalEnemy.AttackRange;
			cachedAttackDamage = originalEnemy.AttackDamage;
			cachedKnockbackForce = originalEnemy.KnockbackForce;
			cachedKnockbackUpForce = originalEnemy.KnockbackUpForce;
			cachedFlashColor = originalEnemy.FlashColor;
			cachedFlashDuration = originalEnemy.FlashDuration;
			cachedDeathSound = originalEnemy.DeathSound;
			cachedHeadshotStaggerMultiplier = originalEnemy.HeadshotStaggerMultiplier;
			cachedPatrolPoints = new List<Transform>( originalEnemy.PatrolPoints ?? new List<Transform>() );
			Log.Info( $"EnemySpawner: Cached original enemy properties (patrol points: {cachedPatrolPoints.Count})" );
		}
	}

	protected override void OnUpdate()
	{
		// Debug: check basic state
		if ( !Enabled )
		{
			Log.Warning( "EnemySpawner: Spawner is disabled!" );
			return;
		}

		if ( player is null )
		{
			Log.Warning( "EnemySpawner: Player is null" );
			return;
		}

		if ( EnemyPrefab is null )
		{
			Log.Warning( "EnemySpawner: EnemyPrefab is null" );
			return;
		}

		// Aggressive cleanup: Remove ALL dead enemies (null, inactive, or Enemy component disabled)
		var deadEnemies = 0;
		for ( int i = spawnedEnemies.Count - 1; i >= 0; i-- )
		{
			try
			{
				var enemy = spawnedEnemies[i];
				bool isDead = false;

				if ( enemy is null )
				{
					isDead = true;
				}
				else if ( !enemy.Active )
				{
					isDead = true;
				}
				else
				{
					var enemyComponent = enemy.Components.Get<Enemy>();
					if ( enemyComponent is null || !enemyComponent.Enabled )
					{
						isDead = true;
					}
				}

				if ( isDead )
				{
					spawnedEnemies.RemoveAt( i );
					deadEnemies++;
				}
			}
			catch ( System.Exception ex )
			{
				Log.Warning( $"EnemySpawner: Exception during cleanup: {ex.Message}" );
				spawnedEnemies.RemoveAt( i );
				deadEnemies++;
			}
		}

		if ( deadEnemies > 0 )
		{
			Log.Info( $"EnemySpawner: Cleaned up {deadEnemies} dead enemies. Alive: {spawnedEnemies.Count}/{MaxEnemies}" );
		}

		cycleTimer += Time.Delta;

		// Check if we need to switch between spawning and pausing
		float currentPhaseDuration = isSpawning ? SpawnDuration : SpawnPauseTime;
		if ( cycleTimer >= currentPhaseDuration )
		{
			cycleTimer = 0f;
			isSpawning = !isSpawning;
			Log.Info( $"EnemySpawner: Switched to {(isSpawning ? "SPAWNING" : "PAUSED")} phase. Alive: {spawnedEnemies.Count}/{MaxEnemies}" );
		}

		if ( !isSpawning )
		{
			Log.Info( $"EnemySpawner: In PAUSE phase ({cycleTimer:F1}/{SpawnPauseTime:F1}s), not spawning" );
			return; // Don't spawn during pause phase
		}

		// Spawn enemies - STRICT limit enforcement
		spawnTimer += Time.Delta;
		if ( spawnTimer >= SpawnInterval )
		{
			spawnTimer = 0f;
			
			if ( spawnedEnemies.Count >= MaxEnemies )
			{
				Log.Warning( $"EnemySpawner: BLOCKED SPAWN - At cap ({spawnedEnemies.Count}/{MaxEnemies})" );
				return;
			}

			Log.Info( "EnemySpawner: Spawn interval reached, attempting spawn..." );
			try
			{
				SpawnEnemy();
			}
			catch ( System.Exception ex )
			{
				Log.Error( $"EnemySpawner: Exception during spawn: {ex.Message}\n{ex.StackTrace}" );
			}
		}
	}

	private void SpawnEnemy()
	{
		if ( EnemyPrefab is null )
		{
			Log.Error( "EnemySpawner: No enemy prefab assigned!" );
			return;
		}

		if ( player is null )
		{
			Log.Error( "EnemySpawner: Player is null during spawn attempt!" );
			return;
		}

		// Random spawn position around the player
		float angle = Game.Random.Float( 0f, 360f );
		float distance = Game.Random.Float( SpawnRadius * 0.8f, SpawnRadius );
		
		// Ensure minimum distance from player
		if ( distance < 200f )
		{
			distance = 200f;
		}
		
		try
		{
			Log.Info( "EnemySpawner: Starting spawn..." );

			// Start raycast from high above to find ground level
			Vector3 raycastStart = player.Transform.Position + new Vector3(
				(float)System.Math.Cos( angle * System.Math.PI / 180f ) * distance,
				(float)System.Math.Sin( angle * System.Math.PI / 180f ) * distance,
				500f  // Raycast from high in the air
			);
			Log.Info( "EnemySpawner: Raycast start calculated" );

			// Raycast down to find ground level
			var trace = Scene.Trace
				.Ray( raycastStart, raycastStart + Vector3.Down * 1000f )
				.Run();
			
			Log.Info( "EnemySpawner: Raycast completed" );

			// Reject spawn if trace doesn't hit (no ground found)
			if ( !trace.Hit )
			{
				Log.Warning( $"EnemySpawner: No ground found at spawn position, rejecting spawn" );
				return;
			}
			
			Vector3 spawnPos = trace.EndPosition + Vector3.Up * 80f;  // Place high above ground to guarantee clear space
			
			// Validate spawn position - reject if it's below a certain threshold (likely inside geometry)
			if ( spawnPos.z < 20f )
			{
				Log.Warning( $"EnemySpawner: Spawn position too low ({spawnPos.z:F1}), rejecting spawn" );
				return;
			}
			
			Log.Info( "EnemySpawner: Spawn position calculated at {0}" );

			// Instantiate the enemy
			var spawnedEnemy = EnemyPrefab.Clone( spawnPos );
			Log.Info( "EnemySpawner: Enemy cloned" );

			if ( spawnedEnemy is null )
			{
				Log.Error( "EnemySpawner: Failed to clone prefab!" );
				return;
			}

			// 1. Set color to red FIRST (before Enemy.OnAwake runs)
			var modelRenderer = spawnedEnemy.Components.Get<ModelRenderer>();
			if ( modelRenderer is not null )
			{
				modelRenderer.Tint = Color.Red;
			}
			Log.Info( "EnemySpawner: Model renderer set" );

			// 2. Ensure CharacterController exists (BEFORE creating Enemy)
			var characterController = spawnedEnemy.Components.Get<CharacterController>();
			if ( characterController is null )
			{
				characterController = spawnedEnemy.Components.Create<CharacterController>();
				characterController.Radius = 25f;
				characterController.Height = 100f;
			}
			characterController.Enabled = true;
			characterController.Velocity = Vector3.Zero;
			characterController.IsOnGround = true;  // Start on ground
			Log.Info( "EnemySpawner: Character controller setup" );

			// 3. Setup Rigidbody (create if needed for ragdoll when enemy dies)
			try
			{
				var rigidbody = spawnedEnemy.Components.Get<Rigidbody>();
				if ( rigidbody is null )
				{
					rigidbody = spawnedEnemy.Components.Create<Rigidbody>();
				}
				rigidbody.Enabled = true;  // Enable for ragdoll physics when enemy dies
				Log.Info( "EnemySpawner: Rigidbody setup complete" );
			}
			catch ( System.Exception rbEx )
			{
				Log.Warning( $"EnemySpawner: Could not setup rigidbody: {rbEx.Message}" );
			}

			// 4. Ensure HealthComponent exists (required by Enemy)
			var healthComponent = spawnedEnemy.Components.Get<HealthComponent>();
			if ( healthComponent is null )
			{
				healthComponent = spawnedEnemy.Components.Create<HealthComponent>();
				healthComponent.MaxHealth = 100f;
				healthComponent.CurrentHealth = 100f;
				healthComponent.IsPlayer = false;
			}
			else
			{
				// Reset health if component exists
				healthComponent.CurrentHealth = healthComponent.MaxHealth;
			}
			Log.Info( "EnemySpawner: Health component setup" );

			// 5. Now enable or create Enemy component
			var allEnemies = spawnedEnemy.Components.GetAll<Enemy>().ToList();
			Log.Info( $"EnemySpawner: Found {allEnemies.Count} Enemy components on clone" );

			Enemy spawnedEnemyComponent = null;
			
			if ( allEnemies.Count > 0 )
			{
				spawnedEnemyComponent = allEnemies.First();
				spawnedEnemyComponent.Enabled = true;
				Log.Info( "EnemySpawner: Enabled existing Enemy component" );
			}
			else
			{
				spawnedEnemyComponent = spawnedEnemy.Components.Create<Enemy>();
				Log.Info( "EnemySpawner: Created new Enemy component" );
			}

			// 6. Apply cached properties to the spawned enemy
			if ( spawnedEnemyComponent is not null )
			{
				spawnedEnemyComponent.PlayerReference = player;  // Set the correct player reference
				spawnedEnemyComponent.PatrolSpeed = cachedPatrolSpeed;
				spawnedEnemyComponent.ChaseSpeed = cachedChaseSpeed;
				spawnedEnemyComponent.DetectionRange = cachedDetectionRange;
				spawnedEnemyComponent.AttackRange = cachedAttackRange;
				spawnedEnemyComponent.AttackDamage = cachedAttackDamage;
				spawnedEnemyComponent.KnockbackForce = cachedKnockbackForce;
				spawnedEnemyComponent.KnockbackUpForce = cachedKnockbackUpForce;
				spawnedEnemyComponent.FlashColor = cachedFlashColor;
				spawnedEnemyComponent.FlashDuration = cachedFlashDuration;
				spawnedEnemyComponent.DeathSound = cachedDeathSound;
				spawnedEnemyComponent.HeadshotStaggerMultiplier = cachedHeadshotStaggerMultiplier;
				spawnedEnemyComponent.PatrolPoints = new List<Transform>( cachedPatrolPoints );
				Log.Info( $"EnemySpawner: Applied cached properties to spawned enemy (patrol points: {spawnedEnemyComponent.PatrolPoints.Count})" );
			}

			spawnedEnemies.Add( spawnedEnemy );
			Log.Info( $"EnemySpawner: Spawned enemy at distance {distance:F0}. Total: {spawnedEnemies.Count}/{MaxEnemies}" );
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"EnemySpawner: Exception in spawn logic: {ex.Message}\n{ex.StackTrace}" );
		}
	}

	public int GetAliveEnemyCount()
	{
		// Validate list integrity
		spawnedEnemies.RemoveAll( e => e is null || !e.Active );
		return spawnedEnemies.Count;
	}
	public bool IsSpawning() => isSpawning;
	public float GetPhaseProgress() => cycleTimer / (isSpawning ? SpawnDuration : SpawnPauseTime);
}
