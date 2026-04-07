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

	/// <summary>
	/// Logical spawn region for event-based gating (e.g. Cargo, Beach, Yard).
	/// "Global" means always allowed unless explicitly filtered out.
	/// </summary>
	[Property] public string SpawnRegion { get; set; } = "Global";

	/// <summary>
	/// Seconds after a spawn before this node can be used again.
	/// Prevents enemies from clustering at the same entry point.
	/// </summary>
	[Property] public float UsageCooldown { get; set; } = 8f;

	private float lastUsedTime = -999f;

	public bool IsOnCooldown => (Time.Now - lastUsedTime) < UsageCooldown;

	/// <summary>Mark this node as just used, starting its cooldown.</summary>
	public void MarkUsed() => lastUsedTime = Time.Now;

	protected override void OnStart()
	{
		// Remove any stale entries from previous editor play sessions before adding
		All.RemoveAll( n => n is null || !n.IsValid() );
		All.Add( this );
	}

	protected override void OnDestroy()
	{
		All.Remove( this );
	}

	/// <summary>
	/// Returns true if this node is currently eligible for spawning.
	/// </summary>
	public bool IsEligible( GameObject player, bool ignoreCooldown = false )
	{
		if ( player == null || !IsValid ) return false;

		if ( !ignoreCooldown && IsOnCooldown ) return false;

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
