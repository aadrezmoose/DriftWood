using Sandbox;

/// <summary>
/// Gradually transitions from day to night over the course of the level.
/// Snaps to full night when the player reaches the end safe room.
/// Attach anywhere in the scene.
/// </summary>
public sealed class DayNightCycle : Component
{
	[Property] public float CycleDuration { get; set; } = 600f; // seconds to go from day to night normally

	[Property] public Color DayLightColor { get; set; } = new Color( 1f, 0.95f, 0.8f );
	[Property] public Color NightLightColor { get; set; } = new Color( 0.05f, 0.08f, 0.2f );

	[Property] public Color DaySkyColor { get; set; } = new Color( 0.59f, 0.58f, 0.58f );
	[Property] public Color NightSkyColor { get; set; } = new Color( 0.02f, 0.02f, 0.05f );

	[Property] public float DayBrightness { get; set; } = 3f;
	[Property] public float NightBrightness { get; set; } = 0.1f;

	[Property] public float NightTransitionSpeed { get; set; } = 0.15f; // how fast it snaps to night at end room

	[Property] public Color DaySkyboxTint { get; set; } = Color.White;
	[Property] public Color NightSkyboxTint { get; set; } = new Color( 0.05f, 0.05f, 0.1f );

	[Property] public Color DayFogColor { get; set; } = new Color( 0.55f, 0.55f, 0.55f );
	[Property] public Color NightFogColor { get; set; } = new Color( 0.02f, 0.02f, 0.04f );

	private DirectionalLight light;
	private SkyBox2D skybox;
	private GradientFog fog;
	private float elapsed = 0f;
	private float t = 0f;
	private bool snapToNight = false;

	protected override void OnStart()
	{
		light = Components.Get<DirectionalLight>()
			?? Scene.GetAllComponents<DirectionalLight>().FirstOrDefault();
		if ( light == null )
			Log.Warning( "DayNightCycle: No DirectionalLight found in scene." );

		skybox = Scene.GetAllComponents<SkyBox2D>().FirstOrDefault();
		fog = Scene.GetAllComponents<GradientFog>().FirstOrDefault();

		SafeRoom.OnPlayerEntered += OnPlayerEnteredSafeRoom;
	}

	protected override void OnUpdate()
	{
		if ( light == null ) return;

		// All players: locally compute t based on elapsed time
		if ( snapToNight )
		{
			t = MathX.Lerp( t, 1f, Time.Delta * NightTransitionSpeed );
		}
		else
		{
			elapsed += Time.Delta;
			t = System.Math.Min( elapsed / CycleDuration, 1f );
		}

		light.LightColor = Color.Lerp( DayLightColor * DayBrightness, NightLightColor * NightBrightness, t );
		light.SkyColor = Color.Lerp( DaySkyColor, NightSkyColor, t );

		if ( skybox != null )
			skybox.Tint = Color.Lerp( DaySkyboxTint, NightSkyboxTint, t );

		if ( fog != null )
			fog.Color = Color.Lerp( DayFogColor, NightFogColor, t );
	}

	private void OnPlayerEnteredSafeRoom( SafeRoom room )
	{
		if ( !room.IsEndRoom ) return;
		snapToNight = true;
	}

	protected override void OnDestroy()
	{
		SafeRoom.OnPlayerEntered -= OnPlayerEnteredSafeRoom;
	}
}
