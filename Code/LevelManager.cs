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

	protected override void OnAwake()
	{
		SafeRoom.OnPlayerEntered += OnPlayerEnteredSafeRoom;
	}

	protected override void OnDestroy()
	{
		SafeRoom.OnPlayerEntered -= OnPlayerEnteredSafeRoom;
	}

	private void OnPlayerEnteredSafeRoom( SafeRoom room )
	{
		if ( EndRoom == null ) return;
		if ( !ReferenceEquals( room, EndRoom ) ) return;

		PlayerStats.LevelComplete = true;
		Log.Info( "LevelManager: Player reached the end safe room — level complete!" );
	}
}
