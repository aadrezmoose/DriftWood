using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Per-player data bus — the multiplayer-ready replacement for the static PlayerStats class.
/// Place this component on the player root GameObject alongside PlayerMovement, WeaponManager, etc.
///
/// [Sync] properties are replicated from the owning client to all proxies automatically.
/// List&lt;T&gt; properties (AllSlotNames, CarriedItems) cannot be [Sync]'d — they are owner-only
/// and only read by the local HUD, so no sync is needed for them.
/// </summary>
public sealed class PlayerIdentity : Component
{
	[Property] public bool EnableNetworkDiagnostics { get; set; } = false;

	private bool IsOwnedByLocalConnection()
	{
		if ( !Networking.IsActive ) return true;

		var movement = Movement ?? Components.GetInDescendantsOrSelf<PlayerMovement>();
		var targetObject = movement?.GameObject ?? GameObject;

		var owner = targetObject?.Network?.Owner;
		var localConn = Connection.Local;
		if ( owner != null && localConn != null )
			return owner.SteamId == localConn.SteamId;

		if ( movement != null )
			return !movement.IsProxy;

		return !IsProxy;
	}

	public static bool IsOwnedByLocal( GameObject gameObject )
	{
		if ( gameObject == null || !gameObject.IsValid() ) return false;
		if ( !Networking.IsActive ) return true;

		var movement = gameObject.Components.GetInDescendantsOrSelf<PlayerMovement>()
			?? gameObject.Components.GetInAncestorsOrSelf<PlayerMovement>();
		var identity = gameObject.Components.GetInDescendantsOrSelf<PlayerIdentity>()
			?? gameObject.Components.GetInAncestorsOrSelf<PlayerIdentity>();

		var targetObject = movement?.GameObject ?? identity?.GameObject ?? gameObject;

		var owner = targetObject.Network?.Owner;
		var localConn = Connection.Local;
		if ( owner != null && localConn != null )
			return owner.SteamId == localConn.SteamId;

		if ( movement != null )
			return !movement.IsProxy;

		if ( identity != null )
			return identity.IsOwnedByLocalConnection();

		return false;
	}

	/// <summary>All active PlayerIdentity instances in the scene. Follows the SpawnNode.All pattern.</summary>
	public static readonly List<PlayerIdentity> All = new();

	/// <summary>Fired when a PlayerIdentity registers itself (OnStart). AIDirector uses this to subscribe to health events.</summary>
	public static event System.Action<PlayerIdentity> OnPlayerRegistered;
	/// <summary>Fired when a PlayerIdentity is destroyed.</summary>
	public static event System.Action<PlayerIdentity> OnPlayerUnregistered;

	// ── Cached sibling component refs ──────────────────────────────────
	public HealthComponent         Health   { get; private set; }
	public PlayerMovement          Movement { get; private set; }
	public WeaponManager           Weapons  { get; private set; }
	public IncapacitationComponent Incap    { get; private set; }

	// ── Health / stamina ───────────────────────────────────────────────
	[Sync] public float CurrentHealth  { get; set; } = 100f;
	[Sync] public float HealthMax      { get; set; } = 100f;
	[Sync] public float CurrentStamina { get; set; } = 0f;
	[Sync] public float StaminaMax     { get; set; } = 5f;

	// ── Incapacitation / death ─────────────────────────────────────────
	[Sync] public bool  IsIncapacitated { get; set; } = false;
	[Sync] public float BleedOutHealth  { get; set; } = 100f;
	[Sync] public bool  IsDead          { get; set; } = false;

	// ── Weapon / ammo ──────────────────────────────────────────────────
	[Sync] public int    CurrentAmmo { get; set; } = 0;
	[Sync] public int    ReserveAmmo { get; set; } = 0;
	[Sync] public int    AmmoClip    { get; set; } = 1;
	[Sync] public string WeaponName  { get; set; } = string.Empty;
	[Sync] public string ActiveHeldModelPath { get; set; } = string.Empty;

	// ── Inventory slots (List<T> cannot be [Sync]'d — owner/local HUD only) ──
	public List<string> AllSlotNames   { get; } = new();
	[Sync] public int   ActiveSlotIndex  { get; set; } = 0;
	[Sync] public int   WeaponSlotCount  { get; set; } = 0;
	[Sync] public string CarriedItem     { get; set; } = string.Empty;
	public List<string> CarriedItems     { get; } = new();

	// ── Camera / input ─────────────────────────────────────────────────
	public float PendingRecoil    { get; set; } = 0f;
	[Sync] public bool  IsPaused  { get; set; } = false;
	[Sync] public bool  FlashlightOn { get; set; } = false;
	[Sync] public string NearbyPickupHint { get; set; } = string.Empty;

	// ── Objectives (global state — written by host, synced to all) ─────
	[Sync] public string ObjectiveText       { get; set; } = string.Empty;
	[Sync] public float  ObjectiveProgress01 { get; set; } = -1f;
	[Sync] public bool   ObjectiveUrgent     { get; set; } = false;
	[Sync] public bool   LevelComplete       { get; set; } = false;

	// ── Display name & character (set once on spawn, persists until object destroyed) ──
	[Sync] public string PlayerName        { get; set; } = string.Empty;
	[Sync] public string CharacterModel    { get; set; } = string.Empty;
	[Sync] public string CharacterClothing { get; set; } = string.Empty;

	// ── End-of-level stats ─────────────────────────────────────────────
	[Sync] public int   TotalKills         { get; set; } = 0;
	[Sync] public int   TotalDamageTaken   { get; set; } = 0;
	[Sync] public int   TimesIncapacitated { get; set; } = 0;
	[Sync] public int   ShotsFired         { get; set; } = 0;
	[Sync] public int   ShotsHit           { get; set; } = 0;
	public float AccuracyPercent => ShotsFired > 0
		? System.Math.Min( (float)ShotsHit / ShotsFired * 100f, 100f ) : 0f;

	// ── Heal progress (owner-only, not synced — local HUD only) ──────
	public float HealProgress { get; set; } = -1f;
	public string HealTargetName { get; set; } = string.Empty;
	public bool HealIsRevive { get; set; } = false;

	// ── Directional damage indicators (owner-only, not synced) ────────
	private readonly System.Collections.Generic.Queue<DamageIndicator> _pendingIndicators = new();
	private readonly List<DamageIndicator>            _activeIndicators  = new();
	public DamageIndicator[] DamageIndicatorsSnapshot { get; private set; } = System.Array.Empty<DamageIndicator>();

	public void AddDamageIndicator( float angle )
	{
		_pendingIndicators.Enqueue( new DamageIndicator { Angle = angle, Alpha = 1f } );
	}

	public void TickDamageIndicators( float delta )
	{
		while ( _pendingIndicators.Count > 0 )
			_activeIndicators.Add( _pendingIndicators.Dequeue() );

		for ( int i = _activeIndicators.Count - 1; i >= 0; i-- )
		{
			var d = _activeIndicators[i];
			d.Alpha -= delta * 1.2f;
			if ( d.Alpha <= 0f ) { _activeIndicators.RemoveAt( i ); continue; }
			_activeIndicators[i] = d;
		}

		DamageIndicatorsSnapshot = _activeIndicators.ToArray();
	}

	// ── Static accessors ───────────────────────────────────────────────

	/// <summary>
	/// The local player's identity.
	/// In a networked session: the instance NOT flagged as a proxy (owned by this connection).
	/// In single-player (Networking.IsActive == false): All[0].
	/// </summary>
	public static PlayerIdentity Local
	{
		get
		{
			if ( !Networking.IsActive )
				return All.Count > 0 ? All[0] : null;

			var localByOwner = All
				.Where( p => p != null && p.IsValid && p.GameObject != null && p.GameObject.IsValid && p.GameObject.Active && p.IsOwnedByLocalConnection() )
				.LastOrDefault();
			if ( localByOwner != null )
				return localByOwner;

			var localByProxy = All
				.Where( p => p != null && p.IsValid && p.GameObject != null && p.GameObject.IsValid && p.GameObject.Active && !p.IsProxy )
				.LastOrDefault();
			if ( localByProxy != null )
				return localByProxy;

			return null;
		}
	}
	public static PlayerIdentity Nearest( Vector3 worldPos )
	{
		PlayerIdentity best     = null;
		float          bestDist = float.MaxValue;
		foreach ( var p in All )
		{
			if ( p.IsDead ) continue;
			float d = Vector3.DistanceBetween( worldPos, p.GameObject.WorldPosition );
			if ( d < bestDist ) { bestDist = d; best = p; }
		}
		return best;
	}

	/// <summary>True if at least one player is alive and not dead.</summary>
	public static bool AnyAlive => All.Exists( p => !p.IsDead );

	/// <summary>True if all living players are incapacitated (no one can self-revive).</summary>
	public static bool AllIncapacitated => All.Count > 0 && All.TrueForAll( p => p.IsIncapacitated || p.IsDead );

	// ── Lifecycle ──────────────────────────────────────────────────────

	protected override void OnStart()
	{
		// Clean stale entries from previous editor play sessions
		All.RemoveAll( p => p is null || !p.IsValid );
		All.Add( this );

		// Cache sibling components — OnStart is safe because the full hierarchy is initialized
		Health   = Components.GetInDescendantsOrSelf<HealthComponent>();
		Movement = Components.GetInDescendantsOrSelf<PlayerMovement>();
		Weapons  = Components.GetInDescendantsOrSelf<WeaponManager>();
		Incap    = Components.GetInDescendantsOrSelf<IncapacitationComponent>();

		// Only the owner resets state; proxies receive synced values from the owner.
		// Prefer explicit owner check in multiplayer since proxy flags can be ambiguous during spawn.
		var owner = GameObject?.Network?.Owner;
		var localConn = Connection.Local;
		bool ownerIsLocal = !Networking.IsActive
			|| (owner != null && localConn != null && owner.SteamId == localConn.SteamId)
			|| (owner == null && !IsProxy);
		var dbgChar = GameSettings.SelectedCharacter;
		Log.Info( $"[PlayerIdentity] ownerIsLocal={ownerIsLocal} SelectedIndex={GameSettings.SelectedCharacterIndex} SelectedChar={dbgChar?.Name ?? "null"} ClothingPathsLen={dbgChar?.ClothingPaths?.Length ?? -1} ClothingConfig='{dbgChar?.ClothingConfig ?? "null"}'" );
		Log.Info( $"[PlayerIdentity] CharDef.All.Count={CharacterDefinition.All.Count} All[0].ClothingPathsLen={( CharacterDefinition.All.Count > 0 ? CharacterDefinition.All[0].ClothingPaths?.Length.ToString() ?? "null" : "N/A" )}" );
		if ( ownerIsLocal )
		{
			Reset();
			PlayerName        = Connection.Local?.DisplayName ?? "Player";
			CharacterModel    = GameSettings.SelectedCharacter?.ModelPath ?? string.Empty;
			CharacterClothing = GameSettings.SelectedCharacter?.ClothingConfig ?? string.Empty;
			Log.Info( $"[PlayerIdentity] Set CharacterModel='{CharacterModel}' CharacterClothing='{CharacterClothing}'" );
		}

		OnPlayerRegistered?.Invoke( this );

		localConn = Connection.Local;
		if ( EnableNetworkDiagnostics )
			Log.Info( $"[PlayerIdentity][{(localConn?.IsHost==true?"HOST":"CLIENT")} {localConn?.DisplayName}] Registered — {All.Count} player(s), IsProxy={IsProxy}" );

		if ( EnableNetworkDiagnostics )
		{
			bool isLocalIdentity = ReferenceEquals( Local, this );
			owner = GameObject?.Network?.Owner;
			ownerIsLocal = owner != null && localConn != null && owner.SteamId == localConn.SteamId;
			Log.Info( $"[PlayerIdentity] REGISTER: IsProxy={IsProxy} IsLocalIdentity={isLocalIdentity} OwnerIsLocal={ownerIsLocal} Owner={owner?.DisplayName}({owner?.SteamId}) LocalConn={localConn?.DisplayName}({localConn?.SteamId})" );

			// Delayed dump after 1s to show final ownership state
			_ = DumpStateAfterDelay( 1f );
		}
	}

	private async System.Threading.Tasks.Task DumpStateAfterDelay( float seconds )
	{
		await GameTask.DelaySeconds( seconds );
		if ( !IsValid ) return;
		var localConn = Connection.Local;
		var owner = GameObject?.Network?.Owner;
		var localIdentity = Local;
		bool ownerIsLocal = owner != null && localConn != null && owner.SteamId == localConn.SteamId;
		Log.Info( $"[PlayerIdentity] DELAYED[{(localConn?.IsHost==true?"HOST":"CLIENT")}]: GO={GameObject?.Name} IsProxy={IsProxy} Active={GameObject?.Active} OwnerIsLocal={ownerIsLocal} Owner={owner?.DisplayName}({owner?.SteamId}) Local={localIdentity?.GameObject?.Name ?? "none"} AllCount={All.Count}" );
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
		OnPlayerUnregistered?.Invoke( this );
	}

	/// <summary>Resets all per-session state. Call between rounds/restarts. Owner only.</summary>
	public void Reset()
	{
		CurrentHealth    = HealthMax;
		IsIncapacitated  = false;
		BleedOutHealth   = 100f;
		IsDead           = false;
		LevelComplete    = false;
		IsPaused         = false;
		FlashlightOn     = false;
		PendingRecoil    = 0f;
		ObjectiveText    = string.Empty;
		ObjectiveProgress01 = -1f;
		ObjectiveUrgent  = false;
		NearbyPickupHint = string.Empty;
		TotalKills       = 0;
		TotalDamageTaken = 0;
		TimesIncapacitated = 0;
		ShotsFired       = 0;
		ShotsHit         = 0;
		HealProgress     = -1f;
		HealTargetName   = string.Empty;
		HealIsRevive     = false;
		ActiveHeldModelPath = string.Empty;
		CharacterClothing   = string.Empty;

		_pendingIndicators.Clear();
		_activeIndicators.Clear();
		DamageIndicatorsSnapshot = System.Array.Empty<DamageIndicator>();
	}
}
