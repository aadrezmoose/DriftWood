using Sandbox;
using System;

/// <summary>
/// Procedural first-person weapon viewmodel animation.
/// Add this component to the Camera object alongside CameraMovement.
/// It automatically finds the GunModel child and applies breathing, walk bob, sway, jump, and sprint animations.
/// Adapted from the SWB Simple Weapon Base procedural viewmodel system.
/// </summary>
public sealed class ViewModelHandler : Component
{
	[Property] public PlayerMovement Player { get; set; }
	[Property] public float AnimSpeed { get; set; } = 1f;

	/// <summary>Gun position offset while sprinting.</summary>
	[Property, Group( "Sprint" )] public Vector3 SprintPos { get; set; } = new Vector3( 2f, 0f, -2f );
	/// <summary>Gun angle offset while sprinting.</summary>
	[Property, Group( "Sprint" )] public Angles SprintAngles { get; set; } = new Angles( 5f, -10f, 5f );

	// Cached gun model reference
	private GameObject gunModel;
	private Vector3 restPosition;
	private Angles restAngles;

	// Animation interpolation
	private Vector3 targetPos;
	private Vector3 targetRot;
	private Vector3 finalPos;
	private Vector3 finalRot;

	// Sway state
	private Rotation lastEyeRot;

	// Jump animation state
	private float jumpTime;
	private float landTime;

	// Per-frame local velocity (camera-relative)
	private Vector3 localVel;

	protected override void OnUpdate()
	{
		if ( Player is null ) return;

		// Re-find GunModel whenever the cached one is disabled (weapon switched) or gone
		if ( gunModel is null || !gunModel.IsValid() || !gunModel.Enabled )
		{
			gunModel = FindDescendantByName( GameObject, "GunModel" );
			if ( gunModel is null ) return;
			restPosition = gunModel.LocalPosition;
			restAngles   = gunModel.LocalRotation.Angles();
			finalPos     = Vector3.Zero;
			finalRot     = Vector3.Zero;
		}

		// Smooth lerp toward targets
		var speed = 10f * AnimSpeed;
		finalPos = Vector3.Lerp( finalPos, targetPos, speed * Time.Delta );
		finalRot = Vector3.Lerp( finalRot, targetRot, speed * Time.Delta );

		// Apply to gun model local transform
		gunModel.LocalPosition = restPosition + finalPos;
		gunModel.LocalRotation = Rotation.From( restAngles + new Angles( finalRot.x, finalRot.y, finalRot.z ) );

		// Reset targets for this frame
		targetPos = Vector3.Zero;
		targetRot = Vector3.Zero;

		// Camera-local velocity for walk/sway calculations
		var vel = Player.Velocity;
		localVel = new Vector3(
			WorldRotation.Right.Dot( vel ),
			WorldRotation.Forward.Dot( vel ),
			vel.z
		);

		HandleIdleAnimation();
		HandleWalkAnimation();
		HandleSwayAnimation();
		HandleJumpAnimation();
		HandleSprintAnimation();
	}

	private static GameObject FindDescendantByName( GameObject parent, string name )
	{
		foreach ( var child in parent.Children )
		{
			if ( child.Name == name && child.Enabled ) return child;
			var found = FindDescendantByName( child, name );
			if ( found is not null ) return found;
		}
		return null;
	}

	// ── Breathing idle sway ───────────────────────────────────────────
	private void HandleIdleAnimation()
	{
		var t = Time.Now * 2f;
		targetPos -= new Vector3( MathF.Cos( t / 4f ) / 8f, 0f, -MathF.Cos( t / 4f ) / 32f );
		targetRot -= new Vector3( MathF.Cos( t / 5f ), MathF.Cos( t / 4f ), MathF.Cos( t / 7f ) );

		if ( Player.isCrouching && Player.IsOnGround )
			targetPos += new Vector3( -1f, -1f, 0.5f );
	}

	// ── Walk bob ──────────────────────────────────────────────────────
	private void HandleWalkAnimation()
	{
		if ( !Player.IsOnGround ) return;

		var vel = Player.Velocity;
		var walkSpeed = new Vector3( vel.x, vel.y, 0f ).Length;
		var maxSpeed = Player.isSprinting ? 100f : 200f;
		var t = Time.Now * (Player.isSprinting ? 18f : 16f);

		var roll = localVel.x > 0f ? -7f * (localVel.x / maxSpeed) : 0f;
		var yaw  = localVel.x < 0f ?  3f * (localVel.x / maxSpeed) : 0f;

		targetPos -= new Vector3(
			(-MathF.Cos( t / 2f ) / 5f) * walkSpeed / maxSpeed - yaw / 4f,
			0f, 0f );
		targetRot -= new Vector3(
			(Math.Clamp( MathF.Cos( t ), -0.3f, 0.3f ) * 2f) * walkSpeed / maxSpeed,
			(-MathF.Cos( t / 2f ) * 1.2f) * walkSpeed / maxSpeed - yaw * 1.5f,
			roll );
	}

	// ── Mouse look sway ───────────────────────────────────────────────
	private void HandleSwayAnimation()
	{
		lastEyeRot = Rotation.Lerp( lastEyeRot, WorldRotation, 5f * Time.Delta );

		var angDif = WorldRotation.Angles() - lastEyeRot.Angles();
		angDif = new Angles(
			angDif.pitch,
			MathX.RadianToDegree( MathF.Atan2( MathF.Sin( MathX.DegreeToRadian( angDif.yaw ) ), MathF.Cos( MathX.DegreeToRadian( angDif.yaw ) ) ) ),
			0f );

		targetPos += new Vector3(
			Math.Clamp( angDif.yaw   * 0.04f, -1.5f, 1.5f ),
			0f,
			Math.Clamp( angDif.pitch * 0.04f, -1.5f, 1.5f ) );
		targetRot += new Vector3(
			Math.Clamp( angDif.pitch * 0.2f, -4f, 4f ),
			Math.Clamp( angDif.yaw   * 0.2f, -4f, 4f ),
			0f );
	}

	// ── Jump / land bezier ────────────────────────────────────────────
	private void HandleJumpAnimation()
	{
		if ( !Player.IsOnGround )
			landTime = Time.Now + 0.31f;

		if ( landTime < Time.Now && landTime != 0f )
		{
			landTime = 0f;
			jumpTime = 0f;
		}

		if ( Input.Down( "Jump" ) && jumpTime == 0f )
		{
			jumpTime = Time.Now + 0.31f;
			landTime = 0f;
		}

		if ( jumpTime > Time.Now )
		{
			var f = 0.31f - (jumpTime - Time.Now);
			targetPos += new Vector3( BezierY( f, 0f, -4f, 0f ), 0f, BezierY( f, 0f, -2f, -5f ) ) / 4f;
			targetRot += new Vector3( BezierY( f, 0f, -4.36f, 10f ), BezierY( f, 0f, -4f, 0f ), BezierY( f, 0f, -10.82f, -5f ) ) / 4f;
		}
		else if ( !Player.IsOnGround )
		{
			var t = Time.Now * 30f;
			targetPos += new Vector3( MathF.Cos( t / 2f ) / 16f, 0f, -5f + MathF.Sin( t / 3f ) / 16f ) / 4f;
			targetRot += new Vector3( 10f - MathF.Sin( t / 3f ) / 4f, MathF.Cos( t / 2f ) / 4f, -5f ) / 4f;
		}
		else if ( landTime > Time.Now )
		{
			var f = landTime - Time.Now;
			targetPos += new Vector3( BezierY( f, 0f, -4f, 0f ), 0f, BezierY( f, 0f, -2f, -5f ) ) / 2f;
			targetRot += new Vector3( BezierY( f, 0f, -4.36f, 10f ), BezierY( f, 0f, -4f, 0f ), BezierY( f, 0f, -10.82f, -5f ) ) / 2f;
		}
	}

	// ── Sprint tuck ───────────────────────────────────────────────────
	private void HandleSprintAnimation()
	{
		if ( Player.isSprinting )
		{
			targetPos += SprintPos;
			targetRot += new Vector3( SprintAngles.pitch, SprintAngles.yaw, SprintAngles.roll );
		}
	}

	// Quadratic bezier interpolation (from SWB)
	private static float BezierY( float t, float p0, float p1, float p2 )
		=> MathF.Pow( 1 - t, 2 ) * p0 + 2f * (1 - t) * t * p1 + MathF.Pow( t, 2 ) * p2;
}
