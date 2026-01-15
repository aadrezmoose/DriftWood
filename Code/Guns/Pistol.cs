using Sandbox;

public sealed class Pistol : Gun
{
	[Property] public float BulletDistance { get; set; } = 5000f;

	protected override void OnFire()
	{
		Log.Info("Pistol.OnFire() called");

		if (playerHead == null)
		{
			Log.Warning("Player head not set on pistol!");
			return;
		}

		var startPos = playerHead.Transform.World.Position;
		var fireDirection = playerHead.Transform.World.Rotation.Forward;
		var endPos = startPos + fireDirection * BulletDistance;

		Log.Info($"Firing from {startPos} in direction {fireDirection}");

		// Raycast from camera forward
		// Ignore the player's own collider and widen the trace a little to make hits more forgiving
		var trace = Scene.Trace
			.Ray(startPos, endPos)
			.Size(3f)
			.WithoutTags( "trigger" )
			.IgnoreGameObject( owner ?? playerHead )
			.Run();

		if (trace.Hit)
		{
			// Find an IHealth component on the hit object or nearby in hierarchy
			IHealth hitHealth = trace.GameObject?.Components.Get<IHealth>();
			if ( hitHealth is null ) hitHealth = trace.GameObject?.Components.GetInAncestorsOrSelf<IHealth>();
			if ( hitHealth is null ) hitHealth = trace.GameObject?.Components.GetInDescendantsOrSelf<IHealth>();

			if (hitHealth != null)
			{
				hitHealth.TakeDamage(Damage);
			}

			// Visual effect at impact point - longer duration
			DebugOverlay.Sphere(new Sphere(trace.EndPosition, 5f), Color.Red, 2f);
		}
		else
		{
			Log.Info("Ray did not hit anything");
			// Draw to end of bullet distance if no hit
			DebugOverlay.Sphere(new Sphere(endPos, 3f), Color.Blue, 2f);
		}

		// Visual effect for gun fire - longer duration
		DebugOverlay.Line(startPos, trace.EndPosition, Color.Yellow, 2f);
	}
}

public interface IHealth
{
	void TakeDamage(float damage);
}
