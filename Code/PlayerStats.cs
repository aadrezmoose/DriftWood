public struct DamageIndicator
{
	public float Angle;   // degrees: 0=front, 90=right, 180=back, -90=left
	public float Alpha;   // 1.0 -> 0.0 as it fades
}

public static class PlayerStats
{
	// Exposed for UI. Updated every frame by PlayerMovement.
	public static float CurrentStamina { get; set; } = 0f;
	public static float StaminaMax { get; set; } = 5f;

	// Exposed for UI. Updated by HealthComponent.
	public static float CurrentHealth { get; set; } = 100f;
	public static float HealthMax { get; set; } = 100f;

	// Exposed for UI. Updated by WeaponManager.
	public static string CarriedItem { get; set; } = string.Empty;
	public static System.Collections.Generic.List<string> CarriedItems { get; } = new();

	// Legacy compat — Inventory.cs still calls this; WeaponManager owns the real sync now
	public static void SyncCarriedItems( System.Collections.Generic.IReadOnlyList<BaseItem> items )
	{
		CarriedItems.Clear();
		foreach ( var item in items )
			if ( item != null ) CarriedItems.Add( item.ItemName );
		CarriedItem = CarriedItems.Count > 0 ? CarriedItems[0] : string.Empty;
	}

	// Unified inventory slot display (weapons + carry items in order)
	public static System.Collections.Generic.List<string> AllSlotNames { get; } = new();
	public static int ActiveSlotIndex { get; set; } = 0;
	public static int WeaponSlotCount { get; set; } = 0;

	// Exposed for UI. Updated by WeaponManager.
	public static int CurrentAmmo { get; set; }
	public static int ReserveAmmo { get; set; }
	public static int AmmoClip { get; set; } = 1;
	public static string WeaponName { get; set; } = "Pistol";

	// Incapacitation / death state
	public static bool IsIncapacitated { get; set; } = false;
	public static float BleedOutHealth { get; set; } = 100f; // 100 -> 0
	public static bool IsDead { get; set; } = false;

	// Win condition
	public static bool LevelComplete { get; set; } = false;

	// Objective state for HUD
	public static string ObjectiveText { get; set; } = string.Empty;
	public static float ObjectiveProgress01 { get; set; } = -1f; // < 0 hides bar
	public static bool ObjectiveUrgent { get; set; } = false;

	// Pause state — set by PauseMenu, read by CameraMovement
	public static bool IsPaused { get; set; } = false;

	// Recoil — written by Gun.Fire(), consumed by CameraMovement
	public static float PendingRecoil { get; set; } = 0f;

	// End-of-level stats
	public static int TotalKills { get; set; } = 0;
	public static int TotalDamageTaken { get; set; } = 0;
	public static int TimesIncapacitated { get; set; } = 0;
	public static int ShotsFired { get; set; } = 0;
	public static int ShotsHit { get; set; } = 0;
	public static float AccuracyPercent => ShotsFired > 0 ? System.Math.Min( (float)ShotsHit / ShotsFired * 100f, 100f ) : 0f;
	public static float LevelElapsedSeconds { get; set; } = 0f;

	// Heal progress — -1 when not healing, 0-1 while holding medkit
	public static float HealProgress { get; set; } = -1f;
	// Name of the teammate being healed, empty = healing self
	public static string HealTargetName { get; set; } = string.Empty;
	// True when the progress bar represents a manual revive rather than a medkit heal
	public static bool HealIsRevive { get; set; } = false;

	// Directional damage indicators (all access is on the game thread — no concurrent queue needed)
	private static readonly System.Collections.Generic.Queue<DamageIndicator> _pending = new();
	private static readonly System.Collections.Generic.List<DamageIndicator> _active = new();

	/// <summary>Read-only snapshot for HUD rendering. Swapped each game tick.</summary>
	public static DamageIndicator[] DamageIndicatorsSnapshot { get; private set; } = System.Array.Empty<DamageIndicator>();

	/// <summary>Call from OnAwake to clear stale state between editor play sessions.</summary>
	public static void ResetDamageIndicators()
	{
		_pending.Clear();
		_active.Clear();
		DamageIndicatorsSnapshot = System.Array.Empty<DamageIndicator>();

		// Reset per-session flags
		IsIncapacitated = false;
		BleedOutHealth = 100f;
		IsDead = false;
		LevelComplete = false;
		IsPaused = false;
		PendingRecoil = 0f;
		ShowDirectorDebug = false;
		ObjectiveText = string.Empty;
		ObjectiveProgress01 = -1f;
		ObjectiveUrgent = false;

		// Reset stats
		TotalKills = 0;
		TotalDamageTaken = 0;
		TimesIncapacitated = 0;
		ShotsFired = 0;
		ShotsHit = 0;
		LevelElapsedSeconds = 0f;
		HealProgress = -1f;
		HealTargetName = string.Empty;
		HealIsRevive = false;
	}

	/// <summary>Enqueue a new damage indicator (game-thread only).</summary>
	public static void AddDamageIndicator( float angle )
	{
		_pending.Enqueue( new DamageIndicator { Angle = angle, Alpha = 1f } );
	}

	/// <summary>Call once per frame from the game thread (CameraMovement.OnUpdate).</summary>
	public static void TickDamageIndicators( float delta )
	{
		// Drain pending additions
		while ( _pending.Count > 0 )
			_active.Add( _pending.Dequeue() );

		// Fade and remove expired indicators
		for ( int i = _active.Count - 1; i >= 0; i-- )
		{
			var d = _active[i];
			d.Alpha -= delta * 1.2f;
			if ( d.Alpha <= 0f ) { _active.RemoveAt( i ); continue; }
			_active[i] = d;
		}

		DamageIndicatorsSnapshot = _active.ToArray();
	}

	// Pickup hint — set by WeaponManager each frame when player is near a pickup
	public static string NearbyPickupHint { get; set; } = string.Empty;

	// Human-readable debug message the HUD can show when something is wrong.
	public static string DebugMessage { get; set; } = string.Empty;

	// Director debug overlay toggle — toggled via console command "director_debug"
	public static bool ShowDirectorDebug { get; set; } = false;

	// Health system events
	public static event System.Action<float> OnDamageTaken;
	public static event System.Action<float> OnHealed;
	public static event System.Action OnPlayerDeath;

	public static void RaiseDamageTaken( float damage ) => OnDamageTaken?.Invoke( damage );
	public static void RaiseHealed( float amount ) => OnHealed?.Invoke( amount );
	public static void RaisePlayerDeath() => OnPlayerDeath?.Invoke();
}
