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
	[Property] public List<Transform> PatrolPoints { get; set; } = new();

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

		// Update attack cooldown
		if ( attackCooldownTimer > 0f )
			attackCooldownTimer -= Time.Delta;

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
			.WithoutTags( "trigger" )
			.IgnoreGameObject( GameObject )
			.Run();

		// Draw debug line to visualize
		if ( trace.Hit && trace.Distance < distanceToPlayer - 10f )
		{
			Gizmo.Draw.Line( startPos, trace.EndPosition );
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.LineSphere( trace.EndPosition, 10f );
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

		Log.Info( "Enemy died" );
	}

	protected override void OnFixedUpdate()
	{
		if ( characterController is null || !Enabled )
			return;

		// Simple movement without gravity
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
		}
	}
}
