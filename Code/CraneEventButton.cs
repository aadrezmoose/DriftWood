using Sandbox;

/// <summary>
/// Interactable world button that starts an ObjectiveEvent when the player presses Use.
/// </summary>
public sealed class CraneEventButton : Component
{
	[Property] public ObjectiveEvent TargetEvent { get; set; }
	[Property] public string InteractionHint { get; set; } = "Start Crane";
	[Property] public bool OneShot { get; set; } = true;
	[Property] public SoundEvent PressSound { get; set; }
	[Property] public float PressPitchJitter { get; set; } = 0.04f;

	// Idle highlight: slow pulse on the button model so players notice it
	[Property] public Color HighlightColor { get; set; } = new Color( 1.0f, 0.85f, 0.3f ); // warm yellow
	[Property] public float HighlightPulseSpeed { get; set; } = 2.5f;
	[Property] public float HighlightMinBrightness { get; set; } = 1.0f;
	[Property] public float HighlightMaxBrightness { get; set; } = 2.0f;

	private bool hasActivated;

	public bool CanInteract()
	{
		if ( TargetEvent == null ) return false;
		if ( hasActivated && OneShot ) return false;
		return true;
	}

	public string GetInteractHint()
	{
		if ( !CanInteract() ) return string.Empty;
		return InteractionHint;
	}

	public bool TryUse()
	{
		if ( !CanInteract() ) return false;
		if ( TargetEvent == null ) return false;

		bool started = TargetEvent.TryStartFromButton();
		if ( started && PressSound != null )
		{
			try
			{
				var handle = Sound.Play( PressSound, WorldPosition );
				handle.Pitch = 1f + Game.Random.Float( -PressPitchJitter, PressPitchJitter );
			}
			catch { }
		}

		if ( started && OneShot )
			hasActivated = true;

		return started;
	}

	protected override void OnUpdate()
	{
		if ( CanInteract() )
		{
			float pulse = (System.MathF.Sin( Time.Now * HighlightPulseSpeed ) + 1f) * 0.5f;
			float brightness = MathX.Lerp( HighlightMinBrightness, HighlightMaxBrightness, pulse );
			var tint = new Color(
				HighlightColor.r * brightness,
				HighlightColor.g * brightness,
				HighlightColor.b * brightness );
			SetTintRecursive( GameObject, tint );
		}
		else if ( hasActivated )
		{
			SetTintRecursive( GameObject, Color.White );
		}
	}

	private static void SetTintRecursive( GameObject go, Color tint )
	{
		var smr = go.Components.Get<SkinnedModelRenderer>();
		if ( smr != null ) smr.Tint = tint;

		var mr = go.Components.Get<ModelRenderer>();
		if ( mr != null ) mr.Tint = tint;

		foreach ( var child in go.Children )
			SetTintRecursive( child, tint );
	}
}
