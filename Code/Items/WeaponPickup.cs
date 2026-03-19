using Sandbox;

/// <summary>
/// Place on a GameObject in the world (safe room shelf, ground, etc.) to make a
/// weapon that the player can pick up with E.
///
/// Setup in inspector:
///   - Category          — Primary (slot 0) or Secondary (slot 1)
///   - Weapon Prefab     — the GameObject prefab that has the Gun/MeleeWeapon component
///   - World Model       — the model shown lying on the ground
///   - ViewModel Model / AnimGraph / Hands Model / Position Offset — passed to GunViewModel on equip
/// </summary>
public sealed class WeaponPickup : Component
{
	[Property] public WeaponCategory Category { get; set; } = WeaponCategory.Primary;
	[Property] public string WeaponDisplayName { get; set; } = "Weapon";
	[Property] public PrefabFile WeaponPrefab { get; set; }
	[Property] public float PickupRadius { get; set; } = 150f;

	// Viewmodel info forwarded to GunViewModel.UpdateWeapon when equipped
	[Property] public Model ViewModelModel { get; set; }
	[Property] public AnimationGraph ViewModelAnimGraph { get; set; }
	[Property] public Model ViewModelHandsModel { get; set; }
	[Property] public Vector3 ViewModelPositionOffset { get; set; }

	// World representation
	[Property] public Model WorldModel { get; set; }
	[Property] public bool SpinInWorld { get; set; } = true;

	private ModelRenderer worldRenderer;
	private GameObject player;

	protected override void OnAwake()
	{
		if ( WorldModel != null )
		{
			worldRenderer = Components.GetOrCreate<ModelRenderer>();
			worldRenderer.Model = WorldModel;
		}
	}

	protected override void OnStart()
	{
		player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;
	}

	protected override void OnUpdate()
	{
		// Slowly spin the weapon model so it's visible
		if ( SpinInWorld )
			WorldRotation = WorldRotation.RotateAroundAxis( Vector3.Up, Time.Delta * 60f );
	}

	/// <summary>Called by WeaponManager when the player presses E nearby.</summary>
	public void Pickup( GameObject playerGO )
	{
		GameObject.Enabled = false;
	}
}
