using Sandbox;
using System.Linq;

/// <summary>
/// Dev bootstrap for special infected when dedicated prefabs/models are not ready yet.
/// - Auto-assigns missing special prefabs from a fallback enemy prefab.
/// - Exposes per-type one-shot spawn toggles in the inspector.
/// Attach this to any scene GameObject during development.
/// </summary>
public sealed class SpecialInfectedTestSetup : Component
{
	private bool IsAuthoritativeInstance() => !Networking.IsActive || Connection.Local?.IsHost == true;

	[Property] public bool AutoAssignMissingPrefabs { get; set; } = true;
	[Property] public bool AutoEnableSpawners { get; set; } = true;
	[Property] public bool EnableSetupLogs { get; set; } = true;

	[Property] public GameObject FallbackEnemyPrefab { get; set; }
	[Property] public GameObject TankPrefabOverride { get; set; }
	[Property] public GameObject HunterPrefabOverride { get; set; }
	[Property] public GameObject SpitterPrefabOverride { get; set; }
	[Property] public GameObject ChargerPrefabOverride { get; set; }

	/// <summary>Optional spawn point for testing. If set, all manual spawns go here instead of random positions.</summary>
	[Property] public GameObject TestSpawnPoint { get; set; }

	[Property] public bool SpawnTankNow { get; set; } = false;
	[Property] public bool SpawnHunterNow { get; set; } = false;
	[Property] public bool SpawnSpitterNow { get; set; } = false;
	[Property] public bool SpawnChargerNow { get; set; } = false;

	private TankSpawner tankSpawner;
	private HunterSpawner hunterSpawner;
	private SpitterSpawner spitterSpawner;
	private ChargerSpawner chargerSpawner;

	protected override void OnAwake()
	{
		CacheSpawners();
		ResolveFallbackPrefab();

		if ( AutoAssignMissingPrefabs )
			ApplyMissingPrefabAssignments();

		if ( AutoEnableSpawners )
			EnableFoundSpawners();
	}

	protected override void OnUpdate()
	{
		if ( !IsAuthoritativeInstance() )
			return;

		if ( SpawnTankNow )
		{
			SpawnTankNow = false;
			TrySpawnTank();
		}

		if ( SpawnHunterNow )
		{
			SpawnHunterNow = false;
			TrySpawnHunter();
		}

		if ( SpawnSpitterNow )
		{
			SpawnSpitterNow = false;
			TrySpawnSpitter();
		}

		if ( SpawnChargerNow )
		{
			SpawnChargerNow = false;
			TrySpawnCharger();
		}
	}

	private void CacheSpawners()
	{
		tankSpawner = Scene.GetAllComponents<TankSpawner>().FirstOrDefault();
		hunterSpawner = Scene.GetAllComponents<HunterSpawner>().FirstOrDefault();
		spitterSpawner = Scene.GetAllComponents<SpitterSpawner>().FirstOrDefault();
		chargerSpawner = Scene.GetAllComponents<ChargerSpawner>().FirstOrDefault();
	}

	private void ResolveFallbackPrefab()
	{
		if ( FallbackEnemyPrefab is not null )
			return;

		var enemySpawner = Scene.GetAllComponents<EnemySpawner>().FirstOrDefault();
		FallbackEnemyPrefab = enemySpawner?.EnemyPrefabs?.FirstOrDefault( prefab => prefab is not null );

		if ( EnableSetupLogs )
		{
			if ( FallbackEnemyPrefab is not null )
				Log.Info( $"[SpecialTestSetup] Using fallback prefab '{FallbackEnemyPrefab.Name}' from EnemySpawner" );
			else
				Log.Warning( "[SpecialTestSetup] No fallback enemy prefab found. Assign one manually in inspector." );
		}
	}

	private void ApplyMissingPrefabAssignments()
	{
		if ( tankSpawner is not null && tankSpawner.TankPrefab is null )
		{
			tankSpawner.TankPrefab = TankPrefabOverride ?? FallbackEnemyPrefab;
			LogSetupAssignment( "TankSpawner.TankPrefab", tankSpawner.TankPrefab );
		}

		if ( hunterSpawner is not null && hunterSpawner.HunterPrefab is null )
		{
			hunterSpawner.HunterPrefab = HunterPrefabOverride ?? FallbackEnemyPrefab;
			LogSetupAssignment( "HunterSpawner.HunterPrefab", hunterSpawner.HunterPrefab );
		}

		if ( spitterSpawner is not null && spitterSpawner.SpitterPrefab is null )
		{
			spitterSpawner.SpitterPrefab = SpitterPrefabOverride ?? FallbackEnemyPrefab;
			LogSetupAssignment( "SpitterSpawner.SpitterPrefab", spitterSpawner.SpitterPrefab );
		}

		if ( chargerSpawner is not null && chargerSpawner.ChargerPrefab is null )
		{
			chargerSpawner.ChargerPrefab = ChargerPrefabOverride ?? FallbackEnemyPrefab;
			LogSetupAssignment( "ChargerSpawner.ChargerPrefab", chargerSpawner.ChargerPrefab );
		}
	}

	private void EnableFoundSpawners()
	{
		if ( tankSpawner is not null ) tankSpawner.Enabled = true;
		if ( hunterSpawner is not null ) hunterSpawner.Enabled = true;
		if ( spitterSpawner is not null ) spitterSpawner.Enabled = true;
		if ( chargerSpawner is not null ) chargerSpawner.Enabled = true;
	}

	private void TrySpawnTank()
	{
		if ( tankSpawner is null )
		{
			Log.Warning( "[SpecialTestSetup] TankSpawner not found in scene." );
			return;
		}

		bool spawned = tankSpawner.TrySpawnTank();
		if ( EnableSetupLogs )
			Log.Info( $"[SpecialTestSetup] Tank spawn {(spawned ? "OK" : "blocked/failed")}" );
	}

	private void TrySpawnHunter()
	{
		if ( hunterSpawner is null )
		{
			Log.Warning( "[SpecialTestSetup] HunterSpawner not found in scene." );
			return;
		}

		Vector3? pos = TestSpawnPoint?.WorldPosition;
		bool spawned = hunterSpawner.TrySpawnHunter( pos );
		if ( EnableSetupLogs )
			Log.Info( $"[SpecialTestSetup] Hunter spawn {(spawned ? "OK" : "blocked/failed")}" );
	}

	private void TrySpawnSpitter()
	{
		if ( spitterSpawner is null )
		{
			Log.Warning( "[SpecialTestSetup] SpitterSpawner not found in scene." );
			return;
		}

		bool spawned = spitterSpawner.TrySpawnSpitter();
		if ( EnableSetupLogs )
			Log.Info( $"[SpecialTestSetup] Spitter spawn {(spawned ? "OK" : "blocked/failed")}" );
	}

	private void TrySpawnCharger()
	{
		if ( chargerSpawner is null )
		{
			Log.Warning( "[SpecialTestSetup] ChargerSpawner not found in scene." );
			return;
		}

		bool spawned = chargerSpawner.TrySpawnCharger();
		if ( EnableSetupLogs )
			Log.Info( $"[SpecialTestSetup] Charger spawn {(spawned ? "OK" : "blocked/failed")}" );
	}

	private void LogSetupAssignment( string target, GameObject prefab )
	{
		if ( !EnableSetupLogs )
			return;

		if ( prefab is null )
			Log.Warning( $"[SpecialTestSetup] {target} remains null (no override/fallback available)." );
		else
			Log.Info( $"[SpecialTestSetup] Assigned {target} = '{prefab.Name}'" );
	}
}
