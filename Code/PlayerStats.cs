public static class PlayerStats
{
    // Exposed for UI. Updated every frame by PlayerMovement.
    public static float CurrentStamina { get; set; } = 0f;
    public static float StaminaMax { get; set; } = 5f;
    // Exposed for UI. Updated by health system (not shown yet).
    public static float CurrentHealth { get; set; } = 100f;
    public static float HealthMax { get; set; } = 100f;
    // Human-readable debug message the HUD can show when something is wrong.
    public static string DebugMessage { get; set; } = string.Empty;
}
