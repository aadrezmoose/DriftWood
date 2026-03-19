// using Sandbox;

// public sealed class Pistol : Gun
// {
// 	[Property] public float BulletDistance { get; set; } = 5000f;

// 	protected override void OnFire()
// 	{
// 		Log.Info("Pistol.OnFire() called");

// 		if (playerHead == null)
// 		{
// 			Log.Warning("Player head not set on pistol!");
// 			return;
// 		}

// 		// Play gunshot sound from cloud - replace with your desired cloud sound path
// 		// Example format: "sounds/weapon_pistol.vsnd" or cloud package path
// 		Sound.Play( "sounds/luger pistol fire.sound", playerHead.Transform.World.Position );

// 		var startPos = playerHead.Transform.World.Position;
// 		var fireDirection = playerHead.Transform.World.Rotation.Forward;
// 		var endPos = startPos + fireDirection * BulletDistance;

// 		Log.Info($"Firing from {startPos} in direction {fireDirection}");

// 		// Raycast from camera forward
// 		// Ignore the player's own collider and widen the trace a little to make hits more forgiving
// 		var trace = Scene.Trace
// 			.Ray(startPos, endPos)
// 			.Size(3f)
// 			.IgnoreGameObject( owner ?? playerHead )
// 			.Run();

// 		if (trace.Hit)
// 		{
// 			// Check for headshot zone first
// 			var headshotZone = trace.GameObject?.Components.Get<HeadshotZone>();
// 			if ( headshotZone is null ) headshotZone = trace.GameObject?.Components.GetInAncestorsOrSelf<HeadshotZone>();
// 			if ( headshotZone is null ) headshotZone = trace.GameObject?.Components.GetInDescendantsOrSelf<HeadshotZone>();
// 			float damageMultiplier = 1.0f;
// 			GameObject targetEntity = trace.GameObject;

// 			if ( headshotZone is not null )
// 			{
// 				damageMultiplier = headshotZone.DamageMultiplier;
// 				targetEntity = headshotZone.GetTargetEntity() ?? trace.GameObject;
// 				Log.Info( $"HEADSHOT! zone={headshotZone.GameObject?.Name}, target={targetEntity?.Name}, mult={damageMultiplier}x" );
				
// 				// Visual feedback - use yellow/gold for headshot
// 				DebugOverlay.Sphere(new Sphere(trace.EndPosition, 8f), Color.Yellow, 2f);
// 			}
// 			else
// 			{
// 				// Normal hit - red sphere
// 				DebugOverlay.Sphere(new Sphere(trace.EndPosition, 5f), Color.Red, 2f);
// 			}

// 			// Find an IHealth component on the target entity
// 			IHealth hitHealth = targetEntity?.Components.Get<IHealth>();
// 			if ( hitHealth is null ) hitHealth = targetEntity?.Components.GetInAncestorsOrSelf<IHealth>();
// 			if ( hitHealth is null ) hitHealth = targetEntity?.Components.GetInDescendantsOrSelf<IHealth>();

// 			// Fallback: directly get HealthComponent if interface lookup fails
// 			if ( hitHealth is null )
// 			{
// 				var hc = targetEntity?.Components.Get<HealthComponent>()
// 						?? targetEntity?.Components.GetInAncestorsOrSelf<HealthComponent>()
// 						?? targetEntity?.Components.GetInDescendantsOrSelf<HealthComponent>();
// 				if ( hc is not null ) hitHealth = hc as IHealth;
// 			}

// 			if (hitHealth != null)
// 			{
// 				float finalDamage = Damage * damageMultiplier;
// 				Log.Info($"Applying damage to {targetEntity?.Name}: base={Damage}, mult={damageMultiplier}, final={finalDamage}");
// 				hitHealth.TakeDamage(finalDamage, owner ?? playerHead);

// 				// Apply headshot stagger if it was a headshot
// 				if ( damageMultiplier > 1.0f )
// 				{
// 					var enemy = targetEntity?.Components.Get<Enemy>();
// 					if ( enemy is not null )
// 					{
// 						enemy.OnHeadshotDamage( finalDamage, owner ?? playerHead );
// 					}
// 				}
// 			}
// 		}
// 		else
// 		{
// 			Log.Info("Ray did not hit anything");
// 			// Draw to end of bullet distance if no hit
// 			DebugOverlay.Sphere(new Sphere(endPos, 3f), Color.Blue, 2f);
// 		}

// 		// Visual effect for gun fire - longer duration
// 		DebugOverlay.Line(startPos, trace.EndPosition, Color.Yellow, 2f);
// 	}
// }

// public interface IHealth
// {
// 	void TakeDamage(float damage, GameObject attacker = null);
// }