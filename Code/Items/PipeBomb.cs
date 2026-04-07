using Sandbox;

/// <summary>
/// A throwable Pipe Bomb.
/// On use, launches a PipeBombProjectile that beeps for FuseDuration seconds,
/// then explodes dealing heavy AOE damage to nearby enemies.
/// Occupies the Utility item slot (slot 4).
/// </summary>
public sealed class PipeBomb : ThrowableBase
{
	public PipeBomb()
	{
		ItemName = "Pipe Bomb";
		PickupSound = "sounds/coin1.sound";
	}

	/// <summary>
	/// Clones the projectile prefab, wires up ownership, and gives it an arcing velocity.
	/// </summary>
	protected override void SpawnProjectile( Vector3 position, Vector3 direction )
	{
		if ( ProjectilePrefab == null )
		{
			Log.Warning( "PipeBomb.SpawnProjectile: ProjectilePrefab not assigned" );
			return;
		}

		var prefabScene = SceneUtility.GetPrefabScene( ProjectilePrefab );
		if ( prefabScene == null )
		{
			Log.Warning( "PipeBomb.SpawnProjectile: Could not load prefab scene" );
			return;
		}

		var projectileGO = prefabScene.Clone( position );
		projectileGO.WorldPosition = position;
		projectileGO.LocalScale = Vector3.One * ProjectileScale;

		// Wire up owner for damage attribution
		var proj = projectileGO.Components.GetInDescendantsOrSelf<PipeBombProjectile>();
		if ( proj != null )
		{
			proj.Owner = GameObject;
		}
		else
		{
			Log.Warning( "PipeBomb.SpawnProjectile: PipeBombProjectile component not found on prefab" );
		}

		// Apply throw velocity — same arc as Molotov
		var rb = projectileGO.Components.GetInDescendantsOrSelf<Rigidbody>();
		if ( rb != null )
		{
			rb.Velocity = direction * ThrowSpeed + Vector3.Up * ArcForce;
		}
		else
		{
			Log.Warning( "PipeBomb.SpawnProjectile: No Rigidbody found on projectile prefab" );
		}

		Log.Info( $"Pipe Bomb thrown from {position} in direction {direction}" );
	}
}
