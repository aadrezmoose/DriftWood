using Sandbox;

/// <summary>
/// Pain Pills item that provides temporary health boost to the player.
/// Can be carried and used later. Temporary health optionally decays over time.
/// </summary>
public sealed class PainPills : BaseItem
{
	[Property] public float TempHealthAmount { get; set; } = 50f;
	[Property] public bool DecayOverTime { get; set; } = true;
	[Property] public float DecayRate { get; set; } = 5f; // HP lost per second

	public PainPills()
	{
		// Pills can be carried, not auto-used
		ItemName = "Pain Pills";
		CanCarry = true;
		AutoUse = false;
		PickupSound = "sounds/coin1.sound";
		SlotType = ItemSlotType.SecondaryHeal;
	}

	/// <summary>
	/// Called when player walks over the pills.
	/// Adds them to inventory if there's space.
	/// </summary>
	public override void OnPickup(GameObject player)
	{
		if (player == null)
		{
			Log.Warning("PainPills.OnPickup: player is null");
			return;
		}

		Log.Info($"PainPills.OnPickup called for {player.Name}");

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
				Log.Info($"Pain Pills added to inventory");

				// Update PlayerStats for UI
				PlayerStats.CarriedItem = ItemName;

				// Disable the GameObject so it's no longer visible/collidable
				GameObject.Enabled = false;
			}
			else
			{
				Log.Warning("Inventory is full, cannot pick up Pain Pills");
			}
		}
		else
		{
			Log.Warning("PainPills.OnPickup: Player has no Inventory component, using item immediately");
			// If no inventory, just use it immediately
			OnUse(player);
		}
	}

	/// <summary>
	/// Called when player uses the pills from inventory.
	/// Adds temporary health to the player and optionally starts decay.
	/// </summary>
	public override void OnUse(GameObject player)
	{
		if (player == null)
		{
			Log.Warning("PainPills.OnUse: player is null");
			return;
		}

		Log.Info($"PainPills.OnUse called for {player.Name}");

		// Find the player's health component
		var health = player.Components.GetInDescendantsOrSelf<HealthComponent>();
		if (health == null)
		{
			// Try to find on root
			health = player.Root.Components.GetInDescendantsOrSelf<HealthComponent>();
		}

		if (health != null)
		{
			Log.Info($"Adding {TempHealthAmount} temporary HP to player (Current: {health.CurrentHealth}/{health.MaxHealth})");

			// Add temporary health - this allows player to exceed max health temporarily
			// Note: In L4D, temp health appears as a different color on the health bar
			// For now, we'll just add it as regular health with a cap at MaxHealth * 1.5
			float maxTempHealth = health.MaxHealth * 1.5f;
			float newHealth = health.CurrentHealth + TempHealthAmount;

			if (newHealth > maxTempHealth)
			{
				newHealth = maxTempHealth;
			}

			// Directly set the health value (bypassing the Heal method's MaxHealth cap)
			float healAmount = newHealth - health.CurrentHealth;
			health.Heal(healAmount);

			// Play pills sound
			Sound.Play("sounds/coin1.sound", player.WorldPosition);

			// Clear carried item from UI
			PlayerStats.CarriedItem = "";

			// If decay is enabled, start a decay component
			if (DecayOverTime)
			{
				var decayComponent = player.Components.GetOrCreate<TempHealthDecay>();
				if (decayComponent != null)
				{
					decayComponent.StartDecay(TempHealthAmount, DecayRate, health.MaxHealth);
				}
			}

			// Destroy the pills GameObject
			WasConsumed = true;
			GameObject.Destroy();
		}
		else
		{
			Log.Warning("PainPills.OnUse: Could not find HealthComponent on player");
		}
	}
}

/// <summary>
/// Component that handles temporary health decay over time.
/// Attached to player when they use pills.
/// </summary>
public sealed class TempHealthDecay : Component
{
	private float remainingTempHealth;
	private float decayRate;
	private float baseMaxHealth;
	private bool isDecaying;

	public void StartDecay(float tempHealthAmount, float decayPerSecond, float playerMaxHealth)
	{
		remainingTempHealth = tempHealthAmount;
		decayRate = decayPerSecond;
		baseMaxHealth = playerMaxHealth;
		isDecaying = true;

		Log.Info($"Started temp health decay: {tempHealthAmount} HP at {decayRate} HP/s");
	}

	protected override void OnUpdate()
	{
		if (!isDecaying || remainingTempHealth <= 0f)
		{
			// Stop decaying when temp health is depleted
			if (isDecaying && remainingTempHealth <= 0f)
			{
				isDecaying = false;
				Log.Info("Temp health fully decayed");
			}
			return;
		}

		// Find the player's health component
		var health = Components.GetInDescendantsOrSelf<HealthComponent>();
		if (health == null)
		{
			health = GameObject.Root.Components.GetInDescendantsOrSelf<HealthComponent>();
		}

		if (health != null)
		{
			// Only decay if health is above the base max health
			if (health.CurrentHealth > baseMaxHealth)
			{
				float decayAmount = decayRate * Time.Delta;
				remainingTempHealth -= decayAmount;

				// Apply decay damage
				health.TakeDamage(decayAmount);

				// Stop decay if we've reached base health or depleted temp health
				if (health.CurrentHealth <= baseMaxHealth || remainingTempHealth <= 0f)
				{
					isDecaying = false;
					Log.Info("Temp health decay complete");
				}
			}
			else
			{
				// Already at or below base health, stop decay
				isDecaying = false;
			}
		}
		else
		{
			// No health component, stop decay
			isDecaying = false;
		}
	}
}
