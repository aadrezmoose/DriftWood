using Sandbox;

/// <summary>
/// A throwable Molotov cocktail.
/// On use, launches a MolotovProjectile that shatters on impact and spawns a FireZone.
/// Occupies the Utility item slot (slot 4).
/// </summary>
public sealed class Molotov : ThrowableBase
{
	public Molotov()
	{
		ItemName = "Molotov";
		PickupSound = "sounds/coin1.sound";
	}

	/// <summary>
	/// Clones the projectile prefab, wires up ownership, and gives it an arcing velocity.
	/// </summary>
	protected override void SpawnProjectile( Vector3 position, Vector3 direction )
	{
		if ( ProjectilePrefab == null )
		{
			Log.Warning( "Molotov.SpawnProjectile: ProjectilePrefab not assigned" );
			return;
		}

		var prefabScene = SceneUtility.GetPrefabScene( ProjectilePrefab );
		if ( prefabScene == null )
		{
			Log.Warning( "Molotov.SpawnProjectile: Could not load prefab scene" );
			return;
		}

		var projectileGO = prefabScene.Clone( position );
		projectileGO.WorldPosition = position;

		// Wire up owner so the projectile ignores the thrower on its first trace
		var proj = projectileGO.Components.GetInDescendantsOrSelf<MolotovProjectile>();
		if ( proj != null )
		{
			proj.Owner = GameObject;
		}
		else
		{
			Log.Warning( "Molotov.SpawnProjectile: MolotovProjectile component not found on prefab" );
		}

		// Apply throw velocity — flatten horizontal so looking up doesn't send it to space
		var rb = projectileGO.Components.GetInDescendantsOrSelf<Rigidbody>();
		if ( rb != null )
		{
			Vector3 flatDir = direction.WithZ( 0 ).Normal;
			rb.Velocity = flatDir * ThrowSpeed + Vector3.Up * ArcForce;
		}
		else
		{
			Log.Warning( "Molotov.SpawnProjectile: No Rigidbody found on projectile prefab" );
		}

		Log.Info( $"Molotov thrown from {position} in direction {direction}" );
	}
}
