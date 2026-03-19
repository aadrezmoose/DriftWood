using Sandbox;

/// <summary>
/// SMG - High fire rate, moderate damage, good for close-mid range
/// </summary>
public sealed class SMG : Gun
{
	[Property] public float Range { get; set; } = 3000f;
	[Property] public float HeadshotMultiplier { get; set; } = 1.5f;
	[Property] public float Spread { get; set; } = 0.03f; // Slight inaccuracy
	[Property] public SoundEvent FireSound { get; set; }
	[Property] public SoundEvent ReloadSound { get; set; }

	protected override void OnAwake()
	{
		// Set SMG stats - spray and pray style
		Damage = 12f;
		FireRate = 0.08f; // ~12.5 shots per second
		AmmoClip = 30;
		AmmoReserve = 150;
		ReloadTime = 2.0f;

		base.OnAwake();
	}

	protected override void OnFire()
	{
		if ( playerHead is null )
		{
			Log.Warning( "SMG: No player head reference set!" );
			return;
		}

		// Play fire sound
		if ( FireSound != null )
		{
			Sound.Play( FireSound );
		}

		// Get firing direction with spread
		var startPos = playerHead.WorldPosition;
		var baseDirection = playerHead.WorldRotation.Forward;

		// Add random spread
		float spreadX = Game.Random.Float( -Spread, Spread );
		float spreadY = Game.Random.Float( -Spread, Spread );
		var direction = (baseDirection + playerHead.WorldRotation.Right * spreadX + playerHead.WorldRotation.Up * spreadY).Normal;

		// Perform raycast
		var trace = Scene.Trace.Ray( startPos, startPos + direction * Range )
			.IgnoreGameObject( owner )
			.WithoutTags( "trigger" )
			.Run();

		if ( trace.Hit )
		{
			// Check for headshot
			var headshotZone = trace.GameObject?.Components.Get<HeadshotZone>();
			if ( headshotZone != null )
			{
				var targetEntity = headshotZone.GetTargetEntity();
				if ( targetEntity != null )
				{
					var health = targetEntity.Components.Get<HealthComponent>();
					if ( health != null )
					{
						float headshotDamage = Damage * HeadshotMultiplier * headshotZone.DamageMultiplier;
						health.TakeDamage( headshotDamage, owner );
						PlayerStats.ShotsHit++;

						var enemy = targetEntity.Components.Get<Enemy>();
						if ( enemy != null )
						{
							enemy.OnHeadshotDamage( headshotDamage, owner );
						}
					}
				}
			}
			else
			{
				// Regular body shot
				var health = trace.GameObject?.Components.Get<HealthComponent>();
				if ( health != null )
				{
					if ( !health.IsPlayer )
						PlayerStats.ShotsHit++;
					health.TakeDamage( Damage, owner );
				}
				else
				{
					health = trace.GameObject?.Components.GetInDescendantsOrSelf<HealthComponent>();
					if ( health != null )
					{
						if ( !health.IsPlayer )
							PlayerStats.ShotsHit++;
						health.TakeDamage( Damage, owner );
					}
				}
			}

			// Visual feedback
			Gizmo.Draw.Color = Color.Orange;
			Gizmo.Draw.Line( startPos, trace.EndPosition );
		}
	}

	public override void Reload()
	{
		base.Reload();

		if ( isReloading && ReloadSound != null )
		{
			Sound.Play( ReloadSound );
		}
	}
}
