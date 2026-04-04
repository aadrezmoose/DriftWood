using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;
using System.Linq;

public sealed class EnemySpawner : Component
{
	private bool IsAuthoritativeInstance() => !Networking.IsActive || Connection.Local?.IsHost == true;

	[Property] public float SpawnInterval { get; set; } = 3f;
	[Property] public int SpawnBatchSize { get; set; } = 1;
	[Property] public int MaxEnemies { get; set; } = 5;
	[Property] public List<GameObject> EnemyPrefabs { get; set; } = new();
	[Property] public GameObject PlayerReference { get; set; }
	[Property] public bool Enabled { get; set; } = true;

	/// <summary>If no spawn nodes are found, fall back to radial probing around the player.</summary>
	[Property] public bool AllowRadialFallback { get; set; } = true;
	[Property] public float RadialFallbackRadius { get; set; } = 900f;
	[Property] public int SpawnPositionAttempts { get; set; } = 3;
	[Property] public int BurstSpawnAttemptMultiplier { get; set; } = 3;
	/// <summary>Minimum distance between a new spawn and any already-alive enemy. Prevents clumping.</summary>
	[Property] public float MinSpawnSpread { get; set; } = 150f;
	[Property] public float MinSpawnDistanceFromPlayer { get; set; } = 300f;
	[Property] public float MinSpawnDistanceAtHighIntensity { get; set; } = 220f;
	/// <summary>
	/// How strongly forward-facing nodes are preferred over rear nodes (0 = ignore facing, 1 = strong bias).
	/// Biases spawns toward the direction the player is moving/looking so pressure comes from ahead.
	/// </summary>
	[Property] public float FacingBias { get; set; } = 0.4f;
	[Property] public int RecentNodeHistorySize { get; set; } = 4;
	[Property] public float RecentNodeWeightMultiplier { get; set; } = 0.2f;
	[Property] public bool PreventImmediateNodeRepeat { get; set; } = true;
	[Property] public AIDirector Director { get; set; }

	/// <summary>
	/// Active region filter for SpawnNodes.
	/// "All" means every region is allowed.
	/// </summary>
	[Property] public string ActiveSpawnRegion { get; set; } = "All";
	/// <summary>
	/// When true, SpawnNode UsageCooldown is ignored (intended for horde swathes).
	/// </summary>
	public bool IgnoreNodeCooldown { get; set; } = false;
	/// <summary>
	/// When true, MinSpawnSpread spacing checks are bypassed (intended for dense horde bursts).
	/// </summary>
	public bool IgnoreSpawnSpread { get; set; } = false;

	// ── Budget callbacks wired by AIDirector ─────────────────────────────
	/// <summary>Called before each spawn — pass cost, returns true to allow, false to block.</summary>
	public System.Func<float, bool> OnBeforeSpawn;
	/// <summary>Called after a successful spawn with the spawned GameObject.</summary>
	public System.Action<GameObject> OnEnemySpawned;
	/// <summary>Called when an enemy leaves tracking. bool = soft-despawned (true) vs killed (false).</summary>
	public System.Action<GameObject, bool> OnEnemyRemoved;
	/// <summary>Called when FindSpawnPosition fails — passes cost that was already spent.</summary>
	public System.Action<float> OnSpawnFailed;

	/// <summary>Cost the Director has set for the current spawn mode (common vs horde).</summary>
	public float CurrentSpawnCost { get; set; } = 1f;

	public bool IsSafeZoneActive { get; set; } = false;

	// ── Spawn debug stats ─────────────────────────────────────────────
	public bool  DebugLastSpawnWasRadial { get; private set; }
	public int   DebugSpawnSuccesses     { get; private set; }
	public int   DebugSpawnFailures      { get; private set; }
	public string DebugActiveRegion      => string.IsNullOrWhiteSpace( ActiveSpawnRegion ) ? "All" : ActiveSpawnRegion;
	public int   DebugNodeTotal          => SpawnNode.All.Count;
	public int   DebugNodeOnCooldown     => SpawnNode.All.Count( n => n.IsOnCooldown );
	public int   DebugNodeEligible       => player == null ? 0 : SpawnNode.All.Count( n => IsNodeAllowedForRegion( n ) && n.IsEligible( player, IgnoreNodeCooldown ) );
	public void  ResetDebugCounters()    { DebugSpawnSuccesses = 0; DebugSpawnFailures = 0; }

	public void SetActiveSpawnRegion( string region )
	{
		ActiveSpawnRegion = string.IsNullOrWhiteSpace( region ) ? "All" : region.Trim();
	}

	public void ClearSpawnRegionFilter() => ActiveSpawnRegion = "All";

	private float spawnTimer = 0f;
	private List<GameObject> spawnedEnemies = new();
	private GameObject player;
	private readonly Queue<System.Guid> recentNodeHistory = new();

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
	private float cachedHeadshotStaggerMultiplier = 1.5f;
	private List<Transform> cachedPatrolPoints = new();

	protected override void OnAwake()
	{
		SafeRoom.OnPlayerEntered += OnPlayerEnteredSafeRoom;
		SafeRoom.OnPlayerExited  += OnPlayerExitedSafeRoom;

		// PlayerIdentity may not be registered yet (OnAwake fires before OnStart).
		// OnUpdate refreshes this per-frame so it connects as soon as the player is ready.
		player = PlayerReference ?? PlayerIdentity.Local?.GameObject;
		if ( player is null )
			Log.Warning( "EnemySpawner: Player not found at OnAwake — will retry each frame" );

		var firstPrefab = EnemyPrefabs.FirstOrDefault();
		var originalEnemy = firstPrefab?.Components.Get<Enemy>()
			?? firstPrefab?.Components.GetInDescendantsOrSelf<Enemy>();
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
		// Note: Don't cache sounds here — prefab components aren't fully loaded until clone time.
		// Sounds will be read fresh from each cloned prefab at spawn.
			cachedHeadshotStaggerMultiplier = originalEnemy.HeadshotStaggerMultiplier;
			cachedPatrolPoints              = new List<Transform>( originalEnemy.PatrolPoints ?? new List<Transform>() );
		}
	}

	protected override void OnUpdate()
	{
		if ( !IsAuthoritativeInstance() ) return;

		// Refresh player ref each frame via PlayerIdentity
		if ( PlayerReference == null )
			player = PlayerIdentity.Local?.GameObject;

		if ( !Enabled || player is null || EnemyPrefabs.Count == 0 ) return;

		// Clean up removed enemies — notify Director when they are actually gone.
		// Important: do NOT treat !Enemy.Enabled as removable, because death disables AI
		// while ragdoll/fade still needs to exist and replicate to clients.
		for ( int i = spawnedEnemies.Count - 1; i >= 0; i-- )
		{
			try
			{
				var e = spawnedEnemies[i];
				bool removed = e is null || !e.Active;
				if ( removed )
				{
					OnEnemyRemoved?.Invoke( spawnedEnemies[i], false );
					spawnedEnemies.RemoveAt( i );
				}
			}
			catch
			{
				spawnedEnemies.RemoveAt( i );
			}
		}

		spawnTimer += Time.Delta;
		float interval = System.Math.Max( SpawnInterval, 0.01f );
		int tickGuard = 0;
		while ( spawnTimer >= interval && tickGuard < 8 )
		{
			spawnTimer -= interval;
			tickGuard++;

			if ( spawnedEnemies.Count >= MaxEnemies )
				continue;

			int burst = System.Math.Max( SpawnBatchSize, 1 );
			int successes = 0;
			int attempts = 0;
			int maxAttempts = System.Math.Max( burst * System.Math.Max( BurstSpawnAttemptMultiplier, 1 ), burst );

			while ( successes < burst && attempts < maxAttempts && spawnedEnemies.Count < MaxEnemies )
			{
				attempts++;
				try
				{
					if ( SpawnEnemy() ) successes++;
				}
				catch ( System.Exception ex )
				{
					Log.Error( $"EnemySpawner: {ex.Message}\n{ex.StackTrace}" );
				}
			}
		}
	}

	private bool IsNodeAllowedForRegion( SpawnNode node )
	{
		if ( node == null ) return false;

		string active = string.IsNullOrWhiteSpace( ActiveSpawnRegion ) ? "All" : ActiveSpawnRegion;
		if ( active.Equals( "All", System.StringComparison.OrdinalIgnoreCase ) )
			return true;

		string nodeRegion = string.IsNullOrWhiteSpace( node.SpawnRegion ) ? "Global" : node.SpawnRegion;
		if ( nodeRegion.Equals( "Global", System.StringComparison.OrdinalIgnoreCase ) )
			return true;

		return nodeRegion.Equals( active, System.StringComparison.OrdinalIgnoreCase );
	}

	private bool IsRecentlyUsedNode( SpawnNode node )
	{
		if ( node == null || node.GameObject == null ) return false;
		return recentNodeHistory.Contains( node.GameObject.Id );
	}

	private void RecordNodeUse( SpawnNode node )
	{
		if ( node == null || node.GameObject == null ) return;

		recentNodeHistory.Enqueue( node.GameObject.Id );
		int max = System.Math.Max( RecentNodeHistorySize, 0 );
		while ( recentNodeHistory.Count > max )
			recentNodeHistory.Dequeue();
	}

	private bool SpawnEnemy()
	{
		if ( IsSafeZoneActive ) return false;

		// Spend budget first — refund via OnSpawnFailed if position or clone fails
		float cost = CurrentSpawnCost;
		if ( OnBeforeSpawn != null && !OnBeforeSpawn( cost ) )
			return false;

		var spawnPos = Vector3.Zero;
		int attempts = System.Math.Clamp( SpawnPositionAttempts, 1, 8 );
		for ( int attempt = 0; attempt < attempts; attempt++ )
		{
			spawnPos = FindSpawnPosition();
			if ( spawnPos == Vector3.Zero )
				continue;

			float intensity = Director?.CurrentIntensity ?? 0f;
			float minDistToPlayer = MathX.Lerp( MinSpawnDistanceFromPlayer, MinSpawnDistanceAtHighIntensity, intensity );

			if ( player != null && Vector3.DistanceBetween( spawnPos, player.WorldPosition ) < minDistToPlayer )
			{
				spawnPos = Vector3.Zero;
				continue;
			}

			break;
		}

		if ( spawnPos == Vector3.Zero )
		{
			DebugSpawnFailures++;
			OnSpawnFailed?.Invoke( cost );
			return false;
		}

		var validPrefabs = EnemyPrefabs.Where( p => p != null ).ToList();
		if ( validPrefabs.Count == 0 ) { DebugSpawnFailures++; OnSpawnFailed?.Invoke( cost ); return false; }
		var prefab = validPrefabs[Game.Random.Int( 0, validPrefabs.Count - 1 )];
		var sourceEnemy = prefab.Components.Get<Enemy>()
			?? prefab.Components.GetInDescendantsOrSelf<Enemy>();
		var spawnedEnemy = prefab.Clone( spawnPos );
		if ( spawnedEnemy is null )
		{
			DebugSpawnFailures++;
			OnSpawnFailed?.Invoke( cost );
			return false;
		}


		// CharacterController
		var cc = spawnedEnemy.Components.Get<CharacterController>();
		if ( cc is null )
		{
			cc = spawnedEnemy.Components.Create<CharacterController>();
			cc.Radius = 25f;
			cc.Height = 100f;
		}
		cc.Enabled    = true;
		cc.Velocity   = Vector3.Zero;
		cc.IsOnGround = true;

		// Disable Rigidbody — re-enabled on death for ragdoll
		var rb = spawnedEnemy.Components.Get<Rigidbody>();
		if ( rb != null ) rb.Enabled = false;

		// BoxCollider for bullet hits
		if ( spawnedEnemy.Components.Get<BoxCollider>() is null )
		{
			var col    = spawnedEnemy.Components.Create<BoxCollider>();
			col.Scale  = new Vector3( 40f, 40f, 72f );
			col.Center = new Vector3( 0f, 0f, 36f );
		}

		// Headshot zone
		var headHitbox           = new GameObject( true, "HeadshotZone" );
		headHitbox.Parent        = spawnedEnemy;
		headHitbox.LocalPosition = new Vector3( 0f, 0f, 62f );
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
		var enemy = spawnedEnemy.Components.Get<Enemy>()
			?? spawnedEnemy.Components.GetInDescendantsOrSelf<Enemy>();
		enemy ??= spawnedEnemy.Components.Create<Enemy>();

		enemy.Enabled = true;

		float speedVariance = Game.Random.Float( 0.85f, 1.15f );
		// Don't set PlayerReference — Enemy now dynamically targets PlayerIdentity.Nearest() each frame

		// Apply stats from source prefab, with fallbacks to cached defaults
		enemy.PatrolSpeed               = (sourceEnemy?.PatrolSpeed ?? cachedPatrolSpeed) * speedVariance;
		enemy.ChaseSpeed                = (sourceEnemy?.ChaseSpeed ?? cachedChaseSpeed) * speedVariance;
		enemy.DetectionRange            = sourceEnemy?.DetectionRange ?? cachedDetectionRange;
		enemy.SightRange                = sourceEnemy?.SightRange ?? cachedSightRange;
		enemy.AttackRange               = sourceEnemy?.AttackRange ?? cachedAttackRange;
		enemy.AttackDamage              = sourceEnemy?.AttackDamage ?? cachedAttackDamage;
		enemy.KnockbackForce            = sourceEnemy?.KnockbackForce ?? cachedKnockbackForce;
		enemy.KnockbackUpForce          = sourceEnemy?.KnockbackUpForce ?? cachedKnockbackUpForce;
		enemy.FlashColor                = sourceEnemy?.FlashColor ?? cachedFlashColor;
		enemy.FlashDuration             = sourceEnemy?.FlashDuration ?? cachedFlashDuration;
		enemy.HeadshotStaggerMultiplier = sourceEnemy?.HeadshotStaggerMultiplier ?? cachedHeadshotStaggerMultiplier;

		var sourcePatrolPoints = sourceEnemy?.PatrolPoints ?? cachedPatrolPoints;
		enemy.PatrolPoints = new List<Transform>( sourcePatrolPoints ?? new List<Transform>() );

		// Leave audio fields as-is from the cloned prefab — don't overwrite with null fallbacks
		// The clone already has the prefab's sound assignments if they exist

		// Re-wire animation helper
		var skinned = spawnedEnemy.Components.Get<SkinnedModelRenderer>();
		if ( skinned is not null )
		{
			var anim    = spawnedEnemy.Components.GetOrCreate<CitizenAnimationHelper>();
			anim.Target = skinned;
		}

		if ( Networking.IsActive )
			spawnedEnemy.NetworkSpawn();

		spawnedEnemies.Add( spawnedEnemy );
		DebugSpawnSuccesses++;
		OnEnemySpawned?.Invoke( spawnedEnemy );
		return true;
	}

	/// <summary>
	/// During Relent, quietly remove spawner enemies that are far from the player and out of LOS.
	/// Director calls this on a throttled 2s interval — not every frame.
	/// </summary>
	public void SoftDespawnDistantEnemies( float minDistance )
	{
		if ( !IsAuthoritativeInstance() ) return;
		if ( player == null ) return;

		for ( int i = spawnedEnemies.Count - 1; i >= 0; i-- )
		{
			var go = spawnedEnemies[i];
			if ( go == null || !go.IsValid() || !go.Active ) continue;

			float dist = Vector3.DistanceBetween( go.WorldPosition, player.WorldPosition );
			if ( dist < minDistance ) continue;

			// Keep if player can see them — despawning a visible enemy is jarring
			var trace = Scene.Trace
				.Ray( go.WorldPosition + Vector3.Up * 60f, player.WorldPosition + Vector3.Up * 60f )
				.WithoutTags( "trigger" )
				.Run();
			if ( !trace.Hit ) continue; // clear LOS — leave them

			OnEnemyRemoved?.Invoke( go, true );
			spawnedEnemies.RemoveAt( i );
			go.Destroy();
		}
	}

	/// <summary>Picks an eligible SpawnNode. Falls back to radial probing if no nodes exist or none are eligible.</summary>
	private Vector3 FindSpawnPosition()
	{
		var nodes = SpawnNode.All;

		if ( nodes.Count > 0 )
		{
			var eligible = nodes.Where( n => IsNodeAllowedForRegion( n ) && n.IsEligible( player, IgnoreNodeCooldown ) ).ToList();

			if ( eligible.Count == 0 )
				goto radialFallback; // no eligible nodes — fall through to radial rather than blocking all spawns

			float intensity  = Director?.CurrentIntensity ?? 0f;
			var   playerFwd  = player.WorldRotation.Forward.WithZ( 0f ).Normal;
			float maxDist    = eligible.Max( n => Vector3.DistanceBetween( player.WorldPosition, n.WorldPosition ) );
			if ( maxDist < 1f ) maxDist = 1f;

			float ComputeWeight( SpawnNode n )
			{
				float dist           = Vector3.DistanceBetween( player.WorldPosition, n.WorldPosition );
				float normalizedDist = dist / maxDist;
				// At low intensity prefer far nodes; at high intensity prefer close (flanking) nodes
				float distWeight     = MathX.Lerp( normalizedDist, 1f - normalizedDist, intensity );

				// Directional bias: early phases prefer forward pressure; peak phases bias flanks.
				var   toNode     = (n.WorldPosition - player.WorldPosition).WithZ( 0f );
				float dot        = toNode.Length > 1f ? Vector3.Dot( playerFwd, toNode.Normal ) : 0f;
				float frontPreference = (dot + 1f) * 0.5f;
				float flankPreference = 1f - System.Math.Abs( dot );
				float directionalPreference = MathX.Lerp( frontPreference, flankPreference, intensity );
				float facingMult = 1f + FacingBias * directionalPreference;

				bool applyRecentPenalty = !IgnoreNodeCooldown;
				float recentMult = (applyRecentPenalty && IsRecentlyUsedNode( n )) ? System.Math.Clamp( RecentNodeWeightMultiplier, 0f, 1f ) : 1f;

				return System.Math.Max( 0.01f, n.Weight * distWeight * facingMult * recentMult );
			}

			float totalWeight = eligible.Sum( n => ComputeWeight( n ) );
			float roll        = Game.Random.Float( 0f, totalWeight );
			float cumulative  = 0f;

			foreach ( var node in eligible )
			{
				if ( !IgnoreNodeCooldown && PreventImmediateNodeRepeat && IsRecentlyUsedNode( node ) && eligible.Count > 1 )
					continue;

				cumulative += ComputeWeight( node );
				if ( roll <= cumulative )
				{
					var pos = node.WorldPosition + Vector3.Up * 5f;
					if ( !IsTooCloseToAliveEnemy( pos ) )
					{
						node.MarkUsed();
						RecordNodeUse( node );
						DebugLastSpawnWasRadial = false;
						return pos;
					}
				}
			}

			// Weighted roll landed on a crowded node — try any eligible that passes spread
			foreach ( var node in eligible )
			{
				if ( !IgnoreNodeCooldown && PreventImmediateNodeRepeat && IsRecentlyUsedNode( node ) && eligible.Count > 1 )
					continue;

				var pos = node.WorldPosition + Vector3.Up * 5f;
				if ( !IsTooCloseToAliveEnemy( pos ) )
				{
					node.MarkUsed();
					RecordNodeUse( node );
					DebugLastSpawnWasRadial = false;
					return pos;
				}
			}

			// All nodes too crowded — fall through to radial
		}

		radialFallback:
		// ── Radial fallback ───────────────────────────────────────────────
		if ( !AllowRadialFallback ) return Vector3.Zero;

		var fwd       = player.WorldRotation.Forward;
		float facingDeg = (float)(System.Math.Atan2( fwd.y, fwd.x ) * 180.0 / System.Math.PI);
		Vector3 bestPos = Vector3.Zero;

		for ( int attempt = 0; attempt < 16; attempt++ )
		{
			// First 10 attempts bias toward the forward hemisphere; last 6 are fully random
			float angle    = attempt < 10
				? facingDeg + Game.Random.Float( -120f, 120f )
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
			if ( IsTooCloseToAliveEnemy( candidate ) ) continue;

			var los = Scene.Trace
				.Ray( candidate + Vector3.Up * 60f, player.WorldPosition + Vector3.Up * 60f )
				.WithoutTags( "trigger" )
				.Run();

			if ( los.Hit )
			{
				DebugLastSpawnWasRadial = true;
				return candidate; // preferred: out of player LOS
			}

			if ( bestPos == Vector3.Zero )
				bestPos = candidate; // fallback: visible but spread-checked
		}

		if ( bestPos != Vector3.Zero )
			DebugLastSpawnWasRadial = true;

		return bestPos;
	}

	/// <summary>Returns true if the candidate position is within MinSpawnSpread of any currently alive spawned enemy.</summary>
	private bool IsTooCloseToAliveEnemy( Vector3 pos )
	{
		if ( IgnoreSpawnSpread ) return false;
		if ( MinSpawnSpread <= 0f ) return false;
		foreach ( var e in spawnedEnemies )
		{
			if ( e == null || !e.Active ) continue;
			if ( Vector3.DistanceBetween( pos, e.WorldPosition ) < MinSpawnSpread )
				return true;
		}
		return false;
	}

	public int GetAliveEnemyCount()
	{
		spawnedEnemies.RemoveAll( e => e is null || !e.Active );
		return spawnedEnemies.Count;
	}

	void OnPlayerEnteredSafeRoom( SafeRoom safeRoom ) => IsSafeZoneActive = true;
	void OnPlayerExitedSafeRoom( SafeRoom safeRoom )  => IsSafeZoneActive = false;

	protected override void OnDestroy()
	{
		SafeRoom.OnPlayerEntered -= OnPlayerEnteredSafeRoom;
		SafeRoom.OnPlayerExited  -= OnPlayerExitedSafeRoom;
	}
}
