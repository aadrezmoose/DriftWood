using Sandbox;
using Sandbox.Citizen;

/// <summary>
/// Drives the player's CitizenAnimationHelper hold type based on the equipped weapon,
/// and handles downed-state arm pose. Does NOT create a separate arms model —
/// proper FPS hands require a dedicated skinned arms model with BoneMergeTarget
/// pointing at a skinned weapon viewmodel (see SWB's ViewModelHands pattern).
/// </summary>
public sealed class ViewModelArms : Component
{
	[Property] public PlayerMovement Player { get; set; }
	[Property] public WeaponManager WeaponManager { get; set; }
	[Property] public CameraMovement CameraMovement { get; set; }
	[Property] public ViewModelHandler ProcViewModelHandler { get; set; }

	private CitizenAnimationHelper playerAnim;

	protected override void OnStart()
	{
		// Drive hold type on the player's existing CitizenAnimationHelper
		playerAnim = Player?.GameObject.Components.Get<CitizenAnimationHelper>();
	}

	protected override void OnUpdate()
	{
		if ( playerAnim is null || !playerAnim.Target.IsValid() ) return;
		if ( PlayerStats.IsDead ) return;

		// HoldType is also set by PlayerMovement.UpdateAnimations — this overrides it
		// for the downed state, which PlayerMovement now also handles.
		// This component is currently a no-op placeholder until a proper
		// BoneMerge hands model is set up.
	}
}
