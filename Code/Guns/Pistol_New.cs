using Sandbox;

/// <summary>
/// Pistol - A basic semi-automatic weapon for the L4D-style game
/// </summary>
public sealed class Pistol : Gun
{
	[Property] public float Range { get; set; } = 5000f;
	[Property] public float HeadshotMultiplier { get; set; } = 2.0f;
	[Property] public SoundEvent FireSound { get; set; }
	[Property] public SoundEvent ReloadClipOutSound { get; set; }
	[Property] public SoundEvent ReloadClipInSound { get; set; }
	[Property] public SoundEvent ReloadSlideSound { get; set; }

	protected override void OnAwake()
	{
		// Set default pistol stats
		Category = WeaponCategory.Secondary;
		Damage = 25f;
		FireRate = 0.2f;
		AmmoClip = 15;
		AmmoReserve = 75;
		ReloadTime = 1.5f;
		IsAutomatic = false;
		UnlimitedAmmo = true;

		base.OnAwake();
	}

	protected override void OnFire()
	{
		if ( playerHead is null )
		{
			Log.Warning( "Pistol: No player head reference set!" );
			return;
		}

		// Play fire sound
		if ( FireSound != null )
		{
			Sound.Play( FireSound );
		}

		// Get firing direction from player head
		var fireOrigin = playerCamera ?? playerHead;
		var startPos = fireOrigin.WorldPosition;
		var direction = fireOrigin.WorldRotation.Forward;

		// Perform raycast
		var trace = Scene.Trace.Ray( startPos, startPos + direction * Range )
			.IgnoreGameObject( owner ) // Don't hit ourselves
			.WithoutTags( "trigger" )
			.Run();

		if ( trace.Hit )
		{
			Log.Info( $"Pistol hit: {trace.GameObject?.Name ?? "Unknown"} at distance {trace.Distance:F1}" );

			// Check if we hit a headshot zone
			var headshotZone = trace.GameObject?.Components.Get<HeadshotZone>();
			if ( headshotZone != null )
			{
				// Headshot!
				var targetEntity = headshotZone.GetTargetEntity();
				if ( targetEntity != null )
				{
					var health = targetEntity.Components.Get<HealthComponent>();
					if ( health != null )
					{
						float headshotDamage = Damage * HeadshotMultiplier * headshotZone.DamageMultiplier;
						health.TakeDamage( headshotDamage, owner );
						PlayerStats.ShotsHit++;
						Log.Info( $"HEADSHOT! Dealt {headshotDamage} damage to {targetEntity.Name}" );

						// Notify enemy of headshot for extra stagger
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
				// Search self, ancestors, and descendants for HealthComponent
				var health = trace.GameObject?.Components.Get<HealthComponent>();
				health ??= trace.GameObject?.Components.GetInAncestorsOrSelf<HealthComponent>();
				health ??= trace.GameObject?.Components.GetInDescendantsOrSelf<HealthComponent>();

				if ( health != null )
				{
					if ( !health.IsPlayer )
						PlayerStats.ShotsHit++;
					health.TakeDamage( Damage, owner );
					Log.Info( $"Hit {trace.GameObject?.Name}: {Damage} dmg, HP left: {health.CurrentHealth}" );
				}
				else
				{
					Log.Info( $"Hit {trace.GameObject?.Name} - no HealthComponent found" );
				}
			}

			// Visual feedback - draw a line in the world
			Gizmo.Draw.Color = Color.Yellow;
			Gizmo.Draw.Line( startPos, trace.EndPosition );
			Gizmo.Draw.LineSphere( trace.EndPosition, 5f );
		}
		else
		{
			// Miss - draw to max range
			Gizmo.Draw.Color = Color.Gray;
			Gizmo.Draw.Line( startPos, startPos + direction * Range );
		}
	}

	public override void Reload()
	{
		base.Reload();

		if ( !isReloading ) return;

		// Stagger the three reload sounds across the reload duration
		if ( ReloadClipOutSound != null )
			Sound.Play( ReloadClipOutSound );
		if ( ReloadClipInSound != null )
			_ = PlayDelayed( ReloadClipInSound, ReloadTime * 0.4f );
		if ( ReloadSlideSound != null )
			_ = PlayDelayed( ReloadSlideSound, ReloadTime * 0.75f );
	}

	private async System.Threading.Tasks.Task PlayDelayed( SoundEvent sound, float delay )
	{
		await System.Threading.Tasks.Task.Delay( (int)(delay * 1000) );
		if ( IsValid )
			Sound.Play( sound, WorldPosition );
	}
}
