using Sandbox;

/// <summary>
/// Pistol - A basic semi-automatic weapon for the L4D-style game
/// </summary>
public sealed class Pistol : Gun
{
	[Property] public float Range { get; set; } = 5000f;
	[Property] public float HeadshotMultiplier { get; set; } = 2.0f;
	[Property] public float HeadshotAssistRadius { get; set; } = 24f;
	[Property] public float FalloffStartDistance { get; set; } = 500f;
	[Property] public float FalloffEndDistance { get; set; } = 2500f;
	[Property] public float MinDamage { get; set; } = 10f;
	[Property] public bool VerboseHitLogging { get; set; } = false;
	[Property] public SoundEvent FireSound { get; set; }
	[Property] public SoundEvent ReloadClipOutSound { get; set; }
	[Property] public SoundEvent ReloadClipInSound { get; set; }
	[Property] public SoundEvent ReloadSlideSound { get; set; }

	protected override void OnAwake()
	{
		// Set default pistol stats
		Category = WeaponCategory.Secondary;
		Damage = 35f;
		FireRate = 0.2f;
		AmmoClip = 15;
		AmmoReserve = 75;
		ReloadTime = 1.5f;
		IsAutomatic = false;
		UnlimitedAmmo = true;
		RecoilAmount = 2.5f;

		base.OnAwake();
	}

	protected override void OnFire()
	{
		if ( playerHead is null )
		{
			Log.Warning( "Pistol: No player head reference set!" );
			return;
		}

		// Play fire sound at player head so spatialized SoundEvents don't fall back to world origin
		if ( FireSound != null )
			Sound.Play( FireSound, playerHead?.WorldPosition ?? WorldPosition );

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
			if ( VerboseHitLogging )
				Log.Info( $"Pistol hit: {trace.GameObject?.Name ?? "Unknown"} at distance {trace.Distance:F1}" );

			// Check if we hit a headshot zone directly (or on ancestor chain)
			var headshotZone = trace.GameObject?.Components.GetInAncestorsOrSelf<HeadshotZone>();
			headshotZone ??= trace.GameObject?.Components.Get<HeadshotZone>();
			if ( headshotZone != null )
			{
				// Headshot!
				var targetEntity = headshotZone.GetTargetEntity();
				if ( targetEntity != null )
				{
					var health = targetEntity.Components.Get<HealthComponent>();
					if ( health != null )
					{
						float baseDamage = Damage * HeadshotMultiplier * headshotZone.DamageMultiplier;
						float headshotDamage = ApplyFalloff( baseDamage, trace.Distance );
						health.TakeDamage( headshotDamage, owner );
						TrackShotHit();
						if ( VerboseHitLogging )
							Log.Info( $"HEADSHOT! Dealt {headshotDamage} damage to {targetEntity.Name} at distance {trace.Distance:F0}" );

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
					// Fallback headshot assist: if body collider was hit first but impact point is near head zone,
					// count it as headshot so side/front shots register more naturally.
					var fallbackHead = health.GameObject?.Components.GetInDescendantsOrSelf<HeadshotZone>();
					if ( fallbackHead != null )
					{
						float headDist = Vector3.DistanceBetween( trace.EndPosition, fallbackHead.GameObject.WorldPosition );
						if ( headDist <= HeadshotAssistRadius )
						{
							float baseDamage = Damage * HeadshotMultiplier * fallbackHead.DamageMultiplier;
							float headshotDamage = ApplyFalloff( baseDamage, trace.Distance );
							health.TakeDamage( headshotDamage, owner );
							TrackShotHit();
							if ( VerboseHitLogging )
								Log.Info( $"HEADSHOT(assist)! Dealt {headshotDamage} damage to {health.GameObject?.Name} at distance {trace.Distance:F0}" );

							var enemy = health.GameObject?.Components.Get<Enemy>();
							enemy?.OnHeadshotDamage( headshotDamage, owner );
							return;
						}
					}

					if ( !health.IsPlayer )
						TrackShotHit();
					float bodyDamage = ApplyFalloff( Damage, trace.Distance );
					health.TakeDamage( bodyDamage, owner );
					if ( VerboseHitLogging )
						Log.Info( $"Hit {trace.GameObject?.Name}: {bodyDamage:F1} dmg (base {Damage}) at distance {trace.Distance:F0}, HP left: {health.CurrentHealth}" );
				}
				else
				{
					if ( VerboseHitLogging )
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
		bool wasAlreadyReloading = isReloading;
		base.Reload();

		// Only play sounds if this call actually started a NEW reload
		if ( !isReloading || wasAlreadyReloading ) return;

		reloadGeneration++;
		int myGeneration = reloadGeneration;

		// Stagger the three reload sounds across the reload duration
		var headPos = playerHead?.WorldPosition ?? WorldPosition;
		if ( ReloadClipOutSound != null )
			Sound.Play( ReloadClipOutSound, headPos );
		if ( ReloadClipInSound != null )
			_ = PlayDelayed( ReloadClipInSound, ReloadTime * 0.4f, myGeneration );
		if ( ReloadSlideSound != null )
			_ = PlayDelayed( ReloadSlideSound, ReloadTime * 0.75f, myGeneration );
	}

	private int reloadGeneration = 0;

	public override void CancelReload()
	{
		reloadGeneration++; // invalidate any pending async sound tasks
		base.CancelReload();
	}

	private async System.Threading.Tasks.Task PlayDelayed( SoundEvent sound, float delay, int generation )
	{
		await Task.DelaySeconds( delay );
		if ( IsValid && reloadGeneration == generation )
			Sound.Play( sound, playerHead?.WorldPosition ?? WorldPosition );
	}

	/// <summary>Apply distance-based falloff to damage. No falloff before FalloffStartDistance, then scales linearly to MinDamage at FalloffEndDistance.</summary>
	private float ApplyFalloff( float baseDamage, float distance )
	{
		if ( distance <= FalloffStartDistance )
			return baseDamage;

		if ( distance >= FalloffEndDistance )
			return MinDamage;

		float falloffRange = FalloffEndDistance - FalloffStartDistance;
		float distanceIntoFalloff = distance - FalloffStartDistance;
		float falloffT = System.Math.Clamp( distanceIntoFalloff / falloffRange, 0f, 1f );

		return baseDamage + (MinDamage - baseDamage) * falloffT;
	}
}
