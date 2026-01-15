using System;
using Sandbox;

public sealed class HealthComponent : Component, IHealth
{
	[Property]
	public float MaxHealth { get; set; } = 100f;

	[Property]
	public float CurrentHealth { get; set; }


	private bool isDead = false;

	/// <summary>
	/// Called when the player takes damage.
	/// </summary>
	public event Action<float> OnDamageTaken;

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

		// Update UI
		PlayerStats.CurrentHealth = CurrentHealth;
		PlayerStats.HealthMax = MaxHealth;
	}

	protected override void OnUpdate()
	{
		if ( isDead )
			return;

		// Update UI
		PlayerStats.CurrentHealth = CurrentHealth;
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

		OnDamageTaken?.Invoke( damageAmount );

		if ( CurrentHealth <= 0f )
		{
			Die( attacker );
		}

		PlayerStats.CurrentHealth = CurrentHealth;
	}

	/// <summary>
	/// Explicit interface implementation for IHealth.
	/// </summary>
	void IHealth.TakeDamage( float damage ) => TakeDamage( damage );

	/// <summary>
	/// Heal the player.
	/// </summary>
	public void Heal( float healAmount )
	{
		if ( isDead || healAmount <= 0f )
			return;

		CurrentHealth = MathF.Min( MaxHealth, CurrentHealth + healAmount );
		OnHealed?.Invoke( healAmount );

		PlayerStats.CurrentHealth = CurrentHealth;
	}

	/// <summary>
	/// Kill the player.
	/// </summary>
	public void Die( GameObject attacker = null )
	{
		if ( isDead )
			return;

		isDead = true;
		CurrentHealth = 0f;

		// Disable input/movement
		var playerMovement = GameObject.Root.Components.GetInDescendantsOrSelf<PlayerMovement>();
		if ( playerMovement is not null )
		{
			playerMovement.Enabled = false;
		}

		OnDeath?.Invoke();

		PlayerStats.CurrentHealth = 0f;

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

		PlayerStats.CurrentHealth = CurrentHealth;
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
