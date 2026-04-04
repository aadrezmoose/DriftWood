using Sandbox;

/// <summary>
/// Runs a one-time spawn node coverage report at scene start.
/// Drop this component anywhere in the scene — it self-removes after reporting.
/// Output goes to the console log; warnings flag weak coverage bands for level design review.
/// </summary>
public sealed class NodeHealthCheck : Component
{
	/// <summary>Set false to skip the report (e.g. shipped builds).</summary>
	[Property] public bool RunOnStart { get; set; } = true;

	// ── Thresholds (mirror the spec in POTENTIAL_IDEAS.md) ─────────────
	[Property] public int   MinTotalNodes    { get; set; } = 12;
	[Property] public float MinHiddenRatio   { get; set; } = 0.60f;
	[Property] public int   MinBand300to500  { get; set; } = 3;
	[Property] public int   MinBand500to900  { get; set; } = 4;
	[Property] public int   MinBand900plus   { get; set; } = 3;
	/// <summary>Eligible node count below this triggers a fallback-risk warning.</summary>
	[Property] public int   FallbackRiskThreshold { get; set; } = 6;

	// Run one frame after OnStart so all SpawnNodes (which also use OnStart) are registered first.
	private bool _ran = false;
	protected override void OnUpdate()
	{
		if ( _ran || !RunOnStart ) return;
		_ran = true;
		RunReport();
	}

	void RunReport()
	{
		var player = Scene.Components.Get<PlayerMovement>()?.GameObject;
		if ( player == null )
		{
			// Try broader search
			foreach ( var pm in Scene.GetAllComponents<PlayerMovement>() )
			{
				player = pm.GameObject;
				break;
			}
		}

		var nodes = SpawnNode.All;

		int total    = nodes.Count;
		int eligible = 0;
		int hidden   = 0;   // RequireLOS=true AND geometry blocks LOS
		int visible  = 0;   // RequireLOS=false OR no geometry blocking

		int band_300_500 = 0;
		int band_500_900 = 0;
		int band_900plus = 0;

		var playerPos = player?.WorldPosition ?? Vector3.Zero;

		foreach ( var node in nodes )
		{
			if ( node == null || !node.IsValid() ) continue;

			float dist = Vector3.DistanceBetween( playerPos, node.WorldPosition );

			// Distance band (based on raw distance, not eligibility)
			if      ( dist >= 300f && dist < 500f ) band_300_500++;
			else if ( dist >= 500f && dist < 900f ) band_500_900++;
			else if ( dist >= 900f )                band_900plus++;

			// Eligibility and LOS
			bool isEligible = node.IsEligible( player );
			if ( isEligible ) eligible++;

			// Determine hidden/visible by doing the LOS trace ourselves
			// (same logic as SpawnNode.IsEligible but separated from distance gate)
			if ( node.RequireLOS )
			{
				var trace = Scene.Trace
					.Ray( node.WorldPosition + Vector3.Up * 60f, playerPos + Vector3.Up * 60f )
					.WithoutTags( "trigger" )
					.Run();
				if ( trace.Hit ) hidden++;   // geometry in the way = hidden from player
				else             visible++;
			}
			else
			{
				visible++; // RequireLOS=false means always visible-eligible
			}
		}

		float hiddenRatio = total > 0 ? (float)hidden / total : 0f;
		int   hiddenPct   = (int)(hiddenRatio * 100f);
		int   visibleCnt  = total - hidden;

		// ── Print report ──────────────────────────────────────────────
		Log.Info( $"[NodeHealth] ─────────────────────────────────────" );
		Log.Info( $"[NodeHealth] Total={total}  Eligible={eligible}  Hidden={hidden} ({hiddenPct}%)  Visible={visibleCnt}" );
		Log.Info( $"[NodeHealth] Bands:  300-500={band_300_500}   500-900={band_500_900}   900+={band_900plus}" );

		bool allOk = true;

		if ( total < MinTotalNodes )
		{
			Log.Warning( $"[NodeHealth][WARN] Only {total} total nodes (target {MinTotalNodes}+) — add more spawn points" );
			allOk = false;
		}

		if ( hiddenRatio < MinHiddenRatio && total > 0 )
		{
			Log.Warning( $"[NodeHealth][WARN] Hidden ratio {hiddenPct}% < {(int)(MinHiddenRatio*100)}% target — place more LOS-blocked nodes" );
			allOk = false;
		}

		if ( band_300_500 < MinBand300to500 )
		{
			Log.Warning( $"[NodeHealth][WARN] Close band (300-500) only {band_300_500} < {MinBand300to500} target" );
			allOk = false;
		}

		if ( band_500_900 < MinBand500to900 )
		{
			Log.Warning( $"[NodeHealth][WARN] Mid band (500-900) only {band_500_900} < {MinBand500to900} target" );
			allOk = false;
		}

		if ( band_900plus < MinBand900plus )
		{
			Log.Warning( $"[NodeHealth][WARN] Far band (900+) only {band_900plus} < {MinBand900plus} target" );
			allOk = false;
		}

		if ( eligible < FallbackRiskThreshold )
		{
			Log.Warning( $"[NodeHealth][WARN] Fallback risk HIGH — only {eligible} eligible nodes at player start (threshold {FallbackRiskThreshold})" );
			allOk = false;
		}

		if ( allOk )
			Log.Info( "[NodeHealth][OK] All thresholds met" );

		Log.Info( $"[NodeHealth] ─────────────────────────────────────" );
	}
}
