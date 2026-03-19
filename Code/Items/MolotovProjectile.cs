using Sandbox;

/// <summary>
/// Physics projectile spawned by the Molotov throwable.
/// Travels as a Rigidbody. On impact (or after 5 seconds) it shatters and
/// spawns a FireZone at the landing position.
/// </summary>
public sealed class MolotovProjectile : Component
{
	[Property] public PrefabFile FireZonePrefab { get; set; }
	[Property] public SoundEvent ImpactSound { get; set; }

	/// <summary>Set by Molotov immediately after spawning so we don't hit the thrower.</summary>
	public GameObject Owner { get; set; }

	/// <summary>0.5 = half gravity, 1 = normal, 2 = double. Counteracts or boosts Rigidbody gravity.</summary>
	[Property] public float GravityMultiplier { get; set; } = 0.5f;

	private Rigidbody _rb;
	private bool _hasImpacted = false;
	private float _lifetime = 0f;
	private const float MaxLifetime = 5f;

	protected override void OnStart()
	{
		// Ensure a Rigidbody exists — Molotov sets velocity on this after spawning.
		_rb = Components.GetOrCreate<Rigidbody>();
	}

	protected override void OnFixedUpdate()
	{
		if ( _hasImpacted || _rb == null ) return;
		// Scale gravity: counteract scene gravity then re-apply at desired scale
		// GravityMultiplier < 1 = lighter, > 1 = heavier, 1 = normal
		var gravity = Scene.PhysicsWorld.Gravity;
		_rb.Velocity += gravity * ( GravityMultiplier - 1f ) * Time.Delta;
	}

	protected override void OnUpdate()
	{
		if ( _hasImpacted ) return;

		_lifetime += Time.Delta;

		// Safety fuse: destroy after MaxLifetime even without a solid impact
		if ( _lifetime >= MaxLifetime )
		{
			OnImpact();
			return;
		}

		// Forward trace to detect surfaces before the collider itself fires
		var velocity = _rb != null ? _rb.Velocity : Vector3.Zero;
		if ( velocity.LengthSquared < 1f ) return;

		var traceDir = velocity.Normal;
		var hit = Scene.Trace
			.Ray( WorldPosition, WorldPosition + traceDir * 20f )
			.IgnoreGameObject( GameObject )
			.WithoutTags( "player" )
			.Run();

		if ( hit.Hit && hit.GameObject != Owner )
		{
			OnImpact();
		}
	}

	private void OnImpact()
	{
		if ( _hasImpacted ) return;
		_hasImpacted = true;

		// Play shattering sound
		if ( ImpactSound != null )
			Sound.Play( ImpactSound, WorldPosition );

		// Spawn the fire zone at impact position
		if ( FireZonePrefab != null )
		{
			var fireScene = SceneUtility.GetPrefabScene( FireZonePrefab );
			if ( fireScene != null )
			{
				var fireGO = fireScene.Clone( WorldPosition );
				fireGO.WorldPosition = WorldPosition;
			}
			else
			{
				Log.Warning( "MolotovProjectile: FireZonePrefab scene could not be loaded" );
			}
		}
		else
		{
			// No prefab configured: create a FireZone directly on a new GO
			Log.Warning( "MolotovProjectile: FireZonePrefab not assigned — creating inline FireZone" );
			var fireGO = new GameObject( true, "FireZone" );
			fireGO.WorldPosition = WorldPosition;
			fireGO.Components.Create<FireZone>();
		}

		GameObject.Destroy();
	}
}
