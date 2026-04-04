using System;
using System.Linq;
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

	// Footstep sounds
	[Property] public SoundEvent FootstepWalk { get; set; }
	[Property] public SoundEvent FootstepRun { get; set; }
	[Property] public SoundEvent FootstepCrouch { get; set; }
	[Property] public float WalkStepInterval { get; set; } = 0.5f;
	[Property] public float RunStepInterval { get; set; } = 0.32f;
	[Property] public float CrouchStepInterval { get; set; } = 0.65f;

	//Object Refs

	[Property] public GameObject Head { get; set; }
	[Property] public GameObject Body { get; set; }

	//Memeber vars
	public Vector3 WishVelocity = Vector3.Zero;
	[Sync] public bool isCrouching { get; set; } = false;
	[Sync] public bool isSprinting { get; set; } = false;
	[Sync] public Vector3 NetworkWishVelocity { get; set; } = Vector3.Zero;
	[Sync] public Vector3 NetworkVelocity { get; set; } = Vector3.Zero;
	[Sync] public Angles NetworkLookAngles { get; set; }
	[Sync] public bool NetworkFlashlightOn { get; set; } = false;
	public bool IsPinned { get; set; } = false; // Set by Hunter when player is pinned

	public Vector3 Velocity => characterController?.Velocity ?? Vector3.Zero;
	public bool IsOnGround => characterController?.IsOnGround ?? false;

	private float currentStamina;
	private float jumpCooldownRemaining = 0f;
	private float originalHeight = 72f;
	private float footstepTimer = 0f;
	private bool sprintLocked = false;

	[Property]
	public bool EnableDiagnostics { get; set; } = false;

	private CharacterController characterController;
	private CitizenAnimationHelper animationHelper;

	private PlayerIdentity _identity;
	private PlayerIdentity Identity => _identity ??= Components.GetInDescendantsOrSelf<PlayerIdentity>();
	private string                    _appliedCharacterModel    = null;
	private string                    _appliedCharacterClothing = null;
	private readonly List<GameObject> _clothingObjects          = new();
	private CameraMovement _localCameraMovement;
	private CameraComponent _localCameraComponent;
	private CameraComponent _sceneFallbackCamera;
	private GameObject _emergencyCameraObject;
	private CameraComponent _emergencyCamera;
	private SpotLight _worldFlashlight;
	private GameObject _worldFlashlightGO;
	private bool _loggedMissingFlashlightAnchor;
	private SpotLight _localViewFlashlight;
	private GameObject _localViewFlashlightGO;
	private CameraComponent _localViewFlashlightCamera;

	private bool ShouldUseEmergencyCamera()
	{
		return Networking.IsActive && Connection.Local != null && !Connection.Local.IsHost;
	}

	private CameraComponent EnsureEmergencyCamera()
	{
		if ( !ShouldUseEmergencyCamera() ) return null;

		if ( _emergencyCameraObject == null || !_emergencyCameraObject.IsValid() )
		{
			_emergencyCameraObject = new GameObject( true, "ClientEmergencyCamera" );
			_emergencyCamera = _emergencyCameraObject.Components.Create<CameraComponent>();
			_emergencyCamera.BackgroundColor = Color.Black;
			_emergencyCamera.ZNear = 1f;
			_emergencyCamera.ZFar = 10000f;
		}

		if ( _emergencyCamera == null || !_emergencyCamera.IsValid )
			_emergencyCamera = _emergencyCameraObject?.Components.Get<CameraComponent>();

		return _emergencyCamera;
	}

	private void ForceLocalCameraFallback()
	{
		if ( !IsLocallyControlled() ) return;
		if ( LocalPresentationController.ShouldHandleLocalPresentation( this ) )
		{
			if ( _localCameraComponent != null )
			{
				_localCameraComponent.Enabled = false;
				_localCameraComponent.IsMainCamera = false;
			}
			return;
		}

		if ( _localCameraMovement == null )
			_localCameraMovement = Components.GetInDescendantsOrSelf<CameraMovement>();

		if ( _localCameraComponent == null )
			_localCameraComponent = Components.GetInDescendantsOrSelf<CameraComponent>();

		if ( _localCameraMovement != null && !_localCameraMovement.Enabled )
			_localCameraMovement.Enabled = true;

		if ( _sceneFallbackCamera == null )
		{
			_sceneFallbackCamera = Scene.GetAllComponents<CameraComponent>()
				.FirstOrDefault( c => c?.GameObject?.Name == "Main Camera" )
				?? Scene.GetAllComponents<CameraComponent>()
					.FirstOrDefault( c => c != null && c.Components.GetInAncestorsOrSelf<PlayerMovement>() == null );
		}

		var emergencyCamera = EnsureEmergencyCamera();

		if ( _localCameraComponent != null )
		{
			_localCameraComponent.Enabled = true;
			_localCameraComponent.IsMainCamera = false;
			_localCameraComponent.Priority = 99;
			_localCameraComponent.FieldOfView = GameSettings.FOV;
			if ( _localCameraComponent.GameObject != null )
				_localCameraComponent.GameObject.Enabled = true;
		}

		var renderCamera = emergencyCamera ?? _sceneFallbackCamera ?? _localCameraComponent;
		if ( renderCamera == null ) return;

		renderCamera.Enabled = true;
		renderCamera.IsMainCamera = true;
		renderCamera.Priority = 100;
		renderCamera.FieldOfView = GameSettings.FOV;
		if ( renderCamera.GameObject != null )
			renderCamera.GameObject.Enabled = true;

		foreach ( var other in Scene.GetAllComponents<CameraComponent>() )
		{
			if ( other == null || other == renderCamera || other == _localCameraComponent ) continue;

			other.IsMainCamera = false;
			var otherPlayer = other.Components.GetInAncestorsOrSelf<PlayerMovement>();
			if ( otherPlayer == null || !PlayerIdentity.IsOwnedByLocal( otherPlayer.GameObject ) )
				other.Enabled = false;
		}

		var viewRotation = Head?.WorldRotation ?? GameObject.WorldRotation;
		var viewPosition = Head?.WorldPosition ?? (WorldPosition + Vector3.Up * 64f);

		if ( renderCamera.GameObject != null )
		{
			renderCamera.GameObject.WorldRotation = viewRotation;
			renderCamera.GameObject.WorldPosition = viewPosition;
			renderCamera.GameObject.Enabled = true;
		}

		if ( _localCameraComponent?.GameObject != null )
		{
			_localCameraComponent.GameObject.WorldRotation = viewRotation;
			_localCameraComponent.GameObject.WorldPosition = viewPosition;
		}
	}

	private bool IsOwnedByLocalConnectionStrict()
	{
		if ( !Networking.IsActive ) return true;

		var owner = GameObject?.Network?.Owner;
		var localConn = Connection.Local;
		if ( owner != null && localConn != null )
			return owner.SteamId == localConn.SteamId;

		return false;
	}

	private void EnsureWorldFlashlight()
	{
		var worldFlashlightLocalOffset = Vector3.Forward * 10f + Vector3.Up * 2f;

		var flashlightAnchor = (Head != null && Head.IsValid) ? Head
			: (Body != null && Body.IsValid) ? Body
			: (GameObject != null && GameObject.IsValid()) ? GameObject
			: null;

		if ( flashlightAnchor == null )
		{
			if ( EnableDiagnostics && !_loggedMissingFlashlightAnchor )
			{
				_loggedMissingFlashlightAnchor = true;
				Log.Warning( $"[PlayerMovement Flashlight] No valid flashlight anchor on {GameObject?.Name}. Owned by local: {IsOwnedByLocalConnectionStrict()}" );
			}
			return;
		}

		_loggedMissingFlashlightAnchor = false;

		if ( _worldFlashlightGO == null || !_worldFlashlightGO.IsValid() )
		{
			_worldFlashlightGO = new GameObject( true, "PlayerWorldFlashlight" );
			_worldFlashlightGO.Parent = flashlightAnchor;
			_worldFlashlightGO.LocalPosition = worldFlashlightLocalOffset;
			_worldFlashlightGO.LocalRotation = Rotation.Identity;
			_worldFlashlight = _worldFlashlightGO.Components.Create<SpotLight>();

			_worldFlashlight.Radius = 2000f;
			_worldFlashlight.ConeInner = 12f;
			_worldFlashlight.ConeOuter = 25f;
			_worldFlashlight.LightColor = new Color( 4f, 4f, 3.8f );
			_worldFlashlight.Shadows = false;
			_worldFlashlight.Enabled = false;
			
			var isOwner = IsOwnedByLocalConnectionStrict();
			var anchorPos = flashlightAnchor.WorldPosition;
			if ( EnableDiagnostics )
				Log.Info( $"[PlayerMovement Flashlight] CREATED world light on {GameObject?.Name} (owner={isOwner}, anchor={flashlightAnchor.Name}, pos={anchorPos})" );
		}
		else if ( _worldFlashlightGO.Parent != flashlightAnchor )
		{
			_worldFlashlightGO.Parent = flashlightAnchor;
			_worldFlashlightGO.LocalPosition = worldFlashlightLocalOffset;
			_worldFlashlightGO.LocalRotation = Rotation.Identity;
		}
	}

	private void EnsureLocalViewFlashlight( bool enabled )
	{
		if ( !IsOwnedByLocalConnectionStrict() )
		{
			if ( _localViewFlashlight != null && _localViewFlashlight.IsValid )
				_localViewFlashlight.Enabled = false;
			return;
		}

		var activeCam = Scene.GetAllComponents<CameraComponent>()
			.FirstOrDefault( c => c != null && c.Enabled && c.IsMainCamera )
			?? _emergencyCamera
			?? _localCameraComponent;
		if ( activeCam == null || activeCam.GameObject == null ) return;

		if ( _localViewFlashlightGO == null || !_localViewFlashlightGO.IsValid() || _localViewFlashlightCamera != activeCam )
		{
			if ( _localViewFlashlightGO != null && _localViewFlashlightGO.IsValid() )
				_localViewFlashlightGO.Destroy();

			_localViewFlashlightCamera = activeCam;
			_localViewFlashlightGO = new GameObject( true, "LocalViewFlashlight" );
			_localViewFlashlightGO.Parent = activeCam.GameObject;
			_localViewFlashlightGO.LocalPosition = Vector3.Zero;
			_localViewFlashlightGO.LocalRotation = Rotation.Identity;
			_localViewFlashlight = _localViewFlashlightGO.Components.Create<SpotLight>();
			_localViewFlashlight.Radius = 2000f;
			_localViewFlashlight.ConeInner = 12f;
			_localViewFlashlight.ConeOuter = 25f;
			_localViewFlashlight.LightColor = new Color( 4f, 4f, 3.8f );
			_localViewFlashlight.Shadows = true;
		}

		if ( _localViewFlashlight != null && _localViewFlashlight.IsValid )
			_localViewFlashlight.Enabled = enabled;
	}

	private void UpdateFlashlightState()
	{
		EnsureWorldFlashlight();

		var isOwner = IsOwnedByLocalConnectionStrict();
		if ( isOwner && Input.Pressed( "Flashlight" ) )
		{
			NetworkFlashlightOn = !NetworkFlashlightOn;
			if ( Identity != null )
				Identity.FlashlightOn = NetworkFlashlightOn;
			if ( EnableDiagnostics )
				Log.Info( $"[PlayerMovement Flashlight] TOGGLE by owner: {GameObject?.Name} -> {NetworkFlashlightOn}" );
		}

		// World light: visible to remote players only
		if ( _worldFlashlight != null && _worldFlashlight.IsValid )
		{
			if ( _worldFlashlightGO != null && _worldFlashlightGO.IsValid() )
				_worldFlashlightGO.WorldRotation = Rotation.From( NetworkLookAngles );

			bool isRemoteViewer = !isOwner;
			bool shouldEnable = NetworkFlashlightOn && isRemoteViewer;
			if ( _worldFlashlight.Enabled != shouldEnable )
			{
				_worldFlashlight.Enabled = shouldEnable;
				var owner = GameObject?.Network?.Owner?.SteamId ?? 0;
				var local = Connection.Local?.SteamId ?? 0;
				if ( EnableDiagnostics )
					Log.Info( $"[PlayerMovement Flashlight] SYNC: {GameObject?.Name} owner={owner} local={local} isRemote={isRemoteViewer} syncVal={NetworkFlashlightOn} => enabled={shouldEnable}" );
			}
		}

		EnsureLocalViewFlashlight( NetworkFlashlightOn && isOwner );
	}

	protected override void OnDestroy()
	{
		if ( _worldFlashlightGO != null && _worldFlashlightGO.IsValid() )
			_worldFlashlightGO.Destroy();

		if ( _localViewFlashlightGO != null && _localViewFlashlightGO.IsValid() )
			_localViewFlashlightGO.Destroy();

		if ( _emergencyCameraObject != null && _emergencyCameraObject.IsValid() )
			_emergencyCameraObject.Destroy();

		foreach ( var go in _clothingObjects )
			if ( go != null && go.IsValid() ) go.Destroy();
		_clothingObjects.Clear();
	}

	private bool IsLocallyControlled()
	{
		if ( PlayerIdentity.IsOwnedByLocal( GameObject ) )
			return true;

		var localMovement = PlayerIdentity.Local?.Movement;
		return localMovement != null && localMovement == this;
	}

	private bool IsVisualSelf()
	{
		if ( !IsLocallyControlled() ) return false;

		// In third-person the local body should be fully visible
		var camMovement = Components.GetInDescendantsOrSelf<CameraMovement>();
		if ( camMovement != null && camMovement.IsThirdPerson ) return false;

		var activeCam = Scene.GetAllComponents<CameraComponent>()
			.FirstOrDefault( c => c != null && c.Enabled && c.IsMainCamera )
			?? Scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c != null && c.Enabled );

		if ( activeCam == null )
			return true;

		var cameraPlayer = activeCam.Components.GetInAncestorsOrSelf<PlayerMovement>();
		if ( cameraPlayer != null )
			return cameraPlayer == this;

		return true;
	}

	private static GameObject FindChildByNameRecursive( GameObject parent, string name )
	{
		if ( parent == null ) return null;
		foreach ( var child in parent.Children )
		{
			if ( child.Name == name ) return child;
			var found = FindChildByNameRecursive( child, name );
			if ( found != null ) return found;
		}
		return null;
	}

	private void ResolveHeadBodyRefs()
	{
		if ( Head == null || !Head.IsValid || !Head.IsDescendant( GameObject ) )
			Head = FindChildByNameRecursive( GameObject, "Head" );

		if ( Head == null || !Head.IsValid )
		{
			var localCam = Components.GetAll<CameraComponent>( FindMode.EverythingInSelfAndDescendants )
				.FirstOrDefault( c => c != null );
			if ( localCam?.GameObject?.Parent != null )
				Head = localCam.GameObject.Parent;
		}

		if ( Body == null || !Body.IsValid || !Body.IsDescendant( GameObject ) )
			Body = FindChildByNameRecursive( GameObject, "Body" );

		if ( Body == null || !Body.IsValid )
			Body = GameObject;
	}

	protected override void OnAwake()
	{
		characterController = Components.Get<CharacterController>();
		animationHelper = Components.Get<CitizenAnimationHelper>();

		// Self-heal Head/Body: when a prefab is cloned via Clone(), cross-GO [Property] references
		// aren't remapped and still point to the original "player" scene object.
		// Ensure Head and Body point to children of THIS GO, not an external hierarchy.
		ResolveHeadBodyRefs();

		currentStamina = StaminaMax;
		sprintLocked = false;

		if (characterController != null)
		{
			originalHeight = characterController.Height;
		}

		// initialize UI helper
		PlayerStats.CurrentStamina = currentStamina;
		PlayerStats.StaminaMax = StaminaMax;
		PlayerStats.DebugMessage = string.Empty;

		if ( Identity != null )
		{
			Identity.CurrentStamina = currentStamina;
			Identity.StaminaMax     = StaminaMax;
		}
	}

	protected override void OnStart()
	{
		LocalPresentationController.EnsureForScene( Scene );

		ResolveHeadBodyRefs();
		NetworkLookAngles = (Head?.WorldRotation ?? GameObject.WorldRotation).Angles();

		// Hard-bind camera refs at runtime for network-spawned players.
		_localCameraMovement = Components.GetInDescendantsOrSelf<CameraMovement>();
		_localCameraComponent = Components.GetInDescendantsOrSelf<CameraComponent>();

		if ( _localCameraMovement != null )
		{
			_localCameraMovement.Player = this;
			if ( Head != null ) _localCameraMovement.Head = Head;
			if ( Body != null ) _localCameraMovement.Body = Body;
		}

		// Create world flashlight early so it exists for all peers
		EnsureWorldFlashlight();
	}

	private SkinnedModelRenderer GetBodySMR() =>
		Components.Get<SkinnedModelRenderer>()
		?? Body?.Components.Get<SkinnedModelRenderer>()
		?? Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ).FirstOrDefault();

	private void ApplyCharacterModel( string modelPath )
	{
		if ( string.IsNullOrEmpty( modelPath ) ) return;
		var model = ResourceLibrary.Get<Model>( modelPath );
		if ( model == null ) return;
		var smr = GetBodySMR();
		if ( smr != null ) smr.Model = model;
	}

	private void ApplyCharacterClothing( string clothingConfig )
	{
		foreach ( var old in _clothingObjects )
			if ( old != null && old.IsValid() ) old.Destroy();
		_clothingObjects.Clear();

		var smr = GetBodySMR();
		Log.Info( $"[Clothing] ApplyCharacterClothing called. Config='{clothingConfig}' SMR={smr?.GameObject?.Name ?? "null"}" );

		if ( smr == null || string.IsNullOrEmpty( clothingConfig ) ) return;

		// Diagnostic: test loading the base body model both ways
		var testRL  = ResourceLibrary.Get<Model>( "models/citizen_human/citizen_human_male.vmdl" );
		var testML  = Model.Load( "models/citizen_human/citizen_human_male.vmdl" );
		Log.Info( $"[Clothing] Diag: citizen_human_male ResourceLibrary={( testRL != null ? "OK" : "NULL" )} Model.Load={( testML != null ? "OK" : "NULL" )}" );

		foreach ( var path in clothingConfig.Split( '|' ) )
		{
			var trimmed = path.Trim();
			if ( string.IsNullOrEmpty( trimmed ) ) continue;

			var model = ResourceLibrary.Get<Model>( trimmed );
			if ( model == null ) model = Model.Load( trimmed );
			Log.Info( $"[Clothing] Path='{trimmed}' Model={( model != null ? "OK" : "NULL" )}" );
			if ( model == null ) continue;

			var go = new GameObject( true, "Clothing" );
			go.Parent = smr.GameObject;
			go.LocalPosition = Vector3.Zero;
			go.LocalRotation = Rotation.Identity;
			go.LocalScale    = Vector3.One;

			var clothingSMR = go.Components.Create<SkinnedModelRenderer>();
			clothingSMR.Model           = model;
			clothingSMR.BoneMergeTarget = smr;
			clothingSMR.RenderType      = smr.RenderType;

			_clothingObjects.Add( go );
		}

		Log.Info( $"[Clothing] Applied {_clothingObjects.Count} items." );
	}

	protected override void OnUpdate()
	{
		if ( !Enabled ) return;

		bool isLocallyControlled = IsLocallyControlled();

		// Apply character model + clothing whenever synced values change (works for all clients)
		var currentModel = Identity?.CharacterModel;
		if ( !string.IsNullOrEmpty( currentModel ) && currentModel != _appliedCharacterModel )
		{
			ApplyCharacterModel( currentModel );
			_appliedCharacterModel = currentModel;
		}

		var currentClothing = Identity?.CharacterClothing ?? string.Empty;
		if ( currentClothing != _appliedCharacterClothing )
		{
			ApplyCharacterClothing( currentClothing );
			_appliedCharacterClothing = currentClothing;
		}

		ResolveHeadBodyRefs();
		UpdateFlashlightState();

		if ( isLocallyControlled )
		{
			// Keep local camera bound and active even if prefab references were not remapped.
			if ( _localCameraMovement == null ) _localCameraMovement = Components.GetInDescendantsOrSelf<CameraMovement>();
			if ( _localCameraComponent == null ) _localCameraComponent = Components.GetInDescendantsOrSelf<CameraComponent>();
			if ( _localCameraMovement != null )
			{
				if ( _localCameraMovement.Player != this ) _localCameraMovement.Player = this;
				if ( Head != null && _localCameraMovement.Head != Head ) _localCameraMovement.Head = Head;
				if ( Body != null && _localCameraMovement.Body != Body ) _localCameraMovement.Body = Body;
			}
			if ( _localCameraComponent != null && !_localCameraComponent.Enabled )
				_localCameraComponent.Enabled = true;
			ForceLocalCameraFallback();

			// If pinned by Hunter, disable movement entirely
			if ( IsPinned )
			{
				WishVelocity = Vector3.Zero;
			}
			else
			{
				// Try to recover CharacterController reference if it's null (editor play toggles can leave components in odd states)
				if (characterController is null)
				{
					characterController = Components.Get<CharacterController>();
				}

				//Set our sprinting/crouching states
				isCrouching = Input.Down( "Crouch" );
				// Sprint is a held input but we only allow sprinting when we have stamina and when not crouching
				bool wantsSprint = Input.Down( "Run" );
				isSprinting = wantsSprint && currentStamina > 0f && !isCrouching && !sprintLocked;

				if (Input.Pressed( "Jump" )) Jump();

				// update stamina & cooldown timers
				if (jumpCooldownRemaining > 0f) jumpCooldownRemaining -= Time.Delta;

				if (isSprinting && (characterController != null && characterController.IsOnGround))
				{
					currentStamina = MathF.Max(0f, currentStamina - (StaminaDrainRate * Time.Delta));
					if (currentStamina <= 0f)
					{
						isSprinting = false;
						sprintLocked = true; // prevent immediate re-sprint (hysteresis)
					}
				}
				else
				{
					currentStamina = MathF.Min(StaminaMax, currentStamina + (StaminaRecoverRate * Time.Delta));
					if (sprintLocked && currentStamina >= StaminaMax * 0.25f)
						sprintLocked = false;
				}

				// publish to UI helper
				PlayerStats.CurrentStamina = currentStamina;
				PlayerStats.StaminaMax = StaminaMax;

				if ( Identity != null )
				{
					Identity.CurrentStamina = currentStamina;
					Identity.StaminaMax     = StaminaMax;
				}

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

					if (!ccPresent)
					{
						PlayerStats.DebugMessage += "\n(Missing CharacterController on player entity)";
					}
					bool camPresent = _localCameraComponent != null;
					PlayerStats.DebugMessage += $" Cam:{(camPresent?"Y":"N")}";

					if (!headPresent && !bodyPresent)
					{
						PlayerStats.DebugMessage += "\n(Missing Head/Body references on PlayerMovement component)";
					}
				}
				else
				{
					if (string.IsNullOrEmpty(PlayerStats.DebugMessage) == false)
						PlayerStats.DebugMessage = string.Empty;
				}
			}
		}

		ApplyNetworkAnimationState();
		RotateBody();
		UpdateAnimations();

		if ( isLocallyControlled )
		{
			UpdateFootsteps();
			// allow crouch state transitions (press/release logic)
			UpdateCrouch();
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !IsLocallyControlled() ) return; // Remote/stale local player — never simulate local input/movement
		if ( !Enabled ) return;

		ResolveHeadBodyRefs();

		BuildWishVelocity();		
		RotateBody();
		Move();
	}

	void BuildWishVelocity()
	{
		WishVelocity = 0;
		
		// use Head if available, otherwise fall back to Body; if neither, use identity rotation
		Rotation rot;
		if (Head is not null) rot = Head.WorldRotation;
		else if (Body is not null) rot = Body.WorldRotation;
		else rot = GameObject.WorldRotation;
		if(Input.Down( "Forward" ))		WishVelocity += rot.Forward;
		if(Input.Down( "Backward"))		WishVelocity += rot.Backward;
		if(Input.Down( "Left" 	 ))		WishVelocity += rot.Left;
		if(Input.Down( "Right" 	 ))		WishVelocity += rot.Right;

		WishVelocity = WishVelocity.WithZ( 0 );
		if(!WishVelocity.IsNearZeroLength) WishVelocity = WishVelocity.Normal;

		// determine speed based on state and stamina
		float targetSpeed = Speed;
		if ( PlayerStats.IsIncapacitated ) targetSpeed = CrouchSpeed * 0.5f;
		else if (isCrouching) targetSpeed = CrouchSpeed;
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
		if ( Body is null || Head is null ) return;

		var lookRotation = IsLocallyControlled()
			? Head.WorldRotation
			: NetworkLookAngles.ToRotation();
		var targetAngle = new Angles( 0, lookRotation.Yaw(), 0 ).ToRotation();
		float rotateDifference = Body.WorldRotation.Distance( targetAngle );
		var velocity = IsLocallyControlled()
			? characterController?.Velocity ?? Vector3.Zero
			: NetworkVelocity;

		if(rotateDifference > 50f || velocity.Length > 10f)
		{
			Body.WorldRotation = Rotation.Lerp(Body.WorldRotation, targetAngle, Time.Delta * 2f);

			// CitizenAnimationHelper is on the Player root — rotate it to match body facing.
			// Save and restore Head world rotation so the camera doesn't spin when root changes.
			var savedHeadRot = Head.WorldRotation;
			GameObject.WorldRotation = Body.WorldRotation;
			Head.WorldRotation = savedHeadRot;
		}
	}

	void Jump() {
		if(!characterController.IsOnGround) return;
		if (jumpCooldownRemaining > 0f) return;

		// reset vertical velocity and apply a consistent upward impulse
		characterController.Velocity = characterController.Velocity.WithZ(0);
		characterController.Punch(Vector3.Up * JumpForce);
		if ( animationHelper is not null && Body?.Components.Get<SkinnedModelRenderer>() is not null )
			animationHelper.TriggerJump();
		jumpCooldownRemaining = JumpCooldown;

		// Drain stamina on jump
		currentStamina = MathF.Max(0f, currentStamina - 0.5f);
	}

	void UpdateAnimations()
	{
		if ( animationHelper is null ) return;
		// SkinnedModelRenderer may be on the Player root, not the Body child
		var smr = Components.Get<SkinnedModelRenderer>()
			?? Body?.Components.Get<SkinnedModelRenderer>()
			?? Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ).FirstOrDefault();
		if ( smr is null ) return;

		var isLocal = IsLocallyControlled();
		var isVisualSelf = IsVisualSelf();
		var sourceRotation = Head?.WorldRotation ?? Body?.WorldRotation ?? GameObject.WorldRotation;
		var aimRotation = isLocal ? sourceRotation : NetworkLookAngles.ToRotation();
		var animWishVelocity = isLocal ? WishVelocity : NetworkWishVelocity;
		var animVelocity = isLocal ? characterController.Velocity : NetworkVelocity;
		bool isIncapacitated = Identity?.IsIncapacitated ?? PlayerStats.IsIncapacitated;

		// Visibility ownership rule: local first-person body is shadow-only, remote players are fully visible.
		smr.RenderType = isVisualSelf
			? ModelRenderer.ShadowRenderType.ShadowsOnly
			: ModelRenderer.ShadowRenderType.On;

		// Downed state: full crouch, no movement, hold pistol
		if ( isIncapacitated )
		{
			animationHelper.WithWishVelocity( Vector3.Zero );
			animationHelper.WithVelocity( Vector3.Zero );
			animationHelper.AimAngle = aimRotation;
			animationHelper.IsGrounded = true;
			animationHelper.HoldType = CitizenAnimationHelper.HoldTypes.Pistol;
			animationHelper.DuckLevel = 1.0f;
			animationHelper.MoveStyle = CitizenAnimationHelper.MoveStyles.Run;
			return;
		}

		animationHelper.WithWishVelocity( animWishVelocity );
		animationHelper.WithVelocity( animVelocity );
		animationHelper.AimAngle = aimRotation;
		bool isGrounded = isLocal
			? (characterController?.IsOnGround ?? true)
			: (animVelocity.WithZ( 0f ).Length < 5f || (characterController?.IsOnGround ?? false));
		animationHelper.IsGrounded = isGrounded;
		animationHelper.WithLook( aimRotation.Forward, 1f, 0.75f, 0.5f );
		animationHelper.MoveStyle = CitizenAnimationHelper.MoveStyles.Run;
		animationHelper.DuckLevel  = isCrouching ? 1f : 0f;
	}

	private void ApplyNetworkAnimationState()
	{
		if ( IsLocallyControlled() )
		{
			NetworkWishVelocity = WishVelocity;
			NetworkVelocity = characterController?.Velocity ?? Vector3.Zero;
			NetworkLookAngles = (Head?.WorldRotation ?? GameObject.WorldRotation).Angles();
			return;
		}

		if ( Head != null )
			Head.WorldRotation = NetworkLookAngles.ToRotation();
	}
	void UpdateFootsteps()
	{
		if ( characterController is null ) return;

		// Short downward trace — works even if the ground plane has no CharacterController-detected collision
		var groundTrace = Scene.Trace
			.Ray( WorldPosition + Vector3.Up * 5f, WorldPosition - Vector3.Up * 25f )
			.WithoutTags( "player", "trigger" )
			.Run();
		if ( !groundTrace.Hit ) return;

		// Only step when actually moving horizontally
		var flatVel = characterController.Velocity.WithZ( 0 );
		if ( flatVel.Length < 20f )
		{
			footstepTimer = 0f;
			return;
		}

		float interval = isCrouching ? CrouchStepInterval : (isSprinting ? RunStepInterval : WalkStepInterval);
		footstepTimer -= Time.Delta;

		if ( footstepTimer <= 0f )
		{
			footstepTimer = interval;

			SoundEvent sound = isCrouching ? FootstepCrouch : (isSprinting ? FootstepRun : FootstepWalk);
			// Fall back: crouch→walk, run→walk if not assigned
			sound ??= FootstepWalk;

			if ( sound != null )
				Sound.Play( sound, WorldPosition );
		}
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


