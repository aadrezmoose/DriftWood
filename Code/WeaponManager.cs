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
	private bool _pendingPrimaryViewModelUpdate;
	private bool _pendingSecondaryViewModelUpdate;
	private bool _pendingHealViewModelUpdate;
	private bool _pendingUtilityViewModelUpdate;

	// Item slots — filled at runtime by pickup
	private BaseItem mainHealItem;      // slot 2
	private BaseItem secondaryHealItem; // slot 3
	private BaseItem utilityItem;       // slot 4

	private int currentSlot = 0;
	private const int TotalSlots = 5;

	private PlayerIdentity _identity;
	private PlayerIdentity Identity => _identity ??= Components.GetInDescendantsOrSelf<PlayerIdentity>();

	private bool IsLocallyControlled() => PlayerIdentity.IsOwnedByLocal( GameObject );

	// GunViewModels self-register here in their OnStart() via RegisterViewModel().
	private readonly List<GunViewModel> _registeredViewModels = new();

	public void RegisterViewModel( GunViewModel vm )
	{
		if ( vm != null && !_registeredViewModels.Contains( vm ) )
		{
			_registeredViewModels.Add( vm );
			vm.SetLocalPlayerFlag( IsLocallyControlled() );
			if ( EnableDiagnostics )
				Log.Info( $"[WeaponManager] Registered VM slot={vm.WeaponSlotIndex}" );
		}
	}

	public void UnregisterViewModel( GunViewModel vm )
	{
		if ( vm != null ) _registeredViewModels.Remove( vm );
	}

	private float shoveCooldown = 0f;
	[Property] public float ShoveCooldown { get; set; } = 1.2f;
	[Property] public float ShoveRange { get; set; } = 120f;
	[Property] public float ShoveRadius { get; set; } = 55f;

	private GameObject ActiveWeaponObject =>
		currentSlot == 0 ? PrimarySlot : currentSlot == 1 ? SecondarySlot : null;

	public Gun CurrentGun => ActiveWeaponObject?.Components.Get<Gun>();
	private MeleeWeapon CurrentMelee => ActiveWeaponObject?.Components.Get<MeleeWeapon>();
	private bool IsWeaponSlot => currentSlot < 2;
	private BaseItem ActiveItem => currentSlot == 2 ? mainHealItem
		: currentSlot == 3 ? secondaryHealItem
		: currentSlot == 4 ? utilityItem : null;

	protected override void OnAwake()
	{
		var playerMovement = Components.Get<PlayerMovement>();
		if ( playerMovement?.Head != null )
		{
			var head = playerMovement.Head;
			PrimarySlot?.Components.Get<Gun>()?.SetPlayerHead( head );
			PrimarySlot?.Components.Get<Gun>()?.SetOwner( GameObject );
			SecondarySlot?.Components.Get<Gun>()?.SetPlayerHead( head );
			SecondarySlot?.Components.Get<Gun>()?.SetOwner( GameObject );
			SecondarySlot?.Components.Get<MeleeWeapon>()?.SetPlayerHead( head );
			SecondarySlot?.Components.Get<MeleeWeapon>()?.SetOwner( GameObject );
		}

		SyncAllSlots();
	}

	protected override void OnUpdate()
	{
		if ( shoveCooldown > 0f ) shoveCooldown -= Time.Delta;

		// Retry pending VM updates if they failed last frame (e.g., OnStart not finished)
		if ( _pendingHealViewModelUpdate ) UpdateHealViewModel();
		if ( _pendingUtilityViewModelUpdate ) UpdateUtilityViewModel();

		SyncAllSlots();

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

		HandleSlotSwitching();
		HandleInput();
		UpdateHUD();
		UpdatePickupHint();
	}

	private void OnNetWeaponStateChanged()
	{
		if ( _suppressNetWeaponStateChange ) return;

		// Only update heal/utility overlays if their paths actually changed
		if ( NetMainHealOverlayModel != _lastAppliedMainHealOverlayPath )
		{
			_lastAppliedMainHealOverlayPath = NetMainHealOverlayModel;
			_pendingHealViewModelUpdate = true;
		}
		if ( NetUtilityOverlayModel != _lastAppliedUtilityOverlayPath )
		{
			_lastAppliedUtilityOverlayPath = NetUtilityOverlayModel;
			_pendingUtilityViewModelUpdate = true;
		}
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
			if ( !TryUseNearbyStationary() && !TryPickupNearbyWeapon() && !TryPickupNearbyItem() )
			{
				if ( !IsWeaponSlot ) UseCurrentItem();
			}
		}
	}

	private void TryShove()
	{
		if ( shoveCooldown > 0f ) return;

		var movement = Components.Get<PlayerMovement>();
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

	private const float PickupRadius = 130f;

	private bool TryPickupNearbyWeapon()
	{
		WeaponPickup closest = null;
		float closestDist = float.MaxValue;

		foreach ( var pickup in Scene.GetAllComponents<WeaponPickup>() )
		{
			if ( !pickup.GameObject.Enabled ) continue;
			float dist = pickup.WorldPosition.Distance( WorldPosition );
			if ( dist < pickup.PickupRadius && dist < closestDist )
			{
				closestDist = dist;
				closest = pickup;
			}
		}

		if ( closest != null )
		{
			EquipWeaponPickup( closest );
			return true;
		}
		return false;
	}

	public void EquipWeaponPickup( WeaponPickup pickup )
	{
		if ( pickup.WeaponPrefab == null ) return;

		var pm = Components.Get<PlayerMovement>();
		if ( pm?.Head == null ) return;

		// Spawn the new weapon as a child of the player
		var newWeaponGO = SceneUtility.GetPrefabScene( pickup.WeaponPrefab ).Clone( Vector3.Zero );
		newWeaponGO.Parent = GameObject;
		newWeaponGO.LocalPosition = Vector3.Zero;

		var gun = newWeaponGO.Components.Get<Gun>();
		gun?.SetPlayerHead( pm.Head );
		gun?.SetOwner( GameObject );
		var melee = newWeaponGO.Components.Get<MeleeWeapon>();
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

		// Update the matching GunViewModel so the FPS arms show the correct model
		foreach ( var vm in Scene.GetAllComponents<GunViewModel>() )
		{
			if ( vm.WeaponSlotIndex == slotIndex )
			{
				vm.UpdateWeapon(
					pickup.ViewModelModel,
					pickup.ViewModelAnimGraph,
					pickup.ViewModelHandsModel,
					pickup.ViewModelPositionOffset
				);
				break;
			}
		}

		pickup.Pickup( GameObject );
		currentSlot = slotIndex;
		Log.Info( $"Equipped {pickup.WeaponDisplayName} in slot {slotIndex}" );
		SyncAllSlots();
	}

	private bool TryPickupNearbyItem()
	{
		BaseItem closest = null;
		float closestDist = PickupRadius;

		foreach ( var item in Scene.GetAllComponents<BaseItem>() )
		{
			if ( !item.CanCarry || !item.GameObject.Enabled ) continue;
			if ( IsSlotOccupied( item.SlotType ) ) continue;
			float dist = item.WorldPosition.Distance( GameObject.WorldPosition );
			if ( dist < closestDist ) { closestDist = dist; closest = item; }
		}

		if ( closest != null )
		{
			AssignItemToSlot( closest );
			closest.GameObject.Enabled = false;
			if ( !string.IsNullOrEmpty( closest.PickupSound ) )
				Sound.Play( closest.PickupSound, closest.WorldPosition );
			Log.Info( $"Picked up {closest.ItemName} → slot {closest.SlotType}" );
			SyncAllSlots();
			return true;
		}

		return false;
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

	private GunViewModel GetViewModelForSlot( int slotIndex )
	{
		foreach ( var vm in _registeredViewModels )
			if ( vm.WeaponSlotIndex == slotIndex )
				return vm;
		return null;
	}

	private GunViewModel EnsureRuntimeViewModelForSlot( int slotIndex, Model armsModel, Model overlayModel, Vector3? offset )
	{
		var existingVM = GetViewModelForSlot( slotIndex );
		if ( existingVM != null ) return existingVM;

		// Create new runtime VM
		var vmGO = new GameObject( true, $"VM_Slot{slotIndex}" );
		vmGO.Parent = GameObject;
		var vm = vmGO.AddComponent<GunViewModel>();
		vm.WeaponSlotIndex = slotIndex;
		vm.WeaponModel = armsModel ?? Model.Load( "models/first_person/v_first_person_arms_human.vmdl" );
		if ( offset.HasValue ) vm.PositionOffset = offset.Value;
		vm.SetLocalPlayerFlag( IsLocallyControlled() );
		return vm;
	}

	private void UpdateHealViewModel()
	{
		var vm = GetViewModelForSlot( 2 );
		if ( vm == null )
		{
			// Create runtime VM with arms model for health kits
			var armsModel = Model.Load( "models/first_person/v_first_person_arms_human.vmdl" );
			vm = EnsureRuntimeViewModelForSlot( 2, armsModel, null, null );
			if ( vm == null ) { _pendingHealViewModelUpdate = true; return; }
		}
		var kit = mainHealItem as HealthKit;
		var model = kit?.ViewModelOverlayModel;
		vm.UpdateOverlayModel( model );
		_pendingHealViewModelUpdate = false;
	}

	private void UpdateUtilityViewModel()
	{
		var vm = GetViewModelForSlot( 4 );
		if ( vm == null )
		{
			// Create runtime VM with arms model for throwables
			var armsModel = Model.Load( "models/first_person/v_first_person_arms_human.vmdl" );
			vm = EnsureRuntimeViewModelForSlot( 4, armsModel, null, null );
			if ( vm == null ) { _pendingUtilityViewModelUpdate = true; return; }
		}
		var throwable = utilityItem as ThrowableBase;
		var model = throwable?.ViewModelOverlayModel;
		vm.UpdateOverlayModel( model );
		_pendingUtilityViewModelUpdate = false;
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
	}

	private string GetWeaponName( GameObject slot )
	{
		if ( slot == null ) return null;
		var gun = slot.Components.Get<Gun>();
		var melee = slot.Components.Get<MeleeWeapon>();
		if ( gun != null ) return gun.GetType().Name;
		if ( melee != null ) return melee.GetType().Name;
		return null;
	}

	private bool TryUseNearbyStationary()
	{
		foreach ( var item in Scene.GetAllComponents<BaseItem>() )
		{
			if ( item.CanCarry || item.AutoUse || !item.GameObject.Enabled ) continue;
			if ( item.WorldPosition.Distance( WorldPosition ) <= PickupRadius )
			{
				item.OnUse( GameObject );
				return true;
			}
		}
		return false;
	}

	public void RestoreAmmoForAllGuns()
	{
		PrimarySlot?.Components.Get<Gun>()?.RefillReserve();
		SecondarySlot?.Components.Get<Gun>()?.RefillReserve();
	}

	private void UpdatePickupHint()
	{
		string hint = string.Empty;

		// Check nearby weapon pickups
		foreach ( var pickup in Scene.GetAllComponents<WeaponPickup>() )
		{
			if ( !pickup.GameObject.Enabled ) continue;
			if ( pickup.WorldPosition.Distance( WorldPosition ) <= pickup.PickupRadius )
			{
				hint = pickup.WeaponDisplayName;
				break;
			}
		}

		if ( string.IsNullOrEmpty( hint ) )
		{
			foreach ( var item in Scene.GetAllComponents<BaseItem>() )
			{
				if ( !item.GameObject.Enabled ) continue;
				if ( item.WorldPosition.Distance( WorldPosition ) > PickupRadius ) continue;
				hint = item.ItemName;
				break;
			}
		}

		PlayerStats.NearbyPickupHint = hint;
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
	}
}
