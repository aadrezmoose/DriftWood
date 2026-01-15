using Sandbox;
using System.Collections.Generic;

public sealed class Enemy : Component, Component.ITriggerListener
{
	[Property] public float PatrolSpeed { get; set; } = 100f;
	[Property] public float ChaseSpeed { get; set; } = 200f;
	[Property] public float StoppingDistance { get; set; } = 50f;
	[Property] public float DetectionRange { get; set; } = 500f;
	[Property] public float AttackRange { get; set; } = 150f;
	[Property] public float AttackCooldown { get; set; } = 1.5f;
	[Property] public float AttackDamage { get; set; } = 100f;
	[Property] public float KnockbackForce { get; set; } = 300f;
	[Property] public float KnockbackUpForce { get; set; } = 200f;
	[Property] public List<Transform> PatrolPoints { get; set; } = new();
	[Property] public Color FlashColor { get; set; } = Color.White;
	[Property] public float FlashDuration { get; set; } = 0.2f;
	[Property] public SoundEvent DeathSound { get; set; }

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
		Attack
	}

	private AIState currentState = AIState.Patrol;
	private GameObject player;
	private CharacterController characterController;
	private HealthComponent health;
	private int currentWaypointIndex = 0;
	private float attackCooldownTimer = 0f;
	private Vector3 moveDirection = Vector3.Zero;

	protected override void OnAwake()
	{
		characterController = Components.Get<CharacterController>();
		health = Components.Get<HealthComponent>();
		var render = Components.Get<ModelRenderer>();

		if ( render is not null )
		{
			originalColor = render.Tint;
			Log.Info( $"Enemy found ModelRenderer, originalColor = {originalColor}" );
		}
		else
		{
			Log.Warning( "Enemy has no ModelRenderer - flash won't work" );
		}

		// Find player in scene
		player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;

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
			Log.Info( $"Enemy found player at {player.Transform.Position}" );
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
		if ( !Enabled || player is null || characterController is null )
		{
			if ( player is null )
				Log.Warning( "Enemy update skipped: player is null" );
			return;
		}

		// Update flash timer
		if ( flashTimer > 0f )
		{
			flashTimer -= Time.Delta;
			if ( flashTimer <= 0f )
			{
				var render = Components.Get<ModelRenderer>();
				if ( render is not null )
					render.Tint = originalColor;
			}
		}

		// Update attack cooldown
		if ( attackCooldownTimer > 0f )
			attackCooldownTimer -= Time.Delta;

		// Handle stagger: temporarily disable movement/attacks
		if ( staggerTimer > 0f )
		{
			staggerTimer -= Time.Delta;
			currentState = AIState.Idle;
			moveDirection = Vector3.Zero; // Stop new movement input, but preserve knockback velocity
			return; // skip AI while staggered
		}

		// Determine state
		float distanceToPlayer = (Transform.Position - player.Transform.Position).Length;
		bool hasLineOfSight = HasLineOfSightToPlayer();

		if ( health is not null && health.IsDead )
		{
			currentState = AIState.Idle;
			characterController.Velocity = Vector3.Zero;
			return;
		}

		// State transitions
		if ( distanceToPlayer < DetectionRange && hasLineOfSight )
		{
			if ( distanceToPlayer < AttackRange )
			{
				currentState = AIState.Attack;
			}
			else
			{
				currentState = AIState.Chase;
			}
		}
		else
		{
			currentState = AIState.Patrol;
		}

		// Execute state behavior
		switch ( currentState )
		{
			case AIState.Patrol:
				PatrolBehavior();
				break;
			case AIState.Chase:
				ChaseBehavior( player.Transform.Position );
				break;
			case AIState.Attack:
				AttackBehavior( player );
				break;
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
		Vector3 directionToWaypoint = (targetPoint.Position - Transform.Position).Normal;
		float distanceToWaypoint = (Transform.Position - targetPoint.Position).Length;

		if ( distanceToWaypoint < StoppingDistance )
		{
			currentWaypointIndex = (currentWaypointIndex + 1) % PatrolPoints.Count;
		}

		moveDirection = directionToWaypoint * PatrolSpeed;
	}

	void ChaseBehavior( Vector3 playerPosition )
	{
		Vector3 directionToPlayer = (playerPosition - Transform.Position).Normal;
		moveDirection = directionToPlayer * ChaseSpeed;

		// Face the player
		Transform.Rotation = Rotation.LookAt( directionToPlayer, Vector3.Up );
	}

	void AttackBehavior( GameObject targetPlayer )
	{
		moveDirection = Vector3.Zero;

		// Face the player
		Vector3 directionToPlayer = (targetPlayer.Transform.Position - Transform.Position).Normal;
		Transform.Rotation = Rotation.LookAt( directionToPlayer, Vector3.Up );

		// Attack if cooldown is ready
		if ( attackCooldownTimer <= 0f )
		{
			PerformAttack( targetPlayer );
			attackCooldownTimer = AttackCooldown;
		}
	}

	void PerformAttack( GameObject targetPlayer )
	{
		// Deal damage to player
		var targetHealth = targetPlayer.Components.Get<HealthComponent>();
		if ( targetHealth is not null )
		{
			targetHealth.TakeDamage( AttackDamage, GameObject );
		}
		else
		{
			// Try to find health in descendants/ancestors
			targetHealth = targetPlayer.Components.GetInDescendantsOrSelf<HealthComponent>();
			if ( targetHealth is not null )
			{
				targetHealth.TakeDamage( AttackDamage, GameObject );
				Log.Info( $"Enemy attacked player (found in descendants) for {AttackDamage} damage. Player health: {targetHealth.CurrentHealth}/{targetHealth.MaxHealth}" );
			}
			else
			{
				Log.Warning( $"Enemy attack failed - no HealthComponent found on player GameObject: {targetPlayer.Name}" );
			}
		}
	}

	bool HasLineOfSightToPlayer()
	{
		if ( player is null )
			return false;

		Vector3 directionToPlayer = (player.Transform.Position - Transform.Position).Normal;
		float distanceToPlayer = (Transform.Position - player.Transform.Position).Length;

		// Start trace from slightly above the enemy (eye level)
		Vector3 startPos = Transform.Position + Vector3.Up * 40f;
		Vector3 endPos = player.Transform.Position + Vector3.Up * 40f;

		var trace = Scene.Trace.Ray( startPos, endPos )
			.WithoutTags( "trigger", "nosight" )
			.IgnoreGameObject( GameObject )
			.Run();

		// Draw debug line to visualize
		if ( trace.Hit && trace.Distance < distanceToPlayer - 10f )
		{
			Gizmo.Draw.Line( startPos, trace.EndPosition );
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.LineSphere( trace.EndPosition, 10f );
			Log.Info( $"{GameObject.Name}: LOS BLOCKED by {trace.GameObject?.Name} at distance {trace.Distance:F1}" );
			return false;
		}

		Gizmo.Draw.Color = Color.Green;
		Gizmo.Draw.Line( startPos, endPos );
		return true;
	}

	void OnEnemyDeath()
	{
		currentState = AIState.Idle;
		moveDirection = Vector3.Zero;
		
		if ( characterController is not null )
		{
			characterController.Velocity = Vector3.Zero;
		}

		// Play death sound
		if ( DeathSound != null )
		{
			Sound.Play( "sounds/click7", Transform.Position );
		}

		// Convert to ragdoll
		var modelRenderer = Components.Get<ModelRenderer>();
		if ( modelRenderer != null && modelRenderer.Model != null )
		{
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

		Log.Info( "Enemy died" );
	}

	void OnEnemyDamaged( float damageAmount, GameObject attacker )
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
		Vector3 directionFromAttacker = (Transform.Position - attacker.Transform.Position).Normal;

		// Apply knockback impulse with upward component
		Vector3 knockbackImpulse = (directionFromAttacker * KnockbackForce) + (Vector3.Up * KnockbackUpForce);
		characterController.Punch( knockbackImpulse );
		Log.Info( $"Enemy knockback applied: impulse magnitude={knockbackImpulse.Length}" );

		// Calculate stagger with headshot multiplier
		float staggerMultiplier = isHeadshot ? HeadshotStaggerMultiplier : 1.0f;
		float addStagger = (StaggerDuration + (damageAmount * StaggerDamageScale)) * staggerMultiplier;
		staggerTimer = System.Math.Min( System.Math.Max( staggerTimer, addStagger ), StaggerMaxTime );
		Log.Info( $"Enemy stagger: damage={damageAmount}, multiplier={staggerMultiplier:F1}x, new stagger time={staggerTimer:F2}s" );

		// Flash the model
		var render = Components.Get<ModelRenderer>();
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

		// During stagger: apply gravity and friction, skip acceleration/movement input
		if ( staggerTimer > 0f )
		{
			// Apply gravity (gravity.z is negative, so add it to pull down)
			characterController.Velocity = characterController.Velocity.WithZ( characterController.Velocity.z + (Scene.PhysicsWorld.Gravity.z * Time.Delta) );
			characterController.ApplyFriction( 4.0f );
			characterController.Move();
			return;
		}

		// Normal movement: apply input and friction
		characterController.Velocity = characterController.Velocity.WithZ( 0 );
		characterController.Accelerate( moveDirection );
		characterController.ApplyFriction( 4.0f );
		characterController.Move();
	}

	protected override void OnDestroy()
	{
		if ( health is not null )
		{
			health.OnDeath -= OnEnemyDeath;
			health.OnDamageTakenWithAttacker -= OnEnemyDamaged;
		}
	}
}
