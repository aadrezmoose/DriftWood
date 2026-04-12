using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed class CameraMovement : Component
{
	[Property] public bool EnableNetworkDiagnostics { get; set; } = false;

	[Property] public PlayerMovement Player { get; set; }
	[Property] public GameObject Body { get; set; }
	[Property] public GameObject Head { get; set; }
	[Property] public float Distance { get; set; } = 0f;
	[Property] public float ThirdPersonDistance { get; set; } = 150f;
	[Property] public float ShakeDecay { get; set; } = 5f;

	public bool IsThirdPerson => Distance > 0f;

	/// <summary>Local offset from Head position to actual eye position. Tune if camera clips inside head mesh.</summary>
	[Property] public Vector3 EyeOffset { get; set; } = new Vector3( 0f, 0f, 0f );

	/// <summary>Drag the SkinnedModelRenderer from the player body here directly.</summary>
	[Property] public SkinnedModelRenderer BodyModelRenderer { get; set; }

	public bool isFirstPerson => Distance == 0f;
	[Property] public float RecoilRecovery { get; set; } = 6f;

	/// <summary>Exact ray used to render this frame. Use for interaction raycasts instead of WorldPosition/WorldRotation.</summary>
	public Ray EyeRay { get; private set; }

	private int _eyeRayDebugFrameCount = 0;
	private CameraComponent camera;
	private Vector3 CurrentOffset = Vector3.Zero;
	private float shakeMagnitude = 0f;
	private float recoilOffset = 0f;

	// Cached list of all local-owned renderers that should hide in first-person.
	private readonly List<ModelRenderer> bodyRenderers = new();

	private PlayerIdentity _identity;
	private PlayerIdentity Identity => _identity ??= Player?.Components.GetInDescendantsOrSelf<PlayerIdentity>();

	// Tracks which player we've already subscribed health events to — avoids double-subscribing
	private PlayerMovement _subscribedPlayer;
	private System.Guid _lastLoggedBoundPlayerId;
	private bool _loggedWaitingForLocalPlayer;

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

	private static bool IsLocallyOwnedPlayer( PlayerMovement pm )
	{
		if ( pm == null ) return false;
		return PlayerIdentity.IsOwnedByLocal( pm.GameObject );
	}

	private bool IsLocalMainCamera()
	{
		if ( camera == null ) camera = Components.Get<CameraComponent>();
		return camera != null && camera.Enabled && camera.IsMainCamera;
	}

	private PlayerMovement FindLocalPlayerFallback()
	{
		// Prefer owner-local player movement in multiplayer. In single-player, first available is fine.
		if ( Networking.IsActive )
			return Scene?.GetAllComponents<PlayerMovement>()?.Where( p => IsLocallyOwnedPlayer( p ) )?.LastOrDefault();

		return Scene?.GetAllComponents<PlayerMovement>()?.FirstOrDefault( p => p != null );
	}

	private void ActivateLocalCamera()
	{
		if ( camera == null ) camera = Components.Get<CameraComponent>();
		if ( camera == null ) return;

		camera.Enabled = true;
		camera.IsMainCamera = true;
		camera.Priority = 100;

		foreach ( var other in Scene.GetAllComponents<CameraComponent>() )
		{
			if ( other == null || other == camera ) continue;

			var otherPlayer = other.Components.GetInAncestorsOrSelf<PlayerMovement>();
			if ( otherPlayer == null )
			{
				other.IsMainCamera = false;
				other.Enabled = false;
				continue;
			}

			if ( otherPlayer != Player )
			{
				other.IsMainCamera = false;
				other.Enabled = false;
			}
		}
	}

	private void ResolveHeadBodyRefs( PlayerMovement pm )
	{
		if ( pm == null ) return;

		if ( pm.Head != null ) Head = pm.Head;
		if ( pm.Body != null ) Body = pm.Body;

		if ( Head == null )
			Head = FindChildByNameRecursive( pm.GameObject, "Head" );

		if ( Body == null )
			Body = FindChildByNameRecursive( pm.GameObject, "Body" ) ?? pm.GameObject;
	}

	public void Shake( float magnitude )
	{
		shakeMagnitude = System.Math.Max( shakeMagnitude, magnitude );
	}

	protected override void OnStart()
	{
		LocalPresentationController.EnsureForScene( Scene );

		GameSettings.Load();
		camera = Components.Get<CameraComponent>();

		// Self-heal: when a prefab is cloned via Clone(), cross-GO [Property] references aren't
		// remapped — Camera.Player may still point to the original "player" scene object instead of
		// this clone's PlayerMovement. On joining clients that object is host-owned (IsProxy=true),
		// so the camera would be disabled and never re-acquired. Fix by searching ancestors.
		var hierarchyPM = Components.GetInAncestorsOrSelf<PlayerMovement>();
		if ( hierarchyPM != null )
		{
			if ( EnableNetworkDiagnostics && Player != hierarchyPM )
				Log.Info( $"[CameraMovement] Re-wiring Player from '{Player?.GameObject?.Name ?? "null"}' to '{hierarchyPM.GameObject?.Name}'" );
			Player = hierarchyPM;
			ResolveHeadBodyRefs( Player );
		}

		// The visible citizen model is a SkinnedModelRenderer on the Player root, not on Body.
		// Body has its own (already-disabled) renderer — searching from Body finds the wrong one.
		if ( BodyModelRenderer is null && Player?.GameObject is not null )
			BodyModelRenderer = Player.GameObject.Components.Get<SkinnedModelRenderer>();

		CollectBodyRenderers();

		// Subscribe to health events on the player if already available.
		// In multiplayer, the player may not exist yet — OnUpdate() retries via TrySubscribeToPlayer().
		TrySubscribeToPlayer( Player );
	}

	/// <summary>
	/// Subscribes camera shake and damage-indicator events to a player's HealthComponent.
	/// Safe to call repeatedly — only acts when <paramref name="pm"/> differs from the last subscribed player.
	/// </summary>
	private HealthComponent _subscribedHealth;

	private void TrySubscribeToPlayer( PlayerMovement pm )
	{
		if ( pm == null || pm == _subscribedPlayer ) return;

		// Unsubscribe from old health component before switching
		if ( _subscribedHealth != null )
		{
			_subscribedHealth.OnDamageTaken -= OnDamageTaken;
			_subscribedHealth.OnDamageTakenWithAttacker -= OnDamageTakenWithAttacker;
			_subscribedHealth = null;
		}

		_subscribedPlayer = pm;
		_identity = null; // reset lazy identity cache

		// Re-wire Head and Body from the newly found player so the camera
		// positions correctly when spawned via GameManager in multiplayer.
		ResolveHeadBodyRefs( pm );

		// Search from the player root, not the scene root, to avoid finding enemy HealthComponents
		var health = pm.Components.GetInDescendantsOrSelf<HealthComponent>();
		if ( health != null )
		{
			_subscribedHealth = health;
			health.OnDamageTaken += OnDamageTaken;
			health.OnDamageTakenWithAttacker += OnDamageTakenWithAttacker;
		}

		// Refresh body renderer reference for the new player
		if ( BodyModelRenderer is null )
			BodyModelRenderer = pm.GameObject.Components.Get<SkinnedModelRenderer>();
		CollectBodyRenderers();
	}

	private void OnDamageTaken( float dmg )
	{
		if ( !IsValid ) return;
		Shake( dmg * (PlayerStats.IsIncapacitated ? 0.1f : 0.8f) );
	}

	protected override void OnDestroy()
	{
		if ( _subscribedHealth != null )
		{
			_subscribedHealth.OnDamageTaken -= OnDamageTaken;
			_subscribedHealth.OnDamageTakenWithAttacker -= OnDamageTakenWithAttacker;
			_subscribedHealth = null;
		}
	}

	private void CollectBodyRenderers()
	{
		bodyRenderers.Clear();
		if ( Player?.GameObject is not null )
		{
			foreach ( var renderer in Player.GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			{
				if ( renderer != null )
					bodyRenderers.Add( renderer );
			}
		}
	}

	private void UpdateOwnedRendererVisibility( bool hideOwnedRenderers )
	{
		var renderType = hideOwnedRenderers
			? ModelRenderer.ShadowRenderType.ShadowsOnly
			: ModelRenderer.ShadowRenderType.On;

		if ( BodyModelRenderer != null )
			BodyModelRenderer.RenderType = renderType;

		foreach ( var renderer in bodyRenderers )
		{
			if ( renderer == null || renderer == BodyModelRenderer )
				continue;

			renderer.RenderType = renderType;
		}
	}

	private void OnDamageTakenWithAttacker( float damage, GameObject attacker )
	{
		if ( attacker == null || Head is null ) return;

		var toAttacker = (attacker.WorldPosition - Head.WorldPosition).WithZ( 0 ).Normal;
		var forward = Head.WorldRotation.Forward.WithZ( 0 ).Normal;
		var right = Head.WorldRotation.Right.WithZ( 0 ).Normal;

		float dotForward = Vector3.Dot( toAttacker, forward );
		float dotRight = Vector3.Dot( toAttacker, right );

		float angle = System.MathF.Atan2( dotRight, dotForward ) * (180f / System.MathF.PI);
		PlayerStats.AddDamageIndicator( angle );
		Identity?.AddDamageIndicator( angle );
	}

	protected override void OnUpdate()
	{
		// Compute EyeRay early so it's always available, even if Player is null
		var tempEyeWorldPos = Head?.WorldPosition ?? (Player?.WorldPosition + Vector3.Up * 64f) ?? WorldPosition;
		var tempEyeAngles = (Head?.WorldRotation ?? Player?.GameObject.WorldRotation ?? WorldRotation).Angles();
		var tempCamPos = tempEyeWorldPos + Vector3.Zero + tempEyeAngles.ToRotation() * EyeOffset;
		EyeRay = new Ray( tempCamPos, tempEyeAngles.ToRotation().Forward );

		if ( camera == null ) camera = Components.Get<CameraComponent>();
		var hierarchyPlayer = Components.GetInAncestorsOrSelf<PlayerMovement>() ?? Player;
		if ( hierarchyPlayer != null && Player != hierarchyPlayer )
		{
			Player = hierarchyPlayer;
			TrySubscribeToPlayer( Player );
		}

		bool externalPresentation = LocalPresentationController.ShouldHandleLocalPresentation( hierarchyPlayer );

		var isPaused = Identity?.IsPaused ?? PlayerStats.IsPaused;
		var isDead = Identity?.IsDead ?? PlayerStats.IsDead;
		var levelComplete = Identity?.LevelComplete ?? PlayerStats.LevelComplete;
		var isIncapacitated = Identity?.IsIncapacitated ?? PlayerStats.IsIncapacitated;
		var pendingRecoil = Identity?.PendingRecoil ?? PlayerStats.PendingRecoil;

		if ( Networking.IsActive )
		{
			// CameraMovement must stay attached to its own player hierarchy.
			// Proxy cameras should never hijack the local player, otherwise they can hide remote bodies
			// while their separate world-weapon renderers remain visible as floating duplicate guns.
			bool hasLocalCamera = IsLocalMainCamera();
			bool playerIsProxy = Player == null || ( !IsLocallyOwnedPlayer( Player ) && !hasLocalCamera );
			if ( externalPresentation )
				playerIsProxy = false;

			// HUD diagnostic
			if ( EnableNetworkDiagnostics )
			{
				var local = PlayerIdentity.Local;
				var owner = Player?.GameObject?.Network?.Owner;
				PlayerStats.DebugMessage = $"CAM: Player={Player?.GameObject?.Name ?? "null"} PlayerProxy={Player?.IsProxy} Owner={owner?.DisplayName ?? "none"}({owner?.SteamId}) CamProxy={IsProxy} Local={local?.GameObject?.Name ?? "null"} AllCount={PlayerIdentity.All.Count} Pause={isPaused} Dead={isDead} Win={levelComplete} MX={Input.MouseDelta.x:F2} MY={Input.MouseDelta.y:F2}";
			}

			if ( playerIsProxy )
			{
				// Still no local player yet — disable camera and wait.
				if ( camera != null && camera.Enabled ) camera.Enabled = false;

				// Proxy body should be visible to others.
				if ( BodyModelRenderer != null )
					BodyModelRenderer.RenderType = Sandbox.ModelRenderer.ShadowRenderType.On;
				else if ( bodyRenderers.Count > 0 )
					foreach ( var r in bodyRenderers )
						if ( r is not null ) r.RenderType = Sandbox.ModelRenderer.ShadowRenderType.On;

				if ( EnableNetworkDiagnostics && !_loggedWaitingForLocalPlayer )
				{
					_loggedWaitingForLocalPlayer = true;
					Log.Info( Player == null
						? "[CameraMovement] Waiting for local player to spawn..."
						: $"[CameraMovement] Player '{Player.GameObject?.Name}' is proxy — waiting for local player..." );
				}
				return;
			}

			_loggedWaitingForLocalPlayer = false;

			if ( !externalPresentation )
				ActivateLocalCamera();

			if ( EnableNetworkDiagnostics && Player.GameObject != null &&
			     _lastLoggedBoundPlayerId != Player.GameObject.Id )
			{
				_lastLoggedBoundPlayerId = Player.GameObject.Id;
				Log.Info( $"[CameraMovement] Local camera active for GO={Player.GameObject.Name} Id={Player.GameObject.Id}" );
			}
		}
		else
		{
			// Single-player: re-acquire if Player is missing.
			if ( Player == null )
			{
				var fallback = FindLocalPlayerFallback();
				if ( fallback != null )
				{
					Player = fallback;
					TrySubscribeToPlayer( Player );
				}
			}

			// Single-player: ensure camera is active
			if ( camera != null && !camera.Enabled )
				camera.Enabled = true;
		}

		PlayerStats.TickDamageIndicators( Time.Delta );
		Identity?.TickDamageIndicators( Time.Delta );

		if ( Player != null && (Head == null || Body == null) )
			ResolveHeadBodyRefs( Player );

		if ( Player is null ) return;

		// Re-collect every frame so late-added weapon/world-model renderers are included.
		// Multiplayer joins and weapon equips can add descendants after OnStart().
		CollectBodyRenderers();

		bool hideOwnedRenderers = IsLocallyOwnedPlayer( Player ) && (isFirstPerson || isIncapacitated);
		UpdateOwnedRendererVisibility( hideOwnedRenderers );

		// Third-person toggle — bind "ThirdPerson" action in S&box Settings → Bindings
		if ( Input.Pressed( "ThirdPerson" ) && IsLocallyOwnedPlayer( Player ) )
			Distance = IsThirdPerson ? 0f : ThirdPersonDistance;

		var lookSourceRotation = Head is not null ? Head.WorldRotation : Player.GameObject.WorldRotation;
		var eyeAngles = lookSourceRotation.Angles();
		if ( !isDead && !levelComplete && !isPaused && !externalPresentation && (IsLocallyOwnedPlayer( Player ) || IsLocalMainCamera()) )
		{
			eyeAngles.pitch -= Input.MouseDelta.y * -0.1f * GameSettings.Sensitivity;
			eyeAngles.yaw -= Input.MouseDelta.x * 0.1f * GameSettings.Sensitivity;
		}

		if ( externalPresentation && camera != null )
		{
			camera.Enabled = false;
			camera.IsMainCamera = false;
		}

		// Recoil — kick up on fire, smooth recovery back to original aim
		// Apply only the delta each frame so recovery actually returns to center
		float prevRecoil = recoilOffset;
		recoilOffset = MathX.Lerp( recoilOffset, 0f, Time.Delta * RecoilRecovery );
		recoilOffset += pendingRecoil;
		if ( Identity != null ) Identity.PendingRecoil = 0f;
		PlayerStats.PendingRecoil = 0f;
		eyeAngles.pitch -= (recoilOffset - prevRecoil);
		eyeAngles.roll = 0;
		eyeAngles.pitch = eyeAngles.pitch.Clamp( -89.9f, 89.9f );
		if ( isIncapacitated ) eyeAngles.roll = MathX.Lerp( eyeAngles.roll, 25f, Time.Delta * 3f );
		else eyeAngles.roll = MathX.Lerp( eyeAngles.roll, 0f, Time.Delta * 5f );
		if ( Head is not null )
		{
			Head.WorldRotation = eyeAngles.ToRotation();
		}
		else
		{
			// Fallback for network-spawned players whose Head ref did not bind.
			// Keep player yaw in sync so movement follows view direction.
			Player.GameObject.WorldRotation = new Angles( 0f, eyeAngles.yaw, 0f ).ToRotation();
		}

		var targetOffset = Vector3.Zero;
		if ( Player.isCrouching ) targetOffset += Vector3.Down * 32f;
		if ( isIncapacitated ) targetOffset += Vector3.Down * 35f;
		CurrentOffset = Vector3.Lerp( CurrentOffset, targetOffset, Time.Delta * 5f );

		var shakeOffset = Vector3.Zero;
		if ( shakeMagnitude > 0.01f )
		{
			var rng = Game.Random;
			if ( rng is not null )
				shakeOffset = new Vector3(
					rng.Float( -shakeMagnitude, shakeMagnitude ),
					rng.Float( -shakeMagnitude, shakeMagnitude ),
					rng.Float( -shakeMagnitude, shakeMagnitude )
				);
			shakeMagnitude = MathX.Lerp( shakeMagnitude, 0f, Time.Delta * ShakeDecay );
		}
		else
		{
			shakeMagnitude = 0f;
		}

		if ( camera is null ) camera = Components.Get<CameraComponent>();
		if ( camera is null ) return;

		camera.FieldOfView = GameSettings.FOV;

		var eyeWorldPosition = Head is not null
			? Head.WorldPosition
			: Player.WorldPosition + Vector3.Up * 64f;
		var camPos = eyeWorldPosition + CurrentOffset + eyeAngles.ToRotation() * EyeOffset;
		if ( !isFirstPerson )
		{
			var camForward = eyeAngles.ToRotation().Forward;
			var camTrace = Scene.Trace.Ray( camPos, camPos - (camForward * Distance) )
				.WithoutTags( "player", "trigger" )
				.Run();
			camPos = camTrace.Hit ? camTrace.HitPosition + camTrace.Normal : camTrace.EndPosition;
		}

		// Update EyeRay with final camera position (including shake and third-person offset)
		EyeRay = new Ray( camPos + shakeOffset, eyeAngles.ToRotation().Forward );

		// DEBUG
		if ( _eyeRayDebugFrameCount % 60 == 0 )
			Log.Warning( $"[CM] EyeRay final: camPos={camPos:F1}, shake={shakeOffset:F1}, eyeAngles={eyeAngles}" );
		_eyeRayDebugFrameCount++;

		WorldPosition = camPos + shakeOffset;
		WorldRotation = eyeAngles.ToRotation();
	}
}
