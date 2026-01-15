using System;
using Sandbox;
using Sandbox.Citizen;

public sealed class PlayerMovement : Component
{
	// Movement proprersties
	[Property] public float GroundControl { get; set; } = 4.0f;
	[Property] public float AirControl { get; set; } = 0.1f;
	[Property] public float MaxForce { get; set; } = 50f;
	[Property] public float Speed { get; set; } = 160f;
	[Property] public float RunSpeed { get; set; } = 290f;
	[Property] public float CrouchSpeed { get; set; } = 90f;
	[Property] public float JumpForce { get; set; } = 400f;

	// Stamina / sprinting (Left 4 Dead style sprint drain)
	[Property] public float StaminaMax { get; set; } = 5.0f; // seconds of sprint
	[Property] public float StaminaDrainRate { get; set; } = 1.0f; // per second while sprinting
	[Property] public float StaminaRecoverRate { get; set; } = 0.5f; // per second when not sprinting
	[Property] public float JumpCooldown { get; set; } = 0.15f; // small buffer between jumps


	//Object Refs

	[Property] public GameObject Head { get; set; }
	[Property] public GameObject Body { get; set; }

	//Memeber vars
	public Vector3 WishVelocity = Vector3.Zero;
	public bool isCrouching = false;
	public bool isSprinting = false;

	private float currentStamina;
	private float jumpCooldownRemaining = 0f;
	private float originalHeight = 72f;

	[Property]
	public bool EnableDiagnostics { get; set; } = true;

	private CharacterController characterController;

	private CitizenAnimationHelper animationHelper;

	protected override void OnAwake()
	{
		characterController = Components.Get<CharacterController>();
		animationHelper = Components.Get<CitizenAnimationHelper>();

		currentStamina = StaminaMax;

		if (characterController != null)
		{
			originalHeight = characterController.Height;
		}

		// initialize UI helper
		PlayerStats.CurrentStamina = currentStamina;
		PlayerStats.StaminaMax = StaminaMax;
		PlayerStats.DebugMessage = string.Empty;
	}

	protected override void OnUpdate()
	{
		if ( !Enabled ) return;

		// Try to recover CharacterController reference if it's null (editor play toggles can leave components in odd states)
		if (characterController is null)
		{
			characterController = Components.Get<CharacterController>();
		}

		//Set our sprinting/crouching states
		isCrouching = Input.Down( "Crouch" );
		// Sprint is a held input but we only allow sprinting when we have stamina and when not crouching
		bool wantsSprint = Input.Down( "Run" );
		isSprinting = wantsSprint && currentStamina > 0f && !isCrouching;

		if (Input.Pressed( "Jump" )) Jump();

		// update stamina & cooldown timers
		if (jumpCooldownRemaining > 0f) jumpCooldownRemaining -= Time.Delta;

		if (isSprinting && (characterController != null && characterController.IsOnGround))
		{
			currentStamina = MathF.Max(0f, currentStamina - (StaminaDrainRate * Time.Delta));
			if (currentStamina <= 0f) isSprinting = false; // force stop sprint when drained
		}
		else
		{
			currentStamina = MathF.Min(StaminaMax, currentStamina + (StaminaRecoverRate * Time.Delta));
		}

		// publish to UI helper
		PlayerStats.CurrentStamina = currentStamina;
		PlayerStats.StaminaMax = StaminaMax;

		// Diagnostics output (helps when editor play/stop leaves things in a strange state)
		if (EnableDiagnostics)
		{
			var ccPresent = characterController is not null;
			var headPresent = Head is not null;
			var bodyPresent = Body is not null;
			var onGround = ccPresent ? characterController.IsOnGround : false;
			var forward = Input.Down("Forward");
			var back = Input.Down("Backward");
			var left = Input.Down("Left");
			var right = Input.Down("Right");
			var run = Input.Down("Run");
			var crouch = Input.Down("Crouch");
			var jump = Input.Down("Jump");
			var wishMag = WishVelocity.Length;

			PlayerStats.DebugMessage = $"CC:{(ccPresent?"Y":"N")} GN:{(onGround?"Y":"N")} Head:{(headPresent?"Y":"N")} Body:{(bodyPresent?"Y":"N")}\n" +
			                       $"Inputs F:{(forward?1:0)} B:{(back?1:0)} L:{(left?1:0)} R:{(right?1:0)} Run:{(run?1:0)} Crouch:{(crouch?1:0)} Jump:{(jump?1:0)}\n" +
			                       $"Wish:{wishMag:F2} Stamina:{currentStamina:F2}/{StaminaMax:F2} Sprint:{(isSprinting?"Y":"N")}";

			// If critical wiring is missing, also show a more explicit message to guide the user
			if (!ccPresent)
			{
				PlayerStats.DebugMessage += "\n(Missing CharacterController on player entity)";
			}
			if (!headPresent && !bodyPresent)
			{
				PlayerStats.DebugMessage += "\n(Missing Head/Body references on PlayerMovement component)";
			}
		}
		else
		{
			// clear only the debug line when diagnostics disabled
			if (string.IsNullOrEmpty(PlayerStats.DebugMessage) == false)
				PlayerStats.DebugMessage = string.Empty;
		}

		RotateBody();
		UpdateAnimations();
		// allow crouch state transitions (press/release logic)
		UpdateCrouch();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Enabled ) return;

		BuildWishVelocity();		
		RotateBody();
		Move();
	}

	void BuildWishVelocity()
	{
		WishVelocity = 0;
		
		// use Head if available, otherwise fall back to Body; if neither, use identity rotation
		Rotation rot;
		if (Head is not null) rot = Head.Transform.Rotation;
		else if (Body is not null) rot = Body.Transform.Rotation;
		else rot = Rotation.Identity;
		if(Input.Down( "Forward" ))		WishVelocity += rot.Forward;
		if(Input.Down( "Backward"))		WishVelocity += rot.Backward;
		if(Input.Down( "Left" 	 ))		WishVelocity += rot.Left;
		if(Input.Down( "Right" 	 ))		WishVelocity += rot.Right;

		WishVelocity = WishVelocity.WithZ( 0 );
		if(!WishVelocity.IsNearZeroLength) WishVelocity = WishVelocity.Normal;

		// determine speed based on state and stamina
		float targetSpeed = Speed;
		if (isCrouching) targetSpeed = CrouchSpeed;
		else if (isSprinting) targetSpeed = RunSpeed;
		else targetSpeed = Speed;

		WishVelocity *= targetSpeed;
	}

	void Move()
	{
		//Get gravity from scene
		var gravity = Scene.PhysicsWorld.Gravity;

		if(characterController.IsOnGround)
		{
			//Apply Friction/Acceleration
			characterController.Velocity = characterController.Velocity.WithZ( 0 );
			characterController.Accelerate(WishVelocity);
			characterController.ApplyFriction(GroundControl);
		}
		else
		{
			//Apply Air Control/Gravity
			characterController.Velocity += gravity * Time.Delta * .5f;
			characterController.Accelerate(WishVelocity.ClampLength( MaxForce ));
			characterController.ApplyFriction(AirControl);
		}

		//Move the char controller
		characterController.Move();

		//Apply second half of gravity after movement
		if(!characterController.IsOnGround)
		{
			characterController.Velocity += gravity * Time.Delta * .5f;
		}
		else
		{
			characterController.Velocity = characterController.Velocity.WithZ( 0 );
		}
	}

	void RotateBody() {
		if(Body is null) return;

		var targetAngle = new Angles(0, Head.Transform.Rotation.Yaw(), 0).ToRotation();
		float rotateDifference = Body.Transform.Rotation.Distance( targetAngle );

		if(rotateDifference > 50f || characterController.Velocity.Length > 10f) 
		{
			Body.Transform.Rotation = Rotation.Lerp(Body.Transform.Rotation, targetAngle, Time.Delta * 2f);
		}	
	}

	void Jump() {
		if(!characterController.IsOnGround) return;
		if (jumpCooldownRemaining > 0f) return;

		// reset vertical velocity and apply a consistent upward impulse
		characterController.Velocity = characterController.Velocity.WithZ(0);
		characterController.Punch(Vector3.Up * JumpForce);
		animationHelper?.TriggerJump();
		jumpCooldownRemaining = JumpCooldown;

		// Drain stamina on jump
		currentStamina = MathF.Max(0f, currentStamina - 0.5f);
	}

	void UpdateAnimations() 
	{
		if(animationHelper is null) return;

		animationHelper.WithWishVelocity(WishVelocity);
		animationHelper.WithVelocity(characterController.Velocity);
		animationHelper.AimAngle = Head.Transform.Rotation;
		animationHelper.IsGrounded = characterController.IsOnGround;
		animationHelper.WithLook(Head.Transform.Rotation.Forward, 1f, 0.75f, 0.5f);
		animationHelper.MoveStyle = CitizenAnimationHelper.MoveStyles.Run;
		animationHelper.DuckLevel  = isCrouching ? 1f : 0f;

	}
	void UpdateCrouch()
	{
		if(characterController is null) return;

		// Use hold-based crouch (matches many FPS conventions). If you'd prefer toggle, switch to Input.Pressed toggle logic.
		if (Input.Down("Crouch") && !isCrouching)
		{
			isCrouching = true;
			characterController.Height *= 0.5f;
		}
		else if (!Input.Down("Crouch") && isCrouching)
		{
			isCrouching = false;
			characterController.Height *= 2f;
		}
	}


}


