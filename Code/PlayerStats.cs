public static class PlayerStats
{
	// Exposed for UI. Updated every frame by PlayerMovement.
	public static float CurrentStamina { get; set; } = 0f;
	public static float StaminaMax { get; set; } = 5f;

	// Exposed for UI. Updated by HealthComponent.
	public static float CurrentHealth { get; set; } = 100f;
	public static float HealthMax { get; set; } = 100f;

	// Human-readable debug message the HUD can show when something is wrong.
	public static string DebugMessage { get; set; } = string.Empty;

	// Health system events
	public static event System.Action<float> OnDamageTaken;
	public static event System.Action<float> OnHealed;
	public static event System.Action OnPlayerDeath;

	public static void RaiseDamageTaken( float damage ) => OnDamageTaken?.Invoke( damage );
	public static void RaiseHealed( float amount ) => OnHealed?.Invoke( amount );
	public static void RaisePlayerDeath() => OnPlayerDeath?.Invoke();
}
