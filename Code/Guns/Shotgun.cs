using Sandbox;

/// <summary>
/// Shotgun - High damage, close range, fires multiple pellets.
/// Shell-by-shell reload: each shell takes (ReloadTime / AmmoClip) seconds,
/// stops as soon as the clip is full.
/// </summary>
public sealed class Shotgun : Gun
{
	[Property] public float Range { get; set; } = 1500f;
	[Property] public int PelletCount { get; set; } = 8;
	[Property] public float PelletSpread { get; set; } = 0.08f;
	[Property] public float PelletDamage { get; set; } = 8f; // Per pellet
	[Property] public float HeadshotMultiplier { get; set; } = 1.5f;
	[Property] public SoundEvent FireSound { get; set; }
	[Property] public SoundEvent PumpSound { get; set; }   // plays after each shot
	[Property] public SoundEvent InsertSound { get; set; } // plays per shell during reload
	/// <summary>How long each shell insertion takes in seconds — tune this to match the insert animation clip length.</summary>
	[Property] public float TimePerShell { get; set; } = 0.6f;

	private bool shellReloadActive = false;
	private float shellTimer = 0f;
	private float timePerShell = 0f;

	// Pellet debug visualization
	private struct PelletTrace { public Vector3 Start, End; public bool Hit; public float TimeLeft; }
	private System.Collections.Generic.List<PelletTrace> pelletDebug = new();
	private const float PelletDebugDuration = 1.5f;

	protected override void OnAwake()
	{
		Damage = PelletDamage;
		FireRate = 0.8f;
		AmmoClip = 8;
		AmmoReserve = 32;
		ReloadTime = 3.0f; // Total time for a full reload — per-shell = ReloadTime / AmmoClip

		base.OnAwake();
	}

	protected override void OnUpdate()
	{
		// Shell-by-shell reload logic runs before base so we can set isReloading = false
		// before base checks its reloadTimeRemaining condition
		if ( shellReloadActive )
		{
			shellTimer -= Time.Delta;
			if ( shellTimer <= 0f )
			{
				// Insert one shell
				if ( UnlimitedAmmo )
				{
					currentAmmo = System.Math.Min( currentAmmo + 1, AmmoClip );
				}
				else if ( AmmoReserve > 0 )
				{
					currentAmmo++;
					AmmoReserve--;
				}

				if ( InsertSound != null )
					Sound.Play( InsertSound );

				bool clipFull = currentAmmo >= AmmoClip;
				bool reserveEmpty = !UnlimitedAmmo && AmmoReserve <= 0;

				if ( clipFull || reserveEmpty )
				{
					FinishShellReload();
				}
				else
				{
					shellTimer = timePerShell;
				}
			}
		}

		// Draw persisted pellet debug traces
		for ( int i = pelletDebug.Count - 1; i >= 0; i-- )
		{
			var p = pelletDebug[i];
			p.TimeLeft -= Time.Delta;
			if ( p.TimeLeft <= 0f ) { pelletDebug.RemoveAt( i ); continue; }
			pelletDebug[i] = p;

			float alpha = p.TimeLeft / PelletDebugDuration;
			Gizmo.Draw.Color = p.Hit
				? Color.Red.WithAlpha( alpha )
				: Color.Gray.WithAlpha( alpha * 0.4f );
			Gizmo.Draw.Line( p.Start, p.End );
			if ( p.Hit )
			{
				Gizmo.Draw.Color = Color.Orange.WithAlpha( alpha );
				Gizmo.Draw.LineSphere( p.End, 4f );
			}
		}

		base.OnUpdate();
	}

	public override void CancelReload()
	{
		shellReloadActive = false;
		var renderer = GunViewModel.Current?.ModelRenderer;
		if ( renderer != null )
		{
			renderer.Set( "reload", false );
			renderer.Set( "reload_finished", false );
		}
		base.CancelReload();
	}

	public override void Reload()
	{
		if ( isReloading ) return;
		if ( currentAmmo >= AmmoClip ) return;
		if ( !UnlimitedAmmo && AmmoReserve <= 0 ) return;

		isReloading = true;
		reloadTimeRemaining = 99999f; // Prevent base Gun from calling FinishReload

		timePerShell = TimePerShell;
		shellTimer = timePerShell;
		shellReloadActive = true;

		// Start reload animation sequence
		GunViewModel.Current?.ModelRenderer?.Set( "reload", true );
	}

	private void FinishShellReload()
	{
		shellReloadActive = false;
		isReloading = false;
		reloadTimeRemaining = 0f;

		// reload is non-auto-reset so must be explicitly cleared, otherwise the
		// reload_insert state loops forever
		var renderer = GunViewModel.Current?.ModelRenderer;
		if ( renderer != null )
		{
			renderer.Set( "reload_finished", true );
			renderer.Set( "reload", false );
		}

		if ( PumpSound != null )
			_ = PlayDelayed( PumpSound, 0.1f );
	}

	protected override void OnFire()
	{
		if ( playerHead is null )
		{
			Log.Warning( "Shotgun: No player head reference set!" );
			return;
		}

		if ( FireSound != null )
		{
			var snd = Sound.Play( FireSound );
			snd.Volume = 0.4f;
		}

		// Pump action — play after a short delay so it follows the shot
		if ( PumpSound != null )
			_ = PlayDelayed( PumpSound, 0.3f );

		var fireOrigin = playerCamera ?? playerHead;
		var startPos = fireOrigin.WorldPosition;
		var baseDirection = fireOrigin.WorldRotation.Forward;
		var knockedBack = new System.Collections.Generic.HashSet<Enemy>();

		for ( int i = 0; i < PelletCount; i++ )
		{
			float spreadX = Game.Random.Float( -PelletSpread, PelletSpread );
			float spreadY = Game.Random.Float( -PelletSpread, PelletSpread );
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
							float headshotDamage = PelletDamage * HeadshotMultiplier * headshotZone.DamageMultiplier;
							health.TakeDamage( headshotDamage, owner );
							PlayerStats.ShotsHit++;

							// Only trigger headshot stagger once per shot, not per pellet
							if ( i == 0 )
							{
								var enemy = targetEntity.Components.Get<Enemy>();
								enemy?.OnHeadshotDamage( headshotDamage, owner );
							}
						}
					}
				}
				else
				{
					var health = trace.GameObject?.Components.Get<HealthComponent>();
					health ??= trace.GameObject?.Components.GetInAncestorsOrSelf<HealthComponent>();
					health ??= trace.GameObject?.Components.GetInDescendantsOrSelf<HealthComponent>();

					if ( health != null && !health.IsPlayer )
						PlayerStats.ShotsHit++;

					health?.TakeDamage( PelletDamage, owner );

					// Shotgun body knockback — once per enemy, not per pellet
					var enemy = trace.GameObject?.Components.GetInAncestorsOrSelf<Enemy>()
						?? trace.GameObject?.Components.GetInDescendantsOrSelf<Enemy>();
					if ( enemy != null && knockedBack.Add( enemy ) )
						enemy.ApplyShotgunKnockback( owner );
				}

				pelletDebug.Add( new PelletTrace { Start = startPos, End = trace.EndPosition, Hit = true, TimeLeft = PelletDebugDuration } );
			}
			else
			{
				pelletDebug.Add( new PelletTrace { Start = startPos, End = startPos + direction * Range, Hit = false, TimeLeft = PelletDebugDuration } );
			}
		}

		Log.Info( $"Shotgun fired {PelletCount} pellets" );
	}

	private async System.Threading.Tasks.Task PlayDelayed( SoundEvent sound, float delay )
	{
		await System.Threading.Tasks.Task.Delay( (int)(delay * 1000) );
		if ( IsValid )
			Sound.Play( sound );
	}
}
