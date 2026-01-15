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
		var trace = Scene.Trace
			.Ray(startPos, endPos)
			.Run();

		if (trace.Hit)
		{
			Log.Info($"Hit something at {trace.EndPosition}");

			// Try to damage it if it has a health component
			var hitComponent = trace.GameObject?.Components.Get<IHealth>();
			if (hitComponent != null)
			{
				hitComponent.TakeDamage(Damage);
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
