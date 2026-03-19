using Sandbox;

public sealed class GunViewModel : Component
{
	[Property] public Model WeaponModel { get; set; }
	[Property] public Model HandsModel { get; set; }
	[Property] public AnimationGraph AnimGraph { get; set; }
	[Property] public Vector3 PositionOffset { get; set; } = new Vector3( 25, 6, -10 );
	[Property] public Rotation RotationOffset { get; set; } = Rotation.Identity;

	/// <summary>Which weapon slot index this viewmodel belongs to (0 = first slot, 1 = second, etc.).</summary>
	[Property] public int WeaponSlotIndex { get; set; } = 0;

	/// <summary>The active viewmodel instance — kept for backwards compat, points to whichever slot is active.</summary>
	public static GunViewModel Current { get; private set; }

	private GameObject modelObject;
	public SkinnedModelRenderer ModelRenderer { get; private set; }

	protected override void OnStart()
	{
		modelObject = new GameObject( true, "GunModel" );
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
	}

	protected override void OnUpdate()
	{
		if ( modelObject == null ) return;

		bool isWeaponSlotActive = PlayerStats.ActiveSlotIndex < PlayerStats.WeaponSlotCount;
		bool isThisSlotActive   = PlayerStats.ActiveSlotIndex == WeaponSlotIndex;

		// Hide arms when the slot has no weapon equipped (shows placeholder name)
		bool slotHasWeapon = PlayerStats.AllSlotNames.Count > WeaponSlotIndex
			&& PlayerStats.AllSlotNames[WeaponSlotIndex] != "Primary"
			&& PlayerStats.AllSlotNames[WeaponSlotIndex] != "Secondary";

		modelObject.Enabled = isWeaponSlotActive && isThisSlotActive && slotHasWeapon;

		// Keep Current pointing at whichever viewmodel is visible
		if ( modelObject.Enabled )
			Current = this;
	}

	/// <summary>Trigger a bool animation parameter (auto-resets in the graph).</summary>
	public void PlayAnim( string param )
	{
		ModelRenderer?.Set( param, true );
	}

	/// <summary>Hot-swap the displayed weapon model — called when the player picks up a new weapon.</summary>
	public void UpdateWeapon( Model model, AnimationGraph animGraph, Model handsModel, Vector3 positionOffset )
	{
		if ( ModelRenderer == null ) return;

		ModelRenderer.Model = model ?? Model.Load( "models/dev/box.vmdl" );
		if ( animGraph != null ) ModelRenderer.AnimationGraph = animGraph;
		if ( positionOffset != default ) modelObject.LocalPosition = positionOffset;

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
