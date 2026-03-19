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
	private bool playing = false;

	protected override void OnStart()
	{
		if ( AmbientSound == null ) return;
		handle = Sound.Play( AmbientSound );
		playing = true;
	}

	protected override void OnDestroy()
	{
		if ( playing ) handle.Stop();
	}
}
