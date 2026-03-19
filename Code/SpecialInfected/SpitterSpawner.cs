using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Spawns Spitter special infected. Designed for AIDirector integration.
/// Spawns when AI Director intensity > 0.5 (medium-high).
/// </summary>
public sealed class SpitterSpawner : Component
{
	[Property] public float SpawnCooldown { get; set; } = 45f;
	[Property] public int MaxSpitters { get; set; } = 2;
	[Property] public float SpawnRadius { get; set; } = 500f;
	[Property] public GameObject SpitterPrefab { get; set; }
	[Property] public GameObject PlayerReference { get; set; }
	[Property] public new bool Enabled { get; set; } = true;

	public bool IsSafeZoneActive { get; set; } = false;

	private float cooldownTimer = 0f;
	private List<GameObject> spawnedSpitters = new();
	private GameObject player;

	protected override void OnAwake()
	{
		Log.Info( "SpitterSpawner: OnAwake" );

		SafeRoom.OnPlayerEntered += OnPlayerEnteredSafeRoom;
		SafeRoom.OnPlayerExited  += OnPlayerExitedSafeRoom;

		if ( PlayerReference is not null )
		{
			player = PlayerReference;
		}
		else
		{
			var pm = Scene.Components.Get<PlayerMovement>();
			if ( pm is not null )
				player = pm.GameObject;
			else
			{
				foreach ( var go in Scene.GetAllComponents<PlayerMovement>() )
				{
					player = go.GameObject;
					break;
				}
			}
		}

		if ( player is null )
			Log.Warning( "SpitterSpawner: Could not find player. Assign PlayerReference manually." );
	}

	protected override void OnUpdate()
	{
		if ( !Enabled || player is null || SpitterPrefab is null ) return;

		// Cleanup dead / invalid spitters
		int removed = 0;
		for ( int i = spawnedSpitters.Count - 1; i >= 0; i-- )
		{
			bool dead = false;
			try
			{
				var go = spawnedSpitters[i];
				if ( go is null || !go.Active )
				{
					dead = true;
				}
				else
				{
					var spitter = go.Components.Get<Spitter>();
					if ( spitter is null || !spitter.Enabled )
						dead = true;
				}
			}
			catch
			{
				dead = true;
			}

			if ( dead )
			{
				spawnedSpitters.RemoveAt( i );
				removed++;
			}
		}

		if ( removed > 0 )
			Log.Info( $"SpitterSpawner: Cleaned {removed} dead spitters. Alive: {spawnedSpitters.Count}/{MaxSpitters}" );

		cooldownTimer += Time.Delta;
	}

	/// <summary>
	/// Called by AIDirector when intensity > 0.5. Returns true if a spawn was attempted.
	/// </summary>
	public bool TrySpawnSpitter()
	{
		if ( IsSafeZoneActive ) return false;
		if ( spawnedSpitters.Count >= MaxSpitters ) return false;
		if ( player is null || SpitterPrefab is null ) return false;

		try
		{
			SpawnSpitter();
			return true;
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"SpitterSpawner: Exception during spawn: {ex.Message}\n{ex.StackTrace}" );
			return false;
		}
	}

	public int GetAliveSpitterCount()
	{
		spawnedSpitters.RemoveAll( s => s is null || !s.Active );
		return spawnedSpitters.Count;
	}

	void SpawnSpitter()
	{
		float angle    = Game.Random.Float( 0f, 360f );
		float distance = System.Math.Max( Game.Random.Float( SpawnRadius * 0.8f, SpawnRadius ), 300f );

		Vector3 rayStart = player.WorldPosition + new Vector3(
			(float)System.Math.Cos( angle * System.Math.PI / 180f ) * distance,
			(float)System.Math.Sin( angle * System.Math.PI / 180f ) * distance,
			500f
		);

		var trace = Scene.Trace.Ray( rayStart, rayStart + Vector3.Down * 1000f ).Run();
		if ( !trace.Hit )
		{
			Log.Warning( "SpitterSpawner: No ground found at spawn position" );
			return;
		}

		Vector3 spawnPos = trace.EndPosition + Vector3.Up * 5f;
		if ( spawnPos.z < 20f )
		{
			Log.Warning( $"SpitterSpawner: Spawn position too low ({spawnPos.z:F1})" );
			return;
		}

		var spawned = SpitterPrefab.Clone( spawnPos );
		if ( spawned is null )
		{
			Log.Error( "SpitterSpawner: Clone returned null" );
			return;
		}

		// Ensure CharacterController
		var cc = spawned.Components.Get<CharacterController>();
		if ( cc is null )
		{
			cc = spawned.Components.Create<CharacterController>();
			cc.Radius = 25f;
			cc.Height = 100f;
		}
		cc.Enabled = true;
		cc.Velocity = Vector3.Zero;
		cc.IsOnGround = true;

		// Ensure BoxCollider for bullet hits
		var box = spawned.Components.Get<BoxCollider>();
		if ( box is null )
		{
			box = spawned.Components.Create<BoxCollider>();
			box.Scale  = new Vector3( 40f, 40f, 100f );
			box.Center = new Vector3( 0f, 0f, 50f );
		}

		// Ensure HealthComponent
		var hc = spawned.Components.Get<HealthComponent>();
		if ( hc is null )
		{
			hc = spawned.Components.Create<HealthComponent>();
			hc.MaxHealth     = 80f;
			hc.CurrentHealth = 80f;
			hc.IsPlayer      = false;
		}
		else
		{
			hc.MaxHealth     = 80f;
			hc.CurrentHealth = 80f;
		}

		// Enable / create Spitter component
		var spitterComp = spawned.Components.GetAll<Spitter>().FirstOrDefault();
		if ( spitterComp is null )
			spitterComp = spawned.Components.Create<Spitter>();
		else
			spitterComp.Enabled = true;

		spitterComp.PlayerReference = player;

		spawnedSpitters.Add( spawned );
		Log.Info( $"SpitterSpawner: Spawned Spitter at distance {distance:F0}. Total: {spawnedSpitters.Count}/{MaxSpitters}" );
	}

	void OnPlayerEnteredSafeRoom( SafeRoom room )
	{
		IsSafeZoneActive = true;
		Log.Info( "SpitterSpawner: Safe zone active — spawning paused" );
	}

	void OnPlayerExitedSafeRoom( SafeRoom room )
	{
		IsSafeZoneActive = false;
		Log.Info( "SpitterSpawner: Safe zone deactivated — spawning resumed" );
	}

	protected override void OnDestroy()
	{
		SafeRoom.OnPlayerEntered -= OnPlayerEnteredSafeRoom;
		SafeRoom.OnPlayerExited  -= OnPlayerExitedSafeRoom;
	}
}
