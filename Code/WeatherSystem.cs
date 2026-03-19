using Sandbox;

public enum WeatherPhase { Clear, RainingLight, RainingHeavy }

/// <summary>
/// WeatherSystem - Drives a rain/fog weather cycle independently of the day/night cycle.
/// Cycles through Clear → RainingLight → RainingHeavy → Clear automatically.
/// Reacts to safe room events: entering an end safe room snaps weather to heavy rain.
/// Attach anywhere in the scene.
/// </summary>
public sealed class WeatherSystem : Component
{
	// --- Phase durations ---
	[Property] public float ClearDuration { get; set; } = 120f;
	[Property] public float RainingLightDuration { get; set; } = 60f;
	[Property] public float RainingHeavyDuration { get; set; } = 45f;

	// --- Audio ---
	[Property] public SoundEvent RainSound { get; set; }

	// --- Internal state ---
	private WeatherPhase currentPhase = WeatherPhase.Clear;
	private float phaseTimer = 0f;

	// Rain sound
	private SoundHandle rainHandle;
	private bool raining = false;

	// Triggered by entering an end safe room
	private bool snapToHeavyRain = false;

	protected override void OnStart()
	{
		SafeRoom.OnPlayerEntered += OnPlayerEnteredSafeRoom;

		// Begin in the Clear phase
		EnterPhase( WeatherPhase.Clear );
	}

	protected override void OnUpdate()
	{
		// If the end safe room was reached, override everything and snap to heavy rain.
		if ( snapToHeavyRain )
		{
			EnsureRainPlaying();
			// Do not advance the phase timer while snapped — stay here.
		}
		else
		{
			// Advance phase timer and transition when it expires.
			phaseTimer -= Time.Delta;
			if ( phaseTimer <= 0f )
			{
				WeatherPhase next = currentPhase switch
				{
					WeatherPhase.Clear         => WeatherPhase.RainingLight,
					WeatherPhase.RainingLight  => WeatherPhase.RainingHeavy,
					WeatherPhase.RainingHeavy  => WeatherPhase.Clear,
					_                          => WeatherPhase.Clear,
				};
				EnterPhase( next );
			}
		}

	}

	private void OnPlayerEnteredSafeRoom( SafeRoom room )
	{
		if ( !room.IsEndRoom ) return;

		snapToHeavyRain = true;
		EnsureRainPlaying();
		Log.Info( "WeatherSystem: End safe room reached — snapping to heavy rain." );
	}

	protected override void OnDestroy()
	{
		SafeRoom.OnPlayerEntered -= OnPlayerEnteredSafeRoom;
		if ( raining ) rainHandle.Stop();
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	/// <summary>
	/// Transitions into the given phase: resets the timer, updates fog targets,
	/// and starts/stops rain audio as appropriate.
	/// </summary>
	private void EnterPhase( WeatherPhase phase )
	{
		currentPhase = phase;

		switch ( phase )
		{
			case WeatherPhase.Clear:
				phaseTimer = ClearDuration;
				if ( raining )
				{
					rainHandle.Stop();
					raining = false;
				}
				break;

			case WeatherPhase.RainingLight:
				phaseTimer = RainingLightDuration;
				EnsureRainPlaying();
				break;

			case WeatherPhase.RainingHeavy:
				phaseTimer = RainingHeavyDuration;
				EnsureRainPlaying();
				break;
		}

		Log.Info( $"WeatherSystem: Entering phase {phase}" );
	}

	private void EnsureRainPlaying()
	{
		if ( raining || RainSound == null ) return;
		rainHandle = Sound.Play( RainSound );
		raining = true;
	}
}
