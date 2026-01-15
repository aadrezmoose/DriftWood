public sealed class WeaponManager : Component
{
	[Property] public Gun CurrentGun { get; set; }

	protected override void OnAwake()
	{
		if (CurrentGun == null)
		{
			Log.Warning("WeaponManager: No gun assigned!");
			return;
		}

		// Get player's head and pass it to the gun
		var playerMovement = Components.Get<PlayerMovement>();
		if (playerMovement != null && playerMovement.Head != null)
		{
			CurrentGun.SetPlayerHead(playerMovement.Head);
			CurrentGun.SetOwner(GameObject);
			Log.Info("WeaponManager: Set player head on gun");
		}
		else
		{
			Log.Warning("WeaponManager: Could not find player head!");
		}
	}

	protected override void OnUpdate()
	{
		if (CurrentGun == null)
		{
			Log.Warning("WeaponManager: CurrentGun is null!");
			return;
		}

		// Fire on Attack1 input
		if (Input.Pressed("Attack1"))
		{
			Log.Info("WeaponManager: Firing!");
			CurrentGun.Fire();
		}

		// Reload on Reload input
		if (Input.Pressed("Reload"))
		{
			Log.Info("WeaponManager: Reloading!");
			CurrentGun.Reload();
		}
	}

	public void SwitchGun(Gun newGun)
	{
		CurrentGun = newGun;
		if (CurrentGun != null)
		{
			var playerMovement = Components.Get<PlayerMovement>();
			if (playerMovement != null && playerMovement.Head != null)
			{
				CurrentGun.SetPlayerHead(playerMovement.Head);
				CurrentGun.SetOwner(GameObject);
			}
		}
	}

	public Gun GetCurrentGun() => CurrentGun;
}
