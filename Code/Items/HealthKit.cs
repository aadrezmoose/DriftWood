using Sandbox;

/// <summary>
/// Health Kit item that heals the player to full HP when used.
/// Can be carried by the player and used later.
/// </summary>
public sealed class HealthKit : BaseItem
{
	[Property] public float HealAmount { get; set; } = 100f;

	public HealthKit()
	{
		// Health kits can be carried, not auto-used
		ItemName = "Health Kit";
		CanCarry = true;
		AutoUse = false;
		PickupSound = "sounds/coin1.sound";
	}

	/// <summary>
	/// Called when player walks over the health kit.
	/// Adds it to their inventory if they have space.
	/// </summary>
	public override void OnPickup(GameObject player)
	{
		if (player == null)
		{
			Log.Warning("HealthKit.OnPickup: player is null");
			return;
		}

		Log.Info($"HealthKit.OnPickup called for {player.Name}");

		// Find the player's inventory component
		var inventory = player.Components.GetInDescendantsOrSelf<Inventory>();
		if (inventory == null)
		{
			// Try to find inventory on root
			inventory = player.Root.Components.GetInDescendantsOrSelf<Inventory>();
		}

		if (inventory != null)
		{
			// Try to add the item to inventory
			bool added = inventory.TryAddItem(this);
			if (added)
			{
				Log.Info($"Health Kit added to inventory");

				// Update PlayerStats for UI
				PlayerStats.CarriedItem = ItemName;

				// Disable the GameObject so it's no longer visible/collidable
				GameObject.Enabled = false;
			}
			else
			{
				Log.Warning("Inventory is full, cannot pick up Health Kit");
			}
		}
		else
		{
			Log.Warning("HealthKit.OnPickup: Player has no Inventory component, using item immediately");
			// If no inventory, just use it immediately
			OnUse(player);
		}
	}

	/// <summary>
	/// Called when player uses the health kit from inventory or picks it up without inventory.
	/// Heals the player to full HP and destroys the item.
	/// </summary>
	public override void OnUse(GameObject player)
	{
		if (player == null)
		{
			Log.Warning("HealthKit.OnUse: player is null");
			return;
		}

		Log.Info($"HealthKit.OnUse called for {player.Name}");

		// Find the player's health component
		var health = player.Components.GetInDescendantsOrSelf<HealthComponent>();
		if (health == null)
		{
			// Try to find on root
			health = player.Root.Components.GetInDescendantsOrSelf<HealthComponent>();
		}

		if (health != null)
		{
			// If downed, revive instead of heal
			var incap = player.Components.GetInDescendantsOrSelf<IncapacitationComponent>();
			if ( incap != null && incap.IsIncapacitated )
			{
				incap.Revive();
				Sound.Play( "sounds/coin1.sound", player.WorldPosition );
				PlayerStats.CarriedItem = "";
				WasConsumed = true;
				GameObject.Destroy();
				return;
			}

			if (health.CurrentHealth >= health.MaxHealth)
			{
				Log.Info("Player already at full health, Health Kit not used");
				return;
			}

			health.Heal(health.MaxHealth * 0.8f);
			Sound.Play("sounds/coin1.sound", player.WorldPosition);
			PlayerStats.CarriedItem = "";
			WasConsumed = true;
			GameObject.Destroy();
		}
		else
		{
			Log.Warning("HealthKit.OnUse: Could not find HealthComponent on player");
		}
	}
}
