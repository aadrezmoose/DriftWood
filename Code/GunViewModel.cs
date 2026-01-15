using Sandbox;

public sealed class GunViewModel : Component
{
	[Property] public string ModelPath { get; set; } = "models/weapons/pistol/pistol.vmdl";
	[Property] public Vector3 PositionOffset { get; set; } = new Vector3(10, -5, -15);
	[Property] public Rotation RotationOffset { get; set; } = Rotation.Identity;

	private GameObject modelObject;
	private ModelRenderer modelRenderer;

	protected override void OnAwake()
	{
		// Create a child GameObject for the model
		modelObject = new GameObject(GameObject);
		modelObject.Name = "GunModel";
		
		Log.Info($"GunViewModel: Creating model object");
		
		// Set position and rotation
		var transform = modelObject.Transform;
		transform.Local = new Transform(PositionOffset, RotationOffset);

		// Add a ModelRenderer component
		modelRenderer = modelObject.AddComponent<ModelRenderer>();

		// Hardcoded Facepunch pistol asset
		modelRenderer.Model = Cloud.Model("Gex.Pistol");
		Log.Info($"GunViewModel: Loaded hardcoded cloud model: Pistol");
	}

	protected override void OnDestroy()
	{
		if (modelObject != null)
		{
			modelObject.Destroy();
		}
	}
}

