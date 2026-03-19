using Sandbox;
using System.Collections.Generic;

/// <summary>
/// A lingering acid pool left by the Spitter. Deals damage-over-time to any entity inside it.
/// Self-destructs after Duration seconds.
/// </summary>
public sealed class AcidPool : Component, Component.ITriggerListener
{
	[Property] public float DamagePerSecond { get; set; } = 5f;
	[Property] public float Duration { get; set; } = 8f;

	private float lifeTimer = 0f;
	private float damageTickTimer = 0f;
	private readonly List<GameObject> objectsInside = new();

	protected override void OnAwake()
	{
		// Ensure we have a trigger collider so ITriggerListener callbacks fire
		var col = Components.GetOrCreate<BoxCollider>();
		col.IsTrigger = true;
		col.Scale = new Vector3( 120f, 120f, 20f );
		col.Center = new Vector3( 0f, 0f, 10f );
		GameObject.Tags.Add( "trigger" );
	}

	protected override void OnUpdate()
	{
		lifeTimer += Time.Delta;
		if ( lifeTimer >= Duration )
		{
			GameObject.Destroy();
			return;
		}

		// Damage every entity inside once per second
		damageTickTimer += Time.Delta;
		if ( damageTickTimer >= 1f )
		{
			damageTickTimer = 0f;
			ApplyDamageToInside();
		}
	}

	void ApplyDamageToInside()
	{
		for ( int i = objectsInside.Count - 1; i >= 0; i-- )
		{
			var go = objectsInside[i];
			if ( go is null || !go.Active )
			{
				objectsInside.RemoveAt( i );
				continue;
			}

			var health = go.Components.GetInDescendantsOrSelf<HealthComponent>()
			          ?? go.Components.GetInAncestorsOrSelf<HealthComponent>();
			if ( health is not null && !health.IsDead )
			{
				health.TakeDamage( DamagePerSecond, GameObject );
			}
		}
	}

	// ITriggerListener
	public void OnTriggerEnter( Collider other )
	{
		if ( other?.GameObject is null ) return;
		if ( !objectsInside.Contains( other.GameObject ) )
			objectsInside.Add( other.GameObject );
	}

	public void OnTriggerExit( Collider other )
	{
		if ( other?.GameObject is null ) return;
		objectsInside.Remove( other.GameObject );
	}
}
