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

	private GameObject modelObject;
	public GameObject ModelObject => modelObject;
	public SkinnedModelRenderer ModelRenderer { get; private set; }

	private GameObject overlayGO;
	private SkinnedModelRenderer overlayRenderer;

	/// <summary>Inspector-assigned overlay model (throwables/heal items). Usually null — assigned at runtime.</summary>
	[Property] public Model OverlayModel { get; set; }

	protected override void OnStart()
	{
		modelObject = new GameObject( false, "GunModel" );  // Start disabled
		modelObject.Parent = GameObject;
		modelObject.LocalPosition = PositionOffset;
		modelObject.LocalRotation = RotationOffset;

		ModelRenderer = modelObject.AddComponent<SkinnedModelRenderer>();
		ModelRenderer.Model = WeaponModel ?? Model.Load( "models/dev/box.vmdl" );
		if ( AnimGraph is not null )
			ModelRenderer.AnimationGraph = AnimGraph;

		// Optional hands model — bone-merges onto the weapon skeleton so hands grip the gun
		if ( HandsModel is not null )
		{
			var handsGO = new GameObject( true, "HandsModel" );
			handsGO.Parent = modelObject;
			var handsRenderer = handsGO.AddComponent<SkinnedModelRenderer>();
			handsRenderer.Model = HandsModel;
			handsRenderer.BoneMergeTarget = ModelRenderer;
		}

		// Always create overlay GO (hidden initially) — model assigned at runtime via UpdateOverlayModel()
		overlayGO = new GameObject( false, "OverlayModel" );
		overlayGO.Parent = modelObject;
		overlayGO.LocalPosition = new Vector3( 5, -8, 15 );
		overlayRenderer = overlayGO.AddComponent<SkinnedModelRenderer>();
		// Bone-merge the overlay onto the weapon skeleton
		if ( overlayRenderer is SkinnedModelRenderer skinned )
		{
			skinned.BoneMergeTarget = ModelRenderer;
		}
		if ( OverlayModel != null )
		{
			overlayRenderer.Model = OverlayModel;
			overlayGO.Enabled = true;
		}

		// Always start disabled - WeaponManager will enable when needed
		modelObject.Enabled = false;

		Log.Info( $"[GunViewModel] OnStart: WeaponModel={WeaponModel?.ResourcePath ?? "null"}, HandsModel={HandsModel?.ResourcePath ?? "null"}" );
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
		if ( ModelRenderer == null ) return;

		ModelRenderer.Model = model ?? Model.Load( "models/dev/box.vmdl" );
		if ( animGraph != null ) ModelRenderer.AnimationGraph = animGraph;
		Log.Info($"[GunViewModel] Applying LocalPosition offset: {positionOffset}");
		modelObject.LocalPosition = positionOffset;
		modelObject.LocalRotation = RotationOffset;  // Reset rotation to the inspector value
		if ( !string.IsNullOrEmpty( animFireParam ) ) AnimFireParam = animFireParam;
		if ( !string.IsNullOrEmpty( animReloadParam ) ) AnimReloadParam = animReloadParam;
		if ( !string.IsNullOrEmpty( animReloadEmptyParam ) ) AnimReloadEmptyParam = animReloadEmptyParam;

		// Rebuild hands — destroy old HandsModel child then recreate if needed
		foreach ( var child in modelObject.Children )
			if ( child.Name == "HandsModel" ) { child.Destroy(); break; }

		if ( handsModel != null )
		{
			var handsGO = new GameObject( true, "HandsModel" );
			handsGO.Parent = modelObject;
			var handsRenderer = handsGO.AddComponent<SkinnedModelRenderer>();
			handsRenderer.Model = handsModel;
			handsRenderer.BoneMergeTarget = ModelRenderer;
		}
	}

	protected override void OnDestroy()
	{
		if ( Current == this ) Current = null;
		modelObject?.Destroy();
	}
}
