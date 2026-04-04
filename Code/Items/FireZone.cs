using Sandbox;
using System.Collections.Generic;

/// <summary>
/// A burning fire zone spawned on Molotov impact.
/// Deals damage per second to any HealthComponent that enters the area,
/// spawns a fire particle, and spreads smaller fires outward after a delay.
/// Destroys itself after Duration seconds.
/// </summary>
public sealed class FireZone : Component, Component.ITriggerListener
{
	[Property] public float DamagePerSecond { get; set; } = 8f;
	[Property] public float Duration { get; set; } = 6f;
	[Property] public float TriggerRadius { get; set; } = 80f;
	[Property] public SoundEvent BurnSound { get; set; }

	/// <summary>Particle/visual prefab spawned at this fire's position on start.</summary>
	[Property] public PrefabFile FireParticlePrefab { get; set; }

	/// <summary>How many smaller fires to spread outward.</summary>
	[Property] public int SpreadCount { get; set; } = 4;
	/// <summary>Radius within which spread fires are placed.</summary>
	[Property] public float SpreadRadius { get; set; } = 120f;
	/// <summary>Delay before spreading (seconds).</summary>
	[Property] public float SpreadDelay { get; set; } = 1f;
	/// <summary>Duration of each spread fire — shorter than the main fire.</summary>
	[Property] public float SpreadDuration { get; set; } = 3f;

	private readonly List<HealthComponent> _trackedTargets = new();
	private float _lifetime = 0f;
	private bool _hasSpread = false;
	private GameObject _fireParticle;

	protected override void OnStart()
	{
		var sphere = Components.GetOrCreate<SphereCollider>();
		sphere.IsTrigger = true;
		sphere.Radius = TriggerRadius;

		if ( BurnSound != null )
			Sound.Play( BurnSound, WorldPosition );

		if ( FireParticlePrefab != null )
		{
			var prefabScene = SceneUtility.GetPrefabScene( FireParticlePrefab );
			if ( prefabScene != null )
			{
				_fireParticle = prefabScene.Clone( WorldPosition );
				_fireParticle.Parent = GameObject;
				_fireParticle.LocalPosition = Vector3.Zero;
			}
		}
	}

	protected override void OnUpdate()
	{
		_lifetime += Time.Delta;

		if ( _lifetime >= Duration )
		{
			_fireParticle?.Destroy();
			GameObject.Destroy();
			return;
		}

		// Spread fire after delay (only once, only on the main fire — spread fires have SpreadCount=0)
		if ( !_hasSpread && SpreadCount > 0 && _lifetime >= SpreadDelay )
		{
			_hasSpread = true;
			SpawnSpreadFires();
		}

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

	private void SpawnSpreadFires()
	{
		for ( int i = 0; i < SpreadCount; i++ )
		{
			float angle = (360f / SpreadCount) * i + Game.Random.Float( -20f, 20f );
			float dist  = Game.Random.Float( SpreadRadius * 0.4f, SpreadRadius );
			var offset  = Rotation.FromYaw( angle ).Forward * dist;
			var spawnPos = WorldPosition + offset;

			// Trace down to find ground
			var groundHit = Scene.Trace.Ray( spawnPos + Vector3.Up * 50f, spawnPos - Vector3.Up * 200f )
				.WithoutTags( "player", "trigger" )
				.Run();

			var pos = groundHit.Hit ? groundHit.EndPosition : spawnPos;

			var spreadGO = new GameObject( true, "FireZone_Spread" );
			spreadGO.WorldPosition = pos;
			var spreadFire = spreadGO.Components.Create<FireZone>();
			spreadFire.DamagePerSecond  = DamagePerSecond * 0.6f;
			spreadFire.Duration         = SpreadDuration;
			spreadFire.TriggerRadius    = TriggerRadius * 0.6f;
			spreadFire.FireParticlePrefab = FireParticlePrefab;
			spreadFire.SpreadCount      = 0; // don't chain-spread
			spreadFire.BurnSound        = null; // only main fire plays sound
		}
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( other == null || other.GameObject == null ) return;

		var health = other.GameObject.Components.GetInAncestorsOrSelf<HealthComponent>()
		          ?? other.GameObject.Components.GetInDescendantsOrSelf<HealthComponent>();

		if ( health != null && !_trackedTargets.Contains( health ) )
			_trackedTargets.Add( health );
	}

	public void OnTriggerExit( Collider other )
	{
		if ( other == null || other.GameObject == null ) return;

		var health = other.GameObject.Components.GetInAncestorsOrSelf<HealthComponent>()
		          ?? other.GameObject.Components.GetInDescendantsOrSelf<HealthComponent>();

		if ( health != null )
			_trackedTargets.Remove( health );
	}
}
