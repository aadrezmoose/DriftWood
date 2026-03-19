using Sandbox;
using System;

public sealed class Hunter : Component
{
	// Properties
	[Property] public float MaxHealth { get; set; } = 250f;
	[Property] public float PounceRange { get; set; } = 800f;
	[Property] public float PounceSpeed { get; set; } = 1200f;
	[Property] public float PounceDamageInitial { get; set; } = 25f;
	[Property] public float PinDamageRate { get; set; } = 10f; // per second
	[Property] public float CrouchTime { get; set; } = 1.5f; // telegraph before pounce
	[Property] public float ChaseSpeed { get; set; } = 250f;
	[Property] public float DetectionRange { get; set; } = 600f;
	[Property] public Color FlashColor { get; set; } = Color.White;
	[Property] public float FlashDuration { get; set; } = 0.2f;
	[Property] public GameObject PlayerReference { get; set; } // Allow manual assignment from spawner

	// State machine
	private enum HunterState
	{
		Idle,
		Chase,
		Crouching,
		Pouncing,
		Pinning
	}

	private HunterState currentState = HunterState.Idle;
	private GameObject player;
	private CharacterController characterController;
	private HealthComponent health;
	private ModelRenderer modelRenderer;
	private Color originalColor = Color.White;
	private float flashTimer = 0f;

	// State timers
	private float crouchTimer = 0f;
	private float pinDamageTimer = 0f;
	private GameObject pinnedPlayer = null;
	private Vector3 pounceDirection = Vector3.Zero;
	private Vector3 moveDirection = Vector3.Zero;

	protected override void OnAwake()
	{
		// Cache components
		characterController = Components.Get<CharacterController>();
		health = Components.Get<HealthComponent>();
		modelRenderer = Components.Get<ModelRenderer>();

		if (modelRenderer is not null)
		{
			originalColor = modelRenderer.Tint;
			Log.Info($"Hunter found ModelRenderer, originalColor = {originalColor}");
		}
		else
		{
			Log.Warning("Hunter has no ModelRenderer - flash won't work");
		}

		// Find player in scene - prefer PlayerReference if manually assigned
		if (PlayerReference is not null)
		{
			player = PlayerReference;
			Log.Info("Hunter: Using manually assigned PlayerReference");
		}
		else
		{
			player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;
		}

		if (characterController is null)
		{
			Log.Warning("Hunter missing CharacterController component");
		}

		if (health is null)
		{
			Log.Warning("Hunter missing HealthComponent component");
		}
		else
		{
			// Set health to configured value
			health.MaxHealth = MaxHealth;
			health.CurrentHealth = MaxHealth;

			// Listen to health events
			health.OnDeath += OnHunterDeath;
			health.OnDamageTakenWithAttacker += OnHunterDamaged;
		}

		if (player is null)
		{
			Log.Warning("Hunter could not find player in scene");
		}
		else
		{
			Log.Info($"Hunter found player at {player.WorldPosition}");
		}

		Log.Info("Hunter spawned successfully");
	}

	protected override void OnUpdate()
	{
		if (!Enabled || player is null || characterController is null)
		{
			return;
		}

		// Update flash timer
		if (flashTimer > 0f)
		{
			flashTimer -= Time.Delta;
			if (flashTimer <= 0f)
			{
				if (modelRenderer is not null)
					modelRenderer.Tint = originalColor;
			}
		}

		// Skip AI if dead
		if (health is not null && health.IsDead)
		{
			currentState = HunterState.Idle;
			characterController.Velocity = Vector3.Zero;
			return;
		}

		// State machine
		switch (currentState)
		{
			case HunterState.Idle:
				IdleBehavior();
				break;
			case HunterState.Chase:
				ChaseBehavior();
				break;
			case HunterState.Crouching:
				CrouchingBehavior();
				break;
			case HunterState.Pouncing:
				PouncingBehavior();
				break;
			case HunterState.Pinning:
				PinningBehavior();
				break;
		}
	}

	void IdleBehavior()
	{
		// Check for player in detection range
		float distanceToPlayer = (WorldPosition - player.WorldPosition).Length;
		bool hasLineOfSight = HasLineOfSightToPlayer();

		if (distanceToPlayer < DetectionRange && hasLineOfSight)
		{
			currentState = HunterState.Chase;
		}

		moveDirection = Vector3.Zero;
	}

	void ChaseBehavior()
	{
		float distanceToPlayer = (WorldPosition - player.WorldPosition).Length;
		bool hasLineOfSight = HasLineOfSightToPlayer();

		// Check if we're in pounce range
		if (distanceToPlayer <= PounceRange && hasLineOfSight)
		{
			// Start crouching (telegraph)
			currentState = HunterState.Crouching;
			crouchTimer = CrouchTime;
			moveDirection = Vector3.Zero;
			Log.Info("Hunter entering crouch state");
			return;
		}

		// Chase player
		Vector3 directionToPlayer = (player.WorldPosition - WorldPosition).Normal;
		moveDirection = directionToPlayer * ChaseSpeed;

		// Face the player
		WorldRotation = Rotation.LookAt(directionToPlayer, Vector3.Up);
	}

	void CrouchingBehavior()
	{
		// Stay still and face player during crouch
		moveDirection = Vector3.Zero;
		Vector3 directionToPlayer = (player.WorldPosition - WorldPosition).Normal;
		WorldRotation = Rotation.LookAt(directionToPlayer, Vector3.Up);

		crouchTimer -= Time.Delta;

		if (crouchTimer <= 0f)
		{
			// Launch pounce
			pounceDirection = (player.WorldPosition - WorldPosition).Normal;
			currentState = HunterState.Pouncing;
			Log.Info("Hunter launching pounce!");
		}
	}

	void PouncingBehavior()
	{
		// High-speed lunge toward player
		// Use velocity instead of moveDirection for more dramatic pounce
		characterController.Velocity = pounceDirection * PounceSpeed;

		// Check for collision with player
		if (CheckPounceCollision())
		{
			OnSuccessfulPounce();
		}
	}

	void PinningBehavior()
	{
		// Stay on top of player
		moveDirection = Vector3.Zero;

		if (pinnedPlayer is null || pinnedPlayer.IsValid == false)
		{
			// Player escaped or died
			ReleasePlayer();
			return;
		}

		// Stay attached to player position
		WorldPosition = pinnedPlayer.WorldPosition + Vector3.Up * 50f;

		// Apply damage over time
		pinDamageTimer += Time.Delta;
		if (pinDamageTimer >= 1f)
		{
			var playerHealth = pinnedPlayer.Components.GetInDescendantsOrSelf<HealthComponent>();
			if (playerHealth is not null && !playerHealth.IsDead)
			{
				playerHealth.TakeDamage(PinDamageRate, GameObject);
				Log.Info($"Hunter dealing pin damage: {PinDamageRate}");
			}
			pinDamageTimer = 0f;
		}
	}

	bool CheckPounceCollision()
	{
		// Check if we're close enough to player to pin them
		float distanceToPlayer = (WorldPosition - player.WorldPosition).Length;
		return distanceToPlayer < 100f; // Close enough to pin
	}

	void OnSuccessfulPounce()
	{
		Log.Info("Hunter successfully pounced player!");

		// Deal initial pounce damage
		var playerHealth = player.Components.GetInDescendantsOrSelf<HealthComponent>();
		if (playerHealth is not null)
		{
			playerHealth.TakeDamage(PounceDamageInitial, GameObject);
		}

		// Pin the player
		var playerMovement = player.Components.Get<PlayerMovement>();
		if (playerMovement is not null)
		{
			playerMovement.IsPinned = true;
			Log.Info("Player pinned by Hunter");
		}

		pinnedPlayer = player;
		currentState = HunterState.Pinning;
		pinDamageTimer = 0f;
	}

	void ReleasePlayer()
	{
		Log.Info("Hunter releasing player");

		if (pinnedPlayer is not null && pinnedPlayer.IsValid)
		{
			var playerMovement = pinnedPlayer.Components.Get<PlayerMovement>();
			if (playerMovement is not null)
			{
				playerMovement.IsPinned = false;
			}
		}

		pinnedPlayer = null;
		currentState = HunterState.Idle;
	}

	bool HasLineOfSightToPlayer()
	{
		if (player is null)
			return false;

		Vector3 directionToPlayer = (player.WorldPosition - WorldPosition).Normal;
		float distanceToPlayer = (player.WorldPosition - WorldPosition).Length;

		// Start trace from hunter's head level
		Vector3 startPos = WorldPosition + Vector3.Up * 80f;
		Vector3 playerEyePos = player.WorldPosition + Vector3.Up * 80f;

		var trace = Scene.Trace.Ray(startPos, playerEyePos)
			.WithoutTags("trigger")
			.IgnoreGameObject(GameObject)
			.IgnoreGameObject(player)
			.Run();

		// If ray didn't hit anything, we have clear LOS
		if (!trace.Hit || trace.Distance >= distanceToPlayer - 5f)
		{
			return true;
		}

		return false;
	}

	void OnHunterDeath()
	{
		Log.Info("Hunter died");
		currentState = HunterState.Idle;
		moveDirection = Vector3.Zero;

		// Release player if pinning
		ReleasePlayer();

		if (characterController is not null)
		{
			characterController.Velocity = Vector3.Zero;
		}

		// Convert to ragdoll
		if (modelRenderer is not null)
		{
			modelRenderer.Tint = Color.White;

			// Disable character controller
			if (characterController is not null)
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
	}

	void OnHunterDamaged(float damageAmount, GameObject attacker)
	{
		if (characterController is null || attacker is null)
			return;

		// Flash effect
		if (modelRenderer is not null)
		{
			modelRenderer.Tint = FlashColor;
			flashTimer = FlashDuration;
		}

		// If damage is heavy (>50), release player
		if (damageAmount > 50f && currentState == HunterState.Pinning)
		{
			Log.Info("Hunter took heavy damage - releasing player!");
			ReleasePlayer();
		}

		// Apply knockback
		Vector3 directionFromAttacker = (WorldPosition - attacker.WorldPosition).Normal;
		Vector3 knockbackImpulse = (directionFromAttacker * 200f) + (Vector3.Up * 100f);
		characterController.Punch(knockbackImpulse);
	}

	protected override void OnFixedUpdate()
	{
		if (characterController is null || !Enabled)
			return;

		// During pounce, don't use normal movement (we set velocity directly)
		if (currentState == HunterState.Pouncing)
		{
			characterController.Move();
			return;
		}

		// During pinning, don't move
		if (currentState == HunterState.Pinning)
		{
			return;
		}

		// Normal movement
		var currentVel = characterController.Velocity;
		characterController.Velocity = new Vector3(moveDirection.x, moveDirection.y, currentVel.z);
		characterController.Accelerate(moveDirection);
		characterController.ApplyFriction(4.0f);
		characterController.Move();
	}

	protected override void OnDestroy()
	{
		// Release player if pinning
		ReleasePlayer();

		if (health is not null)
		{
			health.OnDeath -= OnHunterDeath;
			health.OnDamageTakenWithAttacker -= OnHunterDamaged;
		}
	}

	/// <summary>
	/// Reset the hunter to a fresh spawned state (call this when respawning)
	/// </summary>
	public void ResetSpawnState()
	{
		crouchTimer = 0f;
		flashTimer = 0f;
		pinDamageTimer = 0f;
		moveDirection = Vector3.Zero;
		pounceDirection = Vector3.Zero;
		currentState = HunterState.Idle;
		pinnedPlayer = null;

		// Force color to alive state
		if (modelRenderer is not null)
		{
			modelRenderer.Tint = Color.White;
			originalColor = Color.White;
		}

		Log.Info("Hunter: Reset spawn state");
	}
}
