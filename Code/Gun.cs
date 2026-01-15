using Sandbox;

public abstract class Gun : Component
{
	[Property] public float Damage { get; set; } = 10f;
	[Property] public float FireRate { get; set; } = 0.1f; // seconds between shots
	[Property] public int AmmoClip { get; set; } = 30;
	[Property] public int AmmoReserve { get; set; } = 90;
	[Property] public float ReloadTime { get; set; } = 2.0f;

	protected int currentAmmo;
	protected float fireRateRemaining = 0f;
	protected float reloadTimeRemaining = 0f;
	protected bool isReloading = false;
	protected GameObject playerHead; // Reference to player's head for firing
	protected GunViewModel viewModel; // Reference to the gun's visual model
	protected GameObject owner; // Root owner (player), used to ignore self in traces

	protected override void OnAwake()
	{
		currentAmmo = AmmoClip;

		// Try to find or create viewmodel
		viewModel = Components.Get<GunViewModel>();
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
	}

	public virtual void Fire()
	{
		if (isReloading) 
		{
			Log.Warning("Can't fire while reloading!");
			return;
		}
		if (currentAmmo <= 0)
		{
			Log.Warning("Out of ammo!");
			return;
		}
		if (fireRateRemaining > 0f) 
		{
			Log.Info($"Fire rate cooldown: {fireRateRemaining:F2}s");
			return;
		}

		currentAmmo--;
		fireRateRemaining = FireRate;

		Log.Info($"Gun fired! Ammo: {currentAmmo}/{AmmoClip}");
		OnFire();
	}

	protected abstract void OnFire();

	public virtual void Reload()
	{
		if (isReloading) return;
		if (currentAmmo == AmmoClip) return; // Already full
		if (AmmoReserve <= 0) return; // No reserve ammo

		isReloading = true;
		reloadTimeRemaining = ReloadTime;
	}

	private void FinishReload()
	{
		int ammoNeeded = AmmoClip - currentAmmo;
		int ammoToAdd = System.Math.Min(ammoNeeded, AmmoReserve);

		currentAmmo += ammoToAdd;
		AmmoReserve -= ammoToAdd;
	}

	public void SetPlayerHead(GameObject head)
	{
		playerHead = head;
	}

	public void SetOwner(GameObject ownerGameObject)
	{
		owner = ownerGameObject;
	}

	public int GetCurrentAmmo() => currentAmmo;
	public int GetReserveAmmo() => AmmoReserve;
	public bool IsReloading() => isReloading;
	public string GetAmmoText() => $"{currentAmmo}/{AmmoReserve}";
}
