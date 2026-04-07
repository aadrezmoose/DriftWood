using Sandbox;

/// <summary>
/// Place on a GameObject in the world (safe room shelf, ground, etc.) to make a
/// weapon that the player can pick up with E.
///
/// Setup in inspector:
///   - Category          — Primary (slot 0) or Secondary (slot 1)
///   - Weapon Prefab     — the GameObject prefab that has the Gun/MeleeWeapon component
///   - World Model       — the model shown lying on the ground
///   - ViewModel Model / AnimGraph / Hands Model / Position Offset — passed to GunViewModel on equip
/// </summary>
public sealed class WeaponPickup : Component
{
	protected void OnNetworkSpawn()
	{
		// Force host values to sync to clients after network spawn
		ViewModelPositionOffset = ViewModelPositionOffset;
	}
	[Property] public WeaponCategory Category { get; set; } = WeaponCategory.Primary;
	[Property] public string WeaponDisplayName { get; set; } = "Weapon";
	[Property] public PrefabFile WeaponPrefab { get; set; }
	[Property] public float PickupRadius { get; set; } = 150f;

	// Viewmodel info forwarded to GunViewModel.UpdateWeapon when equipped
	[Property] public Model ViewModelModel { get; set; }
	[Property] public AnimationGraph ViewModelAnimGraph { get; set; }
	[Property] public Model ViewModelHandsModel { get; set; }
	[Property, Sync] public Vector3 ViewModelPositionOffset { get; set; }
	[Property] public string AnimFireParam { get; set; }
	[Property] public string AnimReloadParam { get; set; }
	[Property] public string AnimReloadEmptyParam { get; set; }

	// World representation
	[Property] public Model WorldModel { get; set; }

	private ModelRenderer worldRenderer;
	private GameObject player;

	protected override void OnAwake()
	{
		       // Set default offset based on weapon category if not set (or left at zero)
		       if ( ViewModelPositionOffset == default )
		       {
			       switch ( Category )
			       {
				       case WeaponCategory.Primary: // SMG, Rifle, Shotgun
					       ViewModelPositionOffset = new Vector3( 10, -5, -8 );
					       break;
				       case WeaponCategory.Secondary: // Pistol, Melee
					       ViewModelPositionOffset = new Vector3( 13, -5, -6 );
					       break;
				       default:
					       ViewModelPositionOffset = new Vector3( 10, -5, -8 );
					       break;
			       }
		       }

		       // Always enforce inspector value for offset at runtime (host only)
			   Log.Info($"[WeaponPickup] OnAwake: IsProxy={IsProxy} Inspector Offset={ViewModelPositionOffset}");
		       if ( !IsProxy )
		       {
				   Log.Info($"[WeaponPickup] Host pushing inspector offset to network: {ViewModelPositionOffset}");
			       ViewModelPositionOffset = ViewModelPositionOffset;
		       }
	Log.Info($"[WeaponPickup] OnNetworkSpawn: IsProxy={IsProxy} Synced Offset={ViewModelPositionOffset}");

	       if ( WorldModel != null )
	       {
		       worldRenderer = Components.GetOrCreate<ModelRenderer>();
		       worldRenderer.Model = WorldModel;
	       }

		// Auto-create a collider so raycasts can detect this pickup
		if ( Components.Get<Collider>() == null )
		{
			if ( WorldModel != null )
			{
				var col = Components.GetOrCreate<ModelCollider>();
				col.Model = WorldModel;
			}
			else
			{
				var box = Components.GetOrCreate<BoxCollider>();
				box.Scale = new Vector3( 20, 20, 20 );
			}
		}

	       // Always re-enable the pickup for each client (per-player pickup)
	       GameObject.Enabled = true;
	}

	protected override void OnStart()
	{
		player = Scene.GetAllComponents<PlayerMovement>().FirstOrDefault()?.GameObject;
	}

	protected override void OnUpdate()
	{
			       // No spinning world model
	}

		       /// <summary>Called by WeaponManager when the player presses E nearby.</summary>
		       public void Pickup( GameObject playerGO )
		       {
				       // No longer hide the world model for any player. World model always remains visible.
			   }
		}
