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
	private Angles lastAngDif = Angles.Zero;

	// Jump animation state
	private float jumpTime;
	private float landTime;

	// Per-frame local velocity (camera-relative)
	private Vector3 localVel;

	protected override void OnUpdate()
	{
		if ( Player is null ) return;

		// Find GunViewModel and use its ModelObject reference
		if ( gunModel is null || !gunModel.IsValid() || !gunModel.Enabled )
		{
			var vm = Components.GetInDescendantsOrSelf<GunViewModel>();
			gunModel = vm?.ModelObject;
			if ( gunModel is null ) return;
			restPosition = gunModel.LocalPosition;
			restAngles   = gunModel.LocalRotation.Angles();
			finalPos     = Vector3.Zero;
			finalRot     = Vector3.Zero;
		}

		// Update rest state when the gun model's rotation changes (weapon swap)
		// This prevents accumulated rotation from previous weapons
		var currentAngles = gunModel.LocalRotation.Angles();
		if ( currentAngles != restAngles )
		{
			restAngles = currentAngles;
			finalRot = Vector3.Zero;  // Reset procedural rotation when swapping
		}

		// Smooth lerp toward targets - slower for stability while strafing
		var speed = 12f * AnimSpeed;
		finalPos = Vector3.Lerp( finalPos, targetPos, speed * Time.Delta );
		finalRot = Vector3.Lerp( finalRot, targetRot, speed * Time.Delta );

		// Clamp final rotation to prevent wild spinning
		finalRot = new Vector3(
			Math.Clamp( finalRot.x, -5f, 5f ),
			Math.Clamp( finalRot.y, -5f, 5f ),
			Math.Clamp( finalRot.z, -5f, 5f )
		);

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

	// ── Breathing idle sway ───────────────────────────────────────────
	private void HandleIdleAnimation()
	{
		var t = Time.Now * 2f;
		// Minimal idle sway - just subtle breathing
		targetPos -= new Vector3( MathF.Cos( t / 4f ) / 16f, 0f, -MathF.Cos( t / 4f ) / 64f );
		// Very minimal idle rotation
		targetRot -= new Vector3( MathF.Cos( t / 5f ) * 0.1f, MathF.Cos( t / 4f ) * 0.05f, 0f );

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

		var roll = localVel.x > 0f ? -0.5f * (localVel.x / maxSpeed) : 0f;  // Minimal roll when strafing right
		var yaw  = localVel.x < 0f ?  1f * (localVel.x / maxSpeed) : 0f;  // Reduced yaw from 3 to 1

		targetPos -= new Vector3(
			(-MathF.Cos( t / 2f ) / 8f) * walkSpeed / maxSpeed - yaw / 6f,  // Reduced walk bob movement
			0f, 0f );
		targetRot -= new Vector3(
			(Math.Clamp( MathF.Cos( t ), -0.3f, 0.3f ) * 2f) * walkSpeed / maxSpeed,
			(-MathF.Cos( t / 2f ) * 0.5f) * walkSpeed / maxSpeed - yaw * 0.5f,  // Reduced yaw rotation from 1.2f to 0.5f
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

		// Lerp toward zero when rotation difference is small (stopped turning) - faster return to center
		lastAngDif = Angles.Lerp( lastAngDif, angDif, 20f * Time.Delta );

		// Minimal sway to keep gun in frame - very heavily clamped
		targetPos += new Vector3(
			Math.Clamp( lastAngDif.yaw   * 0.005f, -0.3f, 0.3f ),
			0f,
			Math.Clamp( lastAngDif.pitch * 0.005f, -0.3f, 0.3f ) );
		targetRot += new Vector3(
			Math.Clamp( lastAngDif.pitch * 0.02f, -0.5f, 0.5f ),
			Math.Clamp( lastAngDif.yaw   * 0.02f, -0.5f, 0.5f ),
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
