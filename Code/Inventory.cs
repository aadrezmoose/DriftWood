using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Player inventory system for carrying items like health kits and pills.
/// Attached to the player GameObject.
/// </summary>
public sealed class Inventory : Component
{
	[Property] public int MaxInventorySlots { get; set; } = 2;

	private List<BaseItem> carriedItems = new List<BaseItem>();

	protected override void OnAwake()
	{
	}

	/// <summary>
	/// Attempts to add an item to the inventory.
	/// </summary>
	/// <param name="item">The item to add</param>
	/// <returns>True if item was added successfully, false if inventory is full</returns>
	public bool TryAddItem(BaseItem item)
	{
		if (item == null)
		{
			Log.Warning("Inventory.TryAddItem: item is null");
			return false;
		}

		if (carriedItems.Count >= MaxInventorySlots)
		{
			return false;
		}

		if (!item.CanCarry)
		{
			Log.Warning($"Item {item.ItemName} cannot be carried");
			return false;
		}

		carriedItems.Add(item);
		PlayerStats.SyncCarriedItems( carriedItems );
		return true;
	}

	/// <summary>
	/// Uses the item in the specified slot.
	/// </summary>
	/// <param name="slot">The inventory slot index (0-based)</param>
	public void UseItem(int slot)
	{
		if (slot < 0 || slot >= carriedItems.Count)
		{
			return;
		}

		var item = carriedItems[slot];
		if (item == null)
		{
			carriedItems.RemoveAt(slot);
			return;
		}

		// Call the item's OnUse method with the player GameObject
		item.OnUse(GameObject);

		// Remove item from inventory after use
		carriedItems.RemoveAt(slot);
		PlayerStats.SyncCarriedItems( carriedItems );
	}

	/// <summary>
	/// Gets the item in the specified slot without using it.
	/// </summary>
	/// <param name="slot">The inventory slot index</param>
	/// <returns>The item in that slot, or null if empty</returns>
	public BaseItem GetItemInSlot(int slot)
	{
		if (slot < 0 || slot >= carriedItems.Count)
		{
			return null;
		}

		return carriedItems[slot];
	}

	/// <summary>
	/// Removes an item from inventory without using it.
	/// </summary>
	/// <param name="item">The item to remove</param>
	/// <returns>True if item was found and removed</returns>
	public bool RemoveItem(BaseItem item)
	{
		if (item == null)
			return false;

		bool removed = carriedItems.Remove(item);
		if (removed)
			PlayerStats.SyncCarriedItems( carriedItems );

		return removed;
	}

	/// <summary>
	/// Clears all items from inventory.
	/// </summary>
	public void ClearInventory()
	{
		carriedItems.Clear();
		PlayerStats.SyncCarriedItems( carriedItems );
	}

	/// <summary>
	/// Gets the number of items currently in inventory.
	/// </summary>
	public int GetItemCount()
	{
		return carriedItems.Count;
	}

	/// <summary>
	/// Checks if inventory has space for more items.
	/// </summary>
	public bool HasSpace()
	{
		return carriedItems.Count < MaxInventorySlots;
	}

	/// <summary>
	/// Gets all items in inventory (read-only).
	/// </summary>
	public IReadOnlyList<BaseItem> GetAllItems()
	{
		return carriedItems.AsReadOnly();
	}
}
