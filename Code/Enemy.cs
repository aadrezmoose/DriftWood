using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;
using System.Threading.Tasks;

public sealed class Enemy : Component, Component.ITriggerListener
{
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
	[Property] public GameObject PlayerReference { get; set; }

	// Stagger settings
	[Property] public float StaggerDuration { get; set; } = 0.35f;
	[Property] public float StaggerDamageScale { get; set; } = 0.01f;
	[Property] public float StaggerMaxTime { get; set; } = 1.0f;
	[Property] public float HeadshotStaggerMultiplier { get; set; } = 1.5f; // Extra multiplier for headshot stagger

	private Model model;
	private Color originalColor = Color.White;
	private float flashTimer = 0f;
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
	private CitizenAnimationHelper animationHelper;
	private int currentWaypointIndex = 0;
	private float attackCooldownTimer = 0f;
	private float attackAnimTimer = 0f;
	private Vector3 staggerDirection = Vector3.Zero; // captured at stagger start for anim
	private float growlTimer = 0f;
	private float footstepTimer = 0f;
	private Vector3 moveDirection = Vector3.Zero;

	// Last known player position for search behavior
	private Vector3 lastKnownPlayerPos = Vector3.Zero;
	private float searchTimer = 0f;
	private const float SearchDuration = 6f;    // seconds to search before giving up
	private const float SearchArrivalRadius = 80f; // how close to arrive before giving up

	protected override void OnAwake()
	{
		characterController = Components.Get<CharacterController>();
		health = Components.Get<HealthComponent>();
		animationHelper = Components.Get<CitizenAnimationHelper>();
		var render = GetRenderer();

		if ( render is not null )
		{
			originalColor = render.Tint;
			Log.Info( $"Enemy found ModelRenderer, originalColor = {originalColor}" );
		}
		else
		{
			Log.Warning( "Enemy has no ModelRenderer - flash won't work" );
		}

		// Find player in scene - prefer PlayerReference if manually assigned
		if ( PlayerReference is not null )
		{
			player = PlayerReference;
			Log.Info( "Enemy: Using manually assigned PlayerReference" );
		}
		else
		{
			player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;
		}

		if ( characterController is null )
		{
			Log.Warning( "Enemy missing CharacterController component" );
		}

		if ( health is null )
		{
			Log.Warning( "Enemy missing HealthComponent component" );
		}

		if ( player is null )
		{
			Log.Warning( "Enemy could not find player in scene" );
		}
		else
		{
			Log.Info( $"Enemy found player at {player.WorldPosition}" );
		}

		// Listen to health events
		if ( health is not null )
		{
			health.OnDeath += OnEnemyDeath;
			health.OnDamageTakenWithAttacker += OnEnemyDamaged;
		}

		Log.Info( $"Enemy spawned with {PatrolPoints.Count} patrol points" );
	}

	protected override void OnUpdate()
	{
		if ( !Enabled || player is null || !player.Active || characterController is null )
			return;

		// Update flash timer
		if ( flashTimer > 0f )
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
			currentState = AIState.Idle;
			moveDirection = Vector3.Zero;
			UpdateAnimations(); // still run so DuckLevel flinch shows
			return;
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

		if ( canDetect )
		{
			lastKnownPlayerPos = player.WorldPosition;
			searchTimer = SearchDuration;

			if ( distanceToPlayer < AttackRange )
				currentState = AIState.Attack;
			else
				currentState = AIState.Chase;
		}
		else if ( currentState == AIState.Chase || currentState == AIState.Attack )
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
			// Use the captured knockback direction so the anim plays a
			// backstep regardless of how fast friction kills the velocity
			animationHelper.WithWishVelocity( staggerDirection * PatrolSpeed );
			animationHelper.MoveStyle = CitizenAnimationHelper.MoveStyles.Walk;
		}
		else
		{
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
			Log.Info( $"Enemy hit downed player: -{AttackDamage * 0.08f:F1} bleed. Bar: {incap.BleedOutHealth:F0}%" );
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
			.WithoutTags( "trigger" )
			.WithoutTags( "headzone" ) // ignore enemy head hitboxes — they're children not covered by IgnoreGameObject
			.IgnoreGameObject( GameObject )
			.IgnoreGameObject( player )  // Don't stop at player collider
			.Run();

		// If ray didn't hit anything, we have clear LOS
		if ( !trace.Hit || trace.Distance >= distanceToPlayer - 5f )
			return true;

		return false;
	}

	void OnEnemyDeath()
	{
		currentState = AIState.Idle;

		moveDirection = Vector3.Zero;

		if ( animationHelper is not null )
		{
			animationHelper.WithVelocity( Vector3.Zero );
			animationHelper.WithWishVelocity( Vector3.Zero );
			animationHelper.IsGrounded = true;
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

		// True per-bone ragdoll via ModelPhysics
		var skinnedRenderer = Components.Get<SkinnedModelRenderer>();
		if ( skinnedRenderer is not null )
		{
			skinnedRenderer.Tint = Color.White;

			var modelPhysics = Components.GetOrCreate<ModelPhysics>();
			modelPhysics.Renderer = skinnedRenderer;
			modelPhysics.Model = skinnedRenderer.Model;

			_ = FreezeRagdollAfterDelay( modelPhysics );
		}

		Enabled = false;
		PlayerStats.TotalKills++;
		Log.Info( "Enemy died" );
	}

	void OnEnemyDamaged( float damageAmount, GameObject attacker )
	{
		if ( characterController is null || attacker is null )
			return;

		ApplyDamageEffects( damageAmount, attacker, isHeadshot: false );
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

		ApplyDamageEffects( damageAmount, attacker, isHeadshot: true );
	}

	private void ApplyDamageEffects( float damageAmount, GameObject attacker, bool isHeadshot )
	{
		Vector3 directionFromAttacker = (WorldPosition - attacker.WorldPosition).Normal;

		if ( isHeadshot )
		{
			// Full knockback on headshots
			Vector3 knockbackImpulse = (directionFromAttacker * KnockbackForce) + (Vector3.Up * KnockbackUpForce);
			characterController.Punch( knockbackImpulse );
		}
		else if ( staggerTimer <= 0f )
		{
			// Small pistol-scale nudge — only on first hit to prevent stacking
			Vector3 bodyImpulse = directionFromAttacker * (KnockbackForce * 0.1f);
			characterController.Punch( bodyImpulse );
		}

		// Calculate stagger with headshot multiplier
		float staggerMultiplier = isHeadshot ? HeadshotStaggerMultiplier : 1.0f;
		float addStagger = (StaggerDuration + (damageAmount * StaggerDamageScale)) * staggerMultiplier;
		staggerTimer = System.Math.Min( System.Math.Max( staggerTimer, addStagger ), StaggerMaxTime );
		staggerDirection = directionFromAttacker; // direction away from attacker, used for anim
		Log.Info( $"Enemy stagger: damage={damageAmount}, multiplier={staggerMultiplier:F1}x, new stagger time={staggerTimer:F2}s" );

		if ( PainSound != null )
			Sound.Play( PainSound, WorldPosition );

		// Flash the model
		var render = GetRenderer();
		if ( render is not null )
		{
			Log.Info( $"Enemy flashing from {render.Tint} to {FlashColor}" );
			render.Tint = FlashColor;
			flashTimer = FlashDuration;
		}
		else
		{
			Log.Warning( "Enemy damage: no ModelRenderer found for flash" );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( characterController is null || !Enabled )
			return;

		var gravity = Scene.PhysicsWorld.Gravity;

		if ( staggerTimer > 0f )
		{
			// Stagger: apply gravity, let knockback carry, skip movement input
			if ( !characterController.IsOnGround )
				characterController.Velocity += gravity * Time.Delta * 0.5f;

			characterController.ApplyFriction( 4.0f );
			characterController.Move();

			if ( !characterController.IsOnGround )
				characterController.Velocity += gravity * Time.Delta * 0.5f;
			else
				characterController.Velocity = characterController.Velocity.WithZ( 0 );
			return;
		}

		// Normal movement — same Verlet gravity integration as the player
		if ( characterController.IsOnGround )
		{
			characterController.Velocity = characterController.Velocity.WithZ( 0 );
			characterController.Accelerate( moveDirection );
			characterController.ApplyFriction( 4.0f );
		}
		else
		{
			characterController.Velocity += gravity * Time.Delta * 0.5f;
			characterController.Accelerate( moveDirection );
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
		if ( health is not null )
		{
			health.OnDeath -= OnEnemyDeath;
			health.OnDamageTakenWithAttacker -= OnEnemyDamaged;
		}
	}

	async Task FreezeRagdollAfterDelay( ModelPhysics modelPhysics )
	{
		// Let it fall for 2 seconds, then freeze so it stops flopping
		await Task.DelaySeconds( 2f );
		if ( modelPhysics is null || !modelPhysics.IsValid() ) return;

		if ( modelPhysics.PhysicsGroup is not null )
		{
			foreach ( var body in modelPhysics.PhysicsGroup.Bodies )
				body.MotionEnabled = false;
		}
	}

	/// <summary>
	/// Reset the enemy to a fresh spawned state (call this when respawning)
	/// </summary>
	public void ResetSpawnState()
	{
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

		// Force color to alive state (white/normal)
		var render = GetRenderer();
		if ( render is not null )
		{
			render.Tint = Color.White;
			originalColor = Color.White;
		}

		Log.Info( "Enemy: Reset spawn state - color forced to white" );
	}
}
