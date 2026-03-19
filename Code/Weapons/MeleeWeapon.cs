using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Abstract base class for melee weapons. Uses sphere trace collision detection
/// to detect and damage enemies within range with knockback effect.
/// </summary>
public abstract class MeleeWeapon : Component
{
	[Property] public WeaponCategory Category { get; set; } = WeaponCategory.Secondary;
	[Property] public float Damage { get; set; } = 40f;
	[Property] public float Range { get; set; } = 120f;
	[Property] public float SwingSpeed { get; set; } = 0.5f;
	[Property] public float KnockbackForce { get; set; } = 600f;
	[Property] public float SphereRadius { get; set; } = 15f; // Radius of the sphere trace

	private bool isSwinging = false;
	private float swingCooldown = 0f;
	private List<GameObject> hitThisSwing = new();
	protected GameObject playerHead; // Reference to player's head for swing origin
	protected GameObject owner; // Root owner (player), used to ignore self in traces

	protected override void OnUpdate()
	{
		// Decrement cooldown timer
		if ( swingCooldown > 0f )
		{
			swingCooldown -= Time.Delta;

			// When cooldown expires, reset swinging state
			if ( swingCooldown <= 0f )
			{
				isSwinging = false;
			}
		}
	}

	/// <summary>
	/// Initiate a melee swing. Checks cooldown, then performs hit detection.
	/// </summary>
	public void Swing()
	{
		if ( isSwinging || swingCooldown > 0 )
		{
			return;
		}

		isSwinging = true;
		swingCooldown = SwingSpeed;
		hitThisSwing.Clear();

		Log.Info( $"{GetType().Name} swing initiated!" );
		CheckForHits();
	}

	/// <summary>
	/// Perform sphere trace to detect hits in front of the player.
	/// </summary>
	private void CheckForHits()
	{
		if ( playerHead is null )
		{
			Log.Warning( $"{GetType().Name}: No player head reference set!" );
			return;
		}

		// Get starting position and direction from player head
		var startPos = playerHead.WorldPosition;
		var direction = playerHead.WorldRotation.Forward;
		var endPos = startPos + direction * Range;

		// Perform sphere trace from start to end position
		var trace = Scene.Trace.Sphere( SphereRadius, startPos, endPos )
			.IgnoreGameObject( owner )
			.WithoutTags( "trigger" )
			.Run();

		if ( trace.Hit )
		{
			// Check if we already hit this object during this swing
			if ( hitThisSwing.Contains( trace.GameObject ) )
			{
				return;
			}

			Log.Info( $"{GetType().Name} hit {trace.GameObject.Name}!" );

			// Try to find HealthComponent on the hit object
			var health = trace.GameObject?.Components.Get<HealthComponent>();
			if ( health is null )
			{
				health = trace.GameObject?.Components.GetInDescendantsOrSelf<HealthComponent>();
			}

			if ( health != null )
			{
				// Apply damage
				health.TakeDamage( Damage, owner );
				Log.Info( $"{GetType().Name} dealt {Damage} damage to {trace.GameObject.Name}" );

				// Apply knockback
				var characterController = trace.GameObject?.Components.Get<CharacterController>();
				if ( characterController != null )
				{
					// Knockback direction: forward from player + upward lift
					var knockbackImpulse = direction * KnockbackForce + Vector3.Up * 200f;
					characterController.Punch( knockbackImpulse );
					Log.Info( $"{GetType().Name} applied knockback to {trace.GameObject.Name}" );
				}

				// Track this object to prevent hitting it multiple times in one swing
				hitThisSwing.Add( trace.GameObject );
			}
			else
			{
				Log.Info( $"{GetType().Name} hit {trace.GameObject.Name} but no HealthComponent found" );
			}

			// Visual feedback - draw hit sphere
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.LineSphere( new Sphere( trace.EndPosition, SphereRadius ) );
		}
		else
		{
			Log.Info( $"{GetType().Name} swing missed" );

			// Visual feedback - draw miss sphere at end of range
			Gizmo.Draw.Color = Color.Gray;
			Gizmo.Draw.LineSphere( new Sphere( endPos, SphereRadius ) );
		}

		// Draw swing arc for debugging
		Gizmo.Draw.Color = Color.Yellow;
		Gizmo.Draw.Line( startPos, endPos );
	}

	/// <summary>
	/// Set the player head reference for swing origin.
	/// </summary>
	public void SetPlayerHead( GameObject head )
	{
		playerHead = head;
	}

	/// <summary>
	/// Set the owner (player) to ignore in traces.
	/// </summary>
	public void SetOwner( GameObject ownerGameObject )
	{
		owner = ownerGameObject;
	}

	/// <summary>
	/// Check if weapon is currently on cooldown.
	/// </summary>
	public bool IsOnCooldown() => swingCooldown > 0f;
}
