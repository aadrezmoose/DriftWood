using Sandbox;
using System.Linq;

public sealed class GunViewModel : Component
{
	[Property] public Model WeaponModel { get; set; }
	[Property] public Model HandsModel { get; set; }
	[Property] public AnimationGraph AnimGraph { get; set; }
	   [Property] public Vector3 PositionOffset { get; set; } = new Vector3( 25, 6, -10 );
	   [Property] public Rotation RotationOffset { get; set; } = Rotation.FromPitch( 0 );  // Explicitly zero rotation

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

	private GameObject anchorObject;
	private GameObject overlayGO;
	private SkinnedModelRenderer overlayRenderer;
	private PlayerIdentity _identity;
	private PlayerIdentity Identity => _identity ??= Components.GetInAncestorsOrSelf<PlayerIdentity>() ?? Components.GetInDescendantsOrSelf<PlayerIdentity>();

	private bool IsOwnedByLocal()
	{
		if ( !Networking.IsActive ) return true;

		var owner = Identity?.GameObject?.Network?.Owner;
		var localConn = Connection.Local;
		if ( owner != null && localConn != null )
			return owner.SteamId == localConn.SteamId;

		return false;
	}

	private GameObject ResolveViewModelParent()
	{
		var activeCamera = Scene?.GetAllComponents<CameraComponent>()
			?.FirstOrDefault( camera => camera != null && camera.Enabled && camera.IsMainCamera && camera.GameObject != null && camera.GameObject.IsValid() );

		return activeCamera?.GameObject ?? GameObject;
	}

	private void SyncAnchorToActiveCamera()
	{
		if ( anchorObject == null || !anchorObject.IsValid() ) return;
		if ( !IsOwnedByLocal() ) return;

		var targetParent = ResolveViewModelParent();
		if ( targetParent == null ) return;

		anchorObject.WorldPosition = targetParent.WorldPosition;
		anchorObject.WorldRotation = targetParent.WorldRotation;
	}

	private void EnsureVisualObjects()
	{
		if ( modelObject != null && modelObject.IsValid() && ModelRenderer != null )
			return;

		anchorObject = new GameObject( false, "ViewModelAnchor" );
		anchorObject.Parent = Scene;
		SyncAnchorToActiveCamera();

		modelObject = new GameObject( false, "GunModel" );
		modelObject.Parent = anchorObject;
		modelObject.LocalPosition = PositionOffset;
		modelObject.LocalRotation = RotationOffset;

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
		if ( overlayRenderer is SkinnedModelRenderer skinned )
		{
			skinned.BoneMergeTarget = ModelRenderer;
		}
		if ( OverlayModel != null )
		{
			overlayRenderer.Model = OverlayModel;
			overlayGO.Enabled = true;
		}

	}

	/// <summary>Inspector-assigned overlay model (throwables/heal items). Usually null — assigned at runtime.</summary>
	[Property] public Model OverlayModel { get; set; }

	protected override void OnStart()
	{
		RotationOffset = Rotation.Identity;  // Override any stale inspector-baked value
	}

	protected override void OnUpdate()
	{
		if ( !IsOwnedByLocal() )
		{
			if ( anchorObject != null && anchorObject.IsValid() )
				anchorObject.Enabled = false;
			return;
		}

		if ( anchorObject != null && anchorObject.IsValid() )
		{
			SyncAnchorToActiveCamera();
			anchorObject.Enabled = modelObject?.Enabled ?? false;
		}
	}

	/// <summary>Show or hide the viewmodel based on whether the current slot has content.</summary>
	public void ShowModel( bool show )
	{
		if ( !IsOwnedByLocal() )
		{
			if ( anchorObject != null && anchorObject.IsValid() )
				anchorObject.Enabled = false;
			if ( modelObject != null && modelObject.IsValid() )
				modelObject.Enabled = false;
			return;
		}

		EnsureVisualObjects();
		SyncAnchorToActiveCamera();
		anchorObject.Enabled = show;
		modelObject.Enabled = show;
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
		if ( !IsOwnedByLocal() ) return;
		EnsureVisualObjects();
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
		if ( !IsOwnedByLocal() ) return;

		EnsureVisualObjects();
		if ( ModelRenderer == null ) return;
		SyncAnchorToActiveCamera();

		ModelRenderer.Model = model ?? Model.Load( "models/dev/box.vmdl" );
		if ( animGraph != null ) ModelRenderer.AnimationGraph = animGraph;
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
		anchorObject?.Destroy();
		modelObject?.Destroy();
	}
}
