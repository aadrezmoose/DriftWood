using Sandbox;

/// <summary>
/// L4D-style safe room door. Attach this to the door GameObject whose pivot is at the hinge edge.
/// Press E while looking at it to open or close. Smoothly swings on the vertical axis.
///
/// Setup in editor:
///   1. Create a GameObject at the hinge position (left or right edge of the door frame).
///   2. Add your door model as a child (or use a ModelRenderer on this object).
///   3. Add a BoxCollider so the raycast and player collision work.
///   4. Add this component. Adjust OpenAngle and OpensInward to suit the door frame.
/// </summary>
public sealed class SafeRoomDoor : Component
{
	private bool IsAuthoritativeInstance() => !Networking.IsActive || Connection.Local?.IsHost == true;

	/// <summary>Degrees the door rotates when fully open (typically 90).</summary>
	[Property] public float OpenAngle { get; set; } = 90f;
	/// <summary>Flip the swing direction — use if the door opens the wrong way.</summary>
	[Property] public bool OpensInward { get; set; } = false;
	/// <summary>How fast the door swings. Higher = snappier.</summary>
	[Property] public float OpenSpeed { get; set; } = 6f;
	[Property] public SoundEvent OpenSound { get; set; }
	[Property] public SoundEvent CloseSound { get; set; }
	/// <summary>If true, automatically closes when the player leaves the start safe room.</summary>
	[Property] public bool AutoCloseOnPlayerLeave { get; set; } = false;
	/// <summary>If true, the door always swings away from the player when opened.</summary>
	[Property] public bool SmartOpen { get; set; } = true;

	public bool IsOpen { get; private set; } = false;

	private Rotation closedRotation;
	private float currentAngle = 0f;
	private float targetAngle  = 0f;

	protected override void OnAwake()
	{
		closedRotation = WorldRotation;
		if ( AutoCloseOnPlayerLeave )
			SafeRoom.OnPlayerExited += OnPlayerLeftSafeRoom;
	}

	protected override void OnDestroy()
	{
		SafeRoom.OnPlayerExited -= OnPlayerLeftSafeRoom;
	}

	private void OnPlayerLeftSafeRoom( SafeRoom room )
	{
		if ( room.IsStartRoom )
			Close();
	}

	protected override void OnUpdate()
	{
		if ( System.MathF.Abs( currentAngle - targetAngle ) > 0.05f )
		{
			currentAngle = MathX.Lerp( currentAngle, targetAngle, Time.Delta * OpenSpeed );
			float dir = OpensInward ? -1f : 1f;
			WorldRotation = closedRotation * Rotation.FromAxis( Vector3.Up, currentAngle * dir );
		}
	}

	public void Toggle()
	{
		if ( !Networking.IsActive )
		{
			ApplyToggleAuthoritative( GetInteractorPositionFallback() );
			return;
		}

		if ( IsAuthoritativeInstance() )
		{
			ApplyToggleAuthoritative( GetInteractorPositionFallback() );
			BroadcastApplyDoorState( IsOpen, OpensInward );
			return;
		}

		RequestToggleFromHost( GetInteractorPositionFallback() );
	}

	public void Open()
	{
		if ( !Networking.IsActive )
		{
			SetDoorState( true, ResolveInwardFromPosition( GetInteractorPositionFallback() ), true );
			return;
		}

		if ( IsAuthoritativeInstance() )
		{
			SetDoorState( true, ResolveInwardFromPosition( GetInteractorPositionFallback() ), true );
			BroadcastApplyDoorState( IsOpen, OpensInward );
			return;
		}

		RequestSetOpenFromHost( true, GetInteractorPositionFallback() );
	}

	public void Close()
	{
		if ( !Networking.IsActive )
		{
			SetDoorState( false, OpensInward, true );
			return;
		}

		if ( IsAuthoritativeInstance() )
		{
			SetDoorState( false, OpensInward, true );
			BroadcastApplyDoorState( IsOpen, OpensInward );
			return;
		}

		RequestSetOpenFromHost( false, GetInteractorPositionFallback() );
	}

	public string GetInteractHint() => IsOpen ? "Close Door" : "Open Door";

	private Vector3 GetInteractorPositionFallback()
	{
		var localPlayer = PlayerIdentity.Local?.GameObject
			?? Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;
		return localPlayer?.WorldPosition ?? WorldPosition;
	}

	private bool ResolveInwardFromPosition( Vector3 interactorPosition )
	{
		if ( !SmartOpen )
			return OpensInward;

		var toPlayer = (interactorPosition - WorldPosition).WithZ( 0 );
		if ( toPlayer.IsNearZeroLength )
			return OpensInward;

		float dot = Vector3.Dot( toPlayer.Normal, closedRotation.Forward.WithZ( 0 ).Normal );
		return dot < 0f;
	}

	private void ApplyToggleAuthoritative( Vector3 interactorPosition )
	{
		bool nextOpen = !IsOpen;
		bool inward = nextOpen ? ResolveInwardFromPosition( interactorPosition ) : OpensInward;
		SetDoorState( nextOpen, inward, true );
	}

	private void SetDoorState( bool isOpen, bool opensInward, bool playSound )
	{
		bool changed = IsOpen != isOpen || OpensInward != opensInward;
		if ( !changed ) return;

		bool wasOpen = IsOpen;
		IsOpen = isOpen;
		OpensInward = opensInward;
		targetAngle = IsOpen ? OpenAngle : 0f;

		if ( !playSound ) return;

		if ( IsOpen && !wasOpen )
		{
			if ( OpenSound != null ) Sound.Play( OpenSound, WorldPosition );
		}
		else if ( !IsOpen && wasOpen )
		{
			if ( CloseSound != null ) Sound.Play( CloseSound, WorldPosition );
		}
	}

	[Rpc.Broadcast]
	private void RequestToggleFromHost( Vector3 interactorPosition )
	{
		if ( !IsAuthoritativeInstance() ) return;
		ApplyToggleAuthoritative( interactorPosition );
		BroadcastApplyDoorState( IsOpen, OpensInward );
	}

	[Rpc.Broadcast]
	private void RequestSetOpenFromHost( bool open, Vector3 interactorPosition )
	{
		if ( !IsAuthoritativeInstance() ) return;
		bool inward = open ? ResolveInwardFromPosition( interactorPosition ) : OpensInward;
		SetDoorState( open, inward, true );
		BroadcastApplyDoorState( IsOpen, OpensInward );
	}

	[Rpc.Broadcast]
	private void BroadcastApplyDoorState( bool isOpen, bool opensInward )
	{
		SetDoorState( isOpen, opensInward, true );
	}
}
