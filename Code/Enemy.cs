using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;
using System.Threading.Tasks;

public sealed class Enemy : Component, Component.ITriggerListener
{
	[Property] public bool EnableRagdollDiagnostics { get; set; } = false;
	[Property] public bool EnableEnemyInfoLogs { get; set; } = false;
	/// <summary>
	/// Set to true by the host in OnEnemyDeath(). Synced to all clients so each peer can
	/// independently create their local ModelPhysics ragdoll even when the HealthComponent
	/// event subscription races or the health ref is null on the client.
	/// </summary>
	[Sync] public bool SyncedRagdollTriggered { get; set; } = false;

	[Property] public float PatrolSpeed { get; set; } = 100f;
	[Property] public float ChaseSpeed { get; set; } = 200f;
	[Property] public float StoppingDistance { get; set; } = 50f;
	[Property] public float DetectionRange { get; set; } = 300f;  // proximity/hearing — no LOS needed
	[Property] public float SightRange { get; set; } = 1500f;    // max chase distance when LOS is clear
	[Property] public float AttackRange { get; set; } = 150f;
	[Property] public float AttackCooldown { get; set; } = 1.5f;
	[Property] public float AttackDamage { get; set; } = 2f;
	[Property] public float KnockbackForce { get; set; } = 300f;
	[Property] public float KnockbackUpForce { get; set; } = 200f;
	[Property] public List<Transform> PatrolPoints { get; set; } = new();
	[Property] public Color FlashColor { get; set; } = Color.White;
	[Property] public float FlashDuration { get; set; } = 0.2f;
	[Property] public SoundEvent DeathSound { get; set; }
	[Property] public SoundEvent AttackSound { get; set; }
	[Property] public SoundEvent GrowlSound { get; set; }
	[Property] public SoundEvent PainSound { get; set; }
	[Property] public float GrowlInterval { get; set; } = 6f; // seconds between ambient growls
	[Property] public SoundEvent FootstepSound { get; set; }
	[Property] public float FootstepWalkInterval { get; set; } = 0.55f;
	[Property] public float FootstepChaseInterval { get; set; } = 0.35f;

	// Stagger settings
	[Property] public float StaggerDuration { get; set; } = 0.35f;
	[Property] public float StaggerDamageScale { get; set; } = 0.01f;
	[Property] public float StaggerMaxTime { get; set; } = 1.0f;
	[Property] public float HeadshotStaggerMultiplier { get; set; } = 2.5f; // Extra multiplier for headshot stagger
	[Property] public Color HeadshotFlashColor { get; set; } = Color.Red;
	[Property] public float HeadshotFlashDuration { get; set; } = 0.3f;

	private Model model;
	private Color originalColor = Color.White;
	private float flashTimer = 0f;
	private float headshotFlashTimer = 0f;
	private float staggerTimer = 0f;

	private enum AIState
	{
		Idle,
		Patrol,
		Chase,
		Attack,
		Search
	}

	private AIState currentState = AIState.Patrol;
	private GameObject player;
	private CharacterController characterController;
	private HealthComponent health;
	private bool healthEventsBound = false;
	private GameObject proxyRagdollObject;
	private CitizenAnimationHelper animationHelper;
	private int currentWaypointIndex = 0;
	private float attackCooldownTimer = 0f;
	private float attackAnimTimer = 0f;
	private Vector3 staggerDirection = Vector3.Zero; // captured at stagger start for anim
	private float growlTimer = 0f;
	private float footstepTimer = 0f;
	private Vector3 moveDirection = Vector3.Zero;
	private bool deathHandled = false;

	/// <summary>Set to true the first time this enemy detects the player. Used by the AI Director for timeout refund decisions.</summary>
	public bool HasEngaged { get; private set; }

	private void LogRagdollDiag( string stage )
	{
		if ( !EnableRagdollDiagnostics )
			return;

		var owner = GameObject?.Network?.Owner;
		var local = Connection.Local;
		Log.Info( $"[RagdollDiag] {stage} | proxy={IsProxy} deathHandled={deathHandled} healthDead={health?.IsDead} syncRagdoll={SyncedRagdollTriggered} owner={owner?.DisplayName ?? "none"} local={local?.DisplayName ?? "none"}" );
	}

	private void LogEnemyInfo( string message )
	{
		if ( !EnableEnemyInfoLogs )
			return;

		Log.Info( message );
	}

	// Last known player position for search behavior
	private Vector3 lastKnownPlayerPos = Vector3.Zero;
	private float searchTimer = 0f;
	private const float SearchDuration = 6f;    // seconds to search before giving up
	private const float SearchArrivalRadius = 80f; // how close to arrive before giving up

	protected override void OnAwake()
	{
		characterController = Components.Get<CharacterController>();
		health = Components.Get<HealthComponent>() ?? Components.GetInAncestorsOrSelf<HealthComponent>();
		animationHelper = Components.Get<CitizenAnimationHelper>();
		var render = GetRenderer();

		if ( render is not null )
		{
			originalColor = render.Tint;
			LogEnemyInfo( $"Enemy found ModelRenderer, originalColor = {originalColor}" );
		}
		else
		{
			Log.Warning( "Enemy has no ModelRenderer - flash won't work" );
		}

		if ( characterController is null )
			Log.Warning( "Enemy missing CharacterController component" );
		if ( health is null )
			Log.Warning( "Enemy missing HealthComponent component" );

		// Initial target — OnUpdate refreshes this every frame via PlayerIdentity.Nearest
		player = PlayerIdentity.Nearest( WorldPosition )?.GameObject;

		// Immediately bind health events even on initial spawn
		// This ensures we never miss the first damage hit
		BindHealthEventsIfNeeded();

		deathHandled = false;

		LogEnemyInfo( $"Enemy spawned with {PatrolPoints.Count} patrol points" );
	}

	protected override void OnUpdate()
	{
		// Always target the nearest living player via PlayerIdentity
		player = PlayerIdentity.Nearest( WorldPosition )?.GameObject;

		BindHealthEventsIfNeeded();

		// Fallback for networking race: fire on either synced signal (health or dedicated ragdoll flag)
		// so clients get the ragdoll even if health ref was null at OnAwake time.
		if ( (health is not null && health.IsDead) || SyncedRagdollTriggered )
		{
			if ( deathHandled )
				return;

			LogRagdollDiag( "OnUpdate dead trigger: calling OnEnemyDeath" );
			OnEnemyDeath();
			return;
		}

		if ( !Enabled || characterController is null )
			return;

		if ( player is null || !player.Active )
			return;

		// Update flash timers — headshot flash takes priority
		if ( headshotFlashTimer > 0f )
		{
			headshotFlashTimer -= Time.Delta;
			flashTimer = 0f; // suppress body flash while headshot flash is active
			if ( headshotFlashTimer <= 0f )
			{
				var render = GetRenderer();
				if ( render is not null )
					render.Tint = originalColor;
			}
		}
		else if ( flashTimer > 0f )
		{
			flashTimer -= Time.Delta;
			if ( flashTimer <= 0f )
			{
				var render = GetRenderer();
				if ( render is not null )
					render.Tint = originalColor;
			}
		}

		if ( attackCooldownTimer > 0f )
			attackCooldownTimer -= Time.Delta;
		if ( attackAnimTimer > 0f )
			attackAnimTimer -= Time.Delta;

		// Handle stagger: temporarily disable movement/attacks
		if ( staggerTimer > 0f )
		{
			staggerTimer -= Time.Delta;
			if ( staggerTimer <= 0f )
			{
				// After stagger ends, always chase if we know where the player is
				currentState = lastKnownPlayerPos != Vector3.Zero ? AIState.Chase : AIState.Patrol;
			}
			else
			{
				currentState = AIState.Idle;
				moveDirection = Vector3.Zero;
				UpdateAnimations();
				return;
			}
		}

		// Pipe bomb attraction — overrides normal targeting while a bomb is live and in range
		var lure = PipeBombProjectile.ActiveLure;
		if ( lure != null && lure.IsValid() )
		{
			var lureProj = lure.Components.Get<PipeBombProjectile>();
			float lureDist = (WorldPosition - lure.WorldPosition).Length;
			if ( lureProj == null || lureDist <= lureProj.AttractionRadius )
			{
				lastKnownPlayerPos = lure.WorldPosition;
				searchTimer = SearchDuration;
				currentState = AIState.Chase;
				ChaseBehavior( lure.WorldPosition );
				UpdateAnimations();
				UpdateGrowl();
				UpdateFootsteps();
				return;
			}
		}

		// Determine state
		float distanceToPlayer = (WorldPosition - player.WorldPosition).Length;
		bool hasLineOfSight = HasLineOfSightToPlayer();

		if ( health is not null && health.IsDead )
		{
			currentState = AIState.Idle;
			characterController.Velocity = Vector3.Zero;
			return;
		}

		// State transitions
		// Detect within proximity regardless of LOS, or at long range when LOS is clear
		bool canDetect = distanceToPlayer < DetectionRange
			|| ( hasLineOfSight && distanceToPlayer < SightRange );

		var objectiveLure = ObjectiveEvent.ActiveLureTarget;
		if ( objectiveLure != null && objectiveLure.IsValid() )
		{
			float objectiveLureRadius = ObjectiveEvent.ActiveLureRadius;
			float objectiveLureDist = (WorldPosition - objectiveLure.WorldPosition).Length;
			bool inObjectiveRange = objectiveLureRadius <= 0f || objectiveLureDist <= objectiveLureRadius;

			float maxDistFromPlayer = ObjectiveEvent.ActiveLureMaxEnemyDistanceFromPlayer;
			bool nearPlayerZone = maxDistFromPlayer <= 0f || distanceToPlayer <= maxDistFromPlayer;

			float overrideDistance = ObjectiveEvent.ActiveLurePlayerOverrideDistance;
			bool playerTooCloseForLure = overrideDistance > 0f && distanceToPlayer <= overrideDistance;
			bool preferObjectiveLure = inObjectiveRange && nearPlayerZone && !playerTooCloseForLure;

			if ( preferObjectiveLure )
			{
				HasEngaged = true;
				lastKnownPlayerPos = objectiveLure.WorldPosition;
				searchTimer = SearchDuration;
				currentState = AIState.Chase;
				ChaseBehavior( objectiveLure.WorldPosition );
				UpdateAnimations();
				UpdateGrowl();
				UpdateFootsteps();
				return;
			}
		}

		if ( canDetect )
		{
			HasEngaged = true;
			lastKnownPlayerPos = player.WorldPosition;
			searchTimer = SearchDuration;

			if ( distanceToPlayer < AttackRange && HasLineOfSightToPlayer() )
				currentState = AIState.Attack;
			else
				currentState = AIState.Chase;
		}
		else
		{
			if ( currentState == AIState.Chase || currentState == AIState.Attack )
			{
				// Just lost LOS — start searching last known position
				currentState = AIState.Search;
			}
			else if ( currentState == AIState.Search )
			{
				searchTimer -= Time.Delta;
				float distToLastKnown = (WorldPosition - lastKnownPlayerPos).Length;
				if ( searchTimer <= 0f || distToLastKnown < SearchArrivalRadius )
					currentState = AIState.Patrol;
			}
		}

		// Execute state behavior
		switch ( currentState )
		{
			case AIState.Patrol:
				PatrolBehavior();
				break;
			case AIState.Chase:
				ChaseBehavior( player.WorldPosition );
				break;
			case AIState.Search:
				ChaseBehavior( lastKnownPlayerPos );
				break;
			case AIState.Attack:
				AttackBehavior( player );
				break;
		}

		UpdateAnimations();
		UpdateGrowl();
		UpdateFootsteps();
	}

	void UpdateFootsteps()
	{
		if ( FootstepSound is null || characterController is null ) return;
		if ( !characterController.IsOnGround ) return;

		var flatVel = characterController.Velocity.WithZ( 0 );
		if ( flatVel.Length < 20f )
		{
			footstepTimer = 0f;
			return;
		}

		float interval = (currentState == AIState.Chase || currentState == AIState.Search) ? FootstepChaseInterval : FootstepWalkInterval;
		footstepTimer -= Time.Delta;
		if ( footstepTimer <= 0f )
		{
			footstepTimer = interval;
			Sound.Play( FootstepSound, WorldPosition );
		}
	}

	void UpdateGrowl()
	{
		if ( GrowlSound == null ) return;
		growlTimer -= Time.Delta;
		if ( growlTimer <= 0f )
		{
			growlTimer = GrowlInterval + Game.Random.Float( -1f, 2f ); // randomize slightly
			Sound.Play( GrowlSound, WorldPosition );
		}
	}

	// Returns SkinnedModelRenderer if present, falls back to ModelRenderer
	ModelRenderer GetRenderer() =>
		Components.Get<SkinnedModelRenderer>() as ModelRenderer ?? Components.Get<ModelRenderer>();

	void UpdateAnimations()
	{
		if ( animationHelper is null || characterController is null ) return;

		animationHelper.WithVelocity( characterController.Velocity );
		animationHelper.IsGrounded = characterController.IsOnGround;

		if ( staggerTimer > 0f )
		{
			// Backstep — run speed so the animation plays fast enough to look reactive
			animationHelper.WithWishVelocity( staggerDirection * ChaseSpeed );
			animationHelper.MoveStyle = CitizenAnimationHelper.MoveStyles.Run;
			animationHelper.DuckLevel = 0f;
		}
		else
		{
			animationHelper.DuckLevel = 0f;
			animationHelper.WithWishVelocity( moveDirection );
			animationHelper.MoveStyle = (currentState == AIState.Chase || currentState == AIState.Attack || currentState == AIState.Search)
				? CitizenAnimationHelper.MoveStyles.Run
				: CitizenAnimationHelper.MoveStyles.Walk;
		}

		// Only show punch pose during the active swing window, not the whole Attack state
		animationHelper.HoldType = attackAnimTimer > 0f
			? CitizenAnimationHelper.HoldTypes.Punch
			: CitizenAnimationHelper.HoldTypes.None;

		// Head/spine tracks the player — body weight 0 prevents the whole model tilting
		if ( player is not null )
		{
			Vector3 toPlayer = (player.WorldPosition - WorldPosition).Normal;
			animationHelper.WithLook( toPlayer, 1f, 0.5f, 0f );
		}
	}

	void PatrolBehavior()
	{
		if ( PatrolPoints.Count == 0 )
		{
			moveDirection = Vector3.Zero;
			return;
		}

		Transform targetPoint = PatrolPoints[currentWaypointIndex];
		Vector3 directionToWaypoint = (targetPoint.Position - WorldPosition).Normal;
		float distanceToWaypoint = (WorldPosition - targetPoint.Position).Length;

		if ( distanceToWaypoint < StoppingDistance )
		{
			currentWaypointIndex = (currentWaypointIndex + 1) % PatrolPoints.Count;
		}

		moveDirection = directionToWaypoint * PatrolSpeed;
	}

	void ChaseBehavior( Vector3 playerPosition )
	{
		Vector3 directionToPlayer = (playerPosition - WorldPosition).Normal;
		moveDirection = directionToPlayer * ChaseSpeed;

		// Flatten to horizontal so the model doesn't pitch up/down when player is on geometry
		Vector3 flatDir = directionToPlayer.WithZ( 0 ).Normal;
		if ( !flatDir.IsNearZeroLength )
			WorldRotation = Rotation.LookAt( flatDir, Vector3.Up );
	}

	void AttackBehavior( GameObject targetPlayer )
	{
		// Slowly press toward the player even while attacking — feels more threatening than standing frozen
		Vector3 directionToPlayer = (targetPlayer.WorldPosition - WorldPosition).Normal;
		Vector3 flatDir = directionToPlayer.WithZ( 0 ).Normal;
		moveDirection = flatDir.IsNearZeroLength ? Vector3.Zero : flatDir * (ChaseSpeed * 0.4f);

		if ( !flatDir.IsNearZeroLength )
			WorldRotation = Rotation.LookAt( flatDir, Vector3.Up );

		// Attack if cooldown is ready
		if ( attackCooldownTimer <= 0f )
		{
			PerformAttack( targetPlayer );
			attackCooldownTimer = AttackCooldown;
		}
	}

	void PerformAttack( GameObject targetPlayer )
	{
		// Trigger the actual swing animation on the citizen anim graph
		var renderer = Components.Get<SkinnedModelRenderer>();
		if ( renderer is not null )
			renderer.Set( "b_attack", true );
		attackAnimTimer = 0.6f; // hold punch pose for 0.6s then return to idle

		if ( AttackSound != null )
			Sound.Play( AttackSound, WorldPosition );

		// Don't deal damage through floors/ceilings — require LOS and similar Z height
		float zDiff = System.Math.Abs( WorldPosition.z - targetPlayer.WorldPosition.z );
		if ( zDiff > 120f || !HasLineOfSightToPlayer() )
			return;

		var incap = targetPlayer.Components.GetInDescendantsOrSelf<IncapacitationComponent>();
		bool playerIsDowned = incap?.IsIncapacitated ?? false;

		if ( playerIsDowned )
		{
			// Drain the bleed-out bar instead of dealing HP damage
			incap.ApplyBleedDamage( AttackDamage * 0.08f );
			LogEnemyInfo( $"Enemy hit downed player: -{AttackDamage * 0.08f:F1} bleed. Bar: {incap.BleedOutHealth:F0}%" );
		}
		else
		{
			var targetHealth = targetPlayer.Components.Get<HealthComponent>();
			targetHealth ??= targetPlayer.Components.GetInDescendantsOrSelf<HealthComponent>();

			if ( targetHealth is not null )
				targetHealth.TakeDamage( AttackDamage, GameObject );
			else
				Log.Warning( $"Enemy attack failed - no HealthComponent on {targetPlayer.Name}" );
		}
	}

	bool HasLineOfSightToPlayer()
	{
		if ( player is null )
			return false;

		Vector3 directionToPlayer = (player.WorldPosition - WorldPosition).Normal;
		float distanceToPlayer = (player.WorldPosition - WorldPosition).Length;

		// Start trace from enemy's head level (much higher to clear ground)
		Vector3 startPos = WorldPosition + Vector3.Up * 80f;
		Vector3 playerEyePos = player.WorldPosition + Vector3.Up * 80f;

		var trace = Scene.Trace.Ray( startPos, playerEyePos )
			.Size( 8f ) // sphere trace catches thin geometry a point ray can slip through
			.WithoutTags( "trigger" )
			.WithoutTags( "headzone" )
			.IgnoreGameObject( GameObject )
			.IgnoreGameObject( player )
			.Run();

		// If ray didn't hit anything, we have clear LOS
		if ( !trace.Hit || trace.Distance >= distanceToPlayer - 2f )
			return true;

		return false;
	}

	void OnEnemyDeath()
	{
		if ( deathHandled )
			return;

		LogRagdollDiag( "OnEnemyDeath entered" );

		deathHandled = true;

		// Signal all peers so OnUpdate can trigger ragdoll locally even when events aren't wired.
		if ( !Networking.IsActive || !IsProxy )
			SyncedRagdollTriggered = true;

		TrackKill();
		currentState = AIState.Idle;

		moveDirection = Vector3.Zero;

		if ( animationHelper is not null )
		{
			animationHelper.WithVelocity( Vector3.Zero );
			animationHelper.WithWishVelocity( Vector3.Zero );
			animationHelper.IsGrounded = true;
			// Stop the animation helper from running and overriding the bones
			// that ModelPhysics needs to drive for the ragdoll.
			animationHelper.Enabled = false;
		}

		if ( characterController is not null )
		{
			characterController.Velocity = Vector3.Zero;
			characterController.Enabled = false;
		}

		if ( DeathSound != null )
			Sound.Play( DeathSound, WorldPosition );

		// Disable bullet colliders so they don't fight ragdoll bones
		foreach ( var col in Components.GetAll<BoxCollider>() )
			col.Enabled = false;
		foreach ( var child in GameObject.Children )
			foreach ( var col in child.Components.GetAll<BoxCollider>() )
				col.Enabled = false;

		// True per-bone ragdoll via ModelPhysics.
		// ModelPhysics is a LOCAL physics simulation — every client must create it
		// to see the ragdoll. It is NOT replicated over the network.
		var skinnedRenderer = Components.Get<SkinnedModelRenderer>();
		if ( skinnedRenderer is not null )
		{
			skinnedRenderer.Tint = Color.White;
			// Clear the animation graph so the renderer stops evaluating it every frame.
			// CitizenAnimationHelper.Enabled = false only stops parameter writes;
			// the renderer still ticks the graph and snaps bones back to idle otherwise.
			skinnedRenderer.AnimationGraph = null;

			if ( Networking.IsActive && IsProxy )
			{
				bool createdProxyRagdoll = CreateProxyVisualRagdoll( skinnedRenderer );
				if ( createdProxyRagdoll )
					LogRagdollDiag( "OnEnemyDeath created proxy-local ragdoll clone" );
				else
					LogRagdollDiag( "OnEnemyDeath proxy-local ragdoll clone failed; keeping source renderer" );
			}
			else
			{
				var modelPhysics = Components.GetOrCreate<ModelPhysics>();
				modelPhysics.Renderer = skinnedRenderer;
				modelPhysics.Model = skinnedRenderer.Model;
				modelPhysics.Enabled = true;
				LogRagdollDiag( $"OnEnemyDeath created ModelPhysics (Renderer={modelPhysics.Renderer != null}, Model={modelPhysics.Model != null})" );

				_ = FreezeRagdollAfterDelay( modelPhysics );
			}
		}
		else
		{
			LogRagdollDiag( "OnEnemyDeath missing SkinnedModelRenderer" );
		}

		_ = DisableAfterDelay();
		LogRagdollDiag( "OnEnemyDeath exit" );
	}

	async Task DisableAfterDelay()
	{
		await Task.DelaySeconds( 4f ); // Let ragdoll settle and fade
		if ( GameObject.IsValid() )
			Enabled = false;
	}

	// Track kill only once when death occurs
	void TrackKill()
	{
		// Only the authoritative instance tracks kills to avoid double-counting in MP
		if ( !Networking.IsActive || Connection.Local?.IsHost == true )
		{
			PlayerStats.TotalKills++;
			if ( PlayerIdentity.Local != null ) PlayerIdentity.Local.TotalKills++;
		}
		LogEnemyInfo( "Enemy died" );
	}

	private void OnEnemyDamaged( float damageAmount, Vector3 attackerWorldPos )
	{
		if ( characterController is null )
			return;

		if ( attackerWorldPos.IsNearZeroLength && player is not null )
			attackerWorldPos = player.WorldPosition;

		if ( attackerWorldPos.IsNearZeroLength )
			return;

		ApplyDamageEffects( damageAmount, attackerWorldPos, isHeadshot: false );
	}

	/// <summary>Called when the player shoves this enemy (right-click melee push).</summary>
	public void ApplyShove( GameObject attacker )
	{
		if ( characterController is null || attacker is null ) return;
		Vector3 dir = (WorldPosition - attacker.WorldPosition).Normal;
		characterController.Punch( dir * (KnockbackForce * 2.0f) + Vector3.Up * (KnockbackUpForce * 0.6f) );
		float staggerAdd = StaggerDuration * 2f;
		staggerTimer = System.Math.Min( System.Math.Max( staggerTimer, staggerAdd ), StaggerMaxTime );
	}

	/// <summary>Called by the shotgun once per enemy hit to apply weapon-specific body knockback.</summary>
	public void ApplyShotgunKnockback( GameObject attacker )
	{
		if ( characterController is null || attacker is null ) return;
		Vector3 dir = (WorldPosition - attacker.WorldPosition).Normal;
		characterController.Punch( dir * (KnockbackForce * 0.5f) + Vector3.Up * (KnockbackUpForce * 0.25f) );
	}

	public void OnHeadshotDamage( float damageAmount, GameObject attacker )
	{
		if ( characterController is null || attacker is null )
			return;

		ApplyDamageEffects( damageAmount, attacker.WorldPosition, isHeadshot: true );
	}

	private void ApplyDamageEffects( float damageAmount, Vector3 attackerPosition, bool isHeadshot )
	{
		// Being hit always alerts the enemy — update last known position so they chase after stagger
		if ( player is not null )
		{
			lastKnownPlayerPos = player.WorldPosition;
			searchTimer = SearchDuration;
		}

		Vector3 directionFromAttacker = (WorldPosition - attackerPosition).Normal;
		if ( directionFromAttacker.IsNearZeroLength )
			directionFromAttacker = -WorldRotation.Forward;

		if ( isHeadshot )
		{
			// Strong knockback + upward pop on headshots
			Vector3 knockbackImpulse = (directionFromAttacker * KnockbackForce) + (Vector3.Up * KnockbackUpForce);
			characterController.Punch( knockbackImpulse );
		}
		else
		{
			// Every body shot pushes — rapid fire keeps them staggering back
			Vector3 bodyImpulse = directionFromAttacker * (KnockbackForce * 0.3f);
			characterController.Punch( bodyImpulse );
		}

		// Accumulate stagger so rapid fire keeps them interrupted (capped at max)
		float staggerMultiplier = isHeadshot ? HeadshotStaggerMultiplier : 1.0f;
		float addStagger = (StaggerDuration + (damageAmount * StaggerDamageScale)) * staggerMultiplier;
		staggerTimer = System.Math.Min( staggerTimer + addStagger, StaggerMaxTime );
		staggerDirection = directionFromAttacker;

		if ( PainSound != null )
			Sound.Play( PainSound, WorldPosition );

		// Headshots flash red, body shots flash white
		var render = GetRenderer();
		if ( render is not null )
		{
			if ( isHeadshot )
			{
				render.Tint = HeadshotFlashColor;
				headshotFlashTimer = HeadshotFlashDuration;
			}
			else
			{
				render.Tint = FlashColor;
				flashTimer = FlashDuration;
			}
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( characterController is null || !Enabled || deathHandled )
			return;

		var gravity = Scene.PhysicsWorld.Gravity;

		if ( staggerTimer > 0f )
		{
			// Stagger: step backward away from attacker for the full stagger duration
			if ( characterController.IsOnGround )
			{
				characterController.Velocity = characterController.Velocity.WithZ( 0 );
				characterController.Accelerate( staggerDirection * PatrolSpeed );
				characterController.ApplyFriction( 3.0f );
			}
			else
			{
				characterController.Velocity += gravity * Time.Delta * 0.5f;
				characterController.ApplyFriction( 0.5f );
			}

			characterController.Move();

			if ( !characterController.IsOnGround )
				characterController.Velocity += gravity * Time.Delta * 0.5f;
			else
				characterController.Velocity = characterController.Velocity.WithZ( 0 );
			return;
		}

		// Separation push — prevent clipping into the player body
		var resolvedMove = moveDirection;
		if ( player is not null )
		{
			var toPlayer = player.WorldPosition - WorldPosition;
			float flatDist = toPlayer.WithZ( 0 ).Length;
			const float MinSeparation = 55f;
			if ( flatDist < MinSeparation && flatDist > 0.01f )
			{
				// Override move direction to push away from player
				resolvedMove = -toPlayer.WithZ( 0 ).Normal * ChaseSpeed;
			}
		}

		// Normal movement — same Verlet gravity integration as the player
		if ( characterController.IsOnGround )
		{
			characterController.Velocity = characterController.Velocity.WithZ( 0 );
			characterController.Accelerate( resolvedMove );
			characterController.ApplyFriction( 4.0f );
		}
		else
		{
			characterController.Velocity += gravity * Time.Delta * 0.5f;
			characterController.Accelerate( resolvedMove );
			characterController.ApplyFriction( 0.1f ); // minimal air friction
		}

		characterController.Move();

		if ( !characterController.IsOnGround )
			characterController.Velocity += gravity * Time.Delta * 0.5f;
		else
			characterController.Velocity = characterController.Velocity.WithZ( 0 );
	}

	protected override void OnDestroy()
	{
		if ( proxyRagdollObject is not null && proxyRagdollObject.IsValid() )
			proxyRagdollObject.Destroy();

		if ( health is not null && healthEventsBound )
		{
			health.OnDeath -= OnEnemyDeath;
			health.OnDamageTakenWithPosition -= OnEnemyDamaged;
			healthEventsBound = false;
		}
	}

	private bool CreateProxyVisualRagdoll( SkinnedModelRenderer sourceRenderer )
	{
		if ( sourceRenderer is null || sourceRenderer.Model is null )
			return false;

		if ( proxyRagdollObject is not null && proxyRagdollObject.IsValid() )
			proxyRagdollObject.Destroy();

		var ragdollGo = new GameObject( true, $"{GameObject.Name}_ProxyRagdoll" );
		ragdollGo.WorldPosition = GameObject.WorldPosition;
		ragdollGo.WorldRotation = GameObject.WorldRotation;
		ragdollGo.LocalScale = GameObject.LocalScale;

		var ragdollRenderer = ragdollGo.Components.Create<SkinnedModelRenderer>();
		ragdollRenderer.Model = sourceRenderer.Model;
		ragdollRenderer.Tint = Color.White;
		ragdollRenderer.AnimationGraph = null;

		var ragdollPhysics = ragdollGo.Components.Create<ModelPhysics>();
		ragdollPhysics.Renderer = ragdollRenderer;
		ragdollPhysics.Model = ragdollRenderer.Model;
		ragdollPhysics.Enabled = true;

		proxyRagdollObject = ragdollGo;
		sourceRenderer.Enabled = false;
		_ = FadeAndDestroyProxyRagdoll( ragdollGo, ragdollRenderer );
		return true;
	}

	private async Task FadeAndDestroyProxyRagdoll( GameObject ragdollGo, SkinnedModelRenderer ragdollRenderer )
	{
		await Task.DelaySeconds( 2f );

		float elapsed = 0f;
		const float fadeDuration = 2f;
		while ( elapsed < fadeDuration && ragdollGo.IsValid() )
		{
			elapsed += Time.Delta;
			float alpha = 1f - (elapsed / fadeDuration);
			if ( ragdollRenderer is not null && ragdollRenderer.IsValid() )
				ragdollRenderer.Tint = ragdollRenderer.Tint.WithAlpha( alpha );
			await Task.Frame();
		}

		if ( ragdollGo.IsValid() )
			ragdollGo.Destroy();

		if ( ReferenceEquals( proxyRagdollObject, ragdollGo ) )
			proxyRagdollObject = null;
	}

	private void BindHealthEventsIfNeeded()
	{
		if ( health == null )
			health = Components.Get<HealthComponent>() ?? Components.GetInAncestorsOrSelf<HealthComponent>();

		if ( health == null || healthEventsBound )
			return;

		health.OnDeath += OnEnemyDeath;
		health.OnDamageTakenWithPosition += OnEnemyDamaged;
		healthEventsBound = true;
	}

	async Task FreezeRagdollAfterDelay( ModelPhysics modelPhysics )
	{
		LogRagdollDiag( "FreezeRagdollAfterDelay started" );

		// Let it fall for 2 seconds, then freeze so it stops flopping
		await Task.DelaySeconds( 2f );
		if ( modelPhysics is null || !modelPhysics.IsValid() )
		{
			LogRagdollDiag( "FreezeRagdollAfterDelay aborted: modelPhysics invalid" );
			return;
		}

		// ModelPhysics.PhysicsGroup is obsolete and no longer reliable in current s&box.
		// Keep ragdoll simulation running briefly, then fade and destroy.

		// Fade out over 2 seconds then destroy
		var renderer = Components.Get<SkinnedModelRenderer>();
		float elapsed = 0f;
		const float fadeDuration = 2f;
		while ( elapsed < fadeDuration && GameObject.IsValid() )
		{
			elapsed += Time.Delta;
			float alpha = 1f - (elapsed / fadeDuration);
			if ( renderer is not null )
				renderer.Tint = renderer.Tint.WithAlpha( alpha );
			await Task.Frame();
		}

		// Only the host destroys networked objects; the destroy replicates to clients.
		if ( GameObject.IsValid() && !GameObject.IsProxy )
		{
			LogRagdollDiag( "FreezeRagdollAfterDelay destroying object on authority" );
			GameObject.Destroy();
		}
		else
		{
			LogRagdollDiag( "FreezeRagdollAfterDelay finished without destroy on this peer" );
		}
	}

	/// <summary>
	/// Reset the enemy to a fresh spawned state (call this when respawning)
	/// </summary>
	public void ResetSpawnState()
	{
		deathHandled = false;
		SyncedRagdollTriggered = false;

		if ( proxyRagdollObject is not null && proxyRagdollObject.IsValid() )
		{
			proxyRagdollObject.Destroy();
			proxyRagdollObject = null;
		}

		var skinnedRenderer = Components.Get<SkinnedModelRenderer>();
		if ( skinnedRenderer is not null )
			skinnedRenderer.Enabled = true;

		// Reset all timers
		staggerTimer = 0f;
		flashTimer = 0f;
		attackCooldownTimer = 0f;
		attackAnimTimer = 0f;
		searchTimer = 0f;
		currentWaypointIndex = 0;
		moveDirection = Vector3.Zero;
		lastKnownPlayerPos = Vector3.Zero;
		currentState = AIState.Patrol;
		HasEngaged = false;

		// Force color to alive state (white/normal)
		var render = GetRenderer();
		if ( render is not null )
		{
			render.Tint = Color.White;
			originalColor = Color.White;
		}

		LogRagdollDiag( "Reset spawn state completed" );
	}
}
