using Sandbox;
using System.Collections.Generic;

/// <summary>
/// L4D-style AI Director with proper phase-based flow:
/// Rest → BuildUp → Peak → Relent → Rest
///
/// The signature L4D feel comes from RELENT — a sudden, forced quiet after a peak.
/// Players hear nothing and know something worse is coming.
/// </summary>
public sealed class AIDirector : Component
{
	[Property] public EnemySpawner EnemySpawner { get; set; }
	[Property] public TankSpawner TankSpawner { get; set; }
	[Property] public HunterSpawner HunterSpawner { get; set; }
	[Property] public SpitterSpawner SpitterSpawner { get; set; }
	[Property] public ChargerSpawner ChargerSpawner { get; set; }
	[Property] public GameObject Player { get; set; }

	// ── Phase durations ────────────────────────────────────────────────
	[Property] public float RestMin { get; set; }    = 30f;
	[Property] public float RestMax { get; set; }    = 55f;
	[Property] public float BuildUpMin { get; set; } = 40f;
	[Property] public float BuildUpMax { get; set; } = 80f;
	[Property] public float PeakMin { get; set; }    = 30f;
	[Property] public float PeakMax { get; set; }    = 55f;
	[Property] public float RelentMin { get; set; }  = 18f;
	[Property] public float RelentMax { get; set; }  = 32f;

	// ── Horde event ────────────────────────────────────────────────────
	[Property] public float HordeChance { get; set; }        = 0.55f;   // probability per Peak phase
	[Property] public float HordeDuration { get; set; }      = 40f;     // seconds
	[Property] public float HordeSpawnInterval { get; set; } = 0.35f;
	[Property] public int   HordeMaxEnemies { get; set; }    = 22;

	// ── Special infected cooldowns ─────────────────────────────────────
	[Property] public float TankCooldown    { get; set; } = 120f;
	[Property] public float HunterCooldown  { get; set; } = 50f;
	[Property] public float SpitterCooldown { get; set; } = 45f;
	[Property] public float ChargerCooldown { get; set; } = 60f;

	// ── State ──────────────────────────────────────────────────────────

	/// <summary>
	/// 0 = Rest, 1 = Peak. Read by EnemySpawner to weight spawn nodes.
	/// </summary>
	public float CurrentIntensity { get; private set; }

	private enum Phase { Rest, BuildUp, Peak, Relent }
	private Phase   currentPhase;
	private float   phaseTimer;
	private float   phaseDuration;

	private float   tankTimer    = 999f;
	private float   hunterTimer  = 999f;
	private float   spitterTimer = 999f;
	private float   chargerTimer = 999f;

	private bool    hordeActive;
	private float   hordeTimer;
	private bool    hordeScheduled;
	private float   hordeFireAt;   // phase-local time to fire the horde
	private float   savedInterval;
	private int     savedMaxEnemies;

	protected override void OnAwake()
	{
		if ( Player == null )
		{
			var pm = Scene.Components.Get<PlayerMovement>();
			if ( pm != null ) Player = pm.GameObject;
		}

		var playerHealth = Player?.Components.GetInDescendantsOrSelf<HealthComponent>();
		if ( playerHealth != null )
			playerHealth.OnDamageTaken += OnPlayerDamaged;

		SafeRoom.OnPlayerEntered += OnSafeRoomEntered;
		SafeRoom.OnPlayerExited  += OnSafeRoomExited;

		EnterPhase( Phase.Rest );
		Log.Info( "AIDirector: Phase system started" );
	}

	protected override void OnUpdate()
	{
		if ( Player == null || EnemySpawner == null ) return;

		phaseTimer   += Time.Delta;
		tankTimer    += Time.Delta;
		hunterTimer  += Time.Delta;
		spitterTimer += Time.Delta;
		chargerTimer += Time.Delta;

		// Base intensity from phase
		float baseIntensity = currentPhase switch
		{
			Phase.Rest    => 0f,
			Phase.BuildUp => System.Math.Min( phaseTimer / phaseDuration, 1f ) * 0.5f,
			Phase.Peak    => 0.5f + System.Math.Min( phaseTimer / phaseDuration, 1f ) * 0.5f,
			Phase.Relent  => 0.2f,
			_             => 0f
		};

		// Factor in static enemy stress — each alive static enemy adds pressure
		float staticStress = 0f;
		foreach ( var group in StaticEnemyGroup.All )
			staticStress += group.CurrentStress;

		// Normalise: assume ~10 static enemies = full stress contribution (capped at 0.4)
		float staticIntensityContribution = System.Math.Min( staticStress / 10f, 0.4f );

		CurrentIntensity = System.Math.Min( baseIntensity + staticIntensityContribution, 1f );

		// Director yields to safe zone
		if ( EnemySpawner.IsSafeZoneActive ) return;

		// Mercy rule: player went down → immediate relent
		if ( PlayerStats.IsIncapacitated && currentPhase != Phase.Relent && currentPhase != Phase.Rest )
		{
			Log.Info( "AIDirector: Player downed — mercy relent" );
			if ( hordeActive ) EndHorde();
			EnterPhase( Phase.Relent );
			return;
		}

		// Advance phase when timer expires — slow down if static enemies are keeping pressure up
		float staticStressTotal = StaticEnemyGroup.All.Sum( g => g.CurrentStress );
		float phaseSlowdown     = 1f + System.Math.Min( staticStressTotal / 15f, 0.5f ); // up to 50% longer phases
		if ( phaseTimer >= phaseDuration * phaseSlowdown )
			AdvancePhase();

		// Horde scheduling
		if ( hordeScheduled && !hordeActive && currentPhase == Phase.Peak && phaseTimer >= hordeFireAt )
		{
			hordeScheduled = false;
			TriggerHorde();
		}

		// Horde tick
		if ( hordeActive )
		{
			hordeTimer += Time.Delta;
			if ( hordeTimer >= HordeDuration )
				EndHorde();
		}
		else
		{
			ApplyPhaseToSpawner();
		}

		HandleSpecialInfected();
	}

	// ── Phase management ───────────────────────────────────────────────

	void EnterPhase( Phase phase )
	{
		currentPhase  = phase;
		phaseTimer    = 0f;
		hordeScheduled = false;

		switch ( phase )
		{
			case Phase.Rest:
				phaseDuration = Game.Random.Float( RestMin, RestMax );
				Log.Info( $"AIDirector: ▷ REST  ({phaseDuration:F0}s)" );
				break;

			case Phase.BuildUp:
				phaseDuration = Game.Random.Float( BuildUpMin, BuildUpMax );
				Log.Info( $"AIDirector: ▷ BUILD UP  ({phaseDuration:F0}s)" );
				break;

			case Phase.Peak:
				phaseDuration = Game.Random.Float( PeakMin, PeakMax );
				Log.Info( $"AIDirector: ▷ PEAK  ({phaseDuration:F0}s)" );

				if ( Game.Random.Float() < HordeChance )
				{
					hordeScheduled = true;
					hordeFireAt    = Game.Random.Float( 8f, 20f );
					Log.Info( $"AIDirector: Horde queued at t+{hordeFireAt:F0}s" );
				}
				break;

			case Phase.Relent:
				phaseDuration = Game.Random.Float( RelentMin, RelentMax );
				Log.Info( $"AIDirector: ▷ RELENT  ({phaseDuration:F0}s) — backing off" );
				break;
		}
	}

	void AdvancePhase()
	{
		switch ( currentPhase )
		{
			case Phase.Rest:    EnterPhase( Phase.BuildUp ); break;
			case Phase.BuildUp: EnterPhase( Phase.Peak    ); break;
			case Phase.Peak:    EnterPhase( Phase.Relent  ); break;
			case Phase.Relent:  EnterPhase( Phase.Rest    ); break;
		}
	}

	// ── Spawner settings per phase ─────────────────────────────────────

	void ApplyPhaseToSpawner()
	{
		// Keep spawner's internal pause/cycle disabled — Director owns timing
		EnemySpawner.SpawnDuration  = 9999f;
		EnemySpawner.SpawnPauseTime = 1f;

		switch ( currentPhase )
		{
			case Phase.Rest:
				// Barely anything. A zombie or two wandering nearby.
				EnemySpawner.SpawnInterval = 12f;
				EnemySpawner.MaxEnemies    = 2;
				break;

			case Phase.BuildUp:
			{
				// Gradually ramp up from slow → brisk over the phase
				float t = System.Math.Min( phaseTimer / phaseDuration, 1f );
				EnemySpawner.SpawnInterval = 7f + ( 2.5f - 7f ) * t;
				EnemySpawner.MaxEnemies    = (int)( 3f + ( 8f - 3f ) * t );
				break;
			}

			case Phase.Peak:
				// Relentless pressure
				EnemySpawner.SpawnInterval = 1.5f;
				EnemySpawner.MaxEnemies    = 12;
				break;

			case Phase.Relent:
				// Sudden near-silence. This is the moment players remember.
				EnemySpawner.SpawnInterval = 25f;
				EnemySpawner.MaxEnemies    = 1;
				break;
		}
	}

	// ── Horde event ────────────────────────────────────────────────────

	void TriggerHorde()
	{
		if ( hordeActive ) return;

		hordeActive = true;
		hordeTimer  = 0f;

		savedInterval    = EnemySpawner.SpawnInterval;
		savedMaxEnemies  = EnemySpawner.MaxEnemies;

		EnemySpawner.SpawnInterval = HordeSpawnInterval;
		EnemySpawner.MaxEnemies    = HordeMaxEnemies;

		Log.Info( "AIDirector: ★ HORDE EVENT ★" );
	}

	void EndHorde()
	{
		hordeActive = false;
		hordeTimer  = 0f;

		EnemySpawner.SpawnInterval = savedInterval;
		EnemySpawner.MaxEnemies    = savedMaxEnemies;

		Log.Info( "AIDirector: Horde ended" );
	}

	// ── Special infected placement ─────────────────────────────────────

	void HandleSpecialInfected()
	{
		// Tank: only during Peak, one at a time, long cooldown
		if ( currentPhase == Phase.Peak && TankSpawner != null )
		{
			if ( tankTimer >= TankCooldown && !TankSpawner.HasAliveTank() )
			{
				if ( TankSpawner.TrySpawnTank() )
				{
					tankTimer = 0f;
					Log.Info( "AIDirector: Tank spawned during Peak" );
				}
			}
		}

		// Hunter: during BuildUp and Peak, multiple allowed, shorter cooldown
		if ( (currentPhase == Phase.BuildUp || currentPhase == Phase.Peak) && HunterSpawner != null )
		{
			if ( hunterTimer >= HunterCooldown && HunterSpawner.GetAliveHunterCount() < HunterSpawner.MaxHunters )
			{
				hunterTimer = 0f;
				HunterSpawner.Enabled = true;
				Log.Info( $"AIDirector: Hunter triggered during {currentPhase}" );
			}
		}

		// Spitter: during BuildUp and Peak (intensity > 0.5 equivalent), medium cooldown
		if ( (currentPhase == Phase.BuildUp || currentPhase == Phase.Peak) && SpitterSpawner != null )
		{
			if ( spitterTimer >= SpitterCooldown && SpitterSpawner.GetAliveSpitterCount() < SpitterSpawner.MaxSpitters )
			{
				if ( SpitterSpawner.TrySpawnSpitter() )
				{
					spitterTimer = 0f;
					Log.Info( $"AIDirector: Spitter spawned during {currentPhase}" );
				}
			}
		}

		// Charger: only during Peak (intensity > 0.7 equivalent), longer cooldown
		if ( currentPhase == Phase.Peak && ChargerSpawner != null )
		{
			if ( chargerTimer >= ChargerCooldown && ChargerSpawner.GetAliveChargerCount() < ChargerSpawner.MaxChargers )
			{
				if ( ChargerSpawner.TrySpawnCharger() )
				{
					chargerTimer = 0f;
					Log.Info( $"AIDirector: Charger spawned during {currentPhase}" );
				}
			}
		}
	}

	// ── Event handlers ─────────────────────────────────────────────────

	void OnPlayerDamaged( float damage )
	{
		// Player taking heavy damage during BuildUp can accelerate to Peak
		if ( currentPhase == Phase.BuildUp && PlayerStats.CurrentHealth < PlayerStats.HealthMax * 0.25f )
		{
			float remainingBuildUp = phaseDuration - phaseTimer;
			if ( remainingBuildUp > 10f )
			{
				phaseDuration = phaseTimer + 10f; // rush to Peak within 10s
				Log.Info( "AIDirector: Player in danger — accelerating to Peak" );
			}
		}
	}

	void OnSafeRoomEntered( SafeRoom room )
	{
		if ( hordeActive ) EndHorde();
		EnterPhase( Phase.Rest );
		Log.Info( "AIDirector: Safe room — forced Rest" );
	}

	void OnSafeRoomExited( SafeRoom room )
	{
		EnterPhase( Phase.Rest );
		Log.Info( "AIDirector: Left safe room — fresh cycle begins" );
	}

	protected override void OnDestroy()
	{
		if ( hordeActive ) EndHorde();

		var playerHealth = Player?.Components.GetInDescendantsOrSelf<HealthComponent>();
		if ( playerHealth != null )
			playerHealth.OnDamageTaken -= OnPlayerDamaged;

		SafeRoom.OnPlayerEntered -= OnSafeRoomEntered;
		SafeRoom.OnPlayerExited  -= OnSafeRoomExited;
	}
}
