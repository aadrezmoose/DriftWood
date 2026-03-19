using Sandbox;

public enum ItemSlotType { MainHeal, SecondaryHeal, Utility }

/// <summary>
/// Abstract base class for all pickupable items in the game.
/// Implements Component.ITriggerListener to detect player collisions.
/// </summary>
public abstract class BaseItem : Component, Component.ITriggerListener
{
	[Property] public string ItemName { get; set; } = "Item";
	[Property] public bool CanCarry { get; set; } = false;
	[Property] public bool AutoUse { get; set; } = true;
	[Property] public string PickupSound { get; set; } = "sounds/coin1.sound";
	[Property] public ItemSlotType SlotType { get; set; } = ItemSlotType.MainHeal;

	/// <summary>Set to true in OnUse when the item is consumed, so WeaponManager removes it immediately.</summary>
	public bool WasConsumed { get; protected set; } = false;

	/// <summary>
	/// Called when the item is picked up by the player.
	/// </summary>
	/// <param name="player">The player GameObject that picked up the item</param>
	public abstract void OnPickup(GameObject player);

	/// <summary>
	/// Called when the item is used by the player.
	/// </summary>
	/// <param name="player">The player GameObject using the item</param>
	public abstract void OnUse(GameObject player);

	/// <summary>
	/// Triggered when something enters the item's trigger zone.
	/// </summary>
	public void OnTriggerEnter(Collider other)
	{
		if (other == null || other.GameObject == null)
			return;

		// Check if the colliding object is a player
		var player = other.GameObject.Components.Get<PlayerMovement>();
		if (player == null)
		{
			// Try to find PlayerMovement in ancestors or descendants
			player = other.GameObject.Components.GetInAncestorsOrSelf<PlayerMovement>();
			if (player == null)
			{
				player = other.GameObject.Components.GetInDescendantsOrSelf<PlayerMovement>();
			}
		}

		if (player != null)
		{
			Log.Info($"Player triggered {ItemName}");

			// Play pickup sound if configured
			if (!string.IsNullOrEmpty(PickupSound))
			{
				Sound.Play(PickupSound, WorldPosition);
			}

			if (AutoUse)
			{
				// Auto-use items (ammo piles etc.) trigger immediately on contact
				OnUse(other.GameObject);
			}
			// CanCarry items (health kits, pills) require the player to press E
		}
	}

	/// <summary>
	/// Optional: Triggered when something exits the item's trigger zone.
	/// </summary>
	public void OnTriggerExit(Collider other)
	{
		// Optional: Can be used for proximity indicators, etc.
	}

	/// <summary>
	/// Helper method to find the player's root GameObject.
	/// </summary>
	protected GameObject GetPlayerRoot(GameObject obj)
	{
		// Try to find the root player object
		var playerMovement = obj.Components.GetInAncestorsOrSelf<PlayerMovement>();
		if (playerMovement != null)
		{
			return playerMovement.GameObject;
		}

		return obj.Root;
	}
}
