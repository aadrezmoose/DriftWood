using Sandbox;

/// <summary>
/// Plays a looping ambient sound when the scene starts.
/// Assign a SoundEvent with Loop enabled in the asset browser.
/// Add this component to any GameObject in the scene.
/// </summary>
public sealed class AmbientSoundLoop : Component
{
	[Property] public SoundEvent AmbientSound { get; set; }
	[Property] public float Volume { get; set; } = 1f;

	private SoundHandle handle;
	private bool isPlaying = false;

	protected override void OnStart()
	{
		PlaySound();
	}

	protected override void OnUpdate()
	{
		if ( AmbientSound == null ) return;
		if ( isPlaying )
		{
			// Detect when the sound finishes so we can restart it
			try { if ( !handle.IsPlaying ) isPlaying = false; }
			catch { isPlaying = false; }
		}
		else
		{
			PlaySound();
		}
	}

	protected override void OnDestroy()
	{
		if ( isPlaying ) handle.Stop();
		isPlaying = false;
	}

	private void PlaySound()
	{
		if ( AmbientSound == null ) return;
		handle = Sound.Play( AmbientSound );
		handle.Volume = Volume;
		isPlaying = true;
	}
}
