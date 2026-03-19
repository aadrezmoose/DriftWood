using Sandbox;

/// <summary>
/// Attach to any world GameObject that also has a BaseItem component (HealthKit, PainPills, etc.)
/// to give it a spinning world model and a pickup radius.
///
/// Setup in inspector:
///   - World Model     — the model shown lying/floating in the world
///   - Spin In World   — slowly rotates so the player notices it
///   - Pickup Radius   — overrides the default WeaponManager scan radius for this specific item
/// </summary>
public sealed class ItemPickup : Component
{
	[Property] public Model WorldModel { get; set; }
	[Property] public bool SpinInWorld { get; set; } = true;

	private ModelRenderer worldRenderer;

	protected override void OnStart()
	{
		if ( WorldModel != null )
		{
			worldRenderer = Components.GetOrCreate<ModelRenderer>();
			worldRenderer.Model = WorldModel;
		}
	}

	protected override void OnUpdate()
	{
		if ( SpinInWorld )
			WorldRotation = WorldRotation.RotateAroundAxis( Vector3.Up, Time.Delta * 60f );
	}
}
