using Sandbox;
using System.Collections.Generic;

public sealed class CameraMovement : Component
{
	[Property] public PlayerMovement Player { get; set; }
	[Property] public GameObject Body { get; set; }
	[Property] public GameObject Head { get; set; }
	[Property] public float Distance { get; set; } = 0f;
	[Property] public float ShakeDecay { get; set; } = 5f;

	/// <summary>Local offset from Head position to actual eye position. Tune if camera clips inside head mesh.</summary>
	[Property] public Vector3 EyeOffset { get; set; } = new Vector3( 0f, 0f, 0f );

	/// <summary>Drag the SkinnedModelRenderer from the player body here directly.</summary>
	[Property] public SkinnedModelRenderer BodyModelRenderer { get; set; }

	public bool isFirstPerson => Distance == 0f;
	private CameraComponent camera;
	private Vector3 CurrentOffset = Vector3.Zero;
	private float shakeMagnitude = 0f;

	// Cached list of all player body renderers found at start
	private readonly List<ModelRenderer> bodyRenderers = new();

	public void Shake( float magnitude )
	{
		shakeMagnitude = System.Math.Max( shakeMagnitude, magnitude );
	}

	protected override void OnStart()
	{
		camera = Components.Get<CameraComponent>();

		// The visible citizen model is a SkinnedModelRenderer on the Player root, not on Body.
		// Body has its own (already-disabled) renderer — searching from Body finds the wrong one.
		if ( BodyModelRenderer is null && Player?.GameObject is not null )
			BodyModelRenderer = Player.GameObject.Components.Get<SkinnedModelRenderer>();

		CollectBodyRenderers();

		var health = GameObject.Root.Components.GetInDescendantsOrSelf<HealthComponent>();
		if ( health != null )
		{
			health.OnDamageTaken += ( dmg ) => Shake( dmg * 0.8f );
			health.OnDamageTakenWithAttacker += OnDamageTakenWithAttacker;
		}
	}

	private void CollectBodyRenderers()
	{
		bodyRenderers.Clear();
		// Fallback list is no longer the primary path — BodyModelRenderer is auto-assigned in OnStart.
		// Keep this for safety in case BodyModelRenderer is still null at runtime.
		if ( Player?.GameObject is not null )
		{
			foreach ( var smr in Player.GameObject.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
				bodyRenderers.Add( smr );
		}
	}

	private void OnDamageTakenWithAttacker( float damage, GameObject attacker )
	{
		if ( attacker == null || Head is null ) return;

		var toAttacker = (attacker.WorldPosition - Head.WorldPosition).WithZ( 0 ).Normal;
		var forward = Head.WorldRotation.Forward.WithZ( 0 ).Normal;
		var right = Head.WorldRotation.Right.WithZ( 0 ).Normal;

		float dotForward = Vector3.Dot( toAttacker, forward );
		float dotRight = Vector3.Dot( toAttacker, right );

		float angle = System.MathF.Atan2( dotRight, dotForward ) * (180f / System.MathF.PI);
		PlayerStats.AddDamageIndicator( angle );
	}

	protected override void OnUpdate()
	{
		PlayerStats.TickDamageIndicators( Time.Delta );

		if ( Head is null || Player is null ) return;

		// Re-collect renderers if we haven't found any yet
		if ( bodyRenderers.Count == 0 ) CollectBodyRenderers();

		// Hide/show body — use directly assigned renderer first, fall back to searched list
		// Always hide body when incapacitated (camera is below body level and sees it from underneath)
		// Use ShadowsOnly instead of Enabled=false — more reliable for hiding skinned models in S&box
		bool shouldHideBody = isFirstPerson || PlayerStats.IsIncapacitated;
		var hideType = shouldHideBody
			? ModelRenderer.ShadowRenderType.ShadowsOnly
			: ModelRenderer.ShadowRenderType.On;
		if ( BodyModelRenderer is not null )
			BodyModelRenderer.RenderType = hideType;
		else
			foreach ( var r in bodyRenderers )
				if ( r is not null ) r.RenderType = hideType;

		var eyeAngles = Head.WorldRotation.Angles();
		eyeAngles.pitch -= Input.MouseDelta.y * -0.1f;
		eyeAngles.yaw -= Input.MouseDelta.x * 0.1f;
		eyeAngles.roll = 0;
		eyeAngles.pitch = eyeAngles.pitch.Clamp( -89.9f, 89.9f );
		if ( PlayerStats.IsIncapacitated ) eyeAngles.roll = MathX.Lerp( eyeAngles.roll, 25f, Time.Delta * 3f );
		else eyeAngles.roll = MathX.Lerp( eyeAngles.roll, 0f, Time.Delta * 5f );
		Head.WorldRotation = eyeAngles.ToRotation();

		var targetOffset = Vector3.Zero;
		if ( Player.isCrouching ) targetOffset += Vector3.Down * 32f;
		if ( PlayerStats.IsIncapacitated ) targetOffset += Vector3.Down * 35f;
		CurrentOffset = Vector3.Lerp( CurrentOffset, targetOffset, Time.Delta * 5f );

		var shakeOffset = Vector3.Zero;
		if ( shakeMagnitude > 0.01f )
		{
			var rng = Game.Random;
			if ( rng is not null )
				shakeOffset = new Vector3(
					rng.Float( -shakeMagnitude, shakeMagnitude ),
					rng.Float( -shakeMagnitude, shakeMagnitude ),
					rng.Float( -shakeMagnitude, shakeMagnitude )
				);
			shakeMagnitude = MathX.Lerp( shakeMagnitude, 0f, Time.Delta * ShakeDecay );
		}
		else
		{
			shakeMagnitude = 0f;
		}

		if ( camera is null ) camera = Components.Get<CameraComponent>();
		if ( camera is null ) return;

		var camPos = Head.WorldPosition + CurrentOffset + Head.WorldRotation * EyeOffset;
		if ( !isFirstPerson )
		{
			var camForward = eyeAngles.ToRotation().Forward;
			var camTrace = Scene.Trace.Ray( camPos, camPos - (camForward * Distance) )
				.WithoutTags( "player", "trigger" )
				.Run();
			camPos = camTrace.Hit ? camTrace.HitPosition + camTrace.Normal : camTrace.EndPosition;
		}

		WorldPosition = camPos + shakeOffset;
		WorldRotation = eyeAngles.ToRotation();
	}
}
