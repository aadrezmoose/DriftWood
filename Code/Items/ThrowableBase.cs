using Sandbox;

/// <summary>
/// Abstract base class for throwable items (Molotov, Pipe Bomb).
/// Occupies the Utility slot. Player aims with their head direction and throws on use.
/// </summary>
public abstract class ThrowableBase : BaseItem
{
	[Property] public float ThrowSpeed { get; set; } = 400f;
	[Property] public float ArcForce { get; set; } = 40f;
	[Property] public float ProjectileScale { get; set; } = 1f;

	/// <summary>Prefab cloned into the world when this throwable is used.</summary>
	[Property] public PrefabFile ProjectilePrefab { get; set; }

	/// <summary>Model shown in the first-person viewmodel when this throwable is held.</summary>
	[Property] public Model ViewModelOverlayModel { get; set; }

	public ThrowableBase()
	{
		CanCarry = true;
		AutoUse = false;
		SlotType = ItemSlotType.Utility;
	}

	/// <summary>
	/// Called when the player walks near the throwable. Adds it to the Utility slot.
	/// </summary>
	public override void OnPickup( GameObject player )
	{
		if ( player == null )
		{
			Log.Warning( $"{ItemName}.OnPickup: player is null" );
			return;
		}

		var inventory = player.Components.GetInDescendantsOrSelf<Inventory>();
		if ( inventory == null )
			inventory = player.Root.Components.GetInDescendantsOrSelf<Inventory>();

		if ( inventory != null )
		{
			bool added = inventory.TryAddItem( this );
			if ( added )
			{
				PlayerStats.CarriedItem = ItemName;
				GameObject.Enabled = false;
			}
			else
			{
				Log.Warning( $"Inventory is full, cannot pick up {ItemName}" );
			}
		}
		else
		{
			Log.Warning( $"{ItemName}.OnPickup: Player has no Inventory component" );
		}
	}

	/// <summary>
	/// Throws the projectile from the player's head position in the look direction.
	/// </summary>
	public override void OnUse( GameObject player )
	{
		if ( player == null )
		{
			Log.Warning( $"{ItemName}.OnUse: player is null" );
			return;
		}

		if ( ProjectilePrefab == null )
		{
			Log.Warning( $"{ItemName}.OnUse: ProjectilePrefab not assigned" );
			return;
		}

		var movement = player.Components.GetInDescendantsOrSelf<PlayerMovement>();
		var head = movement?.Head;

		Vector3 spawnPos;
		Vector3 throwDir;

		if ( head != null )
		{
			spawnPos = head.WorldPosition;
			throwDir = head.WorldRotation.Forward;
		}
		else
		{
			// Fallback: use player position + rough forward
			spawnPos = player.WorldPosition + Vector3.Up * 60f;
			throwDir = player.WorldRotation.Forward;
		}

		SpawnProjectile( spawnPos, throwDir );

		WasConsumed = true;
		GameObject.Destroy();
	}

	/// <summary>
	/// Implemented by subclasses to clone the correct prefab and configure the projectile.
	/// </summary>
	protected abstract void SpawnProjectile( Vector3 position, Vector3 direction );
}
