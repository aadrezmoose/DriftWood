using Sandbox;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Spitter — ranged special infected. Keeps its distance and launches acid projectiles
/// at the player on a cooldown. Follows the same flash-on-hit, stagger, ragdoll-death
/// pattern as Hunter and Enemy.
/// </summary>
public sealed class Spitter : Component
{
	// Movement / detection
	[Property] public float ChaseSpeed { get; set; } = 180f;
	[Property] public float DetectionRange { get; set; } = 400f;
	[Property] public float AttackRange { get; set; } = 400f;
	[Property] public float AttackCooldown { get; set; } = 4f;
	[Property] public float MaxHealth { get; set; } = 80f;

	// Visuals / audio
	[Property] public Color FlashColor { get; set; } = Color.White;
	[Property] public float FlashDuration { get; set; } = 0.2f;
	[Property] public SoundEvent GrowlSound { get; set; }
	[Property] public SoundEvent DeathSound { get; set; }
	[Property] public SoundEvent AttackSound { get; set; }
	[Property] public SoundEvent PainSound { get; set; }
	[Property] public float GrowlInterval { get; set; } = 6f;

	// Projectile
	[Property] public PrefabFile AcidProjectilePrefab { get; set; }

	// Manual player assignment (set by spawner)
	[Property] public GameObject PlayerReference { get; set; }

	// ── State machine ───────────────────────────────────────────────────
	private enum SpitterState { Idle, Chase, Attack }
	private SpitterState currentState = SpitterState.Idle;

	private GameObject player;
	private CharacterController characterController;
	private HealthComponent health;
	private ModelRenderer modelRenderer;
	private Color originalColor = Color.White;

	private float flashTimer = 0f;
	private float attackCooldownTimer = 0f;
	private float growlTimer = 0f;
	private float staggerTimer = 0f;
	private Vector3 moveDirection = Vector3.Zero;
	private bool deathHandled = false;

	// ── Lifecycle ───────────────────────────────────────────────────────

	protected override void OnAwake()
	{
		characterController = Components.Get<CharacterController>();
		health = Components.Get<HealthComponent>();

		// Prefer SkinnedModelRenderer (citizen model), fall back to ModelRenderer
		modelRenderer = Components.Get<SkinnedModelRenderer>() as ModelRenderer
		             ?? Components.Get<ModelRenderer>();

		if ( modelRenderer is not null )
			originalColor = modelRenderer.Tint;
		else
			Log.Warning( "Spitter: No ModelRenderer found — flash won't work" );

		// Resolve player reference
		if ( PlayerReference is not null )
			player = PlayerReference;
		else
			player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;

		if ( characterController is null ) Log.Warning( "Spitter: Missing CharacterController" );
		if ( health is null )
		{
			Log.Warning( "Spitter: Missing HealthComponent" );
		}
		else
		{
			health.MaxHealth = MaxHealth;
			health.CurrentHealth = MaxHealth;
			health.OnDeath += OnSpitterDeath;
			health.OnDamageTakenWithPosition += OnSpitterDamaged;
		}

		if ( player is null ) Log.Warning( "Spitter: Could not find player" );
		Log.Info( "Spitter: Spawned successfully" );
		deathHandled = false;
	}

	protected override void OnUpdate()
	{
		if ( !Enabled ) return;

		// Dead check
		if ( health is not null && health.IsDead )
		{
			OnSpitterDeath();
			return;
		}

		if ( player is null || characterController is null ) return;

		// Flash timer
		if ( flashTimer > 0f )
		{
			flashTimer -= Time.Delta;
			if ( flashTimer <= 0f && modelRenderer is not null )
				modelRenderer.Tint = originalColor;
		}

		// Stagger blocks AI
		if ( staggerTimer > 0f )
		{
			staggerTimer -= Time.Delta;
			moveDirection = Vector3.Zero;
			return;
		}

		if ( attackCooldownTimer > 0f )
			attackCooldownTimer -= Time.Delta;

		UpdateGrowl();

		float dist = (WorldPosition - player.WorldPosition).Length;
		bool los = HasLineOfSightToPlayer();

		// State transitions
		switch ( currentState )
		{
			case SpitterState.Idle:
				if ( dist < DetectionRange && los )
					currentState = SpitterState.Chase;
				moveDirection = Vector3.Zero;
				break;

			case SpitterState.Chase:
				if ( dist <= AttackRange && los )
				{
					currentState = SpitterState.Attack;
					moveDirection = Vector3.Zero;
				}
				else if ( dist > DetectionRange * 1.5f )
				{
					currentState = SpitterState.Idle;
				}
				else
				{
					ChaseBehavior();
				}
				break;

			case SpitterState.Attack:
				// Face the player, don't move
				moveDirection = Vector3.Zero;
				Vector3 flatDir = (player.WorldPosition - WorldPosition).WithZ( 0 ).Normal;
				if ( !flatDir.IsNearZeroLength )
					WorldRotation = Rotation.LookAt( flatDir, Vector3.Up );

				if ( !los || dist > AttackRange * 1.2f )
				{
					currentState = SpitterState.Chase;
					break;
				}

				if ( attackCooldownTimer <= 0f )
				{
					LaunchProjectile();
					attackCooldownTimer = AttackCooldown;
				}
				break;
		}
	}

	void ChaseBehavior()
	{
		Vector3 dir = (player.WorldPosition - WorldPosition).Normal;
		moveDirection = dir * ChaseSpeed;
		Vector3 flatDir = dir.WithZ( 0 ).Normal;
		if ( !flatDir.IsNearZeroLength )
			WorldRotation = Rotation.LookAt( flatDir, Vector3.Up );
	}

	void LaunchProjectile()
	{
		if ( AcidProjectilePrefab is null )
		{
			Log.Warning( "Spitter: No AcidProjectilePrefab assigned!" );
			return;
		}

		if ( AttackSound is not null )
			Sound.Play( AttackSound, WorldPosition );

		try
		{
			var prefabScene = SceneUtility.GetPrefabScene( AcidProjectilePrefab );
			if ( prefabScene is null )
			{
				Log.Error( "Spitter: Could not load AcidProjectile prefab" );
				return;
			}

			// Spawn at head height, aimed at player center
			Vector3 spawnPos = WorldPosition + Vector3.Up * 80f;
			Vector3 targetPos = player.WorldPosition + Vector3.Up * 60f;
			Vector3 aimDir = (targetPos - spawnPos).Normal;

			var proj = prefabScene.Clone( spawnPos );
			proj.WorldRotation = Rotation.LookAt( aimDir, Vector3.Up );
			Log.Info( "Spitter: Launched acid projectile" );
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"Spitter: Exception launching projectile: {ex.Message}" );
		}
	}

	bool HasLineOfSightToPlayer()
	{
		if ( player is null ) return false;

		Vector3 startPos = WorldPosition + Vector3.Up * 80f;
		Vector3 playerEyePos = player.WorldPosition + Vector3.Up * 80f;
		float dist = (playerEyePos - startPos).Length;

		var trace = Scene.Trace.Ray( startPos, playerEyePos )
			.WithoutTags( "trigger" )
			.IgnoreGameObject( GameObject )
			.IgnoreGameObject( player )
			.Run();

		return !trace.Hit || trace.Distance >= dist - 5f;
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

	// ── Damage handlers ─────────────────────────────────────────────────

	void OnSpitterDamaged( float damageAmount, Vector3 attackerPosition )
	{
		if ( characterController is null ) return;

		if ( attackerPosition.IsNearZeroLength && player is not null )
			attackerPosition = player.WorldPosition;

		if ( attackerPosition.IsNearZeroLength )
			return;

		// Flash
		if ( modelRenderer is not null )
		{
			modelRenderer.Tint = FlashColor;
			flashTimer = FlashDuration;
		}

		if ( PainSound is not null )
			Sound.Play( PainSound, WorldPosition );

		// Stagger
		float addStagger = 0.35f + damageAmount * 0.01f;
		staggerTimer = System.Math.Min( System.Math.Max( staggerTimer, addStagger ), 1.0f );

		// Knockback
		Vector3 dir = (WorldPosition - attackerPosition).Normal;
		if ( dir.IsNearZeroLength )
			dir = -WorldRotation.Forward;
		characterController.Punch( dir * 200f + Vector3.Up * 100f );
	}

	void OnSpitterDeath()
	{
		if ( deathHandled )
			return;

		deathHandled = true;
		Log.Info( "Spitter: Died" );
		currentState = SpitterState.Idle;
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

		// Ragdoll via ModelPhysics
		var skinnedRenderer = Components.Get<SkinnedModelRenderer>();
		if ( skinnedRenderer is not null )
		{
			skinnedRenderer.Tint = Color.White;
			var modelPhysics = Components.GetOrCreate<ModelPhysics>();
			modelPhysics.Enabled = true;
			modelPhysics.Renderer = skinnedRenderer;
			modelPhysics.Model = skinnedRenderer.Model;
		}

		// Let ragdoll physics run before disabling
		_ = DisableAfterDelay();
	}

	async Task DisableAfterDelay()
	{
		await Task.DelaySeconds( 4f );
		if ( GameObject.IsValid() )
			Enabled = false;
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
			health.OnDeath -= OnSpitterDeath;
			health.OnDamageTakenWithPosition -= OnSpitterDamaged;
		}
	}
}
