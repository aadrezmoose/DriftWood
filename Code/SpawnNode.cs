using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Place these around the map at logical enemy entry points — doorways, alley
/// entrances, behind containers, around corners. The EnemySpawner picks from
/// available nodes rather than probing random positions.
/// </summary>
public sealed class SpawnNode : Component
{
	// All active nodes in the scene — spawner reads this list
	public static readonly List<SpawnNode> All = new();

	/// <summary>Minimum distance from player before this node is eligible.</summary>
	[Property] public float MinPlayerDistance { get; set; } = 400f;

	/// <summary>If true, only spawns here when the player has no line of sight to this node.</summary>
	[Property] public bool RequireLOS { get; set; } = true;

	/// <summary>Base weight. Leave at 1 — the AIDirector adjusts dynamically.</summary>
	[Property] public float Weight { get; set; } = 1f;

	protected override void OnStart()
	{
		All.Add( this );
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
	}

	/// <summary>
	/// Returns true if this node is currently eligible for spawning.
	/// </summary>
	public bool IsEligible( GameObject player )
	{
		if ( player == null ) return false;

		var playerPos = player.WorldPosition;
		var nodePos   = WorldPosition;

		// Distance check
		float dist = Vector3.DistanceBetween( playerPos, nodePos );
		if ( dist < MinPlayerDistance ) return false;

		if ( !RequireLOS ) return true;

		// LOS check — node is eligible if geometry blocks the player's view to it
		var trace = Scene.Trace
			.Ray( nodePos + Vector3.Up * 60f, playerPos + Vector3.Up * 60f )
			.WithoutTags( "trigger" )
			.Run();

		return trace.Hit; // geometry in the way = player can't see this node
	}

	/// <summary>
	/// Draw a gizmo in the editor so nodes are visible during placement.
	/// </summary>
	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Green.WithAlpha( 0.8f );
		Gizmo.Draw.LineSphere( Vector3.Zero, 16f );
		Gizmo.Draw.Arrow( Vector3.Zero, Vector3.Up * 48f );
	}
}
