using Sandbox;
using System.Collections.Generic;

public sealed class Tank : Component, Component.ITriggerListener
{
	// Tank Stats - Much higher than regular enemies
	[Property] public float MaxHealth { get; set; } = 1500f;
	[Property] public float WalkSpeed { get; set; } = 150f;
	[Property] public float ChargeSpeed { get; set; } = 600f;
	[Property] public float PunchDamage { get; set; } = 40f;
	[Property] public float PunchKnockback { get; set; } = 800f;
	[Property] public float PunchKnockbackUp { get; set; } = 300f;
	[Property] public float ChargeRange { get; set; } = 500f;
	[Property] public float PunchRange { get; set; } = 200f;
	[Property] public float MinChargeRange { get; set; } = 200f;
	[Property] public float PunchCooldown { get; set; } = 2.0f;
	[Property] public float ChargeDuration { get; set; } = 2.5f;

	// Visual effects
	[Property] public Color FlashColor { get; set; } = Color.White;
	[Property] public float FlashDuration { get; set; } = 0.2f;
	[Property] public SoundEvent DeathSound { get; set; }
	[Property] public GameObject PlayerReference { get; set; } // Allow manual assignment from spawner

	// Stagger settings
	[Property] public float StaggerDuration { get; set; } = 0.2f;
	[Property] public float StaggerDamageScale { get; set; } = 0.005f; // Half of normal enemy (Tank is tougher)
	[Property] public float StaggerMaxTime { get; set; } = 0.8f;
	[Property] public float HeadshotStaggerMultiplier { get; set; } = 1.2f;

	private Color originalColor = Color.White;
	private float flashTimer = 0f;
	private float staggerTimer = 0f;

	private enum TankState
	{
		Idle,
		Chase,
		Charging,
		Punching
	}

	private TankState currentState = TankState.Chase;
	private GameObject player;
	private CharacterController characterController;
	private HealthComponent health;
	private ModelRenderer modelRenderer;
	private float punchCooldownTimer = 0f;
	private float chargeTimer = 0f;
	private Vector3 moveDirection = Vector3.Zero;
	private Vector3 chargeDirection = Vector3.Zero;

	protected override void OnAwake()
	{
		// Cache components
		characterController = Components.Get<CharacterController>();
		health = Components.Get<HealthComponent>();
		modelRenderer = Components.Get<ModelRenderer>();

		if ( modelRenderer is not null )
		{
			originalColor = modelRenderer.Tint;
			Log.Info( $"Tank found ModelRenderer, originalColor = {originalColor}" );
		}
		else
		{
			Log.Warning( "Tank has no ModelRenderer - flash won't work" );
		}

		// Find player in scene - prefer PlayerReference if manually assigned
		if ( PlayerReference is not null )
		{
			player = PlayerReference;
			Log.Info( "Tank: Using manually assigned PlayerReference" );
		}
		else
		{
			player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;
		}

		if ( characterController is null )
		{
			Log.Warning( "Tank missing CharacterController component" );
		}

		if ( health is null )
		{
			Log.Warning( "Tank missing HealthComponent component" );
		}
		else
		{
			// Set Tank health to MaxHealth property
			health.MaxHealth = MaxHealth;
			health.CurrentHealth = MaxHealth;
		}

		if ( player is null )
		{
			Log.Warning( "Tank could not find player in scene" );
		}
		else
		{
			Log.Info( $"Tank found player at {player.WorldPosition}" );
		}

		// Listen to health events
		if ( health is not null )
		{
			health.OnDeath += OnTankDeath;
			health.OnDamageTakenWithAttacker += OnTankDamaged;
		}

		Log.Info( $"Tank spawned with {MaxHealth} HP" );
	}

	protected override void OnUpdate()
	{
		if ( !Enabled || player is null || characterController is null )
		{
			if ( player is null )
				Log.Warning( "Tank update skipped: player is null" );
			return;
		}

		// Update flash timer
		if ( flashTimer > 0f )
		{
			flashTimer -= Time.Delta;
			if ( flashTimer <= 0f )
			{
				if ( modelRenderer is not null )
					modelRenderer.Tint = originalColor;
			}
		}

		// Update punch cooldown
		if ( punchCooldownTimer > 0f )
			punchCooldownTimer -= Time.Delta;

		// Update charge timer
		if ( chargeTimer > 0f )
			chargeTimer -= Time.Delta;

		// Handle stagger: temporarily disable movement/attacks
		if ( staggerTimer > 0f )
		{
			staggerTimer -= Time.Delta;
			currentState = TankState.Idle;
			moveDirection = Vector3.Zero;
			return; // skip AI while staggered
		}

		if ( health is not null && health.IsDead )
		{
			currentState = TankState.Idle;
			characterController.Velocity = Vector3.Zero;
			return;
		}

		// Determine state based on distance to player
		float distanceToPlayer = (WorldPosition - player.WorldPosition).Length;
		bool hasLineOfSight = HasLineOfSightToPlayer();

		// State transitions
		if ( !hasLineOfSight )
		{
			currentState = TankState.Chase;
		}
		else if ( chargeTimer > 0f )
		{
			currentState = TankState.Charging;
		}
		else if ( distanceToPlayer < PunchRange )
		{
			currentState = TankState.Punching;
		}
		else if ( distanceToPlayer >= MinChargeRange && distanceToPlayer <= ChargeRange )
		{
			// Initiate charge
			currentState = TankState.Charging;
			chargeTimer = ChargeDuration;
			chargeDirection = (player.WorldPosition - WorldPosition).Normal;
			Log.Info( $"Tank initiating charge at distance {distanceToPlayer}" );
		}
		else
		{
			currentState = TankState.Chase;
		}

		// Execute state behavior
		switch ( currentState )
		{
			case TankState.Chase:
				ChaseBehavior( player.WorldPosition );
				break;
			case TankState.Charging:
				ChargeBehavior();
				break;
			case TankState.Punching:
				PunchBehavior( player );
				break;
		}
	}

	void ChaseBehavior( Vector3 playerPosition )
	{
		Vector3 directionToPlayer = (playerPosition - WorldPosition).Normal;
		moveDirection = directionToPlayer * WalkSpeed;

		// Face the player
		WorldRotation = Rotation.LookAt( directionToPlayer, Vector3.Up );
	}

	void ChargeBehavior()
	{
		// Move in charge direction at high speed
		moveDirection = chargeDirection * ChargeSpeed;

		// Keep facing charge direction
		WorldRotation = Rotation.LookAt( chargeDirection, Vector3.Up );

		// Check for collision with player during charge
		float distanceToPlayer = (WorldPosition - player.WorldPosition).Length;
		if ( distanceToPlayer < PunchRange )
		{
			// Hit player during charge
			PerformPunch( player );
			chargeTimer = 0f; // End charge
		}
	}

	void PunchBehavior( GameObject targetPlayer )
	{
		moveDirection = Vector3.Zero;

		// Face the player
		Vector3 directionToPlayer = (targetPlayer.WorldPosition - WorldPosition).Normal;
		WorldRotation = Rotation.LookAt( directionToPlayer, Vector3.Up );

		// Punch if cooldown is ready
		if ( punchCooldownTimer <= 0f )
		{
			PerformPunch( targetPlayer );
			punchCooldownTimer = PunchCooldown;
		}
	}

	void PerformPunch( GameObject targetPlayer )
	{
		// Deal damage to player
		var targetHealth = targetPlayer.Components.Get<HealthComponent>();
		if ( targetHealth is not null )
		{
			targetHealth.TakeDamage( PunchDamage, GameObject );
			Log.Info( $"Tank punched player for {PunchDamage} damage. Player health: {targetHealth.CurrentHealth}/{targetHealth.MaxHealth}" );
		}
		else
		{
			// Try to find health in descendants/ancestors
			targetHealth = targetPlayer.Components.GetInDescendantsOrSelf<HealthComponent>();
			if ( targetHealth is not null )
			{
				targetHealth.TakeDamage( PunchDamage, GameObject );
				Log.Info( $"Tank punched player (found in descendants) for {PunchDamage} damage. Player health: {targetHealth.CurrentHealth}/{targetHealth.MaxHealth}" );
			}
			else
			{
				Log.Warning( $"Tank punch failed - no HealthComponent found on player GameObject: {targetPlayer.Name}" );
			}
		}

		// Apply knockback to player
		var playerController = targetPlayer.Components.Get<CharacterController>();
		if ( playerController is not null )
		{
			Vector3 directionToPlayer = (targetPlayer.WorldPosition - WorldPosition).Normal;
			Vector3 knockbackImpulse = (directionToPlayer * PunchKnockback) + (Vector3.Up * PunchKnockbackUp);
			playerController.Punch( knockbackImpulse );
			Log.Info( $"Tank applied massive knockback: impulse magnitude={knockbackImpulse.Length}" );
		}
	}

	bool HasLineOfSightToPlayer()
	{
		if ( player is null )
			return false;

		Vector3 directionToPlayer = (player.WorldPosition - WorldPosition).Normal;
		float distanceToPlayer = (player.WorldPosition - WorldPosition).Length;

		// Start trace from Tank's head level
		Vector3 startPos = WorldPosition + Vector3.Up * 80f;
		Vector3 playerEyePos = player.WorldPosition + Vector3.Up * 80f;

		var trace = Scene.Trace.Ray( startPos, playerEyePos )
			.WithoutTags( "trigger" )
			.IgnoreGameObject( GameObject )
			.IgnoreGameObject( player )
			.Run();

		// If ray didn't hit anything, we have clear LOS
		if ( !trace.Hit || trace.Distance >= distanceToPlayer - 5f )
		{
			return true;
		}

		return false;
	}

	void OnTankDeath()
	{
		currentState = TankState.Idle;
		moveDirection = Vector3.Zero;

		if ( characterController is not null )
		{
			characterController.Velocity = Vector3.Zero;
		}

		// Play death sound
		if ( DeathSound != null )
		{
			Sound.Play( "sounds/click7", WorldPosition );
		}

		// Convert to ragdoll
		if ( modelRenderer != null && modelRenderer.Model != null )
		{
			// Set to white (dead color)
			modelRenderer.Tint = Color.White;

			// Disable character controller
			if ( characterController != null )
			{
				characterController.Enabled = false;
			}

			// Create physics body for ragdoll effect
			var rigidbody = Components.GetOrCreate<Rigidbody>();
			rigidbody.PhysicsBody.MotionEnabled = true;
			rigidbody.PhysicsBody.GravityEnabled = true;

			// Add some random spin for dramatic effect
			rigidbody.PhysicsBody.AngularVelocity = Vector3.Random * 2f;
		}

		// Disable AI
		Enabled = false;

		Log.Info( "Tank died" );
	}

	void OnTankDamaged( float damageAmount, GameObject attacker )
	{
		if ( characterController is null || attacker is null )
			return;

		ApplyDamageEffects( damageAmount, attacker, isHeadshot: false );
	}

	public void OnHeadshotDamage( float damageAmount, GameObject attacker )
	{
		if ( characterController is null || attacker is null )
			return;

		ApplyDamageEffects( damageAmount, attacker, isHeadshot: true );
	}

	private void ApplyDamageEffects( float damageAmount, GameObject attacker, bool isHeadshot )
	{
		// Calculate direction away from attacker
		Vector3 directionFromAttacker = (WorldPosition - attacker.WorldPosition).Normal;

		// Apply reduced knockback (Tank is heavy)
		Vector3 knockbackImpulse = (directionFromAttacker * 150f) + (Vector3.Up * 100f);
		characterController.Punch( knockbackImpulse );

		// Calculate stagger with headshot multiplier (Tank has lower stagger)
		float staggerMultiplier = isHeadshot ? HeadshotStaggerMultiplier : 1.0f;
		float addStagger = (StaggerDuration + (damageAmount * StaggerDamageScale)) * staggerMultiplier;
		staggerTimer = System.Math.Min( System.Math.Max( staggerTimer, addStagger ), StaggerMaxTime );
		Log.Info( $"Tank stagger: damage={damageAmount}, multiplier={staggerMultiplier:F1}x, new stagger time={staggerTimer:F2}s" );

		// Flash the model
		if ( modelRenderer is not null )
		{
			Log.Info( $"Tank flashing from {modelRenderer.Tint} to {FlashColor}" );
			modelRenderer.Tint = FlashColor;
			flashTimer = FlashDuration;
		}
		else
		{
			Log.Warning( "Tank damage: no ModelRenderer found for flash" );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( characterController is null || !Enabled )
			return;

		// During stagger: apply gravity and friction, skip acceleration/movement input
		if ( staggerTimer > 0f )
		{
			// Apply gravity
			var vel = characterController.Velocity;
			vel.z += (Scene.PhysicsWorld.Gravity.z * Time.Delta);

			// Clamp vertical velocity
			vel.z = System.Math.Max( vel.z, -500f );

			characterController.Velocity = vel;
			characterController.ApplyFriction( 4.0f );
			characterController.Move();
			return;
		}

		// Normal movement: only set horizontal velocity, let gravity handle vertical
		var currentVel = characterController.Velocity;
		characterController.Velocity = new Vector3( moveDirection.x, moveDirection.y, currentVel.z );
		characterController.Accelerate( moveDirection );
		characterController.ApplyFriction( 4.0f );
		characterController.Move();
	}

	protected override void OnDestroy()
	{
		if ( health is not null )
		{
			health.OnDeath -= OnTankDeath;
			health.OnDamageTakenWithAttacker -= OnTankDamaged;
		}
	}

	/// <summary>
	/// Reset the Tank to a fresh spawned state (call this when respawning)
	/// </summary>
	public void ResetSpawnState()
	{
		// Reset all timers
		staggerTimer = 0f;
		flashTimer = 0f;
		punchCooldownTimer = 0f;
		chargeTimer = 0f;
		moveDirection = Vector3.Zero;
		chargeDirection = Vector3.Zero;
		currentState = TankState.Chase;

		// Force color to alive state
		if ( modelRenderer is not null )
		{
			modelRenderer.Tint = Color.White;
			originalColor = Color.White;
		}

		Log.Info( "Tank: Reset spawn state - color forced to white" );
	}
}
