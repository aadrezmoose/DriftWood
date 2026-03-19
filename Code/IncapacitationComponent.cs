using Sandbox;

/// <summary>
/// L4D-style incapacitation. When the player hits 0 HP they go down
/// with a bleed-out timer. When that expires they actually die.
/// </summary>
public sealed class IncapacitationComponent : Component
{
	[Property] public float BleedOutDuration { get; set; } = 60f; // seconds until death
	[Property] public int MaxIncaps { get; set; } = 2;

	public bool IsIncapacitated { get; private set; } = false;
	public float BleedOutHealth { get; private set; } = 100f; // 100 -> 0 over BleedOutDuration
	public int IncapCount { get; private set; } = 0;

	private HealthComponent health;
	private PlayerMovement playerMovement;

	protected override void OnAwake()
	{
		// Reset static flags so a new play session doesn't inherit the previous state
		PlayerStats.IsIncapacitated = false;
		PlayerStats.BleedOutHealth = 100f;
		PlayerStats.IsDead = false;
		PlayerStats.CarriedItems.Clear();
		PlayerStats.CarriedItem = string.Empty;
		PlayerStats.ActiveSlotIndex = 0;
		PlayerStats.ResetDamageIndicators();

		health = Components.GetInAncestorsOrSelf<HealthComponent>();
		playerMovement = Components.GetInAncestorsOrSelf<PlayerMovement>();
	}

	protected override void OnUpdate()
	{
		if ( !IsIncapacitated ) return;

		// Drain bleed-out health
		float drainRate = 100f / BleedOutDuration;
		BleedOutHealth -= drainRate * Time.Delta;
		BleedOutHealth = System.Math.Max( 0f, BleedOutHealth );

		PlayerStats.BleedOutHealth = BleedOutHealth;

		if ( BleedOutHealth <= 0f )
		{
			ActuallyDie();
		}
	}

	public bool CanBeIncapacitated() => !IsIncapacitated && IncapCount < MaxIncaps;

	/// <summary>Called by enemies hitting a downed player — drains the bleed-out timer faster.</summary>
	public void ApplyBleedDamage( float amount )
	{
		if ( !IsIncapacitated ) return;
		BleedOutHealth = System.Math.Max( 0f, BleedOutHealth - amount );
		PlayerStats.BleedOutHealth = BleedOutHealth;
	}

	/// <summary>Revives the player with 45% HP. Call this from a health kit or teammate.</summary>
	public void Revive()
	{
		if ( !IsIncapacitated ) return;

		IsIncapacitated = false;
		BleedOutHealth = 100f;
		PlayerStats.IsIncapacitated = false;
		PlayerStats.BleedOutHealth = 100f;

		// Re-enable movement
		if ( playerMovement != null )
			playerMovement.IsPinned = false;

		// Restore 45% HP
		if ( health != null )
		{
			health.CurrentHealth = health.MaxHealth * 0.45f;
			PlayerStats.CurrentHealth = health.CurrentHealth;
		}

		Log.Info( "Player revived at 45% HP." );
	}

	public void Incapacitate()
	{
		if ( IsIncapacitated ) return;

		IsIncapacitated = true;
		IncapCount++;
		BleedOutHealth = 100f;

		PlayerStats.IsIncapacitated = true;
		PlayerStats.BleedOutHealth = 100f;
		PlayerStats.TimesIncapacitated++;

		Log.Info( $"Player incapacitated ({IncapCount}/{MaxIncaps}). Bleeding out..." );
	}

	private void ActuallyDie()
	{
		// Exhaust incap count so HealthComponent.Die() doesn't re-incapacitate
		IncapCount = MaxIncaps;
		IsIncapacitated = false;
		PlayerStats.IsIncapacitated = false;

		if ( health != null )
			health.Die();

		Log.Info( "Player bled out - died." );
	}
}
