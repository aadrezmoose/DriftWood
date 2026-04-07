using Sandbox;

public sealed class WorldHeldItemPresenter : Component
{
	[Property] public string PreferredHandAttachment { get; set; } = "hold_R";
	[Property] public Vector3 WeaponOffset { get; set; } = new Vector3( 8f, -10f, 50f );
	[Property] public Angles WeaponAngles { get; set; } = new Angles( 0f, 0f, 0f );
	[Property] public float WeaponScale { get; set; } = 0.42f;
	[Property] public Vector3 ItemOffset { get; set; } = new Vector3( 8f, -8f, 46f );
	[Property] public Angles ItemAngles { get; set; } = new Angles( 0f, 0f, 0f );
	[Property] public float ItemScale { get; set; } = 0.5f;

	private PlayerIdentity identity;
	private PlayerMovement movement;
	private WeaponManager weaponManager;
	private SkinnedModelRenderer bodyRenderer;
	private GameObject anchorObject;
	private ModelRenderer modelRenderer;
	private string appliedModelPath = string.Empty;

	protected override void OnAwake()
	{
		identity = Components.GetInDescendantsOrSelf<PlayerIdentity>();
		movement = Components.GetInDescendantsOrSelf<PlayerMovement>();
		weaponManager = Components.GetInDescendantsOrSelf<WeaponManager>();
		bodyRenderer = Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();
		EnsureVisualObjects();
	}

	protected override void OnUpdate()
	{
		identity ??= Components.GetInDescendantsOrSelf<PlayerIdentity>();
		movement ??= Components.GetInDescendantsOrSelf<PlayerMovement>();
		weaponManager ??= Components.GetInDescendantsOrSelf<WeaponManager>();
		bodyRenderer ??= Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();
		EnsureVisualObjects();

		if ( anchorObject == null || modelRenderer == null )
			return;

		if ( PlayerIdentity.IsOwnedByLocal( GameObject ) )
		{
			SetVisible( false );
			return;
		}

		var heldModelPath = identity?.ActiveHeldModelPath;
		if ( string.IsNullOrEmpty( heldModelPath ) )
			heldModelPath = weaponManager?.GetActiveHeldModelPathForPresentation();

		if ( string.IsNullOrEmpty( heldModelPath ) )
		{
			SetVisible( false );
			return;
		}

		bool isWeaponSlot = (identity?.ActiveSlotIndex ?? 0) < 2;
		var slotOffset = isWeaponSlot ? WeaponOffset : ItemOffset;
		var slotAngles = (isWeaponSlot ? WeaponAngles : ItemAngles).ToRotation();

		if ( TryGetHandAttachment( out var handPosition, out var handRotation ) )
		{
			if ( anchorObject.Parent != null )
				anchorObject.SetParent( null, true );

			anchorObject.WorldPosition = handPosition + handRotation * slotOffset;
			anchorObject.WorldRotation = handRotation * slotAngles;
		}
		else
		{
			var attachTarget = GameObject;
			if ( anchorObject.Parent != attachTarget )
				anchorObject.Parent = attachTarget;

			anchorObject.LocalPosition = slotOffset;
			anchorObject.LocalRotation = slotAngles;
		}

		anchorObject.LocalScale = (isWeaponSlot ? WeaponScale : ItemScale);

		if ( !string.Equals( appliedModelPath, heldModelPath, System.StringComparison.Ordinal ) )
		{
			modelRenderer.Model = ResourceLibrary.Get<Model>( heldModelPath );
			appliedModelPath = heldModelPath;
		}

		SetVisible( modelRenderer.Model != null );
	}

	private void EnsureVisualObjects()
	{
		if ( anchorObject == null || !anchorObject.IsValid )
		{
			anchorObject = new GameObject( false, "WorldHeldItem" );
			anchorObject.Parent = GameObject;
		}

		modelRenderer ??= anchorObject.Components.GetOrCreate<ModelRenderer>();
	}

	private bool TryGetHandAttachment( out Vector3 position, out Rotation rotation )
	{
		position = default;
		rotation = Rotation.Identity;
		return false;
	}

	private void SetVisible( bool isVisible )
	{
		if ( anchorObject != null )
			anchorObject.Enabled = isVisible;

		if ( !isVisible )
		{
			appliedModelPath = string.Empty;
			if ( modelRenderer != null )
				modelRenderer.Model = null;
		}
	}
}