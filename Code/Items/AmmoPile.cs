using Sandbox;

/// <summary>
/// Ammo Pile item that restores ammunition reserves for all weapons.
/// Auto-used when player walks over it, cannot be carried.
/// </summary>
public sealed class AmmoPile : BaseItem
{
	[Property] public int AmmoRestoreAmount { get; set; } = 999; // Restores to max by default
	[Property] public bool RestoreToMax { get; set; } = true;

	public AmmoPile()
	{
		ItemName = "Ammo Pile";
		PickupSound = "sounds/coin1.sound";
	}

	protected override void OnAwake()
	{
		// Force these after inspector deserialization so they can't be accidentally overridden
		CanCarry = false;
		AutoUse = false;
	}

	/// <summary>
	/// Not used for ammo piles since they are auto-used.
	/// </summary>
	public override void OnPickup(GameObject player)
	{
		Log.Warning("AmmoPile.OnPickup: Ammo piles should be auto-used, not picked up");
		// Fallback to using the item
		OnUse(player);
	}

	/// <summary>
	/// Called when player walks over the ammo pile.
	/// Restores ammunition reserves for all carried weapons.
	/// </summary>
	public override void OnUse(GameObject player)
	{
		if ( player == null ) return;

		var weaponManager = player.Components.GetInAncestorsOrSelf<WeaponManager>()
			?? player.Components.GetInDescendantsOrSelf<WeaponManager>();
		if ( weaponManager == null ) return;

		weaponManager.RestoreAmmoForAllGuns();
		Sound.Play( PickupSound, WorldPosition );
	}
}
