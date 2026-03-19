using Sandbox;

/// <summary>
/// Fire axe - higher damage but slower swing speed.
/// Heavy melee weapon for maximum damage output.
/// </summary>
public sealed class Axe : MeleeWeapon
{
	[Property] public SoundEvent SwingSound { get; set; }
	[Property] public SoundEvent HitSound { get; set; }

	protected override void OnAwake()
	{
		// Set axe-specific stats
		Damage = 60f;            // Higher damage than bat
		Range = 120f;            // Same range as bat
		SwingSpeed = 0.75f;      // Slower swing speed - ~1.3 swings per second
		KnockbackForce = 800f;   // Heavier knockback than bat
		SphereRadius = 15f;      // Standard hit detection radius

		base.OnAwake();

		Log.Info( "Axe initialized with Damage=60, SwingSpeed=0.75s" );
	}
}
