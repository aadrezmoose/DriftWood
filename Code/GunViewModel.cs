using Sandbox;

public sealed class GunViewModel : Component
{
	[Property] public Model WeaponModel { get; set; }
	[Property] public Model HandsModel { get; set; }
	[Property] public AnimationGraph AnimGraph { get; set; }
	[Property] public Vector3 PositionOffset { get; set; } = new Vector3( 25, 6, -10 );
	[Property] public Rotation RotationOffset { get; set; } = Rotation.Identity;

	/// <summary>Animation parameter name triggered on fire.</summary>
	[Property] public string AnimFireParam { get; set; } = "fire";
	/// <summary>Animation parameter name triggered on reload.</summary>
	[Property] public string AnimReloadParam { get; set; } = "reload";
	/// <summary>Animation parameter name triggered on empty reload.</summary>
	[Property] public string AnimReloadEmptyParam { get; set; } = "reload_empty";

	/// <summary>The active viewmodel instance — kept for backwards compat, set by WeaponManager.</summary>
	public static GunViewModel Current { get; set; }

	/// <summary>The intended base position for the viewmodel — set by UpdateWeapon(), read by ViewModelHandler.</summary>
	public Vector3 RestPosition { get; private set; }

	/// <summary>The intended base rotation for the viewmodel — set by UpdateWeapon(), read by ViewModelHandler.</summary>
	public Rotation RestRotation { get; private set; }

	private GameObject modelObject;
	public GameObject ModelObject => modelObject;
	public SkinnedModelRenderer ModelRenderer { get; private set; }
	public bool HasLocalVisuals => modelObject != null && modelObject.IsValid;
	public PlayerMovement OwnerMovement => Components.GetInAncestorsOrSelf<PlayerMovement>();

	private GameObject overlayGO;
	private SkinnedModelRenderer overlayRenderer;

	/// <summary>Inspector-assigned overlay model (throwables/heal items). Usually null — assigned at runtime.</summary>
	[Property] public Model OverlayModel { get; set; }

	protected override void OnStart()
	{
		RestPosition = PositionOffset;
		RestRotation = RotationOffset;
		TryEnsureVisuals();
	}

	protected override void OnUpdate()
	{
		if ( !ShouldCreateLocalVisuals() )
		{
			DestroyVisuals();
			return;
		}

		TryEnsureVisuals();
		EnsureVisualParent();
	}

	/// <summary>Show or hide the viewmodel based on whether the current slot has content.</summary>
	public void ShowModel( bool show )
	{
		if ( modelObject != null ) modelObject.Enabled = show;
	}

	/// <summary>Trigger a bool animation parameter (auto-resets in the graph).</summary>
	public void PlayAnim( string param )
	{
		ModelRenderer?.Set( param, true );
	}

	public void PlayFireAnim()
	{
		if ( !string.IsNullOrEmpty( AnimFireParam ) )
			ModelRenderer?.Set( AnimFireParam, true );
	}

	public void PlayReloadAnim()
	{
		if ( !string.IsNullOrEmpty( AnimReloadParam ) )
			ModelRenderer?.Set( AnimReloadParam, true );
	}

	public void PlayReloadEmptyAnim()
	{
		if ( !string.IsNullOrEmpty( AnimReloadEmptyParam ) )
			ModelRenderer?.Set( AnimReloadEmptyParam, true );
	}

	/// <summary>Swap the overlay model (used for throwable/item meshes layered on top of arms).</summary>
	public void UpdateOverlayModel( Model model )
	{
		OverlayModel = model;
		if ( overlayRenderer == null ) return;
		if ( model != null )
		{
			overlayRenderer.Model = model;
			if ( overlayGO != null )
			{
				overlayGO.Enabled = true;
				overlayRenderer.Enabled = true;
			}
		}
		else
		{
			if ( overlayGO != null ) overlayGO.Enabled = false;
		}
	}

	/// <summary>Hot-swap the displayed weapon model — called when the player picks up a new weapon.</summary>
	public void UpdateWeapon( Model model, AnimationGraph animGraph, Model handsModel, Vector3 positionOffset )
		=> UpdateWeapon( model, animGraph, handsModel, positionOffset, null, null, null );

	/// <summary>Hot-swap the displayed weapon model and animation parameters.</summary>
	public void UpdateWeapon( Model model, AnimationGraph animGraph, Model handsModel, Vector3 positionOffset,
		string animFireParam, string animReloadParam, string animReloadEmptyParam )
	{
		WeaponModel = model;
		HandsModel = handsModel;
		AnimGraph = animGraph;
		PositionOffset = positionOffset;
		RestPosition = positionOffset;
		RestRotation = RotationOffset;
		if ( !string.IsNullOrEmpty( animFireParam ) ) AnimFireParam = animFireParam;
		if ( !string.IsNullOrEmpty( animReloadParam ) ) AnimReloadParam = animReloadParam;
		if ( !string.IsNullOrEmpty( animReloadEmptyParam ) ) AnimReloadEmptyParam = animReloadEmptyParam;

		if ( ModelRenderer == null ) return;

		ModelRenderer.Model = WeaponModel ?? Model.Load( "models/dev/box.vmdl" );
		if ( AnimGraph != null ) ModelRenderer.AnimationGraph = AnimGraph;

		// Rebuild hands — destroy old HandsModel child then recreate if needed
		foreach ( var child in modelObject.Children )
			if ( child.Name == "HandsModel" ) { child.Destroy(); break; }

		if ( HandsModel != null )
		{
			var handsGO = new GameObject( true, "HandsModel" );
			handsGO.Parent = modelObject;
			var handsRenderer = handsGO.AddComponent<SkinnedModelRenderer>();
			handsRenderer.Model = HandsModel;
			handsRenderer.BoneMergeTarget = ModelRenderer;
		}
	}

	protected override void OnDestroy()
	{
		if ( Current == this ) Current = null;
		DestroyVisuals();
	}

	public bool BelongsTo( PlayerMovement player ) => player != null && player == OwnerMovement;

	public bool IsOwnedByLocalPlayer()
	{
		if ( !Networking.IsActive )
			return true;

		var ownerMovement = OwnerMovement;
		var localMovement = PlayerIdentity.Local?.Movement;

		if ( ownerMovement != null && localMovement != null )
			return ownerMovement == localMovement;

		var owner = ownerMovement?.GameObject?.Network?.Owner;
		var localConn = Connection.Local;
		if ( owner != null && localConn != null )
			return owner.SteamId == localConn.SteamId;

		return false;
	}

	private bool ShouldCreateLocalVisuals() => !Networking.IsActive || IsOwnedByLocalPlayer();

	private void TryEnsureVisuals()
	{
		if ( HasLocalVisuals || !ShouldCreateLocalVisuals() )
			return;

		var visualParent = ResolveVisualParent();
		if ( visualParent == null || !visualParent.IsValid )
			return;

		CreateVisuals( visualParent );
	}

	private void EnsureVisualParent()
	{
		if ( modelObject == null || !modelObject.IsValid )
			return;

		var visualParent = ResolveVisualParent();
		if ( visualParent == null || !visualParent.IsValid )
			return;

		if ( modelObject.Parent != visualParent )
			modelObject.Parent = visualParent;
	}

	private GameObject ResolveVisualParent()
	{
		var ownerMovement = OwnerMovement;
		if ( Networking.IsActive )
		{
			var localMovement = PlayerIdentity.Local?.Movement;
			if ( ownerMovement == null || localMovement == null || ownerMovement != localMovement )
				return null;
		}

		if ( ownerMovement != null && LocalPresentationController.ShouldHandleLocalPresentation( ownerMovement ) )
		{
			var controller = LocalPresentationController.EnsureForScene( Scene );
			var anchor = controller?.GetOrCreateViewModelAnchor( ownerMovement );
			if ( anchor != null )
				return anchor;

			if ( Networking.IsActive )
				return null;
		}

		return GameObject;
	}

	private void CreateVisuals( GameObject visualParent )
	{
		modelObject = new GameObject( false, "GunModel" );
		modelObject.Parent = visualParent;
		modelObject.LocalPosition = RestPosition;
		modelObject.LocalRotation = RestRotation;

		ModelRenderer = modelObject.AddComponent<SkinnedModelRenderer>();
		ModelRenderer.Model = WeaponModel ?? Model.Load( "models/dev/box.vmdl" );
		if ( AnimGraph is not null )
			ModelRenderer.AnimationGraph = AnimGraph;

		if ( HandsModel is not null )
		{
			var handsGO = new GameObject( true, "HandsModel" );
			handsGO.Parent = modelObject;
			var handsRenderer = handsGO.AddComponent<SkinnedModelRenderer>();
			handsRenderer.Model = HandsModel;
			handsRenderer.BoneMergeTarget = ModelRenderer;
		}

		overlayGO = new GameObject( false, "OverlayModel" );
		overlayGO.Parent = modelObject;
		overlayGO.LocalPosition = new Vector3( 5, -8, 15 );
		overlayRenderer = overlayGO.AddComponent<SkinnedModelRenderer>();
		overlayRenderer.BoneMergeTarget = ModelRenderer;
		if ( OverlayModel != null )
		{
			overlayRenderer.Model = OverlayModel;
			overlayGO.Enabled = true;
		}

		modelObject.Enabled = false;
	}

	private void DestroyVisuals()
	{
		if ( modelObject != null && modelObject.IsValid )
			modelObject.Destroy();

		modelObject = null;
		ModelRenderer = null;
		overlayGO = null;
		overlayRenderer = null;
	}
}
