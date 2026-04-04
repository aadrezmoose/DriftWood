using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System;

/// <summary>
/// Handles per-connection player spawning for multiplayer.
///
/// SCENE SETUP:
///   1. Create an empty GameObject named "GameManager" in the scene.
///   2. Add this component to it.
///   3. Set PlayerPrefab to your player prefab (the GameObject with PlayerMovement, WeaponManager, etc.).
///   4. Set SpawnPoints to a list of GameObjects marking valid player spawn positions.
///   5. Set PrePlacedPlayer to the existing player in the scene if you have one (used as the
///      host's spawn point in single-player / solo play; destroyed in multiplayer).
///
/// SINGLE-PLAYER:
///   Does nothing — the pre-placed player works as before.
///
/// MULTIPLAYER (host):
///   Destroys the pre-placed player (if any), then spawns one player prefab per connection.
///   Polls Connection.All each frame to detect joins and disconnects.
///
/// NOTE: The player prefab's root GameObject must have its Network Object mode enabled.
///   In the S&box editor, select the prefab root and set the Network property to "Object".
/// </summary>
public sealed class GameManager : Component
{
	[Property] public bool EnableNetworkDiagnostics { get; set; } = false;

	/// <summary>The player prefab to clone for each connecting player.</summary>
	[Property] public GameObject PlayerPrefab { get; set; }

	/// <summary>
	/// Optional pre-placed player already in the scene (single-player workflow).
	/// In multiplayer, the host will destroy this and spawn one per connection instead.
	/// </summary>
	[Property] public GameObject PrePlacedPlayer { get; set; }

	/// <summary>
	/// GameObjects whose WorldPosition is used as spawn locations.
	/// Players are assigned round-robin. Falls back to Vector3.Zero if empty.
	/// </summary>
	[Property] public List<GameObject> SpawnPoints { get; set; } = new();

	// Maps SteamId → spawned player GO, so we can track joins/disconnects
	private readonly Dictionary<ulong, GameObject> _spawnedPlayers = new();
	private int _nextSpawnIndex = 0;
	private bool _prePlacedPendingDestroy = false;
	private bool _prePlacedIsSpawnTemplate = false;
	private bool _networkRoleInitialized = false;
	private bool _disabledAsDuplicate = false;
	private bool _disabledByNetworkHelper = false;
	private string _lastConnectionDiagSnapshot = string.Empty;
	private LocalPresentationController _localPresentationController;
	private bool _rigInitialized = false;
	private float _rigYaw = 0f;
	private float _rigPitch = 0f;

	/// <summary>Returns true if Sandbox.NetworkHelper is present in the scene and should handle spawning instead.</summary>
	private bool SceneUsesBuiltInNetworkHelper() => Scene?.GetAllComponents<Component>()
		?.Any( c => c != null && c.IsValid
			&& c.Enabled
			&& c.GameObject != null
			&& c.GameObject.Enabled
			&& ( c.GetType().FullName == "Sandbox.NetworkHelper"
			  || c.GetType().Name     == "NetworkHelper" ) ) == true;

	protected override void OnStart()
	{
        Log.Info("[GameManager] OnStart called");
		if ( SceneUsesBuiltInNetworkHelper() )
		{
			_disabledByNetworkHelper = true;
			Enabled = false;
			Log.Info( "[GameManager] Sandbox.NetworkHelper detected — disabling custom GameManager (NetworkHelper handles spawning)." );
			return;
		}

		if ( DisableIfDuplicateManager() )
			return;

		EnsureLocalPresentationController();

		// Only infer the pre-placed scene pawn in single-player.
		// In multiplayer clients, this can resolve to the freshly spawned local pawn and destroy it.
		if ( PrePlacedPlayer == null && !Networking.IsActive )
			PrePlacedPlayer = Scene.GetAllComponents<PlayerMovement>()
				.FirstOrDefault( p => p.GameObject != null )?.GameObject;

		EnsurePlayerTemplateResolved();

		TryInitializeNetworkRole();
	}

	protected override void OnUpdate()
	{
        Log.Info("[GameManager] OnUpdate called");
		if ( _disabledByNetworkHelper ) return;
		if ( DisableIfDuplicateManager() )
			return;

		EnsureLocalPresentationController();
		EnsurePlayerTemplateResolved();
		TryInitializeNetworkRole();
		if ( !Networking.IsActive && !LocalPresentationController.Exists )
			EnforceLocalCameraSelection();

		if ( !Networking.IsActive ) return;

		if ( !Connection.Local.IsHost )
		{
			// Host-side scene cleanup replicates to clients. Never destroy anything locally here,
			// otherwise the joining client can delete its own freshly spawned pawn.
			return;
		}

		int connCount = Connection.All.Count();
		int trackedCount = _spawnedPlayers.Count;

		if ( EnableNetworkDiagnostics )
		{
			var connStates = Connection.All
				.Select( conn => $"{conn.SteamId}:{(_spawnedPlayers.ContainsKey( conn.SteamId ) ? 1 : 0)}" )
				.OrderBy( state => state );
			var snapshot = $"C={connCount}|T={trackedCount}|{string.Join( ",", connStates )}";

			if ( !string.Equals( snapshot, _lastConnectionDiagSnapshot, StringComparison.Ordinal ) )
			{
				_lastConnectionDiagSnapshot = snapshot;
				Log.Info( $"[GameManager] Connection state changed: Connected={connCount} Tracked={trackedCount}" );
				foreach ( var conn in Connection.All )
				{
					bool alreadySpawned = _spawnedPlayers.ContainsKey( conn.SteamId );
					Log.Info( $"[GameManager]   Conn: {conn.DisplayName}({conn.SteamId}) Already={alreadySpawned}" );
				}
			}
		}

		// Spawn players that have connected but don't have a pawn yet
		foreach ( var conn in Connection.All )
		{
			if ( !_spawnedPlayers.ContainsKey( conn.SteamId ) )
			{
				Log.Info( $"[GameManager] Spawning player for new connection: {conn.DisplayName}({conn.SteamId})" );
				SpawnPlayerFor( conn );
			}
		}

		// Remove pawns for players that have disconnected
		foreach ( var id in _spawnedPlayers.Keys.ToList() )
		{
			if ( !Connection.All.Any( c => c.SteamId == id ) )
			{
				var go = _spawnedPlayers[id];
				if ( go.IsValid() )
					go.Destroy();
				_spawnedPlayers.Remove( id );
				Log.Info( $"[GameManager] Removed player pawn for disconnected SteamId={id}" );
			}
		}
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

	private void EnsureLocalPresentationController()
	{
		if ( !Networking.IsActive ) return;

		if ( _localPresentationController != null && _localPresentationController.IsValid )
			return;

		_localPresentationController = Scene.GetAllComponents<LocalPresentationController>()
			.FirstOrDefault( controller => controller != null && controller.IsValid );

		if ( _localPresentationController != null )
			return;

		var controllerObject = new GameObject( true, "LocalPresentationController" );
		_localPresentationController = controllerObject.Components.Create<LocalPresentationController>();
	}

	private bool DisableIfDuplicateManager()
	{
		if ( _disabledAsDuplicate )
			return true;

		var managers = Scene?.GetAllComponents<GameManager>()
			?.Where( manager => manager != null && manager.IsValid && manager.GameObject != null && manager.GameObject.IsValid )
			?.ToList();

		if ( managers == null || managers.Count <= 1 )
			return false;

		GameManager primary = managers.FirstOrDefault( manager =>
			string.Equals( manager.GameObject.Name, "GameManager", StringComparison.OrdinalIgnoreCase ) );

		primary ??= managers.FirstOrDefault( manager =>
			manager.PlayerPrefab != null || manager.PrePlacedPlayer != null || (manager.SpawnPoints?.Count ?? 0) > 0 );

		if ( primary == null || ReferenceEquals( primary, this ) )
			return false;

		_disabledAsDuplicate = true;
		Enabled = false;

		if ( EnableNetworkDiagnostics )
			Log.Warning( $"[GameManager] Disabled duplicate manager on '{GameObject?.Name}'. Primary manager is '{primary.GameObject?.Name}'." );

		return true;
	}

	private GameObject FindScenePlayerTemplate()
	{
		var spawnedIds = _spawnedPlayers.Values
			.Where( go => go != null && go.IsValid() )
			.Select( go => go.Id )
			.ToHashSet();

		return Scene.GetAllComponents<PlayerMovement>()
			.Select( player => player?.GameObject )
			.FirstOrDefault( go => go != null && go.IsValid() && !spawnedIds.Contains( go.Id ) );
	}

	private void EnsurePlayerTemplateResolved()
	{
		bool needsPlayerPrefab = PlayerPrefab == null || !PlayerPrefab.IsValid();
		bool needsPrePlaced = PrePlacedPlayer == null || !PrePlacedPlayer.IsValid();

		if ( !needsPlayerPrefab && !needsPrePlaced )
			return;

		var template = FindScenePlayerTemplate();
		if ( template == null )
			return;

		if ( needsPlayerPrefab )
			PlayerPrefab = template;

		if ( needsPrePlaced )
			PrePlacedPlayer = template;

		if ( EnableNetworkDiagnostics )
			Log.Info( $"[GameManager] Recovered scene player template: GO={template.Name} Id={template.Id} PlayerPrefabValid={PlayerPrefab?.IsValid() == true} PrePlacedValid={PrePlacedPlayer?.IsValid() == true}" );
	}

	private static bool IsLocallyOwnedPlayer( PlayerMovement pm )
	{
		if ( pm == null ) return false;
		return PlayerIdentity.IsOwnedByLocal( pm.GameObject );
	}

	private static void AssignOwnershipRecursive( GameObject gameObject, Connection conn )
	{
		if ( gameObject == null || conn == null ) return;

		if ( gameObject.Network != null && gameObject.Network.Active )
			gameObject.Network.AssignOwnership( conn );

		foreach ( var child in gameObject.Children )
			AssignOwnershipRecursive( child, conn );
	}

	private void EnforceLocalCameraSelection()
	{
		PlayerMovement localPlayer = Scene.GetAllComponents<PlayerMovement>()
			?.Where( p => IsLocallyOwnedPlayer( p ) )
			?.LastOrDefault();

		if ( localPlayer == null || localPlayer.GameObject == null ) return;

		// Prefer the scene-level Main Camera. If missing, use any non-player camera.
		var sceneCam = Scene.GetAllComponents<CameraComponent>()
			.FirstOrDefault( c => c?.GameObject?.Name == "Main Camera" )
			?? Scene.GetAllComponents<CameraComponent>()
				.FirstOrDefault( c => c != null && c.Components.GetInAncestorsOrSelf<PlayerMovement>() == null );

		if ( sceneCam == null ) return;

		// Disable all player camera components and camera movement components.
		CameraComponent localPlayerCamera = null;
		foreach ( var cam in Scene.GetAllComponents<CameraComponent>() )
		{
			if ( cam == null ) continue;
			if ( cam == sceneCam ) continue;
			if ( cam.Components.GetInAncestorsOrSelf<PlayerMovement>() != null )
			{
				if ( cam.Components.GetInAncestorsOrSelf<PlayerMovement>() == localPlayer )
					localPlayerCamera = cam;
				cam.Enabled = false;
				cam.IsMainCamera = false;
			}
		}

		foreach ( var cm in Scene.GetAllComponents<CameraMovement>() )
		{
			if ( cm == null ) continue;
			cm.Enabled = false;
		}

		// Render from the local player's camera when available so GunViewModel appears.
		var renderCam = localPlayerCamera ?? sceneCam;
		if ( localPlayerCamera != null )
		{
			sceneCam.Enabled = false;
			sceneCam.IsMainCamera = false;
		}

		renderCam.Enabled = true;
		renderCam.IsMainCamera = true;
		renderCam.Priority = 100;
		renderCam.FieldOfView = GameSettings.FOV;

		var identity = localPlayer.Components.GetInDescendantsOrSelf<PlayerIdentity>();
		bool isPaused = identity?.IsPaused ?? PlayerStats.IsPaused;
		bool isDead = identity?.IsDead ?? PlayerStats.IsDead;
		bool levelComplete = identity?.LevelComplete ?? PlayerStats.LevelComplete;

		if ( !_rigInitialized )
		{
			var ang = localPlayer.GameObject.WorldRotation.Angles();
			_rigYaw = ang.yaw;
			_rigPitch = ang.pitch;
			_rigInitialized = true;
		}

		if ( !isPaused && !isDead && !levelComplete )
		{
			_rigPitch -= Input.MouseDelta.y * -0.1f * GameSettings.Sensitivity;
			_rigYaw -= Input.MouseDelta.x * 0.1f * GameSettings.Sensitivity;
		}

		_rigPitch = _rigPitch.Clamp( -89.9f, 89.9f );

		var eyeRot = new Angles( _rigPitch, _rigYaw, 0f ).ToRotation();
		localPlayer.GameObject.WorldRotation = new Angles( 0f, _rigYaw, 0f ).ToRotation();

		var head = localPlayer.Head ?? FindChildByNameRecursive( localPlayer.GameObject, "Head" );
		if ( head != null )
			head.WorldRotation = eyeRot;

		var eyePos = head != null ? head.WorldPosition : localPlayer.WorldPosition + Vector3.Up * 64f;
		var targetPos = eyePos + eyeRot * new Vector3( 0f, 0f, 10f );
		renderCam.GameObject.WorldPosition = targetPos;
		renderCam.GameObject.WorldRotation = eyeRot;

		// Keep the local player's camera child transform in sync with the scene camera.
		// This preserves viewmodel visibility because GunViewModel/ViewModelHandler live under that hierarchy.
		if ( localPlayerCamera != null )
		{
			localPlayerCamera.GameObject.WorldPosition = targetPos;
			localPlayerCamera.GameObject.WorldRotation = eyeRot;
		}
	}

	private void TryInitializeNetworkRole()
	{
		if ( _networkRoleInitialized ) return;
		if ( !Networking.IsActive ) return;
		if ( DisableIfDuplicateManager() ) return;

		EnsurePlayerTemplateResolved();

		_networkRoleInitialized = true;

		if ( !Connection.Local.IsHost )
		{
			if ( EnableNetworkDiagnostics )
				Log.Info( $"[GameManager] Client mode initialized. SteamId={Connection.Local?.SteamId}" );
			return;
		}

		if ( EnableNetworkDiagnostics )
			Log.Info( $"[GameManager] Host mode initialized. Connections={Connection.All.Count()} LocalSteamId={Connection.Local?.SteamId}" );

		if ( PrePlacedPlayer == null || !PrePlacedPlayer.IsValid() )
			PrePlacedPlayer = Scene.GetAllComponents<PlayerMovement>()
				.FirstOrDefault( p => p.GameObject != null )?.GameObject;

		// Host in multiplayer: disable the pre-placed player so it's invisible/inactive,
		// but DON'T destroy it yet — if PlayerPrefab points to the same scene object,
		// Clone() needs it to still be valid. We'll destroy it after the first spawn.
		if ( PrePlacedPlayer != null && PrePlacedPlayer.IsValid() )
		{
			_prePlacedIsSpawnTemplate = PlayerPrefab == PrePlacedPlayer;
			if ( !_prePlacedIsSpawnTemplate )
				PrePlacedPlayer.Enabled = false;
			_prePlacedPendingDestroy = true;
		}
	}

	private void SpawnPlayerFor( Connection conn )
	{
		EnsurePlayerTemplateResolved();

		if ( PlayerPrefab == null || !PlayerPrefab.IsValid() )
		{
			Log.Warning( "[GameManager] PlayerPrefab is not set or is invalid — cannot spawn player." );
			return;
		}

		var spawnPos = GetNextSpawnPosition();
		var player   = PlayerPrefab.Clone( spawnPos );
		if ( player == null || !player.IsValid() )
		{
			Log.Error( $"[GameManager] Failed to clone player prefab for {conn.DisplayName} (SteamId={conn.SteamId})" );
			return;
		}

		// Ensure freshly spawned pawns are active.
		player.Enabled = true;

		// Safety: guarantee required player identity exists for local-camera/ownership resolution.
		if ( player.Components.GetInDescendantsOrSelf<PlayerIdentity>() == null )
			player.Components.Create<PlayerIdentity>();

		if ( player.Components.GetInDescendantsOrSelf<PlayerMovement>() == null )
			Log.Warning( "[GameManager] Spawned player is missing PlayerMovement component." );

		// NetworkSpawn(conn) replicates the GO to all clients and assigns ownership to conn.
		// The owning client's IsProxy will be false; all others will be true.
		player.NetworkSpawn( conn );

		// Assign ownership of all descendants that have independent NetworkMode set in the prefab.
		// Without this, Body/Head/Camera can remain host-owned and overwrite client transforms.
		AssignOwnershipRecursive( player, conn );

		_spawnedPlayers[conn.SteamId] = player;
		Log.Info( $"[GameManager] Spawned player for {conn.DisplayName} (SteamId={conn.SteamId}) at {spawnPos}" );

		if ( EnableNetworkDiagnostics )
		{
			var identity = player.Components.GetInDescendantsOrSelf<PlayerIdentity>();
			var movement = player.Components.GetInDescendantsOrSelf<PlayerMovement>();
			Log.Info( $"[GameManager] Spawn diagnostics: GO={player.Name} Id={player.Id} Enabled={player.Enabled} Identity={(identity!=null?"Y":"N")} Movement={(movement!=null?"Y":"N")}" );
		}

		// Now that we've cloned the template, destroy the disabled pre-placed player.
		if ( _prePlacedPendingDestroy && PrePlacedPlayer != null && PrePlacedPlayer.IsValid() )
		{
			if ( _prePlacedIsSpawnTemplate )
			{
				// Keep template alive for future joins; just keep it inactive in the world.
				PrePlacedPlayer.Enabled = false;
			}
			else
			{
				PrePlacedPlayer.Destroy();
			}
			_prePlacedPendingDestroy = false;
		}
	}

	private Vector3 GetNextSpawnPosition()
	{
		if ( SpawnPoints.Count == 0 ) return Vector3.Zero;
		var pt = SpawnPoints[_nextSpawnIndex % SpawnPoints.Count];
		_nextSpawnIndex++;
		return pt?.WorldPosition ?? Vector3.Zero;
	}
}
