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
	private PlayerIdentity identity;

	private bool ShouldWriteLocalPlayerStats => !Networking.IsActive || !IsProxy;

	protected override void OnAwake()
	{
		// Reset static flags so a new play session doesn't inherit the previous state
		if ( ShouldWriteLocalPlayerStats )
		{
			PlayerStats.IsIncapacitated = false;
			PlayerStats.BleedOutHealth = 100f;
			PlayerStats.IsDead = false;
			PlayerStats.CarriedItems.Clear();
			PlayerStats.CarriedItem = string.Empty;
			PlayerStats.ActiveSlotIndex = 0;
			PlayerStats.ResetDamageIndicators();
		}

		health         = Components.GetInAncestorsOrSelf<HealthComponent>();
		playerMovement = Components.GetInAncestorsOrSelf<PlayerMovement>();
		identity       = Components.GetInAncestorsOrSelf<PlayerIdentity>();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return; // Owner runs the bleed-out timer; proxies receive BleedOutHealth via [Sync]
		if ( !IsIncapacitated ) return;

		// Drain bleed-out health
		float drainRate = 100f / BleedOutDuration;
		BleedOutHealth -= drainRate * Time.Delta;
		BleedOutHealth = System.Math.Max( 0f, BleedOutHealth );

		PlayerStats.BleedOutHealth = BleedOutHealth;
		if ( identity != null ) identity.BleedOutHealth = BleedOutHealth;

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
		if ( Networking.IsActive && IsProxy ) return;
		BleedOutHealth = System.Math.Max( 0f, BleedOutHealth - amount );
		if ( ShouldWriteLocalPlayerStats )
			PlayerStats.BleedOutHealth = BleedOutHealth;
		if ( identity != null ) identity.BleedOutHealth = BleedOutHealth;
	}

	/// <summary>
	/// Broadcast RPC so a teammate's revive applies on the owning client,
	/// ensuring [Sync] fields replicate correctly.
	/// </summary>
	[Rpc.Broadcast]
	public void RequestReviveFromTeammate()
	{
		if ( Networking.IsActive && IsProxy ) return;
		Revive();
	}

	/// <summary>Revives the player with 45% HP. Call this from a health kit or teammate.</summary>
	public void Revive()
	{
		if ( !IsIncapacitated ) return;

		IsIncapacitated = false;
		BleedOutHealth = 100f;
		if ( ShouldWriteLocalPlayerStats )
		{
			PlayerStats.IsIncapacitated = false;
			PlayerStats.BleedOutHealth = 100f;
		}
		if ( identity != null ) { identity.IsIncapacitated = false; identity.BleedOutHealth = 100f; }

		// Re-enable movement
		if ( playerMovement != null )
			playerMovement.IsPinned = false;

		// Restore 45% HP
		if ( health != null )
		{
			health.CurrentHealth = health.MaxHealth * 0.45f;
			if ( ShouldWriteLocalPlayerStats )
				PlayerStats.CurrentHealth = health.CurrentHealth;
			if ( identity != null ) identity.CurrentHealth = health.CurrentHealth;
		}

		Log.Info( "Player revived at 45% HP." );
	}

	public void Incapacitate()
	{
		if ( IsIncapacitated ) return;
		if ( Networking.IsActive && IsProxy ) return;

		IsIncapacitated = true;
		IncapCount++;
		BleedOutHealth = 100f;

		if ( ShouldWriteLocalPlayerStats )
		{
			PlayerStats.IsIncapacitated = true;
			PlayerStats.BleedOutHealth = 100f;
			PlayerStats.TimesIncapacitated++;
		}
		if ( identity != null ) { identity.IsIncapacitated = true; identity.BleedOutHealth = 100f; identity.TimesIncapacitated++; }

		Log.Info( $"Player incapacitated ({IncapCount}/{MaxIncaps}). Bleeding out..." );
	}

	private void ActuallyDie()
	{
		// Exhaust incap count so HealthComponent.Die() doesn't re-incapacitate
		IncapCount = MaxIncaps;
		IsIncapacitated = false;
		if ( ShouldWriteLocalPlayerStats )
			PlayerStats.IsIncapacitated = false;
		if ( identity != null ) identity.IsIncapacitated = false;

		if ( health != null )
			health.Die();

		Log.Info( "Player bled out - died." );
	}
}
