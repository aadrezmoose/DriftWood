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
	}

	protected override void OnUpdate()
	{
		if ( Input.Pressed( "Flashlight" ) )
		{
			isOn = !isOn;
			spotLight.Enabled = isOn;
		}
	}
}
