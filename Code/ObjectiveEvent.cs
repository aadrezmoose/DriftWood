using Sandbox;
using System.Collections.Generic;

/// <summary>
/// ObjectiveEvent — simple holdout objective trigger.
///
/// Place this on a trigger GameObject to start an objective when the player enters it.
/// During the objective, AI Director pressure ramps up over time.
/// </summary>
public sealed class ObjectiveEvent : Component, Component.ITriggerListener
{
	public static GameObject ActiveLureTarget { get; private set; }
	public static float ActiveLureRadius { get; private set; }
	public static float ActiveLureMaxEnemyDistanceFromPlayer { get; private set; }
	public static float ActiveLurePlayerOverrideDistance { get; private set; }
	private static ObjectiveEvent activeLureOwner;

	[Property] public AIDirector Director { get; set; }
	[Property] public bool OneShot { get; set; } = true;
	[Property] public bool StartOnTrigger { get; set; } = true;
	[Property] public bool AutoStartOnAwake { get; set; } = false;
	[Property] public bool TriggerHordeOnStart { get; set; } = true;
	[Property] public bool EnableObjectiveLure { get; set; } = true;
	[Property] public GameObject ObjectiveLureTarget { get; set; }
	[Property] public float ObjectiveLureRadius { get; set; } = 1600f;
	[Property] public float ObjectiveLureMaxEnemyDistanceFromPlayer { get; set; } = 1200f;
	[Property] public float ObjectiveLurePlayerOverrideDistance { get; set; } = 450f;
	[Property] public float HoldDuration { get; set; } = 45f;
	[Property] public float MoveDuration { get; set; } = 6f;
	[Property] public string HoldMessage { get; set; } = "Objective: Hold this area";
	[Property] public string MovingMessage { get; set; } = "Crane moving container...";
	[Property] public string CompleteMessage { get; set; } = "Path clear. Move forward.";
	[Property] public bool ClearObjectiveUiOnComplete { get; set; } = true;
	[Property] public float CompleteMessageDuration { get; set; } = 2.5f;
	[Property] public bool ShowHudTimer { get; set; } = false;
	[Property] public bool ShowHudProgressBar { get; set; } = false;
	[Property] public SoundEvent MoveStartSound { get; set; }
	[Property] public SoundEvent MoveLoopSound { get; set; }
	[Property] public float MoveLoopVolume { get; set; } = 1f;
	[Property] public float MoveLoopStartDelay { get; set; } = 0f;
	[Property] public SoundEvent MoveEndSound { get; set; }
	[Property] public float MoveEndPitchJitter { get; set; } = 0.03f;
	[Property] public float MoveShakeMagnitude { get; set; } = 0.18f;
	[Property] public float MoveShakePulseInterval { get; set; } = 0.35f;

	[Property] public GameObject BlockingContainer { get; set; }
	[Property] public Vector3 ContainerMoveOffset { get; set; } = new Vector3( 0f, 0f, 200f );
	[Property] public bool DisableContainerCollisionWhileMoving { get; set; } = true;
	[Property] public bool KeepContainerCollisionDisabledAfterMove { get; set; } = true;

	/// <summary>
	/// Optional solid gate/brush that blocks the exit until the objective completes.
	/// Assign an invisible solid brush placed at the passage exit in Hammer.
	/// It is disabled when CompleteObjective() fires.
	/// </summary>
	[Property] public GameObject CompletionGate { get; set; }

	/// <summary>
	/// Optional visual object (crane hook/arm) to move with the container.
	/// </summary>
	[Property] public GameObject CraneVisual { get; set; }
	[Property] public Vector3 CraneMoveOffset { get; set; } = new Vector3( 0f, 180f, 0f );

	private enum ObjectiveStage
	{
		Idle,
		Holding,
		Moving
	}

	private ObjectiveStage stage = ObjectiveStage.Idle;
	private float holdRemaining;
	private float moveElapsed;
	private bool isCompleted;
	private float completeUiTimer = -1f;
	private float shakePulseTimer;
	private float pendingMoveLoopDelay = -1f;
	private SoundHandle moveLoopHandle;
	private bool moveLoopPlaying;

	private Vector3 containerStartPos;
	private Vector3 craneStartPos;
	private readonly List<(Collider collider, bool wasEnabled)> cachedContainerColliders = new();

	protected override void OnAwake()
	{
		if ( Director == null )
			Director = Scene.Components.Get<AIDirector>();

		if ( BlockingContainer != null )
			containerStartPos = BlockingContainer.WorldPosition;

		if ( CraneVisual != null )
			craneStartPos = CraneVisual.WorldPosition;

		if ( AutoStartOnAwake )
			BeginObjective();
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !StartOnTrigger ) return;
		if ( isCompleted && OneShot ) return;
		if ( stage != ObjectiveStage.Idle ) return;

		var player = other.GameObject.Components.GetInAncestorsOrSelf<PlayerMovement>();
		if ( player == null ) return;

		BeginObjective();
	}

	public void OnTriggerExit( Collider other ) { }

	public bool TryStartFromButton()
	{
		if ( isCompleted && OneShot ) return false;
		if ( stage != ObjectiveStage.Idle ) return false;

		BeginObjective();
		return true;
	}

	protected override void OnUpdate()
	{
		if ( completeUiTimer >= 0f )
		{
			completeUiTimer -= Time.Delta;
			if ( completeUiTimer <= 0f )
			{
				completeUiTimer = -1f;
				if ( ClearObjectiveUiOnComplete )
				{
					PlayerStats.ObjectiveText = string.Empty;
					PlayerStats.ObjectiveProgress01 = -1f;
					PlayerStats.ObjectiveUrgent = false;
				}
			}
		}

		if ( stage == ObjectiveStage.Idle ) return;

		if ( stage == ObjectiveStage.Holding )
		{
			holdRemaining -= Time.Delta;
			float duration = System.Math.Max( HoldDuration, 0.01f );
			float progress = 1f - (holdRemaining / duration);
			progress = System.Math.Clamp( progress, 0f, 1f );

			Director?.SetObjectivePressure( progress );

			int secondsLeft = (int)System.MathF.Ceiling( System.Math.Max( holdRemaining, 0f ) );
			PlayerStats.ObjectiveText = ShowHudTimer ? $"{HoldMessage} ({secondsLeft}s)" : HoldMessage;
			PlayerStats.ObjectiveProgress01 = ShowHudProgressBar ? progress : -1f;
			PlayerStats.ObjectiveUrgent = ShowHudTimer && secondsLeft <= 10;

			if ( holdRemaining <= 0f )
				BeginMovingStage();

			return;
		}

		if ( stage == ObjectiveStage.Moving )
		{
			moveElapsed += Time.Delta;

			if ( !moveLoopPlaying && pendingMoveLoopDelay >= 0f )
			{
				pendingMoveLoopDelay -= Time.Delta;
				if ( pendingMoveLoopDelay <= 0f )
				{
					StartMoveLoopSound();
					pendingMoveLoopDelay = -1f;
				}
			}

			// Re-trigger loop sound if it stopped (sound asset may not be set to loop)
			if ( moveLoopPlaying && MoveLoopSound != null )
			{
				bool stillPlaying = false;
				try { stillPlaying = moveLoopHandle.IsPlaying; } catch { }
				if ( !stillPlaying )
				{
					moveLoopPlaying = false;
					StartMoveLoopSound();
				}
			}

			shakePulseTimer -= Time.Delta;
			if ( shakePulseTimer <= 0f )
			{
				PulseMovingShake();
				shakePulseTimer = System.Math.Max( MoveShakePulseInterval, 0.05f );
			}

			float duration = System.Math.Max( MoveDuration, 0.01f );
			float progress = System.Math.Clamp( moveElapsed / duration, 0f, 1f );
			float easedProgress = progress * progress; // ease-in: slow start, rises fast at end

			if ( BlockingContainer != null )
			{
				var target = containerStartPos + ContainerMoveOffset;
				BlockingContainer.WorldPosition = Vector3.Lerp( containerStartPos, target, easedProgress );
			}

			if ( CraneVisual != null )
			{
				var target = craneStartPos + CraneMoveOffset;
				CraneVisual.WorldPosition = Vector3.Lerp( craneStartPos, target, easedProgress );
			}

			Director?.SetObjectivePressure( 1f );

			int secondsLeft = (int)System.MathF.Ceiling( System.Math.Max( duration - moveElapsed, 0f ) );
			PlayerStats.ObjectiveText = ShowHudTimer ? $"{MovingMessage} ({secondsLeft}s)" : MovingMessage;
			PlayerStats.ObjectiveProgress01 = ShowHudProgressBar ? progress : -1f;
			PlayerStats.ObjectiveUrgent = ShowHudTimer && secondsLeft <= 3;

			if ( progress >= 1f )
				CompleteObjective();
		}
	}

	private void BeginMovingStage()
	{
		stage = ObjectiveStage.Moving;
		moveElapsed = 0f;

		if ( BlockingContainer != null )
			containerStartPos = BlockingContainer.WorldPosition;

		if ( CraneVisual != null )
			craneStartPos = CraneVisual.WorldPosition;

		if ( DisableContainerCollisionWhileMoving )
			SetContainerCollisionEnabled( false );

		if ( MoveStartSound != null )
		{
			var soundPos = BlockingContainer?.WorldPosition ?? WorldPosition;
			Sound.Play( MoveStartSound, soundPos );
		}

		pendingMoveLoopDelay = System.Math.Max( MoveLoopStartDelay, 0f );
		if ( pendingMoveLoopDelay <= 0f )
		{
			StartMoveLoopSound();
			pendingMoveLoopDelay = -1f;
		}

		PulseMovingShake();
		shakePulseTimer = System.Math.Max( MoveShakePulseInterval, 0.05f );

		PlayerStats.ObjectiveText = MovingMessage;
		PlayerStats.ObjectiveProgress01 = 0f;
		PlayerStats.ObjectiveUrgent = false;

		Director?.SetObjectivePressure( 1f );
		Log.Info( "ObjectiveEvent: Entered moving stage" );
	}

	private void BeginObjective()
	{
		stage = ObjectiveStage.Holding;
		isCompleted = false;
		holdRemaining = System.Math.Max( HoldDuration, 1f );
		moveElapsed = 0f;

		if ( BlockingContainer != null )
			BlockingContainer.WorldPosition = containerStartPos;

		if ( CraneVisual != null )
			CraneVisual.WorldPosition = craneStartPos;

		PlayerStats.ObjectiveText = HoldMessage;
		PlayerStats.ObjectiveProgress01 = 0f;
		PlayerStats.ObjectiveUrgent = false;

		if ( EnableObjectiveLure )
			ActivateObjectiveLure();

		Director?.SetObjectivePressure( 0f );
		if ( TriggerHordeOnStart )
			Director?.TriggerObjectiveHorde();
		Log.Info( $"ObjectiveEvent: Started '{HoldMessage}' hold for {holdRemaining:F0}s" );
	}

	private void CompleteObjective()
	{
		stage = ObjectiveStage.Idle;
		isCompleted = true;
		DeactivateObjectiveLure();
		completeUiTimer = ClearObjectiveUiOnComplete ? System.Math.Max( CompleteMessageDuration, 0f ) : -1f;

		if ( CompletionGate != null )
			CompletionGate.Enabled = false;

		if ( DisableContainerCollisionWhileMoving && !KeepContainerCollisionDisabledAfterMove )
			SetContainerCollisionEnabled( true );

		pendingMoveLoopDelay = -1f;
		StopMoveLoopSound();
		if ( MoveEndSound != null )
		{
			var soundPos = BlockingContainer?.WorldPosition ?? WorldPosition;
			try
			{
				var handle = Sound.Play( MoveEndSound, soundPos );
				handle.Pitch = 1f + Game.Random.Float( -MoveEndPitchJitter, MoveEndPitchJitter );
			}
			catch { }
		}

		Director?.ClearObjectivePressure();

		PlayerStats.ObjectiveText = CompleteMessage;
		PlayerStats.ObjectiveProgress01 = -1f;
		PlayerStats.ObjectiveUrgent = false;

		Log.Info( "ObjectiveEvent: Completed" );
	}

	protected override void OnDestroy()
	{
		DeactivateObjectiveLure();

		if ( stage != ObjectiveStage.Idle )
			Director?.ClearObjectivePressure();

		pendingMoveLoopDelay = -1f;
		StopMoveLoopSound();

		if ( DisableContainerCollisionWhileMoving && !KeepContainerCollisionDisabledAfterMove )
			SetContainerCollisionEnabled( true );
	}

	private void SetContainerCollisionEnabled( bool enabled )
	{
		if ( BlockingContainer == null ) return;

		if ( !enabled )
		{
			cachedContainerColliders.Clear();
			CacheAndSetContainerCollidersRecursive( BlockingContainer, false );
			return;
		}

		foreach ( var entry in cachedContainerColliders )
		{
			if ( entry.collider != null )
				entry.collider.Enabled = entry.wasEnabled;
		}

		cachedContainerColliders.Clear();
	}

	private void CacheAndSetContainerCollidersRecursive( GameObject go, bool enabled )
	{
		foreach ( var collider in go.Components.GetAll<Collider>() )
		{
			cachedContainerColliders.Add( (collider, collider.Enabled) );
			collider.Enabled = enabled;
		}

		foreach ( var child in go.Children )
			CacheAndSetContainerCollidersRecursive( child, enabled );
	}

	private void PulseMovingShake()
	{
		if ( MoveShakeMagnitude <= 0f ) return;

		foreach ( var cam in Scene.GetAllComponents<CameraMovement>() )
			cam?.Shake( MoveShakeMagnitude );
	}

	private void StartMoveLoopSound()
	{
		if ( MoveLoopSound == null ) return;
		if ( moveLoopPlaying ) return;

		var soundPos = BlockingContainer?.WorldPosition ?? WorldPosition;
		moveLoopHandle = Sound.Play( MoveLoopSound, soundPos );
		moveLoopHandle.Volume = MoveLoopVolume;
		moveLoopPlaying = true;
	}

	private void StopMoveLoopSound()
	{
		if ( !moveLoopPlaying ) return;

		try { moveLoopHandle.Stop(); } catch { }
		moveLoopPlaying = false;
	}

	private void ActivateObjectiveLure()
	{
		var target = ObjectiveLureTarget ?? BlockingContainer ?? CraneVisual ?? GameObject;
		if ( target == null ) return;

		ActiveLureTarget = target;
		ActiveLureRadius = System.Math.Max( ObjectiveLureRadius, 0f );
		ActiveLureMaxEnemyDistanceFromPlayer = System.Math.Max( ObjectiveLureMaxEnemyDistanceFromPlayer, 0f );
		ActiveLurePlayerOverrideDistance = System.Math.Max( ObjectiveLurePlayerOverrideDistance, 0f );
		activeLureOwner = this;
	}

	private void DeactivateObjectiveLure()
	{
		if ( activeLureOwner != this ) return;

		ActiveLureTarget = null;
		ActiveLureRadius = 0f;
		ActiveLureMaxEnemyDistanceFromPlayer = 0f;
		ActiveLurePlayerOverrideDistance = 0f;
		activeLureOwner = null;
	}
}
