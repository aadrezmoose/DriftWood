using Sandbox;

/// <summary>
/// Mark a GameObject (like enemy head) as a headshot zone with damage multiplier.
/// Attach to a child object with a collider to create a headshot hitbox.
/// </summary>
public sealed class HeadshotZone : Component
{
	[Property] public float DamageMultiplier { get; set; } = 2.0f;
	[Property] public GameObject TargetEntity { get; set; }

	/// <summary>
	/// Get the entity that should take damage (usually the parent enemy).
	/// </summary>
	public GameObject GetTargetEntity()
	{
		// If explicit target set, use it
		if ( TargetEntity is not null )
			return TargetEntity;

		// Otherwise find HealthComponent in parent hierarchy
		var health = Components.GetInAncestors<HealthComponent>();
		return health?.GameObject;
	}
}
