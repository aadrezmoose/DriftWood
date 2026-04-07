using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Spawns Charger special infected. Designed for AIDirector integration.
/// Spawns when AI Director intensity > 0.7 (high).
/// </summary>
public sealed class ChargerSpawner : Component
{
	private bool IsAuthoritativeInstance() => !Networking.IsActive || Connection.Local?.IsHost == true;

	[Property] public float SpawnCooldown { get; set; } = 60f;
	[Property] public int MaxChargers { get; set; } = 1;
	[Property] public float SpawnRadius { get; set; } = 600f;
	[Property] public GameObject ChargerPrefab { get; set; }
	[Property] public GameObject PlayerReference { get; set; }
	[Property] public new bool Enabled { get; set; } = true;

	public bool IsSafeZoneActive { get; set; } = false;

	private float cooldownTimer = 0f;
	private List<GameObject> spawnedChargers = new();
	private GameObject player;

	protected override void OnAwake()
	{
		Log.Info( "ChargerSpawner: OnAwake" );

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
			Log.Warning( "ChargerSpawner: Could not find player. Assign PlayerReference manually." );
	}

	protected override void OnUpdate()
	{
		if ( !IsAuthoritativeInstance() )
			return;

		if ( !Enabled || player is null || ChargerPrefab is null ) return;

		// Cleanup dead / invalid chargers
		int removed = 0;
		for ( int i = spawnedChargers.Count - 1; i >= 0; i-- )
		{
			bool dead = false;
			try
			{
				var go = spawnedChargers[i];
				if ( go is null || !go.Active )
				{
					dead = true;
				}
				else
				{
					var charger = go.Components.Get<Charger>();
					if ( charger is null || !charger.Enabled )
						dead = true;
				}
			}
			catch
			{
				dead = true;
			}

			if ( dead )
			{
				spawnedChargers.RemoveAt( i );
				removed++;
			}
		}

		if ( removed > 0 )
			Log.Info( $"ChargerSpawner: Cleaned {removed} dead chargers. Alive: {spawnedChargers.Count}/{MaxChargers}" );

		cooldownTimer += Time.Delta;
	}

	/// <summary>
	/// Called by AIDirector when intensity > 0.7. Returns true if a spawn was attempted.
	/// </summary>
	public bool TrySpawnCharger()
	{
		if ( !IsAuthoritativeInstance() ) return false;
		if ( IsSafeZoneActive ) return false;
		if ( spawnedChargers.Count >= MaxChargers ) return false;
		if ( player is null || ChargerPrefab is null ) return false;

		try
		{
			SpawnCharger();
			return true;
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"ChargerSpawner: Exception during spawn: {ex.Message}\n{ex.StackTrace}" );
			return false;
		}
	}

	public int GetAliveChargerCount()
	{
		spawnedChargers.RemoveAll( c => c is null || !c.Active );
		return spawnedChargers.Count;
	}

	void SpawnCharger()
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
			Log.Warning( "ChargerSpawner: No ground found at spawn position" );
			return;
		}

		Vector3 spawnPos = trace.EndPosition + Vector3.Up * 5f;
		if ( spawnPos.z < 20f )
		{
			Log.Warning( $"ChargerSpawner: Spawn position too low ({spawnPos.z:F1})" );
			return;
		}

		var spawned = ChargerPrefab.Clone( spawnPos );
		if ( spawned is null )
		{
			Log.Error( "ChargerSpawner: Clone returned null" );
			return;
		}

		// Ensure CharacterController
		var cc = spawned.Components.Get<CharacterController>();
		if ( cc is null )
		{
			cc = spawned.Components.Create<CharacterController>();
			cc.Radius = 30f;
			cc.Height = 110f;
		}
		cc.Enabled = true;
		cc.Velocity = Vector3.Zero;
		cc.IsOnGround = true;

		// Ensure BoxCollider for bullet hits
		var box = spawned.Components.Get<BoxCollider>();
		if ( box is null )
		{
			box = spawned.Components.Create<BoxCollider>();
			box.Scale  = new Vector3( 50f, 50f, 110f );
			box.Center = new Vector3( 0f, 0f, 55f );
		}

		// Ensure HealthComponent
		var hc = spawned.Components.Get<HealthComponent>();
		if ( hc is null )
		{
			hc = spawned.Components.Create<HealthComponent>();
			hc.MaxHealth     = 300f;
			hc.CurrentHealth = 300f;
			hc.IsPlayer      = false;
		}
		else
		{
			hc.MaxHealth     = 300f;
			hc.CurrentHealth = 300f;
		}

		// Enable / create Charger component
		var chargerComp = spawned.Components.GetAll<Charger>().FirstOrDefault();
		if ( chargerComp is null )
			chargerComp = spawned.Components.Create<Charger>();
		else
			chargerComp.Enabled = true;

		chargerComp.PlayerReference = player;

		if ( Networking.IsActive )
			spawned.NetworkSpawn();

		spawnedChargers.Add( spawned );
		Log.Info( $"ChargerSpawner: Spawned Charger at distance {distance:F0}. Total: {spawnedChargers.Count}/{MaxChargers}" );
	}

	void OnPlayerEnteredSafeRoom( SafeRoom room )
	{
		IsSafeZoneActive = true;
		Log.Info( "ChargerSpawner: Safe zone active — spawning paused" );
	}

	void OnPlayerExitedSafeRoom( SafeRoom room )
	{
		IsSafeZoneActive = false;
		Log.Info( "ChargerSpawner: Safe zone deactivated — spawning resumed" );
	}

	protected override void OnDestroy()
	{
		SafeRoom.OnPlayerEntered -= OnPlayerEnteredSafeRoom;
		SafeRoom.OnPlayerExited  -= OnPlayerExitedSafeRoom;
	}
}
