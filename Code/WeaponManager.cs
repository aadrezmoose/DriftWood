using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed class WeaponManager : Component
{
	[Property] public bool EnableDiagnostics { get; set; } = false;

	/// <summary>Assign the Primary weapon slot GO (Shotgun, SMG, Rifle, Sniper) in the inspector.</summary>
	[Property] public GameObject PrimarySlot { get; set; }
	/// <summary>Assign the Secondary weapon slot GO (Pistol, Melee) in the inspector.</summary>
	[Property] public GameObject SecondarySlot { get; set; }

	// --- NETWORKED WEAPON STATE ---
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetPrimaryWeaponPrefab { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetPrimaryViewModel { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetPrimaryAnimGraph { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetPrimaryHandsModel { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetPrimaryOverlayModel { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public Vector3 NetPrimaryViewModelOffset { get; set; } = Vector3.Zero;

	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetSecondaryWeaponPrefab { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetSecondaryViewModel { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetSecondaryAnimGraph { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetSecondaryHandsModel { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetSecondaryOverlayModel { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public Vector3 NetSecondaryViewModelOffset { get; set; } = Vector3.Zero;

	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetMainHealOverlayModel { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetSubHealOverlayModel { get; set; } = string.Empty;
	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public string NetUtilityOverlayModel { get; set; } = string.Empty;

	[Sync, Change( nameof(OnNetWeaponStateChanged) )] public int NetCurrentSlot { get; set; } = 0;

	private bool _suppressNetWeaponStateChange = false;

	// Tracks the pickup object used to equip each weapon slot, so it can be dropped on swap
	private WeaponPickup primaryEquippedPickup;
	private WeaponPickup secondaryEquippedPickup;
	private WeaponPickup _lastAppliedPrimaryViewModelPickup;
	private WeaponPickup _lastAppliedSecondaryViewModelPickup;
	private string _lastAppliedPrimaryPrefabPath = string.Empty;
	private Vector3 _lastAppliedPrimaryOffset = Vector3.Zero;
	private string _lastAppliedSecondaryPrefabPath = string.Empty;
	private Vector3 _lastAppliedSecondaryOffset = Vector3.Zero;
	private string _lastAppliedMainHealOverlayPath = string.Empty;
	private string _lastAppliedSubHealOverlayPath = string.Empty;
	private string _lastAppliedUtilityOverlayPath = string.Empty;

	// Item slots — filled at runtime by pickup
	private BaseItem mainHealItem;      // slot 2
	private BaseItem secondaryHealItem; // slot 3
	private BaseItem utilityItem;       // slot 4

	private int currentSlot = 0;
	private const int TotalSlots = 5;

	private PlayerIdentity _identity;
	private PlayerIdentity Identity => _identity ??= Components.GetInDescendantsOrSelf<PlayerIdentity>();

	private bool IsLocallyControlled() => PlayerIdentity.IsOwnedByLocal( GameObject );

	// Per-slot viewmodel data cache — stores model/animgraph/hands/overlay for each slot
	private struct SlotViewModelData
	{
		public Model WeaponModel;
		public AnimationGraph AnimGraph;
		public Model HandsModel;
		public Vector3 PositionOffset;
		public Model OverlayModel;
		public string AnimFireParam;
		public string AnimReloadParam;
		public string AnimReloadEmptyParam;
	}
	private SlotViewModelData[] _slotData = new SlotViewModelData[TotalSlots];

	private PlayerMovement _movement;
	private CameraMovement _cameraCache;
	private bool _cameraFound = false;

	// Single GunViewModel — found lazily since it may not exist yet on clients at OnStart() time
	// No longer stored as direct field — accessed through ViewModel property
	private GunViewModel ViewModelCache;
	private bool ViewModelFound = false;

	// ViewModelHandler for notifying rest-state changes on weapon swaps
	private ViewModelHandler ViewModelHandlerCache;
	private bool ViewModelHandlerFound = false;

	private GunViewModel ViewModel
	{
		get
		{
			// If we already found it once, use the cache
			if ( ViewModelFound ) return ViewModelCache;

			// Try to find it (might fail on client's first frame)
			ViewModelCache = Components.GetInDescendantsOrSelf<GunViewModel>();

			// Only lock the cache once we successfully find it
			if ( ViewModelCache != null )
			{
				ViewModelFound = true;
				GunViewModel.Current = ViewModelCache;
				if ( EnableDiagnostics )
					Log.Info( "[WeaponManager] Found GunViewModel via lazy-load property" );
			}
			else if ( EnableDiagnostics && !ViewModelFound )
			{
				Log.Info( "[WeaponManager] GunViewModel not found yet (will retry next frame)" );
			}

			return ViewModelCache;
		}
	}

	private CameraMovement Camera
	{
		get
		{
			// If we already found it, use the cache
			if ( _cameraFound ) return _cameraCache;

			// Try to find it (might fail on client's first frame)
			_cameraCache = Components.GetInDescendantsOrSelf<CameraMovement>();

			// Only lock the cache once we successfully find it
			if ( _cameraCache != null )
				_cameraFound = true;

			return _cameraCache;
		}
	}

	private ViewModelHandler ViewModelHandler
	{
		get
		{
			// If we already found it, use the cache
			if ( ViewModelHandlerFound ) return ViewModelHandlerCache;

			// Try to find it (might fail on client's first frame)
			ViewModelHandlerCache = Components.GetInDescendantsOrSelf<ViewModelHandler>();

			// Only lock the cache once we successfully find it
			if ( ViewModelHandlerCache != null )
			{
				ViewModelHandlerFound = true;
				Log.Info( "[WeaponManager] Found ViewModelHandler via lazy-load" );
			}
			else if ( !ViewModelHandlerFound )
			{
				Log.Warning( "[WeaponManager] ViewModelHandler not found yet (will retry next frame)" );
			}

			return ViewModelHandlerCache;
		}
	}

	// Per-frame interact target — set by UpdateInteractTarget(), consumed by HandleInput() and UpdatePickupHint()
	private string _hoverHint = string.Empty;
	private WeaponPickup _hoverWeapon;
	private BaseItem _hoverItem;
	private bool _hoverItemIsStationary;
	private SafeRoomDoor _hoverDoor;
	private CraneEventButton _hoverButton;

	private float shoveCooldown = 0f;
	[Property] public float ShoveCooldown { get; set; } = 1.2f;
	[Property] public float ShoveRange { get; set; } = 120f;
	[Property] public float ShoveRadius { get; set; } = 55f;

	private GameObject ActiveWeaponObject =>
		currentSlot == 0 ? PrimarySlot : currentSlot == 1 ? SecondarySlot : null;

	public Gun CurrentGun => ActiveWeaponObject?.Components.GetInDescendantsOrSelf<Gun>();
	private MeleeWeapon CurrentMelee => ActiveWeaponObject?.Components.GetInDescendantsOrSelf<MeleeWeapon>();
	private bool IsWeaponSlot => currentSlot < 2;
	private BaseItem ActiveItem => currentSlot == 2 ? mainHealItem
		: currentSlot == 3 ? secondaryHealItem
		: currentSlot == 4 ? utilityItem : null;

	protected override void OnAwake()
	{
		_movement = Components.Get<PlayerMovement>();
		// Camera is now lazy-loaded via the Camera property to handle multiplayer timing
		var playerMovement = _movement;
		if ( playerMovement?.Head != null )
		{
			var head = playerMovement.Head;
			PrimarySlot?.Components.GetInDescendantsOrSelf<Gun>()?.SetPlayerHead( head );
			PrimarySlot?.Components.GetInDescendantsOrSelf<Gun>()?.SetOwner( GameObject );
			SecondarySlot?.Components.GetInDescendantsOrSelf<Gun>()?.SetPlayerHead( head );
			SecondarySlot?.Components.GetInDescendantsOrSelf<Gun>()?.SetOwner( GameObject );
			SecondarySlot?.Components.GetInDescendantsOrSelf<MeleeWeapon>()?.SetPlayerHead( head );
			SecondarySlot?.Components.GetInDescendantsOrSelf<MeleeWeapon>()?.SetOwner( GameObject );
		}

		SyncAllSlots();
	}

	protected override void OnStart()
	{
		// Initialize slot positions - all moved further back from camera
		_slotData[0].PositionOffset = new Vector3( 5, -8, -15 );   // Primary (Shotgun/SMG) - moved back
		_slotData[1].PositionOffset = new Vector3( 8, -8, -14 );   // Secondary (Pistol) - moved back
		_slotData[2].PositionOffset = new Vector3( 0, -5, -18 );   // MainHeal - moved back
		_slotData[3].PositionOffset = new Vector3( 0, -5, -18 );   // SubHeal - moved back
		_slotData[4].PositionOffset = new Vector3( 0, -5, -18 );   // Utility - moved back

		// GunViewModel lookup is now deferred until it's actually needed via the ViewModel property
		// This allows clients time to spawn the Camera child with GunViewModel before we try to use it
	}

	protected override void OnUpdate()
	{
		var isLocal = IsLocallyControlled();

		if ( !isLocal ) return;

		if ( shoveCooldown > 0f ) shoveCooldown -= Time.Delta;

		SyncAllSlots();
		UpdateViewModelVisibility(currentSlot);

		if ( PlayerStats.IsDead ) return;

		if ( PlayerStats.IsIncapacitated )
		{
			// Auto-switch to secondary (pistol) slot when downed
			if ( currentSlot != 1 ) { currentSlot = 1; SyncAllSlots(); }

			if ( Input.Pressed( "Attack1" ) ) CurrentGun?.Fire();
			if ( Input.Pressed( "Reload" ) ) CurrentGun?.Reload();
			UpdateHUD();
			return;
		}

		UpdateInteractTarget();
		HandleSlotSwitching();
		HandleInput();
		UpdateHUD();
		UpdatePickupHint();
	}

	private void OnNetWeaponStateChanged()
	{
		if ( _suppressNetWeaponStateChange ) return;

		// Viewmodel will be updated in UpdateViewModelVisibility() which runs every frame
		_lastAppliedMainHealOverlayPath = NetMainHealOverlayModel;
		_lastAppliedSubHealOverlayPath = NetSubHealOverlayModel;
		_lastAppliedUtilityOverlayPath = NetUtilityOverlayModel;
	}

	private void HandleSlotSwitching()
	{
		var scroll = Input.MouseWheel.y;
		if ( scroll > 0f ) CycleSlot( -1 );
		else if ( scroll < 0f ) CycleSlot( 1 );

		if ( Input.Pressed( "Slot1" ) ) SelectSlot( 0 );
		if ( Input.Pressed( "Slot2" ) ) SelectSlot( 1 );
		if ( Input.Pressed( "Slot3" ) ) SelectSlot( 2 );
		if ( Input.Pressed( "Slot4" ) ) SelectSlot( 3 );
		if ( Input.Pressed( "Slot5" ) ) SelectSlot( 4 );
	}

	private void CycleSlot( int direction )
	{
		currentSlot = (currentSlot + direction + TotalSlots) % TotalSlots;
		SyncAllSlots();
	}

	private void SelectSlot( int index )
	{
		if ( index < 0 || index >= TotalSlots ) return;
		currentSlot = index;
		SyncAllSlots();
	}

	private void HandleInput()
	{
		if ( Input.Down( "Attack1" ) )
		{
			if ( CurrentGun != null && CurrentGun.IsAutomatic ) CurrentGun.Fire();
			CurrentMelee?.Swing();
		}

		if ( Input.Pressed( "Attack1" ) )
		{
			if ( CurrentGun != null && !CurrentGun.IsAutomatic ) CurrentGun.Fire();
		}

		if ( Input.Pressed( "Reload" ) ) CurrentGun?.Reload();

		if ( Input.Pressed( "Attack2" ) ) TryShove();

		if ( Input.Pressed( "Use" ) )
		{
			if ( !ActOnInteractTarget() )
			{
				if ( !IsWeaponSlot ) UseCurrentItem();
			}
		}
	}

	private void TryShove()
	{
		if ( shoveCooldown > 0f ) return;

		var movement = _movement;
		if ( movement?.Head == null ) return;

		var origin = movement.Head.WorldPosition;
		var forward = movement.Head.WorldRotation.Forward;

		var hits = Scene.Trace.Sphere( ShoveRadius, origin, origin + forward * ShoveRange )
			.IgnoreGameObject( GameObject )
			.WithoutTags( "trigger" )
			.WithoutTags( "headzone" )
			.RunAll();

		var shovedEnemies = new System.Collections.Generic.HashSet<Enemy>();
		foreach ( var hit in hits )
		{
			var enemy = hit.GameObject?.Components.GetInAncestorsOrSelf<Enemy>()
				?? hit.GameObject?.Components.GetInDescendantsOrSelf<Enemy>();
			if ( enemy != null && shovedEnemies.Add( enemy ) )
				enemy.ApplyShove( GameObject );
		}

		if ( shovedEnemies.Count > 0 )
			shoveCooldown = ShoveCooldown;
	}

	private void UseCurrentItem()
	{
		var item = ActiveItem;
		if ( item == null ) return;

		item.OnUse( GameObject );

		if ( item.WasConsumed || !item.IsValid )
		{
			if ( currentSlot == 2 ) mainHealItem = null;
			else if ( currentSlot == 3 ) secondaryHealItem = null;
			else if ( currentSlot == 4 ) utilityItem = null;
			SyncAllSlots();
		}
	}

	private const float InteractRange = 200f;

	/// <summary>
	/// Casts a ray from the player's head each frame and caches what's being looked at.
	/// Must be called once per update before HandleInput() and UpdatePickupHint().
	/// </summary>
	private void UpdateInteractTarget()
	{
		_hoverWeapon = null;
		_hoverItem   = null;
		_hoverItemIsStationary = false;
		_hoverDoor   = null;
		_hoverButton = null;
		_hoverHint   = string.Empty;

		// Get the camera for raycasting
		var cam = Camera ?? Components.GetInDescendantsOrSelf<CameraMovement>();
		if ( cam == null )
		{
			if ( EnableDiagnostics )
				Log.Info( "[WeaponManager] UpdateInteractTarget: No camera found" );
			return;
		}

		// Use EyeRay which is computed before network sync overwrites WorldPosition/WorldRotation
		// Fallback to WorldPosition/WorldRotation if EyeRay hasn't been set yet
		var ray = cam.EyeRay;
		if ( ray.Forward == Vector3.Zero )
			ray = new Ray( cam.WorldPosition, cam.WorldRotation.Forward );

		if ( _onUpdateFrameCount % 60 == 0 )
		{
			Log.Warning( $"[WM] Ray trace running, IsLocal={IsLocallyControlled()}" );
		}
		_onUpdateFrameCount++;

		var tr = Scene.Trace.Ray( ray, InteractRange )
			.IgnoreGameObject( GameObject )
			.WithoutTags( "trigger", "headzone" )
			.Run();

		if ( !tr.Hit )
			return;

		var go = tr.GameObject;

		// WeaponPickup
		var weapon = go.Components.GetInAncestorsOrSelf<WeaponPickup>()
			?? go.Components.GetInDescendantsOrSelf<WeaponPickup>();
		if ( weapon != null && weapon.GameObject.Enabled )
		{
			_hoverWeapon = weapon;
			_hoverHint   = weapon.WeaponDisplayName;
			return;
		}

		// BaseItem — carry or stationary (ammo pile, etc.)
		var item = go.Components.GetInAncestorsOrSelf<BaseItem>()
			?? go.Components.GetInDescendantsOrSelf<BaseItem>();
		if ( item != null && item.GameObject.Enabled )
		{
			if ( item.CanCarry && !IsSlotOccupied( item.SlotType ) )
			{
				_hoverItem             = item;
				_hoverItemIsStationary = false;
				_hoverHint             = item.ItemName;
				return;
			}
			if ( !item.CanCarry && !item.AutoUse )
			{
				_hoverItem             = item;
				_hoverItemIsStationary = true;
				_hoverHint             = item.ItemName;
				return;
			}
		}

		// SafeRoomDoor
		var door = go.Components.GetInAncestorsOrSelf<SafeRoomDoor>();
		if ( door != null )
		{
			_hoverDoor = door;
			_hoverHint = door.GetInteractHint();
			return;
		}

		// CraneEventButton
		var btn = go.Components.GetInAncestorsOrSelf<CraneEventButton>();
		if ( btn != null && btn.CanInteract() )
		{
			_hoverButton = btn;
			_hoverHint   = btn.GetInteractHint();
		}
	}

	/// <summary>Acts on the cached interact target. Returns true if something was activated.</summary>
	private bool ActOnInteractTarget()
	{
		if ( _hoverWeapon != null )
		{
			EquipWeaponPickup( _hoverWeapon );
			return true;
		}

		if ( _hoverItem != null )
		{
			if ( _hoverItemIsStationary )
			{
				_hoverItem.OnUse( GameObject );
			}
			else
			{
				AssignItemToSlot( _hoverItem );
				_hoverItem.GameObject.Enabled = false;

				// Cache overlay model data
				if ( _hoverItem.SlotType == ItemSlotType.MainHeal )
				{
					var kit = _hoverItem as HealthKit;
					_slotData[2].OverlayModel = kit?.ViewModelOverlayModel;
				}
				else if ( _hoverItem.SlotType == ItemSlotType.Utility )
				{
					var throwable = _hoverItem as ThrowableBase;
					_slotData[4].OverlayModel = throwable?.ViewModelOverlayModel;
				}

				if ( !string.IsNullOrEmpty( _hoverItem.PickupSound ) )
					Sound.Play( _hoverItem.PickupSound, _hoverItem.WorldPosition );
				Log.Info( $"Picked up {_hoverItem.ItemName} → slot {_hoverItem.SlotType}" );
				SyncAllSlots();
			}
			return true;
		}

		if ( _hoverDoor != null ) { _hoverDoor.Toggle(); return true; }
		if ( _hoverButton != null ) { _hoverButton.TryUse(); return true; }
		return false;
	}

	public void EquipWeaponPickup( WeaponPickup pickup )
	{
		Log.Info( $"[WeaponManager] EquipWeaponPickup called: {pickup.WeaponDisplayName}" );
		if ( pickup.WeaponPrefab == null ) return;

		var pm = _movement;
		if ( pm?.Head == null ) return;

		// Spawn the new weapon as a child of the player.
		// Prefabs are saved with Enabled=false so they don't run in edit mode — enable explicitly.
		var newWeaponGO = SceneUtility.GetPrefabScene( pickup.WeaponPrefab ).Clone( Vector3.Zero );
		newWeaponGO.Enabled = true;
		newWeaponGO.Parent = GameObject;
		newWeaponGO.LocalPosition = Vector3.Zero;

		var gun = newWeaponGO.Components.GetInDescendantsOrSelf<Gun>();
		gun?.SetPlayerHead( pm.Head );
		gun?.SetOwner( GameObject );
		var melee = newWeaponGO.Components.GetInDescendantsOrSelf<MeleeWeapon>();
		melee?.SetPlayerHead( pm.Head );
		melee?.SetOwner( GameObject );

		// Drop old weapon slot or destroy it, assign new
		int slotIndex;
		if ( pickup.Category == WeaponCategory.Primary )
		{
			PrimarySlot?.Destroy();
			PrimarySlot = newWeaponGO;
			slotIndex = 0;
		}
		else
		{
			SecondarySlot?.Destroy();
			SecondarySlot = newWeaponGO;
			slotIndex = 1;
		}

		// Cache the slot data for viewmodel
		_slotData[slotIndex].WeaponModel = pickup.ViewModelModel;
		_slotData[slotIndex].AnimGraph = pickup.ViewModelAnimGraph;
		_slotData[slotIndex].HandsModel = pickup.ViewModelHandsModel;
		_slotData[slotIndex].PositionOffset = pickup.ViewModelPositionOffset;
		_slotData[slotIndex].AnimFireParam = pickup.AnimFireParam;
		_slotData[slotIndex].AnimReloadParam = pickup.AnimReloadParam;
		_slotData[slotIndex].AnimReloadEmptyParam = pickup.AnimReloadEmptyParam;

		// Update the single GunViewModel directly
		if ( ViewModel != null )
		{
			Log.Info( $"[WeaponManager] Calling UpdateWeapon in EquipWeaponPickup: model={pickup.ViewModelModel?.ResourcePath ?? "null"}, pos={pickup.ViewModelPositionOffset}" );
			ViewModel.UpdateWeapon(
				pickup.ViewModelModel,
				pickup.ViewModelAnimGraph,
				pickup.ViewModelHandsModel,
				pickup.ViewModelPositionOffset,
				pickup.AnimFireParam,
				pickup.AnimReloadParam,
				pickup.AnimReloadEmptyParam
			);
			// Notify ViewModelHandler of the new rest state to prevent stale cached values
			Log.Info( $"[WeaponManager] Calling ForceRestState in EquipWeaponPickup" );
			ViewModelHandler?.ForceRestState( pickup.ViewModelPositionOffset, Rotation.Identity );
		}
		else
		{
			Log.Warning( "[WeaponManager] ViewModel is null in EquipWeaponPickup, skipping UpdateWeapon" );
		}

		pickup.Pickup( GameObject );
		currentSlot = slotIndex;
		Log.Info( $"Equipped {pickup.WeaponDisplayName} in slot {slotIndex}" );
		SyncAllSlots();
	}


	private bool IsSlotOccupied( ItemSlotType slotType ) => slotType switch
	{
		ItemSlotType.MainHeal      => mainHealItem != null,
		ItemSlotType.SecondaryHeal => secondaryHealItem != null,
		ItemSlotType.Utility       => utilityItem != null,
		_                          => false,
	};

	private void AssignItemToSlot( BaseItem item )
	{
		switch ( item.SlotType )
		{
			case ItemSlotType.MainHeal:      mainHealItem = item; break;
			case ItemSlotType.SecondaryHeal: secondaryHealItem = item; break;
			case ItemSlotType.Utility:       utilityItem = item; break;
		}
	}

	private int lastSlotUpdated = -1;
	private string lastSlotContent = "";
	private int _onUpdateFrameCount = 0;

	/// <summary>Update single GunViewModel visibility and content based on given slot.</summary>
	private void UpdateViewModelVisibility( int slotToUpdate )
	{
		if ( ViewModel == null ) return;

		// Determine if current slot should show the viewmodel
		bool slotHasContent = slotToUpdate switch
		{
			0 => PrimarySlot != null,
			1 => SecondarySlot != null,
			2 => mainHealItem != null,
			3 => secondaryHealItem != null,
			4 => utilityItem != null,
			_ => false
		};

		ViewModel.ShowModel( slotHasContent );

		// Get a unique ID for the current slot's content (to detect weapon swaps within same slot)
		string currentContent = slotToUpdate switch
		{
			0 => PrimarySlot?.Name ?? "empty",
			1 => SecondarySlot?.Name ?? "empty",
			2 => mainHealItem?.ItemName ?? "empty",
			3 => secondaryHealItem?.ItemName ?? "empty",
			4 => utilityItem?.ItemName ?? "empty",
			_ => "empty"
		};

		// Only update viewmodel if slot changed OR content changed
		if ( slotToUpdate == lastSlotUpdated && currentContent == lastSlotContent )
			return;

		lastSlotUpdated = slotToUpdate;
		lastSlotContent = currentContent;

		if ( !slotHasContent )
		{
			if ( EnableDiagnostics )
				Log.Info( $"[WeaponManager] Slot {slotToUpdate} has no content, hiding VM" );
			return;
		}

		if ( EnableDiagnostics )
			Log.Info( $"[WeaponManager] UpdateViewModelVisibility: slot={slotToUpdate}" );

		// Apply the cached slot data for the current slot
		var slotData = _slotData[slotToUpdate];

		// Update all slots (both weapon and item)
		if ( slotToUpdate < 2 )
		{
			// Weapon slots — update with cached weapon model
			if ( slotData.WeaponModel != null )
			{
				Vector3 positionOffset = slotData.PositionOffset != Vector3.Zero ? slotData.PositionOffset : new Vector3( 25, 6, -10 );
				if ( EnableDiagnostics )
					Log.Info( $"[WeaponManager] Weapon slot {slotToUpdate}: model={slotData.WeaponModel?.ResourcePath ?? "null"}" );
				ViewModel.UpdateWeapon( slotData.WeaponModel, slotData.AnimGraph, slotData.HandsModel, positionOffset );
				ViewModelHandler?.ForceRestState( positionOffset, Rotation.Identity );
				ViewModel.UpdateOverlayModel( null );
			}
		}
		else
		{
			// Item slot — show arms + overlay model only if there's an item
			Model armsModel = slotData.WeaponModel ?? Model.Load( "models/first_person/v_first_person_arms_human.vmdl" );
			Vector3 positionOffset = slotData.PositionOffset != Vector3.Zero ? slotData.PositionOffset : new Vector3( 25, 6, -10 );
			if ( EnableDiagnostics )
				Log.Info( $"[WeaponManager] Item slot {slotToUpdate}: overlay={slotData.OverlayModel?.ResourcePath ?? "null"}" );
			ViewModel.UpdateWeapon( armsModel, slotData.AnimGraph, slotData.HandsModel, positionOffset );
			ViewModelHandler?.ForceRestState( positionOffset, Rotation.Identity );
			ViewModel.UpdateOverlayModel( slotData.OverlayModel );
		}
	}

	private void SyncAllSlots()
	{
		PlayerStats.WeaponSlotCount = 2;
		PlayerStats.AllSlotNames.Clear();
		PlayerStats.AllSlotNames.Add( GetWeaponName( PrimarySlot ) ?? "Primary" );
		PlayerStats.AllSlotNames.Add( GetWeaponName( SecondarySlot ) ?? "Secondary" );
		PlayerStats.AllSlotNames.Add( mainHealItem?.ItemName ?? "Main Heal" );
		PlayerStats.AllSlotNames.Add( secondaryHealItem?.ItemName ?? "Sub Heal" );
		PlayerStats.AllSlotNames.Add( utilityItem?.ItemName ?? "Utility" );
		PlayerStats.ActiveSlotIndex = currentSlot;

		// Legacy fields
		PlayerStats.CarriedItems.Clear();
		if ( mainHealItem != null ) PlayerStats.CarriedItems.Add( mainHealItem.ItemName );
		if ( secondaryHealItem != null ) PlayerStats.CarriedItems.Add( secondaryHealItem.ItemName );
		if ( utilityItem != null ) PlayerStats.CarriedItems.Add( utilityItem.ItemName );
		PlayerStats.CarriedItem = mainHealItem?.ItemName ?? secondaryHealItem?.ItemName ?? utilityItem?.ItemName ?? string.Empty;

		// Mirror to PlayerIdentity so the HUD reads the correct values (PI is preferred over PlayerStats)
		var id = Identity;
		if ( id != null )
		{
			id.WeaponSlotCount = 2;
			id.ActiveSlotIndex = currentSlot;
			id.AllSlotNames.Clear();
			id.AllSlotNames.AddRange( PlayerStats.AllSlotNames );
			id.CarriedItems.Clear();
			if ( mainHealItem != null ) id.CarriedItems.Add( mainHealItem.ItemName );
			if ( secondaryHealItem != null ) id.CarriedItems.Add( secondaryHealItem.ItemName );
			if ( utilityItem != null ) id.CarriedItems.Add( utilityItem.ItemName );
			id.CarriedItem = PlayerStats.CarriedItem;
		}
	}

	private string GetWeaponName( GameObject slot )
	{
		if ( slot == null ) return null;
		var gun = slot.Components.GetInDescendantsOrSelf<Gun>();
		var melee = slot.Components.GetInDescendantsOrSelf<MeleeWeapon>();
		if ( gun != null ) return gun.GetType().Name;
		if ( melee != null ) return melee.GetType().Name;
		return null;
	}


	public void RestoreAmmoForAllGuns()
	{
		PrimarySlot?.Components.GetInDescendantsOrSelf<Gun>()?.RefillReserve();
		SecondarySlot?.Components.GetInDescendantsOrSelf<Gun>()?.RefillReserve();
	}

	private void UpdatePickupHint()
	{
		PlayerStats.NearbyPickupHint = _hoverHint;
		var id = Identity;
		if ( id != null ) id.NearbyPickupHint = _hoverHint;
	}

	private void UpdateHUD()
	{
		var gun = CurrentGun;
		var melee = CurrentMelee;

		if ( gun != null )
		{
			PlayerStats.CurrentAmmo = gun.GetCurrentAmmo();
			PlayerStats.ReserveAmmo = gun.GetReserveAmmo();
			PlayerStats.WeaponName = gun.GetType().Name;
		}
		else if ( melee != null )
		{
			PlayerStats.CurrentAmmo = 0;
			PlayerStats.ReserveAmmo = 0;
			PlayerStats.WeaponName = melee.GetType().Name;
		}
		else
		{
			PlayerStats.CurrentAmmo = 0;
			PlayerStats.ReserveAmmo = 0;
			PlayerStats.WeaponName = ActiveItem?.ItemName ?? "";
		}

		var id = Identity;
		if ( id != null )
		{
			id.CurrentAmmo = PlayerStats.CurrentAmmo;
			id.ReserveAmmo = PlayerStats.ReserveAmmo;
			id.WeaponName  = PlayerStats.WeaponName;
		}
	}

	public void BroadcastRemoteMuzzleFlashFromWeaponManager( Vector3 position, Rotation rotation )
	{
		// Broadcast muzzle flash visuals to other players
		// For now, just a placeholder for multiplayer muzzle flash effect
	}

	public string GetActiveHeldModelPathForPresentation()
	{
		if ( currentSlot == 0 && PrimarySlot != null )
			return GetWeaponHeldModelPath( 0, PrimarySlot, primaryEquippedPickup );
		else if ( currentSlot == 1 && SecondarySlot != null )
			return GetWeaponHeldModelPath( 1, SecondarySlot, secondaryEquippedPickup );
		else if ( currentSlot == 2 && mainHealItem != null )
			return GetItemHeldModelPath( mainHealItem );
		else if ( currentSlot == 3 && secondaryHealItem != null )
			return GetItemHeldModelPath( secondaryHealItem );
		else if ( currentSlot == 4 && utilityItem != null )
			return GetItemHeldModelPath( utilityItem );
		return string.Empty;
	}

	private string GetWeaponHeldModelPath( int slotIndex, GameObject slot, WeaponPickup equippedPickup )
	{
		// World model is what remote players should see in third-person — prefer it over the 1P viewmodel
		if ( equippedPickup?.WorldModel != null )
			return equippedPickup.WorldModel.ResourcePath ?? string.Empty;
		return string.Empty;
	}

	private string GetItemHeldModelPath( BaseItem item )
	{
		// For heal items, use their overlay model if available
		if ( item is HealthKit kit ) return kit.ViewModelOverlayModel?.ResourcePath ?? string.Empty;
		if ( item is ThrowableBase throwable ) return throwable.ViewModelOverlayModel?.ResourcePath ?? string.Empty;
		return string.Empty;
	}
}
