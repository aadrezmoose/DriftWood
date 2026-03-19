using Sandbox;
using System.Collections.Generic;

/// <summary>
/// A burning fire zone spawned on Molotov impact.
/// Deals damage per second to any HealthComponent that enters the area,
/// and destroys itself after Duration seconds.
/// </summary>
public sealed class FireZone : Component, Component.ITriggerListener
{
	[Property] public float DamagePerSecond { get; set; } = 8f;
	[Property] public float Duration { get; set; } = 6f;
	[Property] public SoundEvent BurnSound { get; set; }

	private readonly List<HealthComponent> _trackedTargets = new();
	private float _lifetime = 0f;
	private bool _burnSoundPlaying = false;

	protected override void OnStart()
	{
		// Create the trigger sphere so enemies and players can enter/exit
		var sphere = Components.GetOrCreate<SphereCollider>();
		sphere.IsTrigger = true;
		sphere.Radius = 80f;

		if ( BurnSound != null && !_burnSoundPlaying )
		{
			Sound.Play( BurnSound, WorldPosition );
			_burnSoundPlaying = true;
		}
	}

	protected override void OnUpdate()
	{
		_lifetime += Time.Delta;

		if ( _lifetime >= Duration )
		{
			GameObject.Destroy();
			return;
		}

		// Damage all tracked health components this frame
		for ( int i = _trackedTargets.Count - 1; i >= 0; i-- )
		{
			var health = _trackedTargets[i];
			if ( health == null || !health.IsValid || health.IsDead )
			{
				_trackedTargets.RemoveAt( i );
				continue;
			}

			health.TakeDamage( DamagePerSecond * Time.Delta );
		}
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( other == null || other.GameObject == null ) return;

		var health = other.GameObject.Components.GetInAncestorsOrSelf<HealthComponent>()
		          ?? other.GameObject.Components.GetInDescendantsOrSelf<HealthComponent>();

		if ( health != null && !_trackedTargets.Contains( health ) )
		{
			_trackedTargets.Add( health );
		}
	}

	public void OnTriggerExit( Collider other )
	{
		if ( other == null || other.GameObject == null ) return;

		var health = other.GameObject.Components.GetInAncestorsOrSelf<HealthComponent>()
		          ?? other.GameObject.Components.GetInDescendantsOrSelf<HealthComponent>();

		if ( health != null )
		{
			_trackedTargets.Remove( health );
		}
	}
}
