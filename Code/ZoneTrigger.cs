using Sandbox;

/// <summary>
/// Activates or deactivates an EnemySpawner when the player walks through a trigger volume.
/// Requires a BoxCollider with IsTrigger = true on this GameObject (configure in editor).
/// </summary>
public sealed class ZoneTrigger : Component, Component.ITriggerListener
{
	/// <summary>
	/// The EnemySpawner to enable or disable when the player enters this trigger.
	/// </summary>
	[Property] public EnemySpawner TargetSpawner { get; set; }

	/// <summary>
	/// If true, enables the spawner when triggered. If false, disables it.
	/// </summary>
	[Property] public bool EnableOnTrigger { get; set; } = true;

	/// <summary>
	/// If true, this trigger disables itself after firing so it only activates once.
	/// </summary>
	[Property] public bool OneShot { get; set; } = true;

	public void OnTriggerEnter( Collider other )
	{
		if ( TargetSpawner is null )
			return;

		// Only react to objects that have a PlayerMovement component
		var player = other.GameObject.Components.GetInAncestorsOrSelf<PlayerMovement>();
		if ( player is null )
			return;

		TargetSpawner.Enabled = EnableOnTrigger;

		if ( OneShot )
		{
			Enabled = false;
		}
	}

	public void OnTriggerExit( Collider other ) { }
}
