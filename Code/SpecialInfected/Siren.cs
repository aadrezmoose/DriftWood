using Sandbox;
using System.Linq;

/// <summary>
/// Siren — stationary special infected. Sits and cries until disturbed.
///
/// Proximity stages:
///   Idle     → player outside AgitationRadius: sits, passive
///   Agitated → player within AgitationRadius: stands, growls, warns
///   Enraged  → player within StartleRadius OR AgitationTimer expires: charges full-speed
///
/// L4D authentic: always hand-placed (no spawner), one per map.
/// Charges exclusively at the player who startled her.
/// </summary>
public sealed class Siren : Component
{
	// --- Detection ---
	[Property] public float AgitationRadius { get; set; } = 350f;
	[Property] public float StartleRadius   { get; set; } = 150f;
	/// <summary>Seconds in Agitated state before she charges without the player leaving.</summary>
	[Property] public float AgitationTimeout { get; set; } = 4f;

	// --- Combat ---
	[Property] public float MaxHealth     { get; set; } = 500f;
	[Property] public float AttackDamage  { get; set; } = 50f;
	[Property] public float ChargeSpeed   { get; set; } = 380f;
	[Property] public float AttackRange   { get; set; } = 90f;
	[Property] public float AttackCooldown { get; set; } = 0.8f;

	// --- Visuals / audio ---
	[Property] public Color FlashColor    { get; set; } = Color.White;
	[Property] public float FlashDuration { get; set; } = 0.15f;
	[Property] public SoundEvent CryingSound   { get; set; }
	[Property] public SoundEvent AgitatedSound { get; set; }
	[Property] public SoundEvent StartleSound  { get; set; }
	[Property] public SoundEvent AttackSound   { get; set; }
	[Property] public SoundEvent DeathSound    { get; set; }
	[Property] public SoundEvent PainSound     { get; set; }

	// --- State ---
	private enum SirenState { Idle, Agitated, Enraged }
	private SirenState currentState = SirenState.Idle;

	private GameObject target;          // player who startled her
	private CharacterController characterController;
	private HealthComponent health;
	private ModelRenderer modelRenderer;
	private Color originalColor;

	private float agitationTimer  = 0f;
	private float attackTimer     = 0f;
	private float flashTimer      = 0f;
	private float staggerTimer    = 0f;
	private Vector3 moveDirection = Vector3.Zero;

	// Crying ambient sound handle
	private SoundHandle cryHandle;
	private bool cryPlaying = false;

	protected override void OnAwake()
	{
		characterController = Components.Get<CharacterController>();
		health = Components.Get<HealthComponent>();

		modelRenderer = Components.Get<SkinnedModelRenderer>() as ModelRenderer
		             ?? Components.Get<ModelRenderer>();
		if ( modelRenderer != null )
			originalColor = modelRenderer.Tint;

		if ( health != null )
		{
			health.MaxHealth     = MaxHealth;
			health.CurrentHealth = MaxHealth;
			health.OnDeath += OnSirenDeath;
			health.OnDamageTakenWithAttacker += OnSirenDamaged;
		}
		else
		{
			Log.Warning( "Siren: No HealthComponent found" );
		}

		if ( characterController == null )
			Log.Warning( "Siren: No CharacterController found" );
	}

	protected override void OnStart()
	{
		// Begin crying
		if ( CryingSound != null )
		{
			cryHandle  = Sound.Play( CryingSound, WorldPosition );
			cryPlaying = true;
		}
	}

	protected override void OnUpdate()
	{
		if ( health != null && health.IsDead ) return;

		// Flash timer
		if ( flashTimer > 0f )
		{
			flashTimer -= Time.Delta;
			if ( flashTimer <= 0f && modelRenderer != null )
				modelRenderer.Tint = originalColor;
		}

		if ( attackTimer > 0f )
			attackTimer -= Time.Delta;

		if ( staggerTimer > 0f )
		{
			staggerTimer -= Time.Delta;
			moveDirection = Vector3.Zero;
			return;
		}

		switch ( currentState )
		{
			case SirenState.Idle:
				moveDirection = Vector3.Zero;
				CheckProximity();
				break;

			case SirenState.Agitated:
				moveDirection = Vector3.Zero;
				agitationTimer -= Time.Delta;

				// Face nearest player while warning
				var nearest = FindNearestPlayer();
				if ( nearest != null )
				{
					FaceTarget( nearest );
					float dist = (nearest.WorldPosition - WorldPosition).Length;

					if ( dist <= StartleRadius || agitationTimer <= 0f )
						Startle( nearest );
					else if ( dist > AgitationRadius * 1.3f )
						ReturnToIdle(); // player backed off
				}
				else
				{
					ReturnToIdle();
				}
				break;

			case SirenState.Enraged:
				if ( target == null || !target.Active )
				{
					// Re-target nearest player
					target = FindNearestPlayer();
					if ( target == null ) { ReturnToIdle(); break; }
				}

				float chaseDistance = (target.WorldPosition - WorldPosition).Length;
				FaceTarget( target );

				if ( chaseDistance <= AttackRange )
				{
					moveDirection = Vector3.Zero;
					if ( attackTimer <= 0f )
					{
						PerformAttack();
						attackTimer = AttackCooldown;
					}
				}
				else
				{
					Vector3 dir = (target.WorldPosition - WorldPosition).Normal;
					moveDirection = dir.WithZ( 0 ).Normal * ChargeSpeed;
				}
				break;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( characterController == null ) return;
		if ( health != null && health.IsDead ) return;

		var gravity = Scene.PhysicsWorld.Gravity;

		if ( characterController.IsOnGround )
		{
			characterController.Velocity = characterController.Velocity.WithZ( 0 );
			characterController.Accelerate( moveDirection );
			characterController.ApplyFriction( 5f );
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

	// ── Proximity checks ─────────────────────────────────────────────────────

	void CheckProximity()
	{
		var nearest = FindNearestPlayer();
		if ( nearest == null ) return;

		float dist = (nearest.WorldPosition - WorldPosition).Length;

		if ( dist <= StartleRadius )
		{
			Startle( nearest );
		}
		else if ( dist <= AgitationRadius )
		{
			EnterAgitated( nearest );
		}
	}

	void EnterAgitated( GameObject player )
	{
		if ( currentState == SirenState.Agitated ) return;

		currentState   = SirenState.Agitated;
		agitationTimer = AgitationTimeout;

		StopCrying();

		if ( AgitatedSound != null )
			Sound.Play( AgitatedSound, WorldPosition );

		Log.Info( "Siren: Agitated — warning player" );
	}

	void Startle( GameObject player )
	{
		if ( currentState == SirenState.Enraged ) return;

		currentState  = SirenState.Enraged;
		target        = player;

		StopCrying();

		if ( StartleSound != null )
			Sound.Play( StartleSound, WorldPosition );

		Log.Info( $"Siren: STARTLED — charging {player.Name}" );
	}

	void ReturnToIdle()
	{
		currentState = SirenState.Idle;

		if ( CryingSound != null && !cryPlaying )
		{
			cryHandle  = Sound.Play( CryingSound, WorldPosition );
			cryPlaying = true;
		}

		Log.Info( "Siren: Player backed off — returning to idle" );
	}

	// ── Combat ───────────────────────────────────────────────────────────────

	void PerformAttack()
	{
		if ( target == null ) return;

		if ( AttackSound != null )
			Sound.Play( AttackSound, WorldPosition );

		var health = target.Components.GetInDescendantsOrSelf<HealthComponent>()
		          ?? target.Components.GetInAncestorsOrSelf<HealthComponent>();

		health?.TakeDamage( AttackDamage, GameObject );
	}

	// ── Damage handlers ──────────────────────────────────────────────────────

	void OnSirenDamaged( float damage, GameObject attacker )
	{
		// Flash
		if ( modelRenderer != null )
		{
			modelRenderer.Tint = FlashColor;
			flashTimer = FlashDuration;
		}

		if ( PainSound != null )
			Sound.Play( PainSound, WorldPosition );

		// Stagger
		staggerTimer = System.Math.Min( System.Math.Max( staggerTimer, 0.25f + damage * 0.008f ), 0.8f );

		// Knockback
		if ( characterController != null )
		{
			Vector3 dir = (WorldPosition - attacker.WorldPosition).Normal;
			characterController.Punch( dir * 150f + Vector3.Up * 80f );
		}

		// Shooting the Siren startles her
		if ( currentState != SirenState.Enraged )
			Startle( attacker );
	}

	void OnSirenDeath()
	{
		Log.Info( "Siren: Died" );
		StopCrying();
		moveDirection = Vector3.Zero;

		if ( characterController != null )
		{
			characterController.Velocity = Vector3.Zero;
			characterController.Enabled  = false;
		}

		if ( DeathSound != null )
			Sound.Play( DeathSound, WorldPosition );

		foreach ( var col in Components.GetAll<BoxCollider>() )
			col.Enabled = false;
		foreach ( var child in GameObject.Children )
			foreach ( var col in child.Components.GetAll<BoxCollider>() )
				col.Enabled = false;

		var skinnedRenderer = Components.Get<SkinnedModelRenderer>();
		if ( skinnedRenderer != null )
		{
			skinnedRenderer.Tint = Color.White;
			var modelPhysics = Components.GetOrCreate<ModelPhysics>();
			modelPhysics.Renderer = skinnedRenderer;
			modelPhysics.Model    = skinnedRenderer.Model;
		}

		Enabled = false;
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	GameObject FindNearestPlayer()
	{
		return Scene.GetAllComponents<PlayerMovement>()
			.OrderBy( pm => (pm.WorldPosition - WorldPosition).LengthSquared )
			.FirstOrDefault()?.GameObject;
	}

	void FaceTarget( GameObject t )
	{
		Vector3 flat = (t.WorldPosition - WorldPosition).WithZ( 0 ).Normal;
		if ( !flat.IsNearZeroLength )
			WorldRotation = Rotation.LookAt( flat, Vector3.Up );
	}

	void StopCrying()
	{
		if ( cryPlaying )
		{
			cryHandle.Stop();
			cryPlaying = false;
		}
	}

	protected override void OnDestroy()
	{
		StopCrying();

		if ( health != null )
		{
			health.OnDeath -= OnSirenDeath;
			health.OnDamageTakenWithAttacker -= OnSirenDamaged;
		}
	}
}
