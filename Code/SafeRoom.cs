using System;
using Sandbox;

/// <summary>
/// SafeRoom - Left 4 Dead style safe zone component
/// Detects when players enter/exit and broadcasts events to stop enemy spawning
/// </summary>
public sealed class SafeRoom : Component, Component.ITriggerListener
{
	[Property] public bool IsStartRoom { get; set; } = true;
	[Property] public bool IsEndRoom { get; set; } = false;
	[Property] public float SafeRadius { get; set; } = 300f;
	/// <summary>Optional end room door — win timer only starts after it is closed.</summary>
	[Property] public SafeRoomDoor EndDoor { get; set; }

	/// <summary>
	/// Called when a player enters any safe room
	/// </summary>
	public static event Action<SafeRoom> OnPlayerEntered;

	/// <summary>
	/// Called when a player exits any safe room
	/// </summary>
	public static event Action<SafeRoom> OnPlayerExited;

	private int _playersInside = 0;
	private bool hasPlayerLeft = false; // Start rooms become inactive once all players have left
	private float winTimer = -1f;
	private const float WinDelay = 2.5f;

	protected override void OnAwake()
	{
		Log.Info( $"SafeRoom initialized: IsStartRoom={IsStartRoom}, IsEndRoom={IsEndRoom}, Radius={SafeRadius}" );
	}

	public void OnTriggerEnter( Collider other ) { }
	public void OnTriggerExit( Collider other ) { }

	protected override void DrawGizmos()
	{
		DrawSafeRoomGizmo();
	}

	protected override void OnUpdate()
	{
		// Count all active players inside the radius
		var allPlayers = Scene.GetAllComponents<PlayerMovement>();
		int totalActive = 0, insideCount = 0;
		foreach ( var pm in allPlayers )
		{
			if ( pm?.GameObject == null || !pm.GameObject.Active ) continue;
			totalActive++;
			if ( (pm.WorldPosition - WorldPosition).Length <= SafeRadius )
				insideCount++;
		}

		// Start rooms are inactive once all players have left them
		if ( IsStartRoom && hasPlayerLeft ) return;

		int prevInside = _playersInside;
		_playersInside = insideCount;

		// End room win: all active players must be inside AND door must be closed
		if ( IsEndRoom )
		{
			bool doorClosed = EndDoor == null || !EndDoor.IsOpen;
			bool allInside = totalActive > 0 && insideCount >= totalActive && doorClosed;
			if ( allInside && winTimer < 0f )
				winTimer = WinDelay;
			else if ( !allInside )
				winTimer = -1f;

			if ( winTimer >= 0f )
			{
				winTimer -= Time.Delta;
				if ( winTimer <= 0f ) { winTimer = -1f; PlayerStats.LevelComplete = true; }
			}
		}

		// Fire OnPlayerEntered: on first entry (0→N) and on each count increase for end rooms
		if ( insideCount > 0 && (prevInside == 0 || (IsEndRoom && insideCount > prevInside)) )
		{
			OnPlayerEntered?.Invoke( this );
			Log.Info( $"Player entered safe room (Start: {IsStartRoom}, End: {IsEndRoom}, {insideCount}/{totalActive})" );
		}

		// Fire OnPlayerExited: when all leave
		if ( insideCount == 0 && prevInside > 0 )
		{
			hasPlayerLeft = true;
			OnPlayerExited?.Invoke( this );
			Log.Info( $"Player exited safe room (Start: {IsStartRoom}, End: {IsEndRoom})" );
		}
	}

	void DrawSafeRoomGizmo()
	{
		// Draw safe room boundary
		Color safeColor = IsStartRoom ? Color.Green : (IsEndRoom ? Color.Blue : Color.Cyan);
		safeColor = safeColor.WithAlpha( 0.3f );

		Gizmo.Draw.Color = safeColor;
		Gizmo.Draw.LineSphere( WorldPosition, SafeRadius );

		// Draw filled sphere at lower opacity
		Gizmo.Draw.Color = safeColor.WithAlpha( 0.1f );
		Gizmo.Draw.LineSphere( WorldPosition, SafeRadius );

		// Draw text label
		Gizmo.Draw.Color = Color.White;
		string label = IsStartRoom ? "START SAFE ROOM" : (IsEndRoom ? "END SAFE ROOM" : "SAFE ROOM");
		Gizmo.Draw.WorldText(
			label,
			new Transform( WorldPosition + Vector3.Up * 100f ),
			"Poppins",
			20
		);

		// Draw status
		if ( _playersInside > 0 )
		{
			Gizmo.Draw.Color = Color.Yellow;
			Gizmo.Draw.WorldText(
				"PLAYER INSIDE",
				new Transform( WorldPosition + Vector3.Up * 120f ),
				"Poppins",
				16
			);
		}
	}

	/// <summary>
	/// Check if any player is currently inside this safe room
	/// </summary>
	public bool IsPlayerInside => _playersInside > 0;
}
