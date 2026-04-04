using Sandbox;

/// <summary>
/// Player flashlight — attach to the Camera GameObject.
/// Toggle with the "Flashlight" input action (bind F in Project Settings > Input).
/// </summary>
public sealed class Flashlight : Component
{
	[Property] public float Radius { get; set; } = 2000f;
	[Property] public float ConeInner { get; set; } = 12f;
	[Property] public float ConeOuter { get; set; } = 25f;
	[Property] public Color LightColor { get; set; } = new Color( 4f, 4f, 3.8f ); // warm white, HDR
	[Property] public bool Shadows { get; set; } = true;
	[Property] public bool StartOn { get; set; } = false;

	private SpotLight spotLight;
	private bool isOn;
	private PlayerIdentity _identity;
	private PlayerIdentity Identity => _identity ??= Components.GetInAncestorsOrSelf<PlayerIdentity>();

	protected override void OnAwake()
	{
		spotLight = Components.GetOrCreate<SpotLight>();
		spotLight.Radius    = Radius;
		spotLight.ConeInner = ConeInner;
		spotLight.ConeOuter = ConeOuter;
		spotLight.LightColor = LightColor;
		spotLight.Shadows   = Shadows;

		isOn = StartOn;
		spotLight.Enabled = isOn;

		// Try to resolve Identity immediately
		if ( _identity == null )
			_identity = Components.GetInAncestorsOrSelf<PlayerIdentity>();

		if ( _identity != null )
			_identity.FlashlightOn = isOn;

		Log.Info( $"[Flashlight] OnAwake: Networking.IsActive={Networking.IsActive}, IsProxy={IsProxy}" );
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsActive )
		{
			if ( spotLight != null )
				spotLight.Enabled = false;
			return;
		}

		if ( _identity == null )
			_identity = Components.GetInAncestorsOrSelf<PlayerIdentity>();

		if ( _identity != null )
			ApplyFlashlightState( _identity.FlashlightOn );
	}

	private void ApplyFlashlightState( bool enabled )
	{
		isOn = enabled;
		if ( Identity != null )
			Identity.FlashlightOn = enabled;
		if ( spotLight != null )
			spotLight.Enabled = enabled;
	}

}
