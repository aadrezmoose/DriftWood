using Sandbox;
using System.Linq;

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
	[Property] public float RecoilAmount { get; set; } = 1.0f;
	[Property] public SoundEvent EmptySound { get; set; }

	/// <summary>Particle prefab spawned at the "muzzle" attachment on the viewmodel skeleton each shot.</summary>
	[Property] public PrefabFile MuzzleFlashParticle { get; set; }
	/// <summary>Optional legacy PointLight child — kept for backwards compat, still works alongside the particle.</summary>
	[Property] public GameObject MuzzleFlashEffect { get; set; }

	/// <summary>Particle prefab spawned at bullet impact points on world geometry.</summary>
	[Property] public PrefabFile ImpactParticlePrefab { get; set; }
	/// <summary>Decal material projected onto surfaces at bullet impact points.</summary>
	[Property] public Material ImpactDecalMaterial { get; set; }
	[Property] public float ImpactDecalSize { get; set; } = 4f;

	private int maxAmmoReserve;
	protected int currentAmmo;
	protected float fireRateRemaining = 0f;
	private float emptySoundCooldown = 0f;
	protected float reloadTimeRemaining = 0f;
	protected bool isReloading = false;
	protected GameObject playerHead;   // Reference to player's head for firing
	protected GameObject playerCamera; // Camera child of head — used as fire origin so shots align with crosshair
	protected GunViewModel viewModel; // Reference to the gun's visual model
	protected GameObject owner; // Root owner (player), used to ignore self in traces
	protected PlayerIdentity ownerIdentity; // Cached for per-player stat tracking

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
	private bool IsLocallyOwnedGun()
	{
		if ( owner != null )
			return PlayerIdentity.IsOwnedByLocal( owner );

		var ownerPlayer = Components.GetInAncestorsOrSelf<PlayerMovement>();
		if ( ownerPlayer != null )
			return PlayerIdentity.IsOwnedByLocal( ownerPlayer.GameObject );

		return !Networking.IsActive || !IsProxy;
	}

	private static GameObject FindChildByNameRecursive( GameObject parent, string name )
	{
		if ( parent == null ) return null;
		foreach ( var child in parent.Children )
		{
			if ( child.Name == name ) return child;
			var found = FindChildByNameRecursive( child, name );
			if ( found != null ) return found;
		}
		return null;
	}

	private bool EnsureFireOrigin()
	{
		if ( playerHead != null && playerHead.IsValid )
			return true;

		var pm = owner?.Components.GetInDescendantsOrSelf<PlayerMovement>()
			?? Components.GetInAncestorsOrSelf<PlayerMovement>();

		var head = pm?.Head;
		if ( head == null || !head.IsValid )
			head = FindChildByNameRecursive( pm?.GameObject ?? owner ?? GameObject, "Head" );

		if ( head != null && head.IsValid )
		{
			SetPlayerHead( head );
			return true;
		}

		// Fallback: use whichever camera is currently rendering for this client.
		var mainCam = Scene.GetAllComponents<CameraComponent>()
			.FirstOrDefault( c => c != null && c.Enabled && c.IsMainCamera )
			?? Scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c != null && c.Enabled );

		if ( mainCam != null )
		{
			playerCamera = mainCam.GameObject;
			playerHead = mainCam.GameObject;
			return true;
		}

		return false;
	}

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

		// Use the shared GunViewModel instance from WeaponManager
		viewModel = ResolveViewModel();

		// Load muzzle flash prefab if not set in inspector
		if ( MuzzleFlashParticle == null )
			MuzzleFlashParticle = ResourceLibrary.Get<PrefabFile>( "prefabs/particles/muzzle/muzzleflash.prefab" );
	}

	protected override void OnUpdate()
	{
		if (fireRateRemaining > 0f) fireRateRemaining -= Time.Delta;
		if (emptySoundCooldown > 0f) emptySoundCooldown -= Time.Delta;
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
		if ( Networking.IsActive && !IsLocallyOwnedGun() )
			return;

		viewModel = ResolveViewModel();

		EnsureFireOrigin();

		if ( isReloading )
		{
			if ( currentAmmo > 0 ) CancelReload(); // only cancel if we have bullets to fire after
			return;
		}
		if ( currentAmmo <= 0 )
		{
			// Already empty — just play a dry-fire click. Reload must be triggered manually (R)
			// or fires automatically when the last bullet is spent (see bottom of this method).
			if ( EmptySound is not null && emptySoundCooldown <= 0f )
			{
				Sound.Play( EmptySound );
				emptySoundCooldown = 0.3f;
			}
			return;
		}
		if (fireRateRemaining > 0f) return;

		currentAmmo--;
		fireRateRemaining = FireRate;
		PlayerStats.ShotsFired++;
	if ( ownerIdentity != null ) ownerIdentity.ShotsFired++;
		PlayerStats.PendingRecoil += RecoilAmount;
		if ( ownerIdentity != null ) ownerIdentity.PendingRecoil += RecoilAmount;

		viewModel?.PlayFireAnim();
		OnFire();
		SpawnMuzzleFlash();

		if ( Networking.IsActive )
		{
			var source = playerCamera ?? playerHead ?? owner ?? GameObject;
			Log.Info( $"[Gun] Broadcasting muzzle flash RPC from {source.Name}" );

			var weaponManager = owner?.Components.GetInDescendantsOrSelf<WeaponManager>()
				?? Components.GetInAncestorsOrSelf<WeaponManager>();
			weaponManager?.BroadcastRemoteMuzzleFlashFromWeaponManager( source.WorldPosition, source.WorldRotation );
		}

		// Auto-reload when clip runs dry
		if ( currentAmmo == 0 && (UnlimitedAmmo || AmmoReserve > 0) )
			Reload();

	}

	private void SpawnMuzzleFlash()
	{
		// Particle-based flash — positioned at the "muzzle" attachment on the viewmodel skeleton
		if ( MuzzleFlashParticle is not null )
		{
			activeFlashParticle?.Destroy();

			var vm = ResolveViewModel()?.ModelRenderer;
			var attach = vm?.GetAttachment( "muzzle" );
			var fallbackPos = playerCamera != null
				? playerCamera.WorldPosition + playerCamera.WorldRotation.Forward * 24f
				: Vector3.Zero;
			var pos = attach?.Position ?? fallbackPos;
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

	private void SpawnRemoteMuzzleFlash( Vector3 position, Rotation rotation )
	{
		if ( MuzzleFlashParticle is not null )
		{
			activeFlashParticle?.Destroy();
			var prefabScene = SceneUtility.GetPrefabScene( MuzzleFlashParticle );
			if ( prefabScene != null )
			{
				activeFlashParticle = prefabScene.Clone( position + rotation.Forward * 24f );
				activeFlashParticle.WorldRotation = rotation;
				muzzleFlashTimer = ParticleFlashLifetime;
			}
		}

		if ( MuzzleLightBrightness > 0f )
		{
			if ( muzzlePointLight is null || !muzzlePointLight.IsValid() )
			{
				var lightGO = new GameObject( false, "MuzzlePointLight" );
				lightGO.Parent = GameObject;
				muzzlePointLight = lightGO.Components.Create<PointLight>();
			}

			muzzlePointLight.GameObject.WorldPosition = position + rotation.Forward * 24f;
			muzzlePointLight.GameObject.WorldRotation = rotation;
			muzzlePointLight.LightColor = MuzzleLightColor * MuzzleLightBrightness;
			muzzlePointLight.Radius = MuzzleLightRadius;
			muzzlePointLight.GameObject.Enabled = true;
			muzzleLightTimer = MuzzleLightDuration;
		}
	}

	public void SetViewModel( GunViewModel vm )
	{
		viewModel = vm;
	}

	private GunViewModel ResolveViewModel()
	{
		if ( viewModel != null && viewModel.IsValid && viewModel.HasLocalVisuals )
			return viewModel;

		var current = GunViewModel.Current;
		if ( current != null && current.IsValid && current.HasLocalVisuals )
			return current;

		return null;
	}

	/// <summary>
	/// Call this from OnFire() after a successful trace hit on world geometry (not enemies).
	/// Spawns an impact particle and projects a bullet hole decal onto the surface.
	/// </summary>
	protected void SpawnImpactEffects( SceneTraceResult hit )
	{
		if ( !hit.Hit ) return;

		var pos    = hit.EndPosition + hit.Normal * 0.5f;
		var rot    = Rotation.LookAt( -hit.Normal, Vector3.Up );

		// Impact particle
		if ( ImpactParticlePrefab != null )
		{
			var prefabScene = SceneUtility.GetPrefabScene( ImpactParticlePrefab );
			if ( prefabScene != null )
			{
				var fx = prefabScene.Clone( pos );
				fx.WorldRotation = rot;
			}
		}

		// Bullet hole decal
		if ( ImpactDecalMaterial != null )
		{
			var decalGO = new GameObject( true, "BulletHole" );
			decalGO.WorldPosition = pos;
			decalGO.WorldRotation = rot;
			var decal = decalGO.Components.Create<DecalRenderer>();
			decal.Material = ImpactDecalMaterial;
			decal.Size = new Vector3( ImpactDecalSize, ImpactDecalSize, ImpactDecalSize * 4f );

			// Auto-destroy after a while so we don't pile up thousands of decals
			_ = DestroyAfter( decalGO, 30f );
		}
	}

	private async System.Threading.Tasks.Task DestroyAfter( GameObject go, float seconds )
	{
		await Task.DelaySeconds( seconds );
		if ( go.IsValid() ) go.Destroy();
	}

	protected abstract void OnFire();

	public virtual void Reload()
	{
		viewModel = ResolveViewModel();

		if (isReloading) return;
		if (currentAmmo == AmmoClip) return;
		if (!UnlimitedAmmo && AmmoReserve <= 0) return;

		isReloading = true;
		reloadTimeRemaining = ReloadTime;

		if ( currentAmmo == 0 )
		{
			viewModel?.PlayReloadEmptyAnim(); // sets b_empty flag
			viewModel?.PlayReloadAnim();       // triggers the actual reload state
		}
		else
			viewModel?.PlayReloadAnim();
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
		if ( head == null || !head.IsValid ) return;

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
		ownerIdentity = owner?.Components.GetInDescendantsOrSelf<PlayerIdentity>();
	}

	protected bool ApplyEnemyHit( HealthComponent health, float damage, bool isHeadshot = false, bool applyShotgunKnockback = false )
	{
		if ( health == null )
			return false;

		if ( Networking.IsActive && Connection.Local?.IsHost != true && !health.IsPlayer )
		{
			var weaponManager = owner?.Components.GetInDescendantsOrSelf<WeaponManager>()
				?? Components.GetInAncestorsOrSelf<WeaponManager>();

			weaponManager?.RequestEnemyHitOnHost( health.GameObject, damage, isHeadshot, applyShotgunKnockback );
			return true;
		}

		health.TakeDamage( damage, owner );
		return true;
	}

	/// <summary>Increments ShotsHit on both PlayerStats and the owning PlayerIdentity.</summary>
	protected void TrackShotHit()
	{
		PlayerStats.ShotsHit++;
		if ( ownerIdentity != null ) ownerIdentity.ShotsHit++;
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
