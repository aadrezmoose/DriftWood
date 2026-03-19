using Sandbox;

/// <summary>
/// Projectile fired by the Spitter. Flies forward until it hits geometry,
/// then spawns an AcidPool prefab at the impact point. Self-destructs on
/// impact or after 3 seconds (max travel time).
/// </summary>
public sealed class AcidProjectile : Component
{
	[Property] public PrefabFile AcidPoolPrefab { get; set; }
	[Property] public float Speed { get; set; } = 800f;

	private float lifeTimer = 0f;
	private const float MaxLifetime = 3f;

	protected override void OnFixedUpdate()
	{
		lifeTimer += Time.Delta;
		if ( lifeTimer >= MaxLifetime )
		{
			// Timed out without hitting anything — spawn pool at current position
			SpawnPool( WorldPosition );
			GameObject.Destroy();
			return;
		}

		float travelDistance = Speed * Time.Delta;
		Vector3 forward = WorldRotation.Forward;

		// Forward trace to detect geometry impact
		var trace = Scene.Trace
			.Ray( WorldPosition, WorldPosition + forward * travelDistance )
			.WithoutTags( "trigger", "player" )
			.IgnoreGameObject( GameObject )
			.Run();

		if ( trace.Hit )
		{
			SpawnPool( trace.EndPosition );
			GameObject.Destroy();
			return;
		}

		// Advance position
		WorldPosition += forward * travelDistance;
	}

	void SpawnPool( Vector3 position )
	{
		if ( AcidPoolPrefab is null )
		{
			Log.Warning( "AcidProjectile: No AcidPoolPrefab assigned!" );
			return;
		}

		try
		{
			var poolScene = SceneUtility.GetPrefabScene( AcidPoolPrefab );
			if ( poolScene is null )
			{
				Log.Error( "AcidProjectile: Could not load AcidPool prefab scene" );
				return;
			}

			var pool = poolScene.Clone( position );
			Log.Info( $"AcidProjectile: Spawned acid pool at {position}" );
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"AcidProjectile: Exception spawning pool: {ex.Message}" );
		}
	}
}
