using Sandbox;
using System.Linq;

public sealed class LocalPresentationController : Component
{
	[Property] public bool EnableDiagnostics { get; set; } = false;
	[Property] public Vector3 FirstPersonOffset { get; set; } = new Vector3( 0f, 0f, 10f );
	[Property] public float CrouchCameraOffset { get; set; } = 32f;
	[Property] public float IncapCameraOffset { get; set; } = 35f;
	[Property] public float OffsetLerpSpeed { get; set; } = 5f;

	public static LocalPresentationController Instance { get; private set; }

	private CameraComponent renderCamera;
	private GameObject viewModelAnchor;
	private float yaw;
	private float pitch;
	private bool initializedAngles;
	private Vector3 currentOffset = Vector3.Zero;
	private float nextDuplicateCleanupLogTime;

	public CameraComponent RenderCamera => renderCamera;

	public static bool Exists => Instance != null && Instance.IsValid;

	private static bool SceneUsesBuiltInNetworkHelper( Scene scene )
	{
		return scene?.GetAllComponents<Component>()?
			.Any( component => component != null
				&& component.IsValid
				&& component.GetType().FullName == "Sandbox.NetworkHelper" ) == true;
	}

	public static LocalPresentationController EnsureForScene( Scene scene )
	{
		if ( Networking.IsActive && SceneUsesBuiltInNetworkHelper( scene ) )
			return null;

		if ( Instance != null && Instance.IsValid )
			return Instance;

		var existing = scene?.GetAllComponents<LocalPresentationController>()
			?.FirstOrDefault( controller => controller != null && controller.IsValid );

		if ( existing != null )
			return existing;

		var controllerObject = new GameObject( true, "LocalPresentationController" );
		return controllerObject.Components.Create<LocalPresentationController>();
	}

	public static bool ShouldHandleLocalPresentation( PlayerMovement player )
	{
		if ( Networking.IsActive && SceneUsesBuiltInNetworkHelper( player?.Scene ) )
			return false;

		return Exists && player != null && PlayerIdentity.IsOwnedByLocal( player.GameObject );
	}

	protected override void OnStart()
	{
		if ( Networking.IsActive && SceneUsesBuiltInNetworkHelper( Scene ) )
		{
			Enabled = false;
			return;
		}

		if ( Instance != null && Instance.IsValid && !ReferenceEquals( Instance, this ) )
		{
			Enabled = false;
			return;
		}

		Instance = this;
		GameSettings.Load();
	}

	protected override void OnDestroy()
	{
		if ( viewModelAnchor != null && viewModelAnchor.IsValid )
			viewModelAnchor.Destroy();

		if ( ReferenceEquals( Instance, this ) )
			Instance = null;
	}

	protected override void OnUpdate()
	{
		EnsureRenderCamera();
		if ( renderCamera == null ) return;
		EnsureViewModelAnchor();

		var player = FindLocalPlayer();
		if ( player == null || player.GameObject == null ) return;
		CleanupDuplicateViewModels( player );

		var identity = player.Components.GetInDescendantsOrSelf<PlayerIdentity>();
		bool isPaused = identity?.IsPaused ?? PlayerStats.IsPaused;
		bool isDead = identity?.IsDead ?? PlayerStats.IsDead;
		bool levelComplete = identity?.LevelComplete ?? PlayerStats.LevelComplete;
		bool isIncapacitated = identity?.IsIncapacitated ?? PlayerStats.IsIncapacitated;

		var head = player.Head ?? FindChildByNameRecursive( player.GameObject, "Head" );
		if ( !initializedAngles )
		{
			var sourceRotation = head?.WorldRotation ?? player.GameObject.WorldRotation;
			var ang = sourceRotation.Angles();
			yaw = ang.yaw;
			pitch = ang.pitch;
			initializedAngles = true;
		}

		if ( !isPaused && !isDead && !levelComplete )
		{
			pitch -= Input.MouseDelta.y * -0.1f * GameSettings.Sensitivity;
			yaw -= Input.MouseDelta.x * 0.1f * GameSettings.Sensitivity;
		}

		pitch = pitch.Clamp( -89.9f, 89.9f );
		var eyeRotation = new Angles( pitch, yaw, 0f ).ToRotation();
		player.GameObject.WorldRotation = new Angles( 0f, yaw, 0f ).ToRotation();

		if ( head != null )
			head.WorldRotation = eyeRotation;

		var targetOffset = Vector3.Zero;
		if ( player.isCrouching ) targetOffset += Vector3.Down * CrouchCameraOffset;
		if ( isIncapacitated ) targetOffset += Vector3.Down * IncapCameraOffset;
		currentOffset = Vector3.Lerp( currentOffset, targetOffset, Time.Delta * OffsetLerpSpeed );

		var eyePosition = head != null ? head.WorldPosition : player.WorldPosition + Vector3.Up * 64f;
		var cameraPosition = eyePosition + currentOffset + eyeRotation * FirstPersonOffset;

		renderCamera.GameObject.WorldPosition = cameraPosition;
		renderCamera.GameObject.WorldRotation = eyeRotation;
		renderCamera.GameObject.Enabled = true;
		renderCamera.Enabled = true;
		renderCamera.IsMainCamera = true;
		renderCamera.Priority = 100;
		renderCamera.FieldOfView = GameSettings.FOV;

		var localPlayerCamera = player.Components.GetInDescendantsOrSelf<CameraComponent>();
		if ( localPlayerCamera != null && localPlayerCamera != renderCamera )
		{
			localPlayerCamera.GameObject.WorldPosition = cameraPosition;
			localPlayerCamera.GameObject.WorldRotation = eyeRotation;
			localPlayerCamera.IsMainCamera = false;
			localPlayerCamera.Enabled = false;
		}

		foreach ( var other in Scene.GetAllComponents<CameraComponent>() )
		{
			if ( other == null || other == renderCamera ) continue;

			other.IsMainCamera = false;
			if ( other != localPlayerCamera )
				other.Enabled = false;
		}

		if ( EnableDiagnostics )
			PlayerStats.DebugMessage = $"LP: Player={player.GameObject.Name} MainCam={renderCamera.GameObject.Name} MX={Input.MouseDelta.x:F2} MY={Input.MouseDelta.y:F2}";
	}

	private void EnsureRenderCamera()
	{
		if ( renderCamera != null && renderCamera.IsValid )
			return;

		renderCamera = Scene.GetAllComponents<CameraComponent>()
			.FirstOrDefault( c => c?.GameObject?.Name == "Main Camera" )
			?? Scene.GetAllComponents<CameraComponent>()
				.FirstOrDefault( c => c != null && c.Components.GetInAncestorsOrSelf<PlayerMovement>() == null );

		if ( renderCamera != null )
			return;

		var cameraObject = new GameObject( true, "RuntimeMainCamera" );
		renderCamera = cameraObject.Components.Create<CameraComponent>();
	}

	public GameObject GetOrCreateViewModelAnchor( PlayerMovement player = null )
	{
		if ( player != null && !PlayerIdentity.IsOwnedByLocal( player.GameObject ) )
			return null;

		EnsureRenderCamera();
		return EnsureViewModelAnchor();
	}

	private GameObject EnsureViewModelAnchor()
	{
		if ( renderCamera == null || !renderCamera.IsValid )
			return null;

		if ( viewModelAnchor == null || !viewModelAnchor.IsValid )
			viewModelAnchor = new GameObject( true, "ViewModelAnchor" );

		var cameraObject = renderCamera.GameObject;
		if ( viewModelAnchor.Parent != cameraObject )
			viewModelAnchor.Parent = cameraObject;

		viewModelAnchor.LocalPosition = Vector3.Zero;
		viewModelAnchor.LocalRotation = Rotation.Identity;
		viewModelAnchor.Enabled = true;
		return viewModelAnchor;
	}

	private void CleanupDuplicateViewModels( PlayerMovement player )
	{
		var localViewModel = player.Components.GetInDescendantsOrSelf<GunViewModel>();
		var allowedModelObject = localViewModel?.ModelObject;

		if ( renderCamera?.GameObject != null )
			DestroyDuplicateGunModelsRecursive( renderCamera.GameObject, allowedModelObject );

		var head = player.Head ?? FindChildByNameRecursive( player.GameObject, "Head" );
		if ( head != null && head.IsValid )
			DestroyDuplicateGunModelsRecursive( head, allowedModelObject );
	}

	private void DestroyDuplicateGunModelsRecursive( GameObject parent, GameObject allowedModelObject )
	{
		if ( parent == null || !parent.IsValid )
			return;

		foreach ( var child in parent.Children.ToArray() )
		{
			if ( child == null || !child.IsValid )
				continue;

			if ( child.Name == "GunModel" && child != allowedModelObject )
			{
				if ( EnableDiagnostics && Time.Now >= nextDuplicateCleanupLogTime )
				{
					nextDuplicateCleanupLogTime = Time.Now + 1f;
					var modelPath = child.Components.Get<SkinnedModelRenderer>()?.Model?.ResourcePath ?? "null";
					Log.Warning( $"[LocalPresentationController] Destroying duplicate GunModel '{child.Name}' model='{modelPath}' parent='{child.Parent?.Name ?? "null"}'" );
				}

				child.Destroy();
				continue;
			}

			DestroyDuplicateGunModelsRecursive( child, allowedModelObject );
		}
	}

	private PlayerMovement FindLocalPlayer()
	{
		if ( PlayerIdentity.Local?.Movement != null )
			return PlayerIdentity.Local.Movement;

		return Scene.GetAllComponents<PlayerMovement>()
			.Where( p => p != null && p.GameObject != null && PlayerIdentity.IsOwnedByLocal( p.GameObject ) )
			.LastOrDefault();
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
}