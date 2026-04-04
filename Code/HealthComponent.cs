using System;
using Sandbox;

public sealed class HealthComponent : Component
{
	[Property]
	public bool EnableDeathDiagnostics { get; set; } = false;

	[Property]
	public float MaxHealth { get; set; } = 100f;

	[Property, Sync]
	public float CurrentHealth { get; set; }

	[Property]
	public bool IsPlayer { get; set; } = false;

	[Sync]
	public bool SyncedIsDead { get; set; } = false;
	private bool deathEventEmitted = false;

	// Lazy ref to the PlayerIdentity on this player — only valid when IsPlayer is true.
	// Never search up to parent/scene-root: on top-level enemies, Parent IS the scene root
	// and GetInDescendantsOrSelf would find the player's PlayerIdentity, corrupting it.
	private PlayerIdentity _identity;
	private PlayerIdentity Identity => IsPlayer
		? (_identity ??= Components.GetInDescendantsOrSelf<PlayerIdentity>())
		: null;

	private bool ShouldWriteLocalPlayerStats => IsPlayer && ( !Networking.IsActive || !IsProxy );
	private bool IsAuthoritativeInstance() => !Networking.IsActive || Connection.Local?.IsHost == true;

	private void LogDeathDiag( string stage )
	{
		if ( !EnableDeathDiagnostics || IsPlayer )
			return;

		var owner = GameObject?.Network?.Owner;
		var local = Connection.Local;
		Log.Info( $"[DeathDiag][Health] {stage} GO={GameObject?.Name} Id={GameObject?.Id} IsProxy={IsProxy} IsAuth={IsAuthoritativeInstance()} SyncedIsDead={SyncedIsDead} EventEmitted={deathEventEmitted} Owner={owner?.DisplayName ?? "none"}({owner?.SteamId}) Local={local?.DisplayName ?? "none"}({local?.SteamId})" );
	}

	/// <summary>
	/// Called when the player takes damage.
	/// </summary>
	public event Action<float> OnDamageTaken;

	/// <summary>
	/// Called when damage is taken with attacker info.
	/// </summary>
	public event Action<float, GameObject> OnDamageTakenWithAttacker;

	/// <summary>
	/// Called for enemy damage reactions with attacker world position.
	/// This is broadcast by the authoritative instance so clients can play hit reactions.
	/// </summary>
	public event Action<float, Vector3> OnDamageTakenWithPosition;

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
		SyncedIsDead = false;
		deathEventEmitted = false;

		// Update UI only if this is the player
		if ( ShouldWriteLocalPlayerStats )
		{
			PlayerStats.CurrentHealth = CurrentHealth;
			PlayerStats.HealthMax = MaxHealth;
		}

		if ( Identity != null )
		{
			Identity.CurrentHealth = CurrentHealth;
			Identity.HealthMax     = MaxHealth;
			Identity.IsDead        = false;
		}
	}

	protected override void OnUpdate()
	{
		if ( SyncedIsDead )
		{
			EmitDeathEventOnce();
			return;
		}

		// Update UI only if this is the player
		if ( ShouldWriteLocalPlayerStats )
			PlayerStats.CurrentHealth = CurrentHealth;

		if ( Identity != null )
			Identity.CurrentHealth = CurrentHealth;
	}

	/// <summary>
	/// Apply damage to the player.
	/// </summary>
	public void TakeDamage( float damageAmount, GameObject attacker = null )
	{
		if ( SyncedIsDead ) return;
		if ( damageAmount <= 0f ) return;

		// In multiplayer, enemy health is host-authoritative.
		// Client shots should request host application rather than mutating proxy state locally.
		if ( Networking.IsActive && !IsPlayer && !IsAuthoritativeInstance() )
		{
			RequestEnemyDamageOnHost( damageAmount, attacker?.WorldPosition ?? Vector3.Zero );
			return;
		}

		ApplyDamageInternal( damageAmount, attacker );
	}

	private void ApplyDamageInternal( float damageAmount, GameObject attacker, Vector3? attackerWorldPos = null )
	{
		if ( SyncedIsDead ) return;

		// In multiplayer, never apply player damage simulation on proxy instances.
		// This avoids cross-client contamination where damaging a remote player
		// incorrectly mutates local static HUD/player state.
		if ( IsPlayer && Networking.IsActive && IsProxy )
			return;

		CurrentHealth = MathF.Max( 0f, CurrentHealth - damageAmount );

		if ( ShouldWriteLocalPlayerStats )
			PlayerStats.TotalDamageTaken += (int)damageAmount;
		if ( Identity != null )
			Identity.TotalDamageTaken += (int)damageAmount;

		OnDamageTaken?.Invoke( damageAmount );
		OnDamageTakenWithAttacker?.Invoke( damageAmount, attacker );

		if ( !IsPlayer && Networking.IsActive && IsAuthoritativeInstance() )
		{
			var pos = attackerWorldPos ?? attacker?.WorldPosition ?? Vector3.Zero;
			BroadcastEnemyDamageReaction( damageAmount, pos );
		}

		if ( CurrentHealth <= 0f )
		{
			// If already incapacitated, don't trigger Die() again — IncapacitationComponent handles death
			bool alreadyIncap = (ShouldWriteLocalPlayerStats && PlayerStats.IsIncapacitated) || (Identity?.IsIncapacitated ?? false);
			if ( alreadyIncap )
			{
				CurrentHealth = 0.1f;
				return;
			}
			Die( attacker );
		}

		if ( ShouldWriteLocalPlayerStats )
			PlayerStats.CurrentHealth = CurrentHealth;
		if ( Identity != null )
			Identity.CurrentHealth = CurrentHealth;
	}

	[Rpc.Broadcast]
	private void RequestEnemyDamageOnHost( float damageAmount, Vector3 attackerWorldPos )
	{
		if ( !Networking.IsActive || !IsAuthoritativeInstance() || IsPlayer )
			return;

		ApplyDamageInternal( damageAmount, attacker: null, attackerWorldPos );
	}

	[Rpc.Broadcast]
	private void BroadcastEnemyDamageReaction( float damageAmount, Vector3 attackerWorldPos )
	{
		if ( IsPlayer )
			return;

		OnDamageTakenWithPosition?.Invoke( damageAmount, attackerWorldPos );
	}

	/// <summary>
	/// Heal the player.
	/// </summary>
	public void Heal( float healAmount )
	{
		if ( SyncedIsDead || healAmount <= 0f )
			return;

		CurrentHealth = MathF.Min( MaxHealth, CurrentHealth + healAmount );
		OnHealed?.Invoke( healAmount );

		if ( ShouldWriteLocalPlayerStats )
			PlayerStats.CurrentHealth = CurrentHealth;
		if ( Identity != null )
			Identity.CurrentHealth = CurrentHealth;
	}

	/// <summary>
	/// Kill the player.
	/// </summary>
	public void Die( GameObject attacker = null )
	{
		LogDeathDiag( "Die() entered" );

		if ( SyncedIsDead )
		{
			LogDeathDiag( "Die() early return: already dead" );
			return;
		}

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

		SyncedIsDead = true;
		CurrentHealth = 0f;
		LogDeathDiag( "Die() applied dead state" );

		// Only disable movement for the player — never search parent/scene-root
		// (on top-level enemies, Parent IS the scene root and would find the player's movement)
		if ( IsPlayer )
		{
			var playerMovement = Components.GetInDescendantsOrSelf<PlayerMovement>()
			                  ?? GameObject.Parent?.Components.GetInDescendantsOrSelf<PlayerMovement>();
			if ( playerMovement is not null )
				playerMovement.Enabled = false;
		}

		EmitDeathEventOnce();
		LogDeathDiag( "Die() after EmitDeathEventOnce" );

		// Broadcast enemy death visuals to all clients; guard prevents double-fire on the host
		if ( !IsPlayer && Networking.IsActive && IsAuthoritativeInstance() )
		{
			LogDeathDiag( "Die() broadcasting enemy death" );
			BroadcastEnemyDeath();
		}

		if ( ShouldWriteLocalPlayerStats )
		{
			PlayerStats.CurrentHealth   = 0f;
			PlayerStats.IsDead          = true;
			PlayerStats.IsIncapacitated = false;
		}

		if ( Identity != null )
		{
			Identity.CurrentHealth   = 0f;
			Identity.IsDead          = true;
			Identity.IsIncapacitated = false;
		}

		if ( IsPlayer )
			Log.Info( $"Player died! Attacker: {(attacker?.Name ?? "Unknown")}" );
	}

	[Rpc.Broadcast]
	private void BroadcastEnemyDeath()
	{
		LogDeathDiag( "BroadcastEnemyDeath() received" );

		// Do NOT gate on SyncedIsDead here — the host sets SyncedIsDead=true in Die() before
		// calling this RPC, so the [Sync] update can reach clients before the RPC, making
		// the old guard skip the client entirely. Let deathEventEmitted (a local, unsynced
		// field) handle double-fire prevention instead — it's false on clients until the
		// death event actually fires there.
		if ( IsPlayer ) return;
		SyncedIsDead = true;
		CurrentHealth = 0f;
		LogDeathDiag( "BroadcastEnemyDeath() applied dead state" );
		EmitDeathEventOnce();
		LogDeathDiag( "BroadcastEnemyDeath() after EmitDeathEventOnce" );
	}

	/// <summary>
	/// Broadcast RPC so a teammate's heal applies on the owning client,
	/// ensuring the [Sync] CurrentHealth replicates to everyone correctly.
	/// </summary>
	[Rpc.Broadcast]
	public void RequestHealFromTeammate( float amount )
	{
		// Only the owner of this player applies the heal — proxies skip it.
		if ( IsPlayer && Networking.IsActive && IsProxy ) return;
		Heal( amount );
	}

	/// <summary>
	/// Revive the player (for respawning).
	/// </summary>
	public void Revive()
	{
		SyncedIsDead = false;
		deathEventEmitted = false;
		CurrentHealth = MaxHealth;

		if ( IsPlayer )
		{
			var playerMovement = Components.GetInDescendantsOrSelf<PlayerMovement>( true )
			                  ?? GameObject.Parent?.Components.GetInDescendantsOrSelf<PlayerMovement>( true );
			if ( playerMovement is not null )
				playerMovement.Enabled = true;
		}

		if ( ShouldWriteLocalPlayerStats )
			PlayerStats.CurrentHealth = CurrentHealth;

		if ( Identity != null )
		{
			Identity.CurrentHealth = CurrentHealth;
			Identity.IsDead        = false;
		}
	}

	/// <summary>
	/// Check if the player is dead.
	/// </summary>
	public bool IsDead => SyncedIsDead;

	private void EmitDeathEventOnce()
	{
		LogDeathDiag( "EmitDeathEventOnce() entered" );

		if ( deathEventEmitted )
		{
			LogDeathDiag( "EmitDeathEventOnce() early return: already emitted" );
			return;
		}

		deathEventEmitted = true;
		LogDeathDiag( "EmitDeathEventOnce() invoking OnDeath" );
		OnDeath?.Invoke();
		LogDeathDiag( "EmitDeathEventOnce() completed" );
	}

	/// <summary>
	/// Get health as a percentage (0-1).
	/// </summary>
	public float HealthPercent => CurrentHealth / MaxHealth;
}
