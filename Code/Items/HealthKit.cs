using Sandbox;

/// <summary>
/// Health Kit item that heals the player to full HP when used.
/// Can be carried by the player and used later.
/// </summary>
public sealed class HealthKit : BaseItem
{
	[Property] public float HealAmount { get; set; } = 100f;
	[Property] public float UseDuration { get; set; } = 3.5f;

	/// <summary>Model shown in the first-person viewmodel when this kit is held.</summary>
	[Property] public Model ViewModelOverlayModel { get; set; }

	public HealthKit()
	{
		// Health kits can be carried, not auto-used
		ItemName = "Health Kit";
		PickupSound = "sounds/coin1.sound";
	}

	protected override void OnAwake()
	{
		CanCarry = true;
		AutoUse = false;
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
		TryApplyToTarget( player, player );
	}

	public bool CanApplyToTarget( GameObject target )
	{
		if ( target == null ) return false;

		var incap = target.Components.GetInDescendantsOrSelf<IncapacitationComponent>()
			?? target.Root.Components.GetInDescendantsOrSelf<IncapacitationComponent>();
		if ( incap != null && incap.IsIncapacitated )
			return true;

		var health = target.Components.GetInDescendantsOrSelf<HealthComponent>()
			?? target.Root.Components.GetInDescendantsOrSelf<HealthComponent>();
		return health != null && health.CurrentHealth < health.MaxHealth;
	}

	public bool TryApplyToTarget( GameObject user, GameObject target )
	{
		if ( target == null )
		{
			Log.Warning( "HealthKit.TryApplyToTarget: target is null" );
			return false;
		}

		if ( user == null )
			user = target;

		Log.Info( $"HealthKit.TryApplyToTarget user={user.Name} target={target.Name}" );

		var health = target.Components.GetInDescendantsOrSelf<HealthComponent>();
		if (health == null)
		{
			health = target.Root.Components.GetInDescendantsOrSelf<HealthComponent>();
		}

		if (health != null)
		{
			var incap = target.Components.GetInDescendantsOrSelf<IncapacitationComponent>()
				?? target.Root.Components.GetInDescendantsOrSelf<IncapacitationComponent>();
			if ( incap != null && incap.IsIncapacitated )
			{
				if ( target == user ) incap.Revive();
				else incap.RequestReviveFromTeammate();

				Sound.Play( "sounds/coin1.sound", user.WorldPosition );
				PlayerStats.CarriedItem = "";
				WasConsumed = true;
				GameObject.Destroy();
				return true;
			}

			if (health.CurrentHealth >= health.MaxHealth)
			{
				Log.Info("Player already at full health, Health Kit not used");
				return false;
			}

			var healAmount = (health.MaxHealth - health.CurrentHealth) * 0.8f;
			if ( target == user ) health.Heal( healAmount );
			else health.RequestHealFromTeammate( healAmount );

			Sound.Play("sounds/coin1.sound", user.WorldPosition);
			PlayerStats.CarriedItem = "";
			WasConsumed = true;
			GameObject.Destroy();
			return true;
		}

		Log.Warning("HealthKit.TryApplyToTarget: Could not find HealthComponent on target");
		return false;
	}
}
