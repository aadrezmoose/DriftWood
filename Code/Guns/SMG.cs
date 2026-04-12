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
		Damage = 15f;
		FireRate = 0.08f; // ~12.5 shots per second
		AmmoClip = 30;
		AmmoReserve = 360;
		ReloadTime = 2.0f;
		RecoilAmount = 1.5f;

		base.OnAwake();
	}

	protected override void OnFire()
	{
		if ( playerHead is null )
		{
			Log.Warning( "SMG: No player head reference set!" );
			return;
		}

		if ( FireSound != null )
			Sound.Play( FireSound, playerHead?.WorldPosition ?? WorldPosition );

		// Shoot from camera so shots align with crosshair
		var fireOrigin = playerCamera ?? playerHead;
		var startPos = fireOrigin.WorldPosition;
		var baseDirection = fireOrigin.WorldRotation.Forward;

		float spreadX = Game.Random.Float( -Spread, Spread );
		float spreadY = Game.Random.Float( -Spread, Spread );
		var direction = (baseDirection + fireOrigin.WorldRotation.Right * spreadX + fireOrigin.WorldRotation.Up * spreadY).Normal;

		var trace = Scene.Trace.Ray( startPos, startPos + direction * Range )
			.IgnoreGameObject( owner )
			.WithoutTags( "trigger" )
			.Run();

		if ( trace.Hit )
		{
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
						ApplyEnemyHit( health, headshotDamage, isHeadshot: true );
						TrackShotHit();
					}
				}
			}
			else
			{
				var health = trace.GameObject?.Components.Get<HealthComponent>()
					?? trace.GameObject?.Components.GetInAncestorsOrSelf<HealthComponent>()
					?? trace.GameObject?.Components.GetInDescendantsOrSelf<HealthComponent>();

				if ( health != null && !health.IsPlayer )
					TrackShotHit();
				if ( health != null )
					ApplyEnemyHit( health, Damage );
			}

			// Impact effects on world geometry (not enemies)
			if ( trace.GameObject?.Components.GetInAncestorsOrSelf<Enemy>() == null &&
			     trace.GameObject?.Components.GetInDescendantsOrSelf<Enemy>() == null )
				SpawnImpactEffects( trace );

			// Tracer from muzzle to hit point
			var muzzleAttach = GunViewModel.Current?.ModelRenderer?.GetAttachment( "muzzle" );
			var tracerStart = muzzleAttach?.Position ?? startPos;
			Gizmo.Draw.Color = Color.Orange;
			Gizmo.Draw.Line( tracerStart, trace.EndPosition );
		}
	}

	public override void Reload()
	{
		base.Reload();

		if ( isReloading && ReloadSound != null )
			Sound.Play( ReloadSound, playerHead?.WorldPosition ?? WorldPosition );
	}
}
