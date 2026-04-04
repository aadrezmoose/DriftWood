using Sandbox;

/// <summary>
/// LevelManager — handles the win condition when the player reaches the designated end SafeRoom.
/// Attach this component to any persistent GameObject in the scene and assign the EndRoom property
/// to the SafeRoom that marks the level exit.
/// </summary>
public sealed class LevelManager : Component
{
	/// <summary>The SafeRoom that triggers level completion when the player enters it.</summary>
	[Property] public SafeRoom EndRoom { get; set; }
	[Property] public float CompletionDelay { get; set; } = 2.5f;

	private float pendingCompleteTimer = -1f;

	protected override void OnAwake()
	{
		SafeRoom.OnPlayerEntered += OnPlayerEnteredSafeRoom;
	}

	protected override void OnDestroy()
	{
		SafeRoom.OnPlayerEntered -= OnPlayerEnteredSafeRoom;
	}

	protected override void OnUpdate()
	{
		if ( !PlayerStats.LevelComplete )
			PlayerStats.LevelElapsedSeconds += Time.Delta;

		if ( pendingCompleteTimer < 0f ) return;

		pendingCompleteTimer -= Time.Delta;
		if ( pendingCompleteTimer > 0f ) return;

		pendingCompleteTimer = -1f;
		PlayerStats.LevelComplete = true;
		Log.Info( "LevelManager: End room completion delay elapsed — level complete!" );
	}

	private void OnPlayerEnteredSafeRoom( SafeRoom room )
	{
		if ( EndRoom == null ) return;
		if ( !ReferenceEquals( room, EndRoom ) ) return;
		if ( pendingCompleteTimer > 0f ) return; // already counting down

		// Require all active players to be inside before starting the timer
		var allPlayers = Scene.GetAllComponents<PlayerMovement>();
		int total = 0, inside = 0;
		foreach ( var pm in allPlayers )
		{
			if ( pm?.GameObject == null || !pm.GameObject.Active ) continue;
			total++;
			if ( (pm.WorldPosition - room.WorldPosition).Length <= room.SafeRadius )
				inside++;
		}
		if ( total > 0 && inside < total ) return;

		// Also require the end door to be closed if one is assigned
		if ( EndRoom.EndDoor != null && EndRoom.EndDoor.IsOpen ) return;

		pendingCompleteTimer = System.Math.Max( CompletionDelay, 0f );
		Log.Info( $"LevelManager: All {inside}/{total} players in end room — completing in {pendingCompleteTimer:F1}s" );
	}
}
