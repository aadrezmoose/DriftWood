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

	/// <summary>
	/// Called when a player enters any safe room
	/// </summary>
	public static event Action<SafeRoom> OnPlayerEntered;

	/// <summary>
	/// Called when a player exits any safe room
	/// </summary>
	public static event Action<SafeRoom> OnPlayerExited;

	private bool playerInside = false;
	private bool hasPlayerLeft = false; // Start rooms become inactive once player leaves
	private GameObject player;

	protected override void OnAwake()
	{
		player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;
		Log.Info( $"SafeRoom initialized: IsStartRoom={IsStartRoom}, IsEndRoom={IsEndRoom}, Radius={SafeRadius}" );
	}

	public void OnTriggerEnter( Collider other ) { }
	public void OnTriggerExit( Collider other ) { }

	protected override void OnUpdate()
	{
		if ( Gizmo.Camera != null )
			DrawSafeRoomGizmo();

		// Proximity-based detection — reliable with CharacterController
		if ( player is null || !player.Active ) return;

		float dist = (player.WorldPosition - WorldPosition).Length;
		bool inside = dist <= SafeRadius;

		// Start rooms are inactive once the player has left them
		if ( IsStartRoom && hasPlayerLeft ) return;

		if ( inside && !playerInside )
		{
			playerInside = true;
			OnPlayerEntered?.Invoke( this );
			if ( IsEndRoom ) PlayerStats.LevelComplete = true;
			Log.Info( $"Player entered safe room (Start: {IsStartRoom}, End: {IsEndRoom})" );
		}
		else if ( !inside && playerInside )
		{
			playerInside = false;
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
		if ( playerInside )
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
	/// Check if player is currently inside this safe room
	/// </summary>
	public bool IsPlayerInside => playerInside;
}
