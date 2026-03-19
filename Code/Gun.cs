using Sandbox;

public enum WeaponCategory { Primary, Secondary }

public abstract class Gun : Component
{
	[Property] public WeaponCategory Category { get; set; } = WeaponCategory.Primary;
	[Property] public float Damage { get; set; } = 10f;
	[Property] public float FireRate { get; set; } = 0.1f; // seconds between shots
	[Property] public int AmmoClip { get; set; } = 30;
	[Property] public int AmmoReserve { get; set; } = 90;
	[Property] public float ReloadTime { get; set; } = 2.0f;
	[Property] public bool IsAutomatic { get; set; } = true;
	[Property] public bool UnlimitedAmmo { get; set; } = false;

	/// <summary>Particle prefab spawned at the "muzzle" attachment on the viewmodel skeleton each shot.</summary>
	[Property] public PrefabFile MuzzleFlashParticle { get; set; }
	/// <summary>Optional legacy PointLight child — kept for backwards compat, still works alongside the particle.</summary>
	[Property] public GameObject MuzzleFlashEffect { get; set; }

	private int maxAmmoReserve;
	protected int currentAmmo;
	protected float fireRateRemaining = 0f;
	protected float reloadTimeRemaining = 0f;
	protected bool isReloading = false;
	protected GameObject playerHead;   // Reference to player's head for firing
	protected GameObject playerCamera; // Camera child of head — used as fire origin so shots align with crosshair
	protected GunViewModel viewModel; // Reference to the gun's visual model
	protected GameObject owner; // Root owner (player), used to ignore self in traces

	[Property] public Color MuzzleLightColor { get; set; } = new Color( 1f, 0.75f, 0.3f );
	[Property] public float MuzzleLightRadius { get; set; } = 300f;
	[Property] public float MuzzleLightBrightness { get; set; } = 10f;
	[Property] public float MuzzleLightDuration { get; set; } = 0.05f;

	private GameObject gunModelObject;
	private Vector3 gunRestPosition;
	private float reloadAnimTimer = 0f;
	private const float ReloadAnimHalfTime = 0.4f;
	private float muzzleFlashTimer = 0f;
	private GameObject activeFlashParticle;
	private const float ParticleFlashLifetime = 1.5f;
	private PointLight muzzlePointLight;
	private float muzzleLightTimer = 0f;

	/// <summary>
	/// World-space positional offset applied during reload animation.
	/// ViewModelArms reads this to shift the hand-attached gun down/up.
	/// When ViewModelArms is not present, it is applied directly to GunModel.LocalPosition.
	/// </summary>
	public Vector3 ReloadOffset { get; private set; } = Vector3.Zero;
	private const float MuzzleFlashDuration = 0.05f;

	protected override void OnAwake()
	{
		currentAmmo = AmmoClip;
		maxAmmoReserve = AmmoReserve;

		// Try to find or create viewmodel
		viewModel = Components.Get<GunViewModel>();

		// Load muzzle flash prefab if not set in inspector
		if ( MuzzleFlashParticle == null )
			MuzzleFlashParticle = ResourceLibrary.Get<PrefabFile>( "prefabs/particles/muzzle/muzzleflash.prefab" );
	}

	protected override void OnUpdate()
	{
		if (fireRateRemaining > 0f) fireRateRemaining -= Time.Delta;
		if (reloadTimeRemaining > 0f) reloadTimeRemaining -= Time.Delta;

		if (reloadTimeRemaining <= 0f && isReloading)
		{
			isReloading = false;
			FinishReload();
		}

		// Clean up muzzle flash after duration
		if ( muzzleFlashTimer > 0f )
		{
			muzzleFlashTimer -= Time.Delta;
			if ( muzzleFlashTimer <= 0f )
			{
				if ( MuzzleFlashEffect is not null ) MuzzleFlashEffect.Enabled = false;
				activeFlashParticle?.Destroy();
				activeFlashParticle = null;
			}
		}

		// Disable muzzle point light after its brief duration
		if ( muzzleLightTimer > 0f )
		{
			muzzleLightTimer -= Time.Delta;
			if ( muzzleLightTimer <= 0f && muzzlePointLight is not null )
				muzzlePointLight.GameObject.Enabled = false;
		}

		UpdateReloadAnimation();
	}

	private void UpdateReloadAnimation()
	{
		if ( isReloading )
		{
			reloadAnimTimer += Time.Delta;
			float half = ReloadTime * 0.5f;
			float t = reloadAnimTimer < half
				? reloadAnimTimer / half
				: 1f - (reloadAnimTimer - half) / half;
			ReloadOffset = new Vector3( 0, 0, MathX.Lerp( 0f, -20f, t ) );
		}
		else
		{
			reloadAnimTimer = 0f;
			ReloadOffset = Vector3.Zero;
		}

		// If no ViewModelArms is controlling the gun, apply offset directly to GunModel
		if ( gunModelObject is not null && !HasViewModelArms() )
		{
			gunModelObject.LocalPosition = gunRestPosition + ReloadOffset;
		}
	}

	private bool HasViewModelArms()
	{
		// Check if the camera (parent of gunModelObject) has a ViewModelArms component
		return gunModelObject?.Parent?.Components.Get<ViewModelArms>() is not null
			|| gunModelObject?.Parent?.Parent?.Components.Get<ViewModelArms>() is not null;
	}

	/// <summary>Cancel an in-progress reload. Override in subclasses to clean up extra state.</summary>
	public virtual void CancelReload()
	{
		isReloading = false;
		reloadTimeRemaining = 0f;
		reloadAnimTimer = 0f;
		ReloadOffset = Vector3.Zero;
	}

	public virtual void Fire()
	{
		if ( isReloading ) { CancelReload(); return; } // cancel reload, fire next press
		if (fireRateRemaining > 0f) return;
		if (currentAmmo <= 0) return;

		currentAmmo--;
		fireRateRemaining = FireRate;
		PlayerStats.ShotsFired++;

		GunViewModel.Current?.PlayAnim( "fire" );
		OnFire();
		SpawnMuzzleFlash();
	}

	private void SpawnMuzzleFlash()
	{
		// Particle-based flash — positioned at the "muzzle" attachment on the viewmodel skeleton
		if ( MuzzleFlashParticle is not null )
		{
			activeFlashParticle?.Destroy();

			var vm = GunViewModel.Current?.ModelRenderer;
			var attach = vm?.GetAttachment( "muzzle" );
			var pos = attach?.Position ?? playerCamera?.WorldPosition ?? Vector3.Zero;
			var rot = attach?.Rotation ?? playerCamera?.WorldRotation ?? Rotation.Identity;

			var prefabScene = SceneUtility.GetPrefabScene( MuzzleFlashParticle );
			if ( prefabScene != null )
			{
				activeFlashParticle = prefabScene.Clone( pos );
				activeFlashParticle.WorldRotation = rot;
				muzzleFlashTimer = ParticleFlashLifetime;
			}
		}

		// Legacy PointLight flash
		if ( MuzzleFlashEffect is not null )
		{
			MuzzleFlashEffect.Enabled = true;
			if ( muzzleFlashTimer < MuzzleFlashDuration )
				muzzleFlashTimer = MuzzleFlashDuration;
		}

		// Dynamic point light — created once, reused each shot
		if ( MuzzleLightBrightness > 0f )
		{
			if ( muzzlePointLight is null || !muzzlePointLight.IsValid() )
			{
				var lightGO = new GameObject( false, "MuzzlePointLight" );
				lightGO.Parent = playerCamera ?? playerHead ?? GameObject;
				lightGO.LocalPosition = Vector3.Forward * 20f;
				muzzlePointLight = lightGO.Components.Create<PointLight>();
			}

			// S&box lights use HDR color values (> 1.0) to control brightness
			muzzlePointLight.LightColor = MuzzleLightColor * MuzzleLightBrightness;
			muzzlePointLight.Radius = MuzzleLightRadius;
			muzzlePointLight.GameObject.Enabled = true;
			muzzleLightTimer = MuzzleLightDuration;
		}
	}

	protected abstract void OnFire();

	public virtual void Reload()
	{
		if (isReloading) return;
		if (currentAmmo == AmmoClip) return;
		if (!UnlimitedAmmo && AmmoReserve <= 0) return;

		isReloading = true;
		reloadTimeRemaining = ReloadTime;

		var animParam = currentAmmo == 0 ? "reload_empty" : "reload";
		GunViewModel.Current?.PlayAnim( animParam );
	}

	private void FinishReload()
	{
		int ammoNeeded = AmmoClip - currentAmmo;
		if ( UnlimitedAmmo )
		{
			currentAmmo = AmmoClip; // Refill clip, reserve untouched
		}
		else
		{
			int ammoToAdd = System.Math.Min( ammoNeeded, AmmoReserve );
			currentAmmo += ammoToAdd;
			AmmoReserve -= ammoToAdd;
		}
	}

	public void SetPlayerHead(GameObject head)
	{
		playerHead = head;

		// Find camera child so shots originate from the exact eye position
		foreach ( var child in head.Children )
		{
			if ( child.Name == "Camera" ) { playerCamera = child; break; }
		}

		// Find GunModel anywhere under the head hierarchy
		foreach ( var child in head.Children )
		{
			var found = FindGunModel( child );
			if ( found != null ) { gunModelObject = found; break; }
		}
		if ( gunModelObject != null )
			gunRestPosition = gunModelObject.LocalPosition;
	}

	private GameObject FindGunModel( GameObject go )
	{
		if ( go.Name == "GunModel" ) return go;
		foreach ( var child in go.Children )
		{
			var found = FindGunModel( child );
			if ( found != null ) return found;
		}
		return null;
	}

	public void SetOwner(GameObject ownerGameObject)
	{
		owner = ownerGameObject;
	}

	protected override void OnDestroy()
	{
		activeFlashParticle?.Destroy();
	}

	public void RefillReserve() { if ( !UnlimitedAmmo ) AmmoReserve = maxAmmoReserve; }
	public int GetCurrentAmmo() => currentAmmo;
	public int GetReserveAmmo() => UnlimitedAmmo ? -1 : AmmoReserve;
	public bool IsReloading() => isReloading;
	public string GetAmmoText() => $"{currentAmmo}/{AmmoReserve}";
}
