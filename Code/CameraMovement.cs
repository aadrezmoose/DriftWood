using Sandbox;

public sealed class CameraMovement : Component
{
	// Camera movement properties
	[Property] public PlayerMovement Player { get; set; }
	[Property] public GameObject Body { get; set; }
	[Property] public GameObject Head { get; set; }
	[Property] public float Distance { get; set; } = 0f;

	///Variables
	public bool isFirstPerson => Distance == 0f;
	private CameraComponent camera;
	private ModelRenderer BodyRenderer;
	private Vector3 CurrentOffset = Vector3.Zero;

	protected override void OnAwake()
	{
		camera = Components.Get<CameraComponent>();
		BodyRenderer = Body.Components.Get<ModelRenderer>();
	}

	protected override void OnUpdate()
	{
		//Rotate the head based on mouse input
	var eyeAngles = Head.Transform.Rotation.Angles();
		eyeAngles.pitch -= Input.MouseDelta.y * -0.1f;
		eyeAngles.yaw -= Input.MouseDelta.x * 0.1f;
		eyeAngles.roll = 0;
		eyeAngles.pitch = eyeAngles.pitch.Clamp( -89.9f, 89.9f );
	Head.Transform.Rotation = eyeAngles.ToRotation();

		var targetOffset = Vector3.Zero;
		if(Player.isCrouching) targetOffset += Vector3.Down * 32f;
		CurrentOffset = Vector3.Lerp( CurrentOffset, targetOffset, Time.Delta * 10f );

		//Set camera position
		if(camera is not null)
		{
			var camPos = Head.Transform.Position + CurrentOffset;
			if(!isFirstPerson)
			{
				//perform trace back to see where we can place the camera
				var camForward = eyeAngles.ToRotation().Forward;
				var camTrace = Scene.Trace.Ray( camPos, camPos - (camForward * Distance ) )
					.WithoutTags( "player" , "trigger" )
					.Run();

				if(camTrace.Hit)
				{
					camPos = camTrace.HitPosition + camTrace.Normal;
				}
				else
				{
					camPos = camTrace.EndPosition;
				}

				//Show the body if not in first person
				BodyRenderer.RenderType = ModelRenderer.ShadowRenderType.On;				
			}
			else
			{
				//Hide the body if in first person
				BodyRenderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
			}
			//Set the pos of cam to calcualted pos
			camera.Transform.Position = camPos;
			camera.Transform.Rotation = eyeAngles.ToRotation();
			
		}
	}
}
