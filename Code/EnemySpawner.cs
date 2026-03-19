using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;
using System.Linq;

public sealed class EnemySpawner : Component
{
	[Property] public float SpawnInterval { get; set; } = 3f;
	[Property] public int MaxEnemies { get; set; } = 5;
	[Property] public float SpawnDuration { get; set; } = 60f;
	[Property] public float SpawnPauseTime { get; set; } = 60f;
	[Property] public GameObject EnemyPrefab { get; set; }
	[Property] public GameObject PlayerReference { get; set; }
	[Property] public bool Enabled { get; set; } = true;

	/// <summary>
	/// If no spawn nodes are found, fall back to radial probing around the player.
	/// Turn off once nodes are placed on all maps.
	/// </summary>
	[Property] public bool AllowRadialFallback { get; set; } = true;
	[Property] public float RadialFallbackRadius { get; set; } = 900f;
	[Property] public AIDirector Director { get; set; }

	public bool IsSafeZoneActive { get; set; } = false;

	private float spawnTimer = 0f;
	private float cycleTimer = 0f;
	private bool isSpawning = true;
	private List<GameObject> spawnedEnemies = new();
	private GameObject player;

	// Cached prefab properties
	private float cachedPatrolSpeed = 100f;
	private float cachedChaseSpeed = 200f;
	private float cachedDetectionRange = 300f;
	private float cachedSightRange = 1500f;
	private float cachedAttackRange = 150f;
	private float cachedAttackDamage = 2f;
	private float cachedKnockbackForce = 300f;
	private float cachedKnockbackUpForce = 200f;
	private Color cachedFlashColor = Color.White;
	private float cachedFlashDuration = 0.2f;
	private SoundEvent cachedDeathSound;
	private SoundEvent cachedGrowlSound;
	private SoundEvent cachedAttackSound;
	private SoundEvent cachedFootstepSound;
	private float cachedHeadshotStaggerMultiplier = 1.5f;
	private List<Transform> cachedPatrolPoints = new();

	protected override void OnAwake()
	{
		SafeRoom.OnPlayerEntered += OnPlayerEnteredSafeRoom;
		SafeRoom.OnPlayerExited += OnPlayerExitedSafeRoom;

		if ( PlayerReference is not null )
		{
			player = PlayerReference;
		}
		else
		{
			var pm = Scene.Components.Get<PlayerMovement>();
			if ( pm is not null )
				player = pm.GameObject;
			else
			{
				foreach ( var go in Scene.GetAllComponents<PlayerMovement>() )
				{
					player = go.GameObject;
					break;
				}
			}
		}

		if ( player is null )
			Log.Warning( "EnemySpawner: Could not find player!" );

		var originalEnemy = EnemyPrefab?.Components.Get<Enemy>();
		if ( originalEnemy is not null )
		{
			cachedPatrolSpeed               = originalEnemy.PatrolSpeed;
			cachedChaseSpeed                = originalEnemy.ChaseSpeed;
			cachedDetectionRange            = originalEnemy.DetectionRange;
			cachedSightRange                = originalEnemy.SightRange;
			cachedAttackRange               = originalEnemy.AttackRange;
			cachedAttackDamage              = originalEnemy.AttackDamage;
			cachedKnockbackForce            = originalEnemy.KnockbackForce;
			cachedKnockbackUpForce          = originalEnemy.KnockbackUpForce;
			cachedFlashColor                = originalEnemy.FlashColor;
			cachedFlashDuration             = originalEnemy.FlashDuration;
			cachedDeathSound                = originalEnemy.DeathSound;
			cachedGrowlSound                = originalEnemy.GrowlSound;
			cachedAttackSound               = originalEnemy.AttackSound;
			cachedFootstepSound             = originalEnemy.FootstepSound;
			cachedHeadshotStaggerMultiplier = originalEnemy.HeadshotStaggerMultiplier;
			cachedPatrolPoints              = new List<Transform>( originalEnemy.PatrolPoints ?? new List<Transform>() );
		}
	}

	protected override void OnUpdate()
	{
		if ( !Enabled || player is null || EnemyPrefab is null ) return;

		// Clean up dead enemies
		for ( int i = spawnedEnemies.Count - 1; i >= 0; i-- )
		{
			try
			{
				var e = spawnedEnemies[i];
				bool dead = e is null || !e.Active;
				if ( !dead )
				{
					var ec = e.Components.Get<Enemy>();
					if ( ec is null || !ec.Enabled ) dead = true;
				}
				if ( dead ) spawnedEnemies.RemoveAt( i );
			}
			catch
			{
				spawnedEnemies.RemoveAt( i );
			}
		}

		cycleTimer += Time.Delta;

		float currentPhaseDuration = isSpawning ? SpawnDuration : SpawnPauseTime;
		if ( cycleTimer >= currentPhaseDuration )
		{
			cycleTimer  = 0f;
			isSpawning  = !isSpawning;
		}

		if ( !isSpawning ) return;

		spawnTimer += Time.Delta;
		if ( spawnTimer >= SpawnInterval )
		{
			spawnTimer = 0f;
			if ( spawnedEnemies.Count < MaxEnemies )
			{
				try { SpawnEnemy(); }
				catch ( System.Exception ex )
				{
					Log.Error( $"EnemySpawner: {ex.Message}\n{ex.StackTrace}" );
				}
			}
		}
	}

	private void SpawnEnemy()
	{
		if ( IsSafeZoneActive ) return;

		var spawnPos = FindSpawnPosition();
		if ( spawnPos == Vector3.Zero )
		{
			Log.Info( "EnemySpawner: No valid spawn node found" );
			return;
		}

		var spawnedEnemy = EnemyPrefab.Clone( spawnPos );
		if ( spawnedEnemy is null ) return;

		// Tint red
		var modelRenderer = spawnedEnemy.Components.Get<SkinnedModelRenderer>() as ModelRenderer
			?? spawnedEnemy.Components.Get<ModelRenderer>();
		if ( modelRenderer is not null )
			modelRenderer.Tint = Color.Red;

		// CharacterController
		var cc = spawnedEnemy.Components.Get<CharacterController>();
		if ( cc is null )
		{
			cc = spawnedEnemy.Components.Create<CharacterController>();
			cc.Radius = 25f;
			cc.Height = 100f;
		}
		cc.Enabled     = true;
		cc.Velocity    = Vector3.Zero;
		cc.IsOnGround  = true;

		// Disable Rigidbody — re-enabled on death for ragdoll
		var rb = spawnedEnemy.Components.Get<Rigidbody>();
		if ( rb != null ) rb.Enabled = false;

		// BoxCollider for bullet hits
		if ( spawnedEnemy.Components.Get<BoxCollider>() is null )
		{
			var col    = spawnedEnemy.Components.Create<BoxCollider>();
			col.Scale  = new Vector3( 40f, 40f, 100f );
			col.Center = new Vector3( 0f, 0f, 50f );
		}

		// Headshot zone
		var headHitbox           = new GameObject( true, "HeadshotZone" );
		headHitbox.Parent        = spawnedEnemy;
		headHitbox.LocalPosition = new Vector3( 0f, 0f, 80f );
		headHitbox.Tags.Add( "headzone" );
		var headCol              = headHitbox.Components.Create<BoxCollider>();
		headCol.Scale            = new Vector3( 30f, 30f, 30f );
		var hsZone               = headHitbox.Components.Create<HeadshotZone>();
		hsZone.DamageMultiplier  = 2.0f;
		hsZone.TargetEntity      = spawnedEnemy;

		// HealthComponent
		var health = spawnedEnemy.Components.Get<HealthComponent>();
		if ( health is null )
		{
			health               = spawnedEnemy.Components.Create<HealthComponent>();
			health.MaxHealth     = 100f;
			health.CurrentHealth = 100f;
			health.IsPlayer      = false;
		}
		else
		{
			health.CurrentHealth = health.MaxHealth;
		}

		// Enemy component
		var allEnemies = spawnedEnemy.Components.GetAll<Enemy>().ToList();
		var enemy      = allEnemies.Count > 0
			? allEnemies.First()
			: spawnedEnemy.Components.Create<Enemy>();

		enemy.Enabled = true;

		float speedVariance                      = Game.Random.Float( 0.85f, 1.15f );
		enemy.PlayerReference                    = player;
		enemy.PatrolSpeed                        = cachedPatrolSpeed * speedVariance;
		enemy.ChaseSpeed                         = cachedChaseSpeed * speedVariance;
		enemy.DetectionRange                     = cachedDetectionRange;
		enemy.SightRange                         = cachedSightRange;
		enemy.AttackRange                        = cachedAttackRange;
		enemy.AttackDamage                       = cachedAttackDamage;
		enemy.KnockbackForce                     = cachedKnockbackForce;
		enemy.KnockbackUpForce                   = cachedKnockbackUpForce;
		enemy.FlashColor                         = cachedFlashColor;
		enemy.FlashDuration                      = cachedFlashDuration;
		enemy.HeadshotStaggerMultiplier          = cachedHeadshotStaggerMultiplier;
		enemy.PatrolPoints                       = new List<Transform>( cachedPatrolPoints );

		// Re-wire animation helper
		var skinned = spawnedEnemy.Components.Get<SkinnedModelRenderer>();
		if ( skinned is not null )
		{
			var anim   = spawnedEnemy.Components.GetOrCreate<CitizenAnimationHelper>();
			anim.Target = skinned;
		}

		spawnedEnemies.Add( spawnedEnemy );
		Log.Info( $"EnemySpawner: Spawned at {spawnPos} (Z={spawnPos.z:F0}). Alive: {spawnedEnemies.Count}/{MaxEnemies}" );
	}

	/// <summary>
	/// Picks an eligible SpawnNode. Falls back to radial probing if no nodes exist.
	/// </summary>
	private Vector3 FindSpawnPosition()
	{
		var nodes = SpawnNode.All;

		if ( nodes.Count > 0 )
		{
			// Gather eligible nodes
			var eligible = nodes.Where( n => n.IsEligible( player ) ).ToList();

			if ( eligible.Count == 0 )
			{
				Log.Info( "EnemySpawner: No eligible spawn nodes (all visible or too close)" );
				return Vector3.Zero;
			}

			// Director-driven weight:
			// At Rest (intensity=0)  → prefer far nodes (ambush)
			// At Peak (intensity=1)  → prefer close nodes (pressure)
			float intensity = Director?.CurrentIntensity ?? 0f;
			float maxDist   = eligible.Max( n => Vector3.DistanceBetween( player.WorldPosition, n.WorldPosition ) );
			if ( maxDist < 1f ) maxDist = 1f;

			float ComputeWeight( SpawnNode n )
			{
				float dist           = Vector3.DistanceBetween( player.WorldPosition, n.WorldPosition );
				float normalizedDist = dist / maxDist; // 0 = close, 1 = far
				// Lerp: Rest prefers far (normalizedDist), Peak prefers close (1-normalizedDist)
				float distWeight = MathX.Lerp( normalizedDist, 1f - normalizedDist, intensity );
				return System.Math.Max( 0.01f, n.Weight * distWeight );
			}

			float totalWeight = eligible.Sum( n => ComputeWeight( n ) );
			float roll        = Game.Random.Float( 0f, totalWeight );
			float cumulative  = 0f;

			foreach ( var node in eligible )
			{
				cumulative += ComputeWeight( node );
				if ( roll <= cumulative )
					return node.WorldPosition + Vector3.Up * 5f;
			}

			return eligible.Last().WorldPosition + Vector3.Up * 5f;
		}

		// ── Radial fallback ────────────────────────────────────────────
		if ( !AllowRadialFallback ) return Vector3.Zero;

		var fwd       = player.WorldRotation.Forward;
		float facingDeg = (float)(System.Math.Atan2( fwd.y, fwd.x ) * 180.0 / System.Math.PI);

		Vector3 bestPos    = Vector3.Zero;

		for ( int attempt = 0; attempt < 12; attempt++ )
		{
			float angle    = attempt < 8
				? facingDeg + Game.Random.Float( -150f, 150f )
				: Game.Random.Float( 0f, 360f );

			float distance = Game.Random.Float( RadialFallbackRadius * 0.75f, RadialFallbackRadius );
			if ( distance < 350f ) distance = 350f;

			Vector3 rayOrigin = new Vector3(
				player.WorldPosition.x + (float)System.Math.Cos( angle * System.Math.PI / 180f ) * distance,
				player.WorldPosition.y + (float)System.Math.Sin( angle * System.Math.PI / 180f ) * distance,
				player.WorldPosition.z + 20f
			);

			var ground = Scene.Trace
				.Ray( rayOrigin, rayOrigin + Vector3.Down * 500f )
				.WithoutTags( "trigger" )
				.Run();

			if ( !ground.Hit ) continue;

			Vector3 candidate = ground.EndPosition + Vector3.Up * 5f;
			if ( candidate.z < player.WorldPosition.z - 50f ) continue;

			var los = Scene.Trace
				.Ray( candidate + Vector3.Up * 60f, player.WorldPosition + Vector3.Up * 60f )
				.WithoutTags( "trigger" )
				.Run();

			if ( los.Hit )
				return candidate; // hidden — use immediately

			if ( bestPos == Vector3.Zero )
				bestPos = candidate; // visible fallback
		}

		return bestPos;
	}

	public int GetAliveEnemyCount()
	{
		spawnedEnemies.RemoveAll( e => e is null || !e.Active );
		return spawnedEnemies.Count;
	}

	public bool IsSpawning() => isSpawning;
	public float GetPhaseProgress() => cycleTimer / (isSpawning ? SpawnDuration : SpawnPauseTime);

	void OnPlayerEnteredSafeRoom( SafeRoom safeRoom ) => IsSafeZoneActive = true;
	void OnPlayerExitedSafeRoom( SafeRoom safeRoom )  => IsSafeZoneActive = false;

	protected override void OnDestroy()
	{
		SafeRoom.OnPlayerEntered -= OnPlayerEnteredSafeRoom;
		SafeRoom.OnPlayerExited  -= OnPlayerExitedSafeRoom;
	}
}
