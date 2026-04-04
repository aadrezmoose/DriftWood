using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Physics projectile spawned by the Pipe Bomb throwable.
/// Beeps on a 0.5-second cadence, then explodes after FuseDuration seconds,
/// dealing AOE damage to all HealthComponents within ExplosionRadius.
/// </summary>
public sealed class PipeBombProjectile : Component
{
	[Property] public SoundEvent BeepSound { get; set; }
	[Property] public SoundEvent ExplosionSound { get; set; }
	[Property] public float FuseDuration { get; set; } = 3f;
	[Property] public float ExplosionRadius { get; set; } = 2000f;
	[Property] public float ExplosionDamage { get; set; } = 150f;
	/// <summary>0.5 = half gravity, 1 = normal, 2 = double.</summary>
	[Property] public float GravityMultiplier { get; set; } = 0.5f;
	/// <summary>Max distance at which enemies are attracted to this bomb.</summary>
	[Property] public float AttractionRadius { get; set; } = 1500f;

	/// <summary>Set by PipeBomb immediately after spawning so the blast can attribute kills.</summary>
	public GameObject Owner { get; set; }

	/// <summary>
	/// The currently active lure target. Enemies check this every frame and chase it
	/// instead of the player while it is set. Cleared when the bomb explodes or is destroyed.
	/// </summary>
	public static GameObject ActiveLure { get; private set; }

	private Rigidbody _rb;
	private float _fuseTimer = 0f;
	private float _beepTimer = 0f;
	private const float BeepInterval = 0.5f;
	private bool _hasExploded = false;

	protected override void OnStart()
	{
		// Ensure a Rigidbody exists — PipeBomb sets velocity on this after spawning.
		_rb = Components.GetOrCreate<Rigidbody>();
		// Register as the active lure so enemies start chasing immediately
		ActiveLure = GameObject;
	}

	protected override void OnDestroy()
	{
		// Clear lure if this bomb is still the registered one (guards against multiple bombs)
		if ( ActiveLure == GameObject )
			ActiveLure = null;
	}

	protected override void OnFixedUpdate()
	{
		if ( _hasExploded || _rb == null ) return;
		var gravity = Scene.PhysicsWorld.Gravity;
		_rb.Velocity += gravity * ( GravityMultiplier - 1f ) * Time.Delta;
	}

	protected override void OnUpdate()
	{
		if ( _hasExploded ) return;

		_fuseTimer += Time.Delta;
		_beepTimer += Time.Delta;

		// Beep every half second
		if ( _beepTimer >= BeepInterval )
		{
			_beepTimer -= BeepInterval;
			if ( BeepSound != null )
				Sound.Play( BeepSound, WorldPosition );
		}

		// Detonate when fuse expires
		if ( _fuseTimer >= FuseDuration )
		{
			Explode();
		}
	}

	private void Explode()
	{
		if ( _hasExploded ) return;
		_hasExploded = true;

		// Release the lure so enemies resume chasing the player
		if ( ActiveLure == GameObject )
			ActiveLure = null;

		// Play explosion sound
		if ( ExplosionSound != null )
			Sound.Play( ExplosionSound, WorldPosition );

		// AOE damage — check all HealthComponents by distance
		var damagedTargets = new HashSet<HealthComponent>();

		foreach ( var health in Scene.GetAllComponents<HealthComponent>() )
		{
			if ( health == null || !health.IsValid || health.IsDead ) continue;

			float dist = (health.WorldPosition - WorldPosition).Length;
			if ( dist > ExplosionRadius ) continue;

			// LOS check — don't damage through walls
			bool isPlayer = health.GameObject.Components.GetInDescendantsOrSelf<PlayerMovement>() != null
				|| health.GameObject.Components.GetInAncestorsOrSelf<PlayerMovement>() != null;

			var los = Scene.Trace.Ray( WorldPosition + Vector3.Up * 10f, health.WorldPosition + Vector3.Up * 10f )
				.WithoutTags( "trigger", "headzone" )
				.IgnoreGameObject( GameObject )
				.IgnoreGameObject( health.GameObject )
				.Run();

			if ( los.Hit && los.Distance < dist - 5f )
			{
				// For the player: walls always block, no passthrough
				if ( isPlayer ) continue;

				// For enemies: only blocked by actual geometry, not other characters
				var hitEnemy = los.GameObject?.Components.GetInAncestorsOrSelf<Enemy>()
					?? los.GameObject?.Components.GetInDescendantsOrSelf<Enemy>();
				var hitPlayer = los.GameObject?.Components.GetInAncestorsOrSelf<PlayerMovement>()
					?? los.GameObject?.Components.GetInDescendantsOrSelf<PlayerMovement>();
				if ( hitEnemy == null && hitPlayer == null ) continue;
			}

			if ( damagedTargets.Add( health ) )
			{
				float falloff = 1f - System.MathF.Min( dist / ExplosionRadius, 1f );
				float scaledDamage = ExplosionDamage * (0.3f + 0.7f * falloff);
				health.TakeDamage( scaledDamage, Owner );
				Log.Info( $"PipeBomb: dealt {scaledDamage:F0} damage to {health.GameObject.Name}" );
			}
		}

		Log.Info( $"PipeBomb exploded — hit {damagedTargets.Count} target(s)" );

		GameObject.Destroy();
	}
}
