using Sandbox;

/// <summary>
/// Baseball bat - moderate damage and swing speed.
/// Balanced melee weapon for general use.
/// </summary>
public sealed class Bat : MeleeWeapon
{
	[Property] public SoundEvent SwingSound { get; set; }
	[Property] public SoundEvent HitSound { get; set; }

	protected override void OnAwake()
	{
		// Set bat-specific stats
		Damage = 40f;            // One-shot regular enemies (100 HP with headshot consideration)
		Range = 120f;            // Medium melee range
		SwingSpeed = 0.5f;       // Moderate swing speed - 2 swings per second
		KnockbackForce = 600f;   // Good knockback
		SphereRadius = 15f;      // Standard hit detection radius

		base.OnAwake();

		Log.Info( "Bat initialized with Damage=40, SwingSpeed=0.5s" );
	}
}
