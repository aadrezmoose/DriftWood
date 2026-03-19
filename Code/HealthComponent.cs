using System;
using Sandbox;

public sealed class HealthComponent : Component
{
	[Property]
	public float MaxHealth { get; set; } = 100f;

	[Property]
	public float CurrentHealth { get; set; }

	[Property]
	public bool IsPlayer { get; set; } = false;

	private bool isDead = false;

	/// <summary>
	/// Called when the player takes damage.
	/// </summary>
	public event Action<float> OnDamageTaken;

	/// <summary>
	/// Called when damage is taken with attacker info.
	/// </summary>
	public event Action<float, GameObject> OnDamageTakenWithAttacker;

	/// <summary>
	/// Called when the player heals.
	/// </summary>
	public event Action<float> OnHealed;

	/// <summary>
	/// Called when the player dies.
	/// </summary>
	public event Action OnDeath;

	protected override void OnAwake()
	{
		CurrentHealth = MaxHealth;
		isDead = false;

		// Update UI only if this is the player
		if ( IsPlayer )
		{
			PlayerStats.CurrentHealth = CurrentHealth;
			PlayerStats.HealthMax = MaxHealth;
		}
	}

	protected override void OnUpdate()
	{
		if ( isDead )
			return;

		// Update UI only if this is the player
		if ( IsPlayer )
		{
			PlayerStats.CurrentHealth = CurrentHealth;
		}
	}

	/// <summary>
	/// Apply damage to the player.
	/// </summary>
	public void TakeDamage( float damageAmount, GameObject attacker = null )
	{
		if ( isDead )
			return;

		if ( damageAmount <= 0f )
			return;

		CurrentHealth = MathF.Max( 0f, CurrentHealth - damageAmount );

		if ( IsPlayer )
			PlayerStats.TotalDamageTaken += (int)damageAmount;

		OnDamageTaken?.Invoke( damageAmount );
		OnDamageTakenWithAttacker?.Invoke( damageAmount, attacker );

		if ( CurrentHealth <= 0f )
		{
			// If already incapacitated, don't trigger Die() again — IncapacitationComponent handles death
			if ( IsPlayer && PlayerStats.IsIncapacitated )
			{
				CurrentHealth = 0.1f;
				return;
			}
			Die( attacker );
		}

		if ( IsPlayer )
		{
			PlayerStats.CurrentHealth = CurrentHealth;
		}
	}

	/// <summary>
	/// Heal the player.
	/// </summary>
	public void Heal( float healAmount )
	{
		if ( isDead || healAmount <= 0f )
			return;

		CurrentHealth = MathF.Min( MaxHealth, CurrentHealth + healAmount );
		OnHealed?.Invoke( healAmount );

		if ( IsPlayer )
		{
			PlayerStats.CurrentHealth = CurrentHealth;
		}
	}

	/// <summary>
	/// Kill the player.
	/// </summary>
	public void Die( GameObject attacker = null )
	{
		if ( isDead )
			return;

		// Check for incapacitation before actual death (player only)
		if ( IsPlayer )
		{
			var incap = Components.GetInDescendantsOrSelf<IncapacitationComponent>()
			         ?? GameObject.Parent?.Components.GetInDescendantsOrSelf<IncapacitationComponent>();
			if ( incap != null && incap.CanBeIncapacitated() )
			{
				incap.Incapacitate();
				CurrentHealth = 1f;
				return;
			}
		}

		isDead = true;
		CurrentHealth = 0f;

		// Disable input/movement
		var playerMovement = GameObject.Root.Components.GetInDescendantsOrSelf<PlayerMovement>();
		if ( playerMovement is not null )
		{
			playerMovement.Enabled = false;
		}

		OnDeath?.Invoke();

		if ( IsPlayer )
		{
			PlayerStats.CurrentHealth = 0f;
			PlayerStats.IsDead = true;
			PlayerStats.IsIncapacitated = false;
		}

		Log.Info( $"Player died! Attacker: {(attacker?.Name ?? "Unknown")}" );
	}

	/// <summary>
	/// Revive the player (for respawning).
	/// </summary>
	public void Revive()
	{
		isDead = false;
		CurrentHealth = MaxHealth;

		var playerMovement = GameObject.Root.Components.GetInDescendantsOrSelf<PlayerMovement>( true );
		if ( playerMovement is not null )
		{
			playerMovement.Enabled = true;
		}

		if ( IsPlayer )
		{
			PlayerStats.CurrentHealth = CurrentHealth;
		}
	}

	/// <summary>
	/// Check if the player is dead.
	/// </summary>
	public bool IsDead => isDead;

	/// <summary>
	/// Get health as a percentage (0-1).
	/// </summary>
	public float HealthPercent => CurrentHealth / MaxHealth;
}
