using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Place these around the map to populate the world with enemies at level start.
/// Each group spawns a random number of enemies within a radius on startup.
/// The AIDirector reads alive counts to factor static pressure into intensity.
/// </summary>
public sealed class StaticEnemyGroup : Component
{
	private bool IsAuthoritativeInstance() => !Networking.IsActive || Connection.Local?.IsHost == true;

	public static readonly List<StaticEnemyGroup> All = new();

	[Property] public GameObject EnemyPrefab { get; set; }
	[Property] public GameObject PlayerReference { get; set; }

	[Property] public int MinEnemies { get; set; } = 2;
	[Property] public int MaxEnemies { get; set; } = 5;
	[Property] public float SpawnRadius { get; set; } = 300f;
	[Property] public float MinPlayerDistance { get; set; } = 400f;

	/// <summary>
	/// How much stress each alive enemy in this group contributes to the director.
	/// Default 1 = same as a dynamically spawned enemy.
	/// </summary>
	[Property] public float StressPerEnemy { get; set; } = 1f;

	private List<GameObject> spawnedEnemies = new();
	private GameObject player;

	public int AliveCount
	{
		get
		{
			spawnedEnemies.RemoveAll( e => e is null || !e.Active || e.Components.Get<Enemy>() == null );
			return spawnedEnemies.Count;
		}
	}

	public float CurrentStress => AliveCount * StressPerEnemy;

	protected override void OnStart()
	{
		All.Add( this );

		if ( !IsAuthoritativeInstance() )
			return;

		if ( PlayerReference is not null )
			player = PlayerReference;
		else
		{
			var pm = Scene.Components.Get<PlayerMovement>();
			if ( pm is not null ) player = pm.GameObject;
		}

		int count = Game.Random.Int( MinEnemies, MaxEnemies );
		for ( int i = 0; i < count; i++ )
			SpawnOne();
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
	}

	private void SpawnOne()
	{
		if ( EnemyPrefab is null ) return;

		// Find a valid ground position within radius
		Vector3 spawnPos = Vector3.Zero;
		for ( int attempt = 0; attempt < 10; attempt++ )
		{
			float angle    = Game.Random.Float( 0f, 360f );
			float distance = Game.Random.Float( 50f, SpawnRadius );

			Vector3 rayOrigin = new Vector3(
				WorldPosition.x + (float)System.Math.Cos( angle * System.Math.PI / 180f ) * distance,
				WorldPosition.y + (float)System.Math.Sin( angle * System.Math.PI / 180f ) * distance,
				WorldPosition.z + 20f
			);

			var ground = Scene.Trace
				.Ray( rayOrigin, rayOrigin + Vector3.Down * 500f )
				.WithoutTags( "trigger" )
				.Run();

			if ( !ground.Hit ) continue;

			Vector3 candidate = ground.EndPosition + Vector3.Up * 5f;
			if ( System.Math.Abs( candidate.z - WorldPosition.z ) > 100f ) continue;

			// Reject if too close to player at spawn time
			if ( player != null && Vector3.DistanceBetween( candidate, player.WorldPosition ) < MinPlayerDistance ) continue;

			spawnPos = candidate;
			break;
		}

		if ( spawnPos == Vector3.Zero )
		{
			spawnPos = WorldPosition + Vector3.Up * 5f;
		}

		var go = EnemyPrefab.Clone( spawnPos );
		if ( go is null ) return;

		// CharacterController
		var cc = go.Components.Get<CharacterController>();
		if ( cc is null )
		{
			cc = go.Components.Create<CharacterController>();
			cc.Radius = 25f;
			cc.Height = 100f;
		}
		cc.Enabled    = true;
		cc.Velocity   = Vector3.Zero;
		cc.IsOnGround = true;

		// Disable Rigidbody — re-enabled on death for ragdoll
		var rb = go.Components.Get<Rigidbody>();
		if ( rb != null ) rb.Enabled = false;

		// BoxCollider for bullet hits
		if ( go.Components.Get<BoxCollider>() is null )
		{
			var col    = go.Components.Create<BoxCollider>();
			col.Scale  = new Vector3( 40f, 40f, 72f );
			col.Center = new Vector3( 0f, 0f, 36f );
		}

		// Headshot zone
		var head           = new GameObject( true, "HeadshotZone" );
		head.Parent        = go;
		head.LocalPosition = new Vector3( 0f, 0f, 62f );
		head.Tags.Add( "headzone" );
		var headCol        = head.Components.Create<BoxCollider>();
		headCol.Scale      = new Vector3( 30f, 30f, 30f );
		var hsZone         = head.Components.Create<HeadshotZone>();
		hsZone.DamageMultiplier = 2.0f;
		hsZone.TargetEntity     = go;

		// HealthComponent
		var health = go.Components.Get<HealthComponent>();
		if ( health is null )
		{
			health               = go.Components.Create<HealthComponent>();
			health.MaxHealth     = 100f;
			health.CurrentHealth = 100f;
			health.IsPlayer      = false;
		}
		else
		{
			health.CurrentHealth = health.MaxHealth;
		}

		// Enemy component
		var enemy = go.Components.GetAll<Enemy>().FirstOrDefault()
			?? go.Components.Create<Enemy>();

		enemy.Enabled = true;
		// Enemy targets via PlayerIdentity.Nearest() — no PlayerReference needed

		// Re-wire animation helper
		var skinned = go.Components.Get<SkinnedModelRenderer>();
		if ( skinned is not null )
		{
			var anim    = go.Components.GetOrCreate<CitizenAnimationHelper>();
			anim.Target = skinned;
		}

		if ( Networking.IsActive )
			go.NetworkSpawn();

		spawnedEnemies.Add( go );
		Log.Info( $"[StaticGroup] Spawned at {spawnPos} (Z={spawnPos.z:F0}, dist from player={( player != null ? Vector3.DistanceBetween( spawnPos, player.WorldPosition ).ToString( "F0" ) : "?" )})" );
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Orange.WithAlpha( 0.3f );
		Gizmo.Draw.LineSphere( Vector3.Zero, SpawnRadius );
		Gizmo.Draw.Color = Color.Orange;
		Gizmo.Draw.LineSphere( Vector3.Zero, 20f );
	}
}
