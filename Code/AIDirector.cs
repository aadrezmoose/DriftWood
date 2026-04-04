using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// L4D-style AI Director with budget-based spawning and reactive phase transitions.
/// Rest → BuildUp → Peak → Relent → Rest
///
/// Budget is earned as income each phase. Spending spawns enemies; refunds flow back
/// on death, failed deployment, or timeout-despawn of non-engaging enemies.
/// </summary>
public sealed class AIDirector : Component
{
	private bool IsAuthoritativeInstance() => !Networking.IsActive || Connection.Local?.IsHost == true;

	[Property] public EnemySpawner       EnemySpawner    { get; set; }
	[Property] public TankSpawner        TankSpawner     { get; set; }
	[Property] public HunterSpawner      HunterSpawner   { get; set; }
	[Property] public SpitterSpawner     SpitterSpawner  { get; set; }
	[Property] public ChargerSpawner     ChargerSpawner  { get; set; }
	[Property] public GameObject         Player          { get; set; }

	// ── Phase durations ────────────────────────────────────────────────
	[Property] public float RestMin    { get; set; } = 8f;
	[Property] public float RestMax    { get; set; } = 25f;
	[Property] public float BuildUpMin { get; set; } = 15f;
	[Property] public float BuildUpMax { get; set; } = 55f;
	[Property] public float PeakMin    { get; set; } = 25f;
	[Property] public float PeakMax    { get; set; } = 60f;
	[Property] public float RelentMin  { get; set; } = 12f;
	[Property] public float RelentMax  { get; set; } = 35f;

	// ── Budget ─────────────────────────────────────────────────────────
	[Property] public float BudgetMax      { get; set; } = 18f;
	[Property] public float IncomeRest     { get; set; } = 0.3f;   // per second — tuned to match spending rate
	[Property] public float IncomeBuildUp  { get; set; } = 0.5f;
	[Property] public float IncomePeak     { get; set; } = 1.0f;   // ~= 1 spawn/1.5s × cost 1.0 → stays tight
	[Property] public float IncomeRelent   { get; set; } = 0.1f;
	[Property] public float IncomeHordeFloor { get; set; } = 3.5f;

	// ── Spawn costs ────────────────────────────────────────────────────
	[Property] public float CostCommon  { get; set; } = 1.0f;
	[Property] public float CostHorde   { get; set; } = 0.3f;
	[Property] public float CostHunter  { get; set; } = 4.0f;
	[Property] public float CostSpitter { get; set; } = 3.0f;
	[Property] public float CostCharger { get; set; } = 5.0f;
	[Property] public float CostTank    { get; set; } = 15.0f;

	// ── Refund fractions ───────────────────────────────────────────────
	/// <summary>Fraction of original cost returned when a common enemy dies.</summary>
	[Property] public float RefundCommonDeath   { get; set; } = 0.25f;
	[Property] public float RefundSpitterDeath  { get; set; } = 1.1f;
	[Property] public float RefundHunterDeath   { get; set; } = 1.5f;
	[Property] public float RefundChargerDeath  { get; set; } = 1.75f;
	[Property] public float RefundTankDeath     { get; set; } = 6.0f;
	/// <summary>Fraction of cost returned when a spawn position isn't found.</summary>
	[Property] public float RefundFailedSpawn   { get; set; } = 0.9f;
	/// <summary>Fraction of cost returned when a never-engaging enemy is soft-despawned.</summary>
	[Property] public float RefundTimeout       { get; set; } = 0.5f;

	// ── Calm-window bonus ──────────────────────────────────────────────
	/// <summary>Consecutive seconds with 0 alive enemies needed to earn the calm bonus.</summary>
	[Property] public float CalmWindowDuration  { get; set; } = 8f;
	/// <summary>Budget added when the calm window triggers.</summary>
	[Property] public float CalmWindowBonus     { get; set; } = 3f;

	// ── Global cap / reactive thresholds ──────────────────────────────
	[Property] public int   GlobalEnemyCap       { get; set; } = 16;
	/// <summary>Extra enemies added to the cap per player beyond the first.</summary>
	[Property] public int   ExtraCapPerPlayer    { get; set; } = 4;
	/// <summary>Extra budget added per player beyond the first.</summary>
	[Property] public float ExtraBudgetPerPlayer { get; set; } = 4f;
	/// <summary>Alive enemies at which BuildUp jumps to Peak early.</summary>
	[Property] public int   EarlyPeakThreshold   { get; set; } = 8;
	/// <summary>Alive enemies at or below which Peak triggers Relent.</summary>
	[Property] public int   RelentThreshold      { get; set; } = 3;
	/// <summary>Alive enemies at or below which a Horde is triggered during Peak.</summary>
	[Property] public int   HordeTriggerCount    { get; set; } = 4;

	// ── Horde event ────────────────────────────────────────────────────
	[Property] public float HordeChance       { get; set; } = 0.55f;
	[Property] public float HordeDuration     { get; set; } = 50f;
	[Property] public float HordeSpawnInterval { get; set; } = 0.22f;
	[Property] public int   HordeBatchSize    { get; set; } = 5;
	[Property] public int   HordeMaxEnemies   { get; set; } = 32;
	[Property] public float HordeBudgetCap    { get; set; } = 42f;
	[Property] public float HordeBudgetInjection { get; set; } = 12f;
	[Property] public float ObjectiveHordeSpawnInterval { get; set; } = 0.14f;
	[Property] public int   ObjectiveHordeBatchSize { get; set; } = 7;
	[Property] public int   ObjectiveHordeMaxEnemies { get; set; } = 42;
	[Property] public float ObjectiveHordeBudgetInjection { get; set; } = 18f;
	[Property] public float ObjectiveHordeIncomeFloor { get; set; } = 5.0f;
	[Property] public int   BuildUpBatchSize  { get; set; } = 1;
	[Property] public int   PeakBatchSize     { get; set; } = 2;

	// ── Special infected cooldowns ─────────────────────────────────────
	[Property] public float TankCooldown    { get; set; } = 120f;
	[Property] public float HunterCooldown  { get; set; } = 50f;
	[Property] public float SpitterCooldown { get; set; } = 45f;
	[Property] public float ChargerCooldown { get; set; } = 60f;

	// ── Soft despawn ───────────────────────────────────────────────────
	[Property] public float SoftDespawnDistance { get; set; } = 1200f;

	// ── Adaptive player-signal tuning ──────────────────────────────────
	[Property] public float LowAmmoThreshold { get; set; } = 35f;
	[Property] public float HighDamageRateForStress { get; set; } = 35f;
	[Property] public float StrongKillRatePerSecond { get; set; } = 0.9f;
	[Property] public float CohesionDistanceTarget { get; set; } = 650f;
	[Property] public float StressIntensityBoost { get; set; } = 0.22f;
	[Property] public float MomentumIntensityReduction { get; set; } = 0.12f;
	[Property] public float DamageRateSmoothing { get; set; } = 3.5f;
	[Property] public float KillRateSmoothing { get; set; } = 3.0f;
	[Property] public float StressPhaseSlowdown { get; set; } = 0.45f;
	[Property] public float MomentumPhaseSpeedup { get; set; } = 0.25f;
	[Property] public int StressRelentThresholdBonus { get; set; } = 2;
	[Property] public int MomentumRelentThresholdPenalty { get; set; } = 1;
	[Property] public int MomentumPeakThresholdPenalty { get; set; } = 2;
	[Property] public int StressPeakThresholdBonus { get; set; } = 2;

	// ── Public read-only state ──────────────────────────────────────────
	/// <summary>0 = Rest, 1 = Peak. Read by EnemySpawner to weight spawn nodes.</summary>
	public float CurrentIntensity { get; private set; }

	private int ScaledEnemyCap  => GlobalEnemyCap  + System.Math.Max( 0, PlayerCount - 1 ) * ExtraCapPerPlayer;
	private float ScaledBudgetMax => BudgetMax       + System.Math.Max( 0, PlayerCount - 1 ) * ExtraBudgetPerPlayer;
	private int PlayerCount     => Networking.IsActive ? (Connection.All?.Count() ?? 1) : 1;

	// Debug HUD read-outs
	public string DebugPhaseName     => currentPhase.ToString();
	public float  DebugPhaseTimer    => phaseTimer;
	public float  DebugPhaseDuration => phaseDuration;
	public float  DebugBudget        => currentBudget;
	public float  DebugIncomeRate    => currentPhase switch
	{
		Phase.Rest    => IncomeRest,
		Phase.BuildUp => IncomeBuildUp,
		Phase.Peak    => IncomePeak,
		Phase.Relent  => IncomeRelent,
		_             => 0f
	};
	public float  DebugBudgetCap     => hordeActive ? System.Math.Max( BudgetMax, HordeBudgetCap ) : BudgetMax;
	public float  DebugSpawnInterval => EnemySpawner?.SpawnInterval ?? 0f;
	public int    DebugMaxEnemies    => EnemySpawner?.MaxEnemies ?? 0;
	public bool   DebugHordeActive   => hordeActive;
	public float  DebugHordeTimer    => hordeTimer;
	public float  DebugTankCD        => System.Math.Max( 0f, TankCooldown    - tankTimer    );
	public float  DebugHunterCD      => System.Math.Max( 0f, HunterCooldown  - hunterTimer  );
	public float  DebugSpitterCD     => System.Math.Max( 0f, SpitterCooldown - spitterTimer );
	public float  DebugChargerCD     => System.Math.Max( 0f, ChargerCooldown - chargerTimer );
	public float  DebugStressScore   => stressScore;
	public float  DebugMomentumScore => momentumScore;
	public float  DebugDamageRate    => smoothedDamageRate;
	public float  DebugKillRate      => smoothedKillRate;

	// ── Private state ──────────────────────────────────────────────────
	private enum Phase { Rest, BuildUp, Peak, Relent }
	private Phase currentPhase;
	private float phaseTimer;
	private float phaseDuration;

	private float currentBudget;

	private class EnemyRecord
	{
		public float   Cost;
		public bool    IsHorde;
		public bool    IsSpecial;
		public float   RefundOnDeath;   // absolute value to refund
		public float   SpawnTime;
	}
	private readonly Dictionary<System.Guid, EnemyRecord> tracked = new();

	private float tankTimer    = 999f;
	private float hunterTimer  = 999f;
	private float spitterTimer = 999f;
	private float chargerTimer = 999f;

	private bool  hordeActive;
	private float hordeTimer;
	private bool  hordeScheduled;
	private float savedInterval;
	private int   savedBatchSize;
	private int   savedMaxEnemies;

	private bool  objectivePressureActive;
	private float objectivePressure01;
	private bool  objectiveHordeActive;
	private bool  objectiveHordeBoostApplied;

	private float calmTimer;
	private bool  calmBonusReady = true;

	private float softDespawnTimer;

	private float pendingDamageThisFrame;
	private float smoothedDamageRate;
	private float smoothedKillRate;
	private int previousTeamKills;
	private float stressScore;
	private float momentumScore;

	protected override void OnAwake()
	{
		if ( !IsAuthoritativeInstance() )
			return;

		currentBudget = 5f; // seed budget so spawns can begin immediately
		tracked.Clear();
		previousTeamKills = GetTeamTotalKills();
		pendingDamageThisFrame = 0f;
		smoothedDamageRate = 0f;
		smoothedKillRate = 0f;
		stressScore = 0f;
		momentumScore = 0f;

		// Auto-find spawner references in case inspector assignments were lost on hot-reload
		if ( EnemySpawner == null )
			EnemySpawner = Scene.GetAllComponents<EnemySpawner>().FirstOrDefault();

		if ( TankSpawner == null )
			TankSpawner = Scene.GetAllComponents<TankSpawner>().FirstOrDefault();

		if ( HunterSpawner == null )
			HunterSpawner = Scene.GetAllComponents<HunterSpawner>().FirstOrDefault();

		if ( SpitterSpawner == null )
			SpitterSpawner = Scene.GetAllComponents<SpitterSpawner>().FirstOrDefault();

		if ( ChargerSpawner == null )
			ChargerSpawner = Scene.GetAllComponents<ChargerSpawner>().FirstOrDefault();

		// PlayerIdentity registers in OnStart which fires after OnAwake — subscribe to the event so
		// we wire health events and set Player as soon as the identity is available.
		PlayerIdentity.OnPlayerRegistered += OnIdentityRegistered;
		// Also handle the case where PlayerIdentity already existed (e.g. editor hot-reload)
		if ( PlayerIdentity.Local != null ) OnIdentityRegistered( PlayerIdentity.Local );

		SafeRoom.OnPlayerEntered += OnSafeRoomEntered;
		SafeRoom.OnPlayerExited  += OnSafeRoomExited;

		if ( EnemySpawner != null )
		{
			EnemySpawner.OnBeforeSpawn  = ( cost ) => TrySpendBudget( cost );
			EnemySpawner.OnEnemySpawned = ( go )   => TrackEnemy( go, EnemySpawner.CurrentSpawnCost );
			EnemySpawner.OnEnemyRemoved = ( go, softDespawn ) => HandleEnemyRemoved( go, softDespawn );
			EnemySpawner.OnSpawnFailed  = ( cost ) => RefundBudget( cost * RefundFailedSpawn );
		}
		else
		{
			Log.Warning( "AIDirector: No EnemySpawner found in scene!" );
		}

		EnterPhase( Phase.Rest );
		Log.Info( "AIDirector: Budget system started" );
	}

	void OnIdentityRegistered( PlayerIdentity identity )
	{
		if ( Player == null ) Player = identity.GameObject;
		// Use the cached Health ref from PlayerIdentity rather than searching the hierarchy
		if ( identity.Health != null )
			identity.Health.OnDamageTaken += OnPlayerDamaged;
	}

	protected override void OnUpdate()
	{
		if ( !IsAuthoritativeInstance() )
			return;

		// Fallback: set Player ref if it still isn't wired (timing edge case)
		if ( Player == null )
			Player = PlayerIdentity.Local?.GameObject;

		if ( Player == null || EnemySpawner == null ) return;

		float dt = System.Math.Max( Time.Delta, 0.0001f );
		float damageLerp = System.Math.Clamp( Time.Delta * DamageRateSmoothing, 0f, 1f );
		float killLerp = System.Math.Clamp( Time.Delta * KillRateSmoothing, 0f, 1f );
		float instDamageRate = pendingDamageThisFrame / dt;
		pendingDamageThisFrame = 0f;
		smoothedDamageRate = MathX.Lerp( smoothedDamageRate, instDamageRate, damageLerp );

		int teamKills = GetTeamTotalKills();
		int deltaKills = System.Math.Max( teamKills - previousTeamKills, 0 );
		previousTeamKills = teamKills;
		float instKillRate = deltaKills / dt;
		smoothedKillRate = MathX.Lerp( smoothedKillRate, instKillRate, killLerp );

		UpdateAdaptiveScores();

		phaseTimer      += Time.Delta;
		tankTimer       += Time.Delta;
		hunterTimer     += Time.Delta;
		spitterTimer    += Time.Delta;
		chargerTimer    += Time.Delta;
		softDespawnTimer += Time.Delta;

		// ── Budget income ──────────────────────────────────────────────
		float income = currentPhase switch
		{
			Phase.Rest    => IncomeRest,
			Phase.BuildUp => IncomeBuildUp,
			Phase.Peak    => IncomePeak,
			Phase.Relent  => IncomeRelent,
			_             => 0f
		};

		// Over-stressed: clamp income when enemy count is already near active cap
		int aliveTotal = TotalAliveEnemies();
		int activeCap = hordeActive ? System.Math.Max( ScaledEnemyCap, HordeMaxEnemies ) : ScaledEnemyCap;
		if ( aliveTotal >= activeCap )
			income = 0f;
		else if ( hordeActive )
			income = System.Math.Max( income, objectiveHordeActive ? ObjectiveHordeIncomeFloor : IncomeHordeFloor );

		currentBudget = System.Math.Min( currentBudget + income * Time.Delta, hordeActive ? System.Math.Max( ScaledBudgetMax, HordeBudgetCap ) : ScaledBudgetMax );

		// ── Calm window bonus ──────────────────────────────────────────
		if ( aliveTotal == 0 && SpawnerAliveEnemies() == 0 )
		{
			calmTimer += Time.Delta;
			if ( calmBonusReady && calmTimer >= CalmWindowDuration )
			{
				RefundBudget( CalmWindowBonus );
				calmBonusReady = false;
				Log.Info( $"AIDirector: Calm bonus +{CalmWindowBonus} budget" );
			}
		}
		else
		{
			calmTimer     = 0f;
			calmBonusReady = true;
		}

		// ── Base intensity ─────────────────────────────────────────────
		float baseIntensity = currentPhase switch
		{
			Phase.Rest    => 0f,
			Phase.BuildUp => System.Math.Min( phaseTimer / phaseDuration, 1f ) * 0.5f,
			Phase.Peak    => 0.5f + System.Math.Min( phaseTimer / phaseDuration, 1f ) * 0.5f,
			Phase.Relent  => 0.2f,
			_             => 0f
		};

		float staticStress = StaticEnemyGroup.All.Sum( g => g.CurrentStress );
		float staticContrib = System.Math.Min( staticStress / 10f, 0.4f );
		float adaptiveOffset = stressScore * StressIntensityBoost - momentumScore * MomentumIntensityReduction;
		CurrentIntensity = System.Math.Clamp( baseIntensity + staticContrib + adaptiveOffset, 0f, 1f );

		if ( objectivePressureActive )
		{
			float oi = 0.55f + objectivePressure01 * 0.45f;
			CurrentIntensity = System.Math.Max( CurrentIntensity, oi );
		}

		if ( EnemySpawner.IsSafeZoneActive ) return;

		// ── Mercy relent ───────────────────────────────────────────────
		if ( PlayerStats.IsIncapacitated && currentPhase != Phase.Relent && currentPhase != Phase.Rest )
		{
			Log.Info( "AIDirector: Player downed — mercy relent" );
			if ( hordeActive ) EndHorde();
			EnterPhase( Phase.Relent );
			return;
		}

		// ── Reactive phase advancement ──────────────────────────────────
		// Use spawner-only count so static group encounters don't pollute the Director's phase cycle
		int spawnerAlive = SpawnerAliveEnemies();
		int dynamicEarlyPeakThreshold = System.Math.Max( 3,
			EarlyPeakThreshold
			+ (int)System.MathF.Round( stressScore * StressPeakThresholdBonus )
			- (int)System.MathF.Round( momentumScore * MomentumPeakThresholdPenalty ) );

		int dynamicRelentThreshold = System.Math.Max( 1,
			RelentThreshold
			+ (int)System.MathF.Round( stressScore * StressRelentThresholdBonus )
			- (int)System.MathF.Round( momentumScore * MomentumRelentThresholdPenalty ) );

		AdvancePhaseIfReady( spawnerAlive, dynamicEarlyPeakThreshold, dynamicRelentThreshold );

		// ── Apply spawner settings ─────────────────────────────────────
		if ( !hordeActive )
			ApplyPhaseToSpawner();

		// ── Horde trigger (area-clear window) ─────────────────────────
		if ( hordeScheduled && !hordeActive && currentPhase == Phase.Peak && spawnerAlive <= HordeTriggerCount )
		{
			hordeScheduled = false;
			TriggerHorde();
		}

		// ── Horde tick ─────────────────────────────────────────────────
		if ( hordeActive )
		{
			hordeTimer += Time.Delta;
			if ( !objectiveHordeActive && hordeTimer >= HordeDuration )
				EndHorde();
		}

		// ── Soft despawn (Relent only, throttled 2s) ───────────────────
		if ( currentPhase == Phase.Relent && softDespawnTimer >= 2f )
		{
			softDespawnTimer = 0f;
			EnemySpawner.SoftDespawnDistantEnemies( SoftDespawnDistance );
		}

		HandleSpecialInfected();
	}

	// ── Budget helpers ─────────────────────────────────────────────────

	bool TrySpendBudget( float cost )
	{
		// Cap only counts spawner enemies — static groups are designer-placed and shouldn't block the Director
		int activeCap = hordeActive ? System.Math.Max( ScaledEnemyCap, HordeMaxEnemies ) : ScaledEnemyCap;
		if ( SpawnerAliveEnemies() >= activeCap ) return false;
		if ( currentBudget < cost ) return false;
		currentBudget -= cost;
		return true;
	}

	void RefundBudget( float amount )
	{
		float cap = hordeActive ? System.Math.Max( ScaledBudgetMax, HordeBudgetCap ) : ScaledBudgetMax;
		currentBudget = System.Math.Min( currentBudget + amount, cap );
	}

	void TrackEnemy( GameObject go, float cost )
	{
		if ( go == null ) return;
		var rec = new EnemyRecord
		{
			Cost         = cost,
			IsHorde      = hordeActive,
			SpawnTime    = Time.Now
		};

		// Determine refund on death based on cost tier
		if ( System.Math.Abs( cost - CostTank )    < 0.1f ) rec.RefundOnDeath = RefundTankDeath;
		else if ( System.Math.Abs( cost - CostCharger ) < 0.1f ) rec.RefundOnDeath = RefundChargerDeath;
		else if ( System.Math.Abs( cost - CostHunter )  < 0.1f ) rec.RefundOnDeath = RefundHunterDeath;
		else if ( System.Math.Abs( cost - CostSpitter )  < 0.1f ) rec.RefundOnDeath = RefundSpitterDeath;
		else rec.RefundOnDeath = cost * RefundCommonDeath;

		tracked[go.Id] = rec;
	}

	void HandleEnemyRemoved( GameObject go, bool softDespawn )
	{
		if ( go == null ) return;
		if ( !tracked.TryGetValue( go.Id, out var rec ) ) return;
		tracked.Remove( go.Id );

		if ( softDespawn )
		{
			// Timeout refund: partial refund, less if they actually engaged the player
			var enemy = go.Components.Get<Enemy>();
			bool engaged = enemy?.HasEngaged ?? true; // assume engaged if we can't check
			if ( !engaged )
				RefundBudget( rec.Cost * RefundTimeout );
			// No refund for enemies that engaged — they served their purpose
		}
		else
		{
			// Killed by player — issue death refund
			RefundBudget( rec.RefundOnDeath );
		}
	}

	// ── Phase management ───────────────────────────────────────────────

	void EnterPhase( Phase phase )
	{
		currentPhase   = phase;
		phaseTimer     = 0f;
		hordeScheduled = false;
		EnemySpawner?.ResetDebugCounters();

		switch ( phase )
		{
			case Phase.Rest:
				phaseDuration = Game.Random.Float( RestMin, RestMax );
				Log.Info( $"AIDirector: ▷ REST ({phaseDuration:F0}s) | Budget: {currentBudget:F1}" );
				break;

			case Phase.BuildUp:
				phaseDuration = Game.Random.Float( BuildUpMin, BuildUpMax );
				Log.Info( $"AIDirector: ▷ BUILD UP ({phaseDuration:F0}s) | Budget: {currentBudget:F1}" );
				break;

			case Phase.Peak:
				phaseDuration = Game.Random.Float( PeakMin, PeakMax );
				Log.Info( $"AIDirector: ▷ PEAK ({phaseDuration:F0}s) | Budget: {currentBudget:F1}" );

				if ( Game.Random.Float() < HordeChance )
				{
					hordeScheduled = true;
					Log.Info( "AIDirector: Horde queued — fires when area is cleared" );
				}
				break;

			case Phase.Relent:
				phaseDuration = Game.Random.Float( RelentMin, RelentMax );
				Log.Info( $"AIDirector: ▷ RELENT ({phaseDuration:F0}s) — backing off" );
				break;
		}
	}

	void AdvancePhaseIfReady( int aliveTotal, int earlyPeakThreshold, int relentThreshold )
	{
		// Phase slowdown when static enemies are still active
		float staticStress   = StaticEnemyGroup.All.Sum( g => g.CurrentStress );
		float phaseSlowdown  = 1f + System.Math.Min( staticStress / 15f, 0.5f );
		phaseSlowdown += stressScore * StressPhaseSlowdown;
		phaseSlowdown -= momentumScore * MomentumPhaseSpeedup;
		phaseSlowdown = System.Math.Clamp( phaseSlowdown, 0.65f, 1.85f );
		bool  hardTimerUp    = phaseTimer >= phaseDuration * phaseSlowdown;

		switch ( currentPhase )
		{
			case Phase.Rest:
				if ( hardTimerUp )
					EnterPhase( Phase.BuildUp );
				break;

			case Phase.BuildUp:
				// Reactive: player already surrounded → skip to Peak early
				if ( aliveTotal >= earlyPeakThreshold )
				{
					Log.Info( $"AIDirector: {aliveTotal} enemies alive — early Peak" );
					EnterPhase( Phase.Peak );
				}
				else if ( hardTimerUp )
					EnterPhase( Phase.Peak );
				break;

			case Phase.Peak:
				// Reactive: player cleared the area → earned Relent
				if ( aliveTotal <= relentThreshold && phaseTimer >= 20f )
				{
					Log.Info( $"AIDirector: Area cleared ({aliveTotal} alive) — earned Relent" );
					if ( hordeActive ) EndHorde();
					EnterPhase( Phase.Relent );
				}
				else if ( hardTimerUp )
					EnterPhase( Phase.Relent );
				break;

			case Phase.Relent:
				if ( hardTimerUp )
					EnterPhase( Phase.Rest );
				break;
		}
	}

	/// <summary>All enemies alive near the player — used for global cap to prevent overcrowding.</summary>
	int TotalAliveEnemies()
	{
		int count = EnemySpawner?.GetAliveEnemyCount() ?? 0;
		if ( Player != null )
		{
			foreach ( var group in StaticEnemyGroup.All )
			{
				if ( Vector3.DistanceBetween( group.WorldPosition, Player.WorldPosition ) <= SoftDespawnDistance )
					count += group.AliveCount;
			}
		}
		return count;
	}

	/// <summary>Only Director-spawned enemies — used for phase transitions so static groups don't pollute the cycle.</summary>
	int SpawnerAliveEnemies() => EnemySpawner?.GetAliveEnemyCount() ?? 0;

	// ── Spawner settings per phase ─────────────────────────────────────

	void ApplyPhaseToSpawner()
	{
		if ( objectivePressureActive )
		{
			EnemySpawner.CurrentSpawnCost = CostCommon;
			EnemySpawner.IgnoreNodeCooldown = false;
			EnemySpawner.IgnoreSpawnSpread = false;
			EnemySpawner.SpawnBatchSize   = PeakBatchSize;
			EnemySpawner.SpawnInterval    = MathX.Lerp( 2.8f, 1.0f, objectivePressure01 );
			EnemySpawner.MaxEnemies       = (int)MathX.Lerp( 8f, 16f, objectivePressure01 );
			return;
		}

		EnemySpawner.CurrentSpawnCost = CostCommon;
		EnemySpawner.IgnoreNodeCooldown = false;
		EnemySpawner.IgnoreSpawnSpread = false;
		EnemySpawner.SpawnBatchSize   = 1;

		switch ( currentPhase )
		{
			case Phase.Rest:
				EnemySpawner.SpawnInterval = 10f;
				EnemySpawner.MaxEnemies    = 3;
				break;

			case Phase.BuildUp:
			{
				float t = System.Math.Min( phaseTimer / phaseDuration, 1f );
				EnemySpawner.SpawnBatchSize = BuildUpBatchSize;
				EnemySpawner.SpawnInterval = MathX.Lerp( 7f, 2.5f, t );
				EnemySpawner.MaxEnemies    = (int)MathX.Lerp( 3f, 8f, t );
				break;
			}

			case Phase.Peak:
				EnemySpawner.SpawnBatchSize = PeakBatchSize;
				EnemySpawner.SpawnInterval = 1.5f;
				EnemySpawner.MaxEnemies    = 12;
				break;

			case Phase.Relent:
				EnemySpawner.SpawnInterval = 30f;
				EnemySpawner.MaxEnemies    = 1;
				break;
		}
	}

	// ── Horde event ────────────────────────────────────────────────────

	void TriggerHorde( bool forceObjectiveHorde = false )
	{
		bool useObjectiveHorde = forceObjectiveHorde || objectiveHordeActive;

		if ( hordeActive )
		{
			if ( !useObjectiveHorde ) return;

			EnemySpawner.CurrentSpawnCost = CostHorde;
			EnemySpawner.IgnoreNodeCooldown = true;
			EnemySpawner.IgnoreSpawnSpread = true;
			EnemySpawner.SpawnInterval = ObjectiveHordeSpawnInterval;
			EnemySpawner.SpawnBatchSize = System.Math.Max( ObjectiveHordeBatchSize, 1 );
			EnemySpawner.MaxEnemies = System.Math.Max( EnemySpawner.MaxEnemies, ObjectiveHordeMaxEnemies );

			if ( !objectiveHordeBoostApplied )
			{
				RefundBudget( ObjectiveHordeBudgetInjection );
				objectiveHordeBoostApplied = true;
			}

			return;
		}

		hordeActive = true;
		hordeTimer  = 0f;

		savedInterval   = EnemySpawner.SpawnInterval;
		savedBatchSize  = EnemySpawner.SpawnBatchSize;
		savedMaxEnemies = EnemySpawner.MaxEnemies;

		EnemySpawner.CurrentSpawnCost = CostHorde;
		EnemySpawner.IgnoreNodeCooldown = true;
		EnemySpawner.IgnoreSpawnSpread = true;
		if ( useObjectiveHorde )
		{
			EnemySpawner.SpawnInterval = ObjectiveHordeSpawnInterval;
			EnemySpawner.SpawnBatchSize = System.Math.Max( ObjectiveHordeBatchSize, 1 );
			EnemySpawner.MaxEnemies = ObjectiveHordeMaxEnemies;
			RefundBudget( ObjectiveHordeBudgetInjection );
			objectiveHordeBoostApplied = true;
		}
		else
		{
			EnemySpawner.SpawnInterval = HordeSpawnInterval;
			EnemySpawner.SpawnBatchSize = System.Math.Max( HordeBatchSize, 1 );
			EnemySpawner.MaxEnemies = HordeMaxEnemies;
			RefundBudget( HordeBudgetInjection );
			objectiveHordeBoostApplied = false;
		}

		Log.Info( "AIDirector: ★ HORDE EVENT ★" );
	}

	void EndHorde()
	{
		hordeActive = false;
		hordeTimer  = 0f;

		EnemySpawner.SpawnInterval = savedInterval;
		EnemySpawner.SpawnBatchSize = savedBatchSize;
		EnemySpawner.MaxEnemies    = savedMaxEnemies;
		EnemySpawner.CurrentSpawnCost = CostCommon;
		EnemySpawner.IgnoreNodeCooldown = false;
		EnemySpawner.IgnoreSpawnSpread = false;
		objectiveHordeBoostApplied = false;

		Log.Info( "AIDirector: Horde ended" );
	}

	// ── Special infected placement ─────────────────────────────────────

	void HandleSpecialInfected()
	{
		// Tank: Peak only, one at a time, budget-gated
		if ( currentPhase == Phase.Peak && TankSpawner != null )
		{
			if ( tankTimer >= TankCooldown && !TankSpawner.HasAliveTank() && currentBudget >= CostTank )
			{
				if ( TankSpawner.TrySpawnTank() )
				{
					currentBudget -= CostTank;
					tankTimer = 0f;
					Log.Info( "AIDirector: Tank spawned" );
				}
			}
		}

		// Hunter: BuildUp + Peak, budget-gated
		if ( (currentPhase == Phase.BuildUp || currentPhase == Phase.Peak) && HunterSpawner != null )
		{
			if ( hunterTimer >= HunterCooldown && HunterSpawner.GetAliveHunterCount() < HunterSpawner.MaxHunters && currentBudget >= CostHunter )
			{
				currentBudget -= CostHunter;
				hunterTimer = 0f;
				HunterSpawner.Enabled = true;
				Log.Info( $"AIDirector: Hunter triggered during {currentPhase}" );
			}
		}

		// Spitter: BuildUp + Peak, budget-gated
		if ( (currentPhase == Phase.BuildUp || currentPhase == Phase.Peak) && SpitterSpawner != null )
		{
			if ( spitterTimer >= SpitterCooldown && SpitterSpawner.GetAliveSpitterCount() < SpitterSpawner.MaxSpitters && currentBudget >= CostSpitter )
			{
				if ( SpitterSpawner.TrySpawnSpitter() )
				{
					currentBudget -= CostSpitter;
					spitterTimer = 0f;
					Log.Info( $"AIDirector: Spitter spawned during {currentPhase}" );
				}
			}
		}

		// Charger: Peak only, budget-gated
		if ( currentPhase == Phase.Peak && ChargerSpawner != null )
		{
			if ( chargerTimer >= ChargerCooldown && ChargerSpawner.GetAliveChargerCount() < ChargerSpawner.MaxChargers && currentBudget >= CostCharger )
			{
				if ( ChargerSpawner.TrySpawnCharger() )
				{
					currentBudget -= CostCharger;
					chargerTimer = 0f;
					Log.Info( $"AIDirector: Charger spawned during {currentPhase}" );
				}
			}
		}
	}

	// ── Event handlers ─────────────────────────────────────────────────

	void OnPlayerDamaged( float damage )
	{
		pendingDamageThisFrame += System.Math.Max( damage, 0f );

		if ( currentPhase == Phase.BuildUp && PlayerStats.CurrentHealth < PlayerStats.HealthMax * 0.25f )
		{
			float remaining = phaseDuration - phaseTimer;
			if ( remaining > 10f )
			{
				phaseDuration = phaseTimer + 10f;
				Log.Info( "AIDirector: Player in danger — accelerating to Peak" );
			}
		}
	}

	void UpdateAdaptiveScores()
	{
		var players = PlayerIdentity.All;

		if ( players == null || players.Count == 0 )
		{
			float healthPct = PlayerStats.HealthMax > 0f
				? System.Math.Clamp( PlayerStats.CurrentHealth / PlayerStats.HealthMax, 0f, 1f )
				: 1f;
			float staminaPct = PlayerStats.StaminaMax > 0f
				? System.Math.Clamp( PlayerStats.CurrentStamina / PlayerStats.StaminaMax, 0f, 1f )
				: 1f;
			float lowAmmo = (PlayerStats.CurrentAmmo + PlayerStats.ReserveAmmo) <= LowAmmoThreshold ? 1f : 0f;
			float downed = PlayerStats.IsIncapacitated ? 1f : 0f;

			float healthStress = 1f - healthPct;
			float staminaStress = 1f - staminaPct;
			float damageStress = System.Math.Clamp( smoothedDamageRate / System.Math.Max( HighDamageRateForStress, 1f ), 0f, 1f );
			float killNorm = System.Math.Clamp( smoothedKillRate / System.Math.Max( StrongKillRatePerSecond, 0.01f ), 0f, 1f );

			stressScore = System.Math.Clamp( healthStress * 0.35f + staminaStress * 0.15f + downed * 0.25f + lowAmmo * 0.1f + damageStress * 0.15f, 0f, 1f );
			momentumScore = System.Math.Clamp( killNorm * 0.45f + (1f - healthStress) * 0.25f + (1f - lowAmmo) * 0.15f + (1f - downed) * 0.15f, 0f, 1f );
			return;
		}

		int playerCount = players.Count;
		float totalHealthPct = 0f;
		float totalStaminaPct = 0f;
		float totalAmmo = 0f;
		float totalShotsFired = 0f;
		float totalShotsHit = 0f;
		int downedCount = 0;
		int deadCount = 0;

		foreach ( var p in players )
		{
			float hMax = System.Math.Max( p.HealthMax, 1f );
			float sMax = System.Math.Max( p.StaminaMax, 1f );
			totalHealthPct += System.Math.Clamp( p.CurrentHealth / hMax, 0f, 1f );
			totalStaminaPct += System.Math.Clamp( p.CurrentStamina / sMax, 0f, 1f );
			totalAmmo += p.CurrentAmmo + p.ReserveAmmo;
			totalShotsFired += System.Math.Max( p.ShotsFired, 0 );
			totalShotsHit += System.Math.Max( p.ShotsHit, 0 );
			if ( p.IsIncapacitated ) downedCount++;
			if ( p.IsDead ) deadCount++;
		}

		float avgHealthPct = totalHealthPct / playerCount;
		float avgStaminaPct = totalStaminaPct / playerCount;
		float avgAmmo = totalAmmo / playerCount;
		float downedRatio = (float)downedCount / playerCount;
		float deadRatio = (float)deadCount / playerCount;
		float lowAmmoRatio = avgAmmo <= LowAmmoThreshold ? 1f : 0f;

		float accuracyNorm = totalShotsFired > 0f
			? System.Math.Clamp( totalShotsHit / totalShotsFired, 0f, 1f )
			: 0.5f;

		float cohesionStress = 0f;
		if ( players.Count > 1 )
		{
			float pairDistTotal = 0f;
			int pairCount = 0;
			for ( int i = 0; i < players.Count; i++ )
			{
				for ( int j = i + 1; j < players.Count; j++ )
				{
					pairDistTotal += Vector3.DistanceBetween( players[i].GameObject.WorldPosition, players[j].GameObject.WorldPosition );
					pairCount++;
				}
			}
			if ( pairCount > 0 )
			{
				float avgPairDist = pairDistTotal / pairCount;
				cohesionStress = System.Math.Clamp( avgPairDist / System.Math.Max( CohesionDistanceTarget, 1f ), 0f, 1f );
			}
		}

		float healthStressTeam = 1f - avgHealthPct;
		float staminaStressTeam = 1f - avgStaminaPct;
		float damageStressTeam = System.Math.Clamp( smoothedDamageRate / System.Math.Max( HighDamageRateForStress, 1f ), 0f, 1f );
		float killNormTeam = System.Math.Clamp( smoothedKillRate / System.Math.Max( StrongKillRatePerSecond, 0.01f ), 0f, 1f );

		stressScore = System.Math.Clamp(
			healthStressTeam * 0.28f
			+ staminaStressTeam * 0.10f
			+ downedRatio * 0.22f
			+ deadRatio * 0.18f
			+ lowAmmoRatio * 0.10f
			+ cohesionStress * 0.06f
			+ damageStressTeam * 0.06f,
			0f, 1f );

		momentumScore = System.Math.Clamp(
			killNormTeam * 0.36f
			+ avgHealthPct * 0.20f
			+ avgStaminaPct * 0.10f
			+ (1f - lowAmmoRatio) * 0.10f
			+ (1f - downedRatio) * 0.10f
			+ (1f - deadRatio) * 0.08f
			+ accuracyNorm * 0.06f,
			0f, 1f );
	}

	int GetTeamTotalKills()
	{
		var players = PlayerIdentity.All;
		if ( players == null || players.Count == 0 )
			return PlayerStats.TotalKills;

		int total = 0;
		foreach ( var p in players )
			total += System.Math.Max( p.TotalKills, 0 );
		return total;
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

	public void SetObjectivePressure( float pressure01 )
	{
		objectivePressureActive = true;
		objectivePressure01 = System.Math.Clamp( pressure01, 0f, 1f );
	}

	/// <summary>
	/// Fires an immediate horde-style surge that runs for the duration of the objective.
	/// Call from ObjectiveEvent when the objective begins.
	/// </summary>
	public void TriggerObjectiveHorde()
	{
		objectiveHordeActive = true;
		TriggerHorde( true );
		Log.Info( "AIDirector: Objective horde triggered" );
	}

	public void ClearObjectivePressure()
	{
		objectivePressureActive = false;
		objectivePressure01 = 0f;

		if ( objectiveHordeActive )
		{
			objectiveHordeActive = false;
			if ( hordeActive ) EndHorde();
			// Objective completed — reward the player with a Relent phase
			EnterPhase( Phase.Relent );
			Log.Info( "AIDirector: Objective complete — entering Relent" );
		}
	}

	[ConCmd( "director_debug" )]
	public static void ToggleDirectorDebug()
	{
		PlayerStats.ShowDirectorDebug = !PlayerStats.ShowDirectorDebug;
		Log.Info( $"[Director] Debug HUD {(PlayerStats.ShowDirectorDebug ? "ON" : "OFF")}" );
	}

	protected override void OnDestroy()
	{
		if ( hordeActive ) EndHorde();

		PlayerIdentity.OnPlayerRegistered -= OnIdentityRegistered;

		var playerHealth = PlayerIdentity.Local?.Health
			?? Player?.Components.GetInDescendantsOrSelf<HealthComponent>();
		if ( playerHealth != null )
			playerHealth.OnDamageTaken -= OnPlayerDamaged;

		SafeRoom.OnPlayerEntered -= OnSafeRoomEntered;
		SafeRoom.OnPlayerExited  -= OnSafeRoomExited;
	}
}
