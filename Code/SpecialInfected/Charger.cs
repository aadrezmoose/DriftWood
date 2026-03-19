using Sandbox;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Charger — bulldozes through the player. Telegraphs with a WindUp, then charges at high speed.
/// On contact, deals damage and pins the player briefly. Stops when hitting a wall.
/// </summary>
public sealed class Charger : Component
{
	// Movement / detection
	[Property] public float ChaseSpeed { get; set; } = 160f;
	[Property] public float DetectionRange { get; set; } = 600f;
	[Property] public float MaxHealth { get; set; } = 300f;

	// Charge stats
	[Property] public float ChargeSpeed { get; set; } = 600f;
	[Property] public float ImpactDamage { get; set; } = 25f;
	[Property] public float PinDuration { get; set; } = 2f;
	[Property] public float WindUpDuration { get; set; } = 1.2f;
	[Property] public float ChargeDuration { get; set; } = 2f;
	[Property] public float RecoverDuration { get; set; } = 1.5f;
	[Property] public float ImpactDetectRange { get; set; } = 80f;
	[Property] public float WallDetectRange { get; set; } = 100f;

	// Visuals / audio
	[Property] public Color FlashColor { get; set; } = Color.White;
	[Property] public float FlashDuration { get; set; } = 0.2f;
	[Property] public SoundEvent GrowlSound { get; set; }
	[Property] public SoundEvent DeathSound { get; set; }
	[Property] public SoundEvent PainSound { get; set; }
	[Property] public SoundEvent ChargeSound { get; set; }
	[Property] public float GrowlInterval { get; set; } = 5f;

	[Property] public GameObject PlayerReference { get; set; }

	// ── State machine ───────────────────────────────────────────────────
	private enum ChargerState { Idle, Chase, WindUp, Charging, Recovering }
	private ChargerState currentState = ChargerState.Idle;

	private GameObject player;
	private CharacterController characterController;
	private HealthComponent health;
	private ModelRenderer modelRenderer;
	private Color originalColor = Color.White;

	private float flashTimer = 0f;
	private float staggerTimer = 0f;
	private float growlTimer = 0f;
	private float stateTimer = 0f;      // generic per-state countdown
	private Vector3 moveDirection = Vector3.Zero;
	private Vector3 chargeDirection = Vector3.Zero;
	private bool hasHitPlayer = false;  // only hit once per charge

	// ── Lifecycle ───────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		characterController = Components.Get<CharacterController>();
		health = Components.Get<HealthComponent>();
		modelRenderer = Components.Get<SkinnedModelRenderer>() as ModelRenderer
		             ?? Components.Get<ModelRenderer>();

		if ( modelRenderer is not null )
			originalColor = modelRenderer.Tint;
		else
			Log.Warning( "Charger: No ModelRenderer — flash won't work" );

		if ( PlayerReference is not null )
			player = PlayerReference;
		else
			player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;

		if ( characterController is null ) Log.Warning( "Charger: Missing CharacterController" );

		if ( health is null )
		{
			Log.Warning( "Charger: Missing HealthComponent" );
		}
		else
		{
			health.MaxHealth     = MaxHealth;
			health.CurrentHealth = MaxHealth;
			health.OnDeath += OnChargerDeath;
			health.OnDamageTakenWithAttacker += OnChargerDamaged;
		}

		if ( player is null ) Log.Warning( "Charger: Could not find player" );
		Log.Info( "Charger: Spawned successfully" );
	}

	protected override void OnUpdate()
	{
		if ( !Enabled || player is null || characterController is null ) return;

		// Flash timer
		if ( flashTimer > 0f )
		{
			flashTimer -= Time.Delta;
			if ( flashTimer <= 0f && modelRenderer is not null )
				modelRenderer.Tint = originalColor;
		}

		// Dead check
		if ( health is not null && health.IsDead )
		{
			currentState = ChargerState.Idle;
			characterController.Velocity = Vector3.Zero;
			return;
		}

		// Stagger blocks AI
		if ( staggerTimer > 0f )
		{
			staggerTimer -= Time.Delta;
			moveDirection = Vector3.Zero;
			return;
		}

		UpdateGrowl();

		float dist = (WorldPosition - player.WorldPosition).Length;

		switch ( currentState )
		{
			// ── Idle ────────────────────────────────────────────────────
			case ChargerState.Idle:
				moveDirection = Vector3.Zero;
				if ( dist < DetectionRange )
					currentState = ChargerState.Chase;
				break;

			// ── Chase ───────────────────────────────────────────────────
			case ChargerState.Chase:
				if ( dist > DetectionRange * 1.5f )
				{
					currentState = ChargerState.Idle;
					break;
				}

				Vector3 chaseDir = (player.WorldPosition - WorldPosition).Normal;
				moveDirection = chaseDir * ChaseSpeed;
				Vector3 chaseFlatDir = chaseDir.WithZ( 0 ).Normal;
				if ( !chaseFlatDir.IsNearZeroLength )
					WorldRotation = Rotation.LookAt( chaseFlatDir, Vector3.Up );

				// Transition to WindUp when in charge range (same as DetectionRange for simplicity)
				if ( dist < DetectionRange * 0.7f )
				{
					EnterWindUp();
				}
				break;

			// ── WindUp ──────────────────────────────────────────────────
			case ChargerState.WindUp:
				moveDirection = Vector3.Zero;
				// Lock rotation onto player during telegraph
				Vector3 windUpDir = (player.WorldPosition - WorldPosition).WithZ( 0 ).Normal;
				if ( !windUpDir.IsNearZeroLength )
					WorldRotation = Rotation.LookAt( windUpDir, Vector3.Up );

				stateTimer -= Time.Delta;
				if ( stateTimer <= 0f )
					EnterCharging();
				break;

			// ── Charging ────────────────────────────────────────────────
			case ChargerState.Charging:
				stateTimer -= Time.Delta;
				if ( stateTimer <= 0f )
				{
					EnterRecovering();
					break;
				}

				// Check for wall collision via forward trace
				var wallTrace = Scene.Trace
					.Ray( WorldPosition + Vector3.Up * 50f, WorldPosition + Vector3.Up * 50f + chargeDirection * WallDetectRange )
					.WithoutTags( "trigger", "player" )
					.IgnoreGameObject( GameObject )
					.Run();

				if ( wallTrace.Hit )
				{
					Log.Info( "Charger: Hit wall — entering recover" );
					EnterRecovering();
					break;
				}

				// Move in locked direction
				moveDirection = chargeDirection * ChargeSpeed;

				// Check for player contact
				if ( !hasHitPlayer )
				{
					float playerDist = (WorldPosition - player.WorldPosition).Length;
					if ( playerDist < ImpactDetectRange )
					{
						hasHitPlayer = true;
						OnChargeImpact();
					}
				}
				break;

			// ── Recovering ──────────────────────────────────────────────
			case ChargerState.Recovering:
				moveDirection = Vector3.Zero;
				stateTimer -= Time.Delta;
				if ( stateTimer <= 0f )
					currentState = ChargerState.Chase;
				break;
		}
	}

	void EnterWindUp()
	{
		currentState = ChargerState.WindUp;
		stateTimer = WindUpDuration;
		moveDirection = Vector3.Zero;
		hasHitPlayer = false;
		Log.Info( "Charger: WindUp — telegraphing charge" );
	}

	void EnterCharging()
	{
		currentState = ChargerState.Charging;
		stateTimer = ChargeDuration;

		// Lock direction toward player at moment of launch
		chargeDirection = (player.WorldPosition - WorldPosition).WithZ( 0 ).Normal;
		if ( chargeDirection.IsNearZeroLength )
			chargeDirection = WorldRotation.Forward.WithZ( 0 ).Normal;

		WorldRotation = Rotation.LookAt( chargeDirection, Vector3.Up );

		if ( ChargeSound is not null )
			Sound.Play( ChargeSound, WorldPosition );

		Log.Info( "Charger: Charging!" );
	}

	void EnterRecovering()
	{
		currentState = ChargerState.Recovering;
		stateTimer = RecoverDuration;
		moveDirection = Vector3.Zero;
		if ( characterController is not null )
			characterController.Velocity = Vector3.Zero;
		Log.Info( "Charger: Recovering" );
	}

	void OnChargeImpact()
	{
		Log.Info( "Charger: Impact!" );

		// Damage the player
		var playerHealth = player.Components.GetInDescendantsOrSelf<HealthComponent>();
		if ( playerHealth is not null && !playerHealth.IsDead )
			playerHealth.TakeDamage( ImpactDamage, GameObject );

		// Pin the player
		var playerMovement = player.Components.GetInDescendantsOrSelf<PlayerMovement>();
		if ( playerMovement is not null )
		{
			playerMovement.IsPinned = true;
			_ = ReleasePinAfterDelay( playerMovement );
		}
	}

	async Task ReleasePinAfterDelay( PlayerMovement pm )
	{
		await Task.DelaySeconds( PinDuration );
		if ( pm is null || !pm.IsValid() ) return;
		pm.IsPinned = false;
		Log.Info( "Charger: Released player pin" );
	}

	// ── Damage handlers ─────────────────────────────────────────────────

	void OnChargerDamaged( float damageAmount, GameObject attacker )
	{
		if ( characterController is null || attacker is null ) return;

		if ( modelRenderer is not null )
		{
			modelRenderer.Tint = FlashColor;
			flashTimer = FlashDuration;
		}

		if ( PainSound is not null )
			Sound.Play( PainSound, WorldPosition );

		float addStagger = 0.35f + damageAmount * 0.005f; // harder to stagger than normal enemies
		staggerTimer = System.Math.Min( System.Math.Max( staggerTimer, addStagger ), 0.8f );

		Vector3 dir = (WorldPosition - attacker.WorldPosition).Normal;
		characterController.Punch( dir * 150f + Vector3.Up * 80f );
	}

	void OnChargerDeath()
	{
		Log.Info( "Charger: Died" );
		currentState = ChargerState.Idle;
		moveDirection = Vector3.Zero;

		if ( characterController is not null )
		{
			characterController.Velocity = Vector3.Zero;
			characterController.Enabled = false;
		}

		if ( DeathSound is not null )
			Sound.Play( DeathSound, WorldPosition );

		// Disable bullet colliders
		foreach ( var col in Components.GetAll<BoxCollider>() )
			col.Enabled = false;
		foreach ( var child in GameObject.Children )
			foreach ( var col in child.Components.GetAll<BoxCollider>() )
				col.Enabled = false;

		// Ragdoll
		var skinnedRenderer = Components.Get<SkinnedModelRenderer>();
		if ( skinnedRenderer is not null )
		{
			skinnedRenderer.Tint = Color.White;
			var modelPhysics = Components.GetOrCreate<ModelPhysics>();
			modelPhysics.Renderer = skinnedRenderer;
			modelPhysics.Model = skinnedRenderer.Model;
		}

		Enabled = false;
	}

	void UpdateGrowl()
	{
		if ( GrowlSound is null ) return;
		growlTimer -= Time.Delta;
		if ( growlTimer <= 0f )
		{
			growlTimer = GrowlInterval + Game.Random.Float( -1f, 2f );
			Sound.Play( GrowlSound, WorldPosition );
		}
	}

	// ── Fixed update — movement ─────────────────────────────────────────

	protected override void OnFixedUpdate()
	{
		if ( characterController is null || !Enabled ) return;

		var gravity = Scene.PhysicsWorld.Gravity;

		if ( staggerTimer > 0f )
		{
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

		if ( currentState == ChargerState.Charging )
		{
			// High-speed, minimal friction during the actual charge
			characterController.Velocity = new Vector3( moveDirection.x, moveDirection.y, characterController.Velocity.z );
			if ( !characterController.IsOnGround )
				characterController.Velocity += gravity * Time.Delta * 0.5f;
			characterController.Move();
			if ( !characterController.IsOnGround )
				characterController.Velocity += gravity * Time.Delta * 0.5f;
			return;
		}

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
			characterController.ApplyFriction( 0.1f );
		}

		characterController.Move();

		if ( !characterController.IsOnGround )
			characterController.Velocity += gravity * Time.Delta * 0.5f;
		else
			characterController.Velocity = characterController.Velocity.WithZ( 0 );
	}

	// ── Cleanup ─────────────────────────────────────────────────────────

	protected override void OnDestroy()
	{
		if ( health is not null )
		{
			health.OnDeath -= OnChargerDeath;
			health.OnDamageTakenWithAttacker -= OnChargerDamaged;
		}
	}
}
