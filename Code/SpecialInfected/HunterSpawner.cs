using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Hunter Spawner - Spawns Hunter special infected at specific intervals
/// Designed for AI Director integration
/// </summary>
public sealed class HunterSpawner : Component
{
	[Property] public float SpawnInterval { get; set; } = 15f; // Seconds between spawns
	[Property] public int MaxHunters { get; set; } = 2; // Max hunters alive at once
	[Property] public float SpawnRadius { get; set; } = 500f; // Distance from player to spawn hunters
	[Property] public GameObject HunterPrefab { get; set; } // Prefab to spawn
	[Property] public GameObject PlayerReference { get; set; } // Optional: manually assign the player
	[Property] public bool Enabled { get; set; } = true; // Toggle spawner on/off

	public bool IsSafeZoneActive { get; set; } = false; // Set by SafeRoom events

	private float spawnTimer = 0f;
	private List<GameObject> spawnedHunters = new List<GameObject>();
	private GameObject player;

	protected override void OnAwake()
	{
		Log.Info("HunterSpawner: OnAwake called");

		// Subscribe to SafeRoom events
		SafeRoom.OnPlayerEntered += OnPlayerEnteredSafeRoom;
		SafeRoom.OnPlayerExited += OnPlayerExitedSafeRoom;

		// Use manual reference if provided
		if (PlayerReference is not null)
		{
			player = PlayerReference;
			Log.Info("HunterSpawner: Using manually assigned player reference");
		}
		else
		{
			// Try to find the player by looking for a PlayerMovement component
			var playerMovement = Scene.Components.Get<PlayerMovement>();
			if (playerMovement is not null)
			{
				player = playerMovement.GameObject;
				Log.Info("HunterSpawner: Player found via Scene.Components");
			}
			else
			{
				// Fallback: search all GameObjects for PlayerMovement
				foreach (var go in Scene.GetAllComponents<PlayerMovement>())
				{
					player = go.GameObject;
					Log.Info("HunterSpawner: Player found via GetAllComponents fallback");
					break;
				}
			}
		}

		if (player is null)
		{
			Log.Warning("HunterSpawner: Could not find player! Try manually assigning PlayerReference property.");
		}
	}

	protected override void OnUpdate()
	{
		if (!Enabled)
		{
			return;
		}

		if (player is null)
		{
			return;
		}

		if (HunterPrefab is null)
		{
			return;
		}

		// Cleanup dead hunters
		var deadHunters = 0;
		for (int i = spawnedHunters.Count - 1; i >= 0; i--)
		{
			try
			{
				var hunter = spawnedHunters[i];
				bool isDead = false;

				if (hunter is null)
				{
					isDead = true;
				}
				else if (!hunter.Active)
				{
					isDead = true;
				}
				else
				{
					var hunterComponent = hunter.Components.Get<Hunter>();
					if (hunterComponent is null || !hunterComponent.Enabled)
					{
						isDead = true;
					}
				}

				if (isDead)
				{
					spawnedHunters.RemoveAt(i);
					deadHunters++;
				}
			}
			catch (System.Exception ex)
			{
				Log.Warning($"HunterSpawner: Exception during cleanup: {ex.Message}");
				spawnedHunters.RemoveAt(i);
				deadHunters++;
			}
		}

		if (deadHunters > 0)
		{
			Log.Info($"HunterSpawner: Cleaned up {deadHunters} dead hunters. Alive: {spawnedHunters.Count}/{MaxHunters}");
		}

		// Spawn hunters
		spawnTimer += Time.Delta;
		if (spawnTimer >= SpawnInterval)
		{
			spawnTimer = 0f;

			if (spawnedHunters.Count >= MaxHunters)
			{
				return;
			}

			try
			{
				SpawnHunter();
			}
			catch (System.Exception ex)
			{
				Log.Error($"HunterSpawner: Exception during spawn: {ex.Message}\n{ex.StackTrace}");
			}
		}
	}

	private void SpawnHunter()
	{
		// Early return if safe zone is active
		if (IsSafeZoneActive)
		{
			Log.Info("HunterSpawner: Spawn blocked - player in safe zone");
			return;
		}

		if (HunterPrefab is null)
		{
			Log.Error("HunterSpawner: No hunter prefab assigned!");
			return;
		}

		if (player is null)
		{
			Log.Error("HunterSpawner: Player is null during spawn attempt!");
			return;
		}

		// Random spawn position around the player
		float angle = Game.Random.Float(0f, 360f);
		float distance = Game.Random.Float(SpawnRadius * 0.8f, SpawnRadius);

		// Ensure minimum distance from player
		if (distance < 300f)
		{
			distance = 300f;
		}

		try
		{
			Log.Info("HunterSpawner: Starting spawn...");

			// Start raycast from high above to find ground level
			Vector3 raycastStart = player.WorldPosition + new Vector3(
				(float)System.Math.Cos(angle * System.Math.PI / 180f) * distance,
				(float)System.Math.Sin(angle * System.Math.PI / 180f) * distance,
				500f  // Raycast from high in the air
			);

			// Raycast down to find ground level
			var trace = Scene.Trace
				.Ray(raycastStart, raycastStart + Vector3.Down * 1000f)
				.Run();

			// Reject spawn if trace doesn't hit (no ground found)
			if (!trace.Hit)
			{
				Log.Warning($"HunterSpawner: No ground found at spawn position, rejecting spawn");
				return;
			}

			Vector3 spawnPos = trace.EndPosition + Vector3.Up * 80f;  // Place above ground

			// Validate spawn position
			if (spawnPos.z < 20f)
			{
				Log.Warning($"HunterSpawner: Spawn position too low ({spawnPos.z:F1}), rejecting spawn");
				return;
			}

			// Instantiate the hunter
			var spawnedHunter = HunterPrefab.Clone(spawnPos);
			Log.Info("HunterSpawner: Hunter cloned");

			if (spawnedHunter is null)
			{
				Log.Error("HunterSpawner: Failed to clone prefab!");
				return;
			}

			// Set tint to green (different from regular enemies)
			var modelRenderer = spawnedHunter.Components.Get<ModelRenderer>();
			if (modelRenderer is not null)
			{
				modelRenderer.Tint = new Color(0.5f, 1.0f, 0.5f); // Green tint for hunters
			}

			// Ensure CharacterController exists
			var characterController = spawnedHunter.Components.Get<CharacterController>();
			if (characterController is null)
			{
				characterController = spawnedHunter.Components.Create<CharacterController>();
				characterController.Radius = 25f;
				characterController.Height = 100f;
			}
			characterController.Enabled = true;
			characterController.Velocity = Vector3.Zero;
			characterController.IsOnGround = true;

			// Setup Rigidbody for ragdoll when hunter dies
			try
			{
				var rigidbody = spawnedHunter.Components.Get<Rigidbody>();
				if (rigidbody is null)
				{
					rigidbody = spawnedHunter.Components.Create<Rigidbody>();
				}
				rigidbody.Enabled = true;
			}
			catch (System.Exception rbEx)
			{
				Log.Warning($"HunterSpawner: Could not setup rigidbody: {rbEx.Message}");
			}

			// Ensure HealthComponent exists
			var healthComponent = spawnedHunter.Components.Get<HealthComponent>();
			if (healthComponent is null)
			{
				healthComponent = spawnedHunter.Components.Create<HealthComponent>();
				healthComponent.MaxHealth = 250f;
				healthComponent.CurrentHealth = 250f;
				healthComponent.IsPlayer = false;
			}
			else
			{
				// Reset health if component exists
				healthComponent.MaxHealth = 250f;
				healthComponent.CurrentHealth = 250f;
			}

			// Now enable or create Hunter component
			var allHunters = spawnedHunter.Components.GetAll<Hunter>().ToList();
			Log.Info($"HunterSpawner: Found {allHunters.Count} Hunter components on clone");

			Hunter spawnedHunterComponent = null;

			if (allHunters.Count > 0)
			{
				spawnedHunterComponent = allHunters.First();
				spawnedHunterComponent.Enabled = true;
				Log.Info("HunterSpawner: Enabled existing Hunter component");
			}
			else
			{
				spawnedHunterComponent = spawnedHunter.Components.Create<Hunter>();
				Log.Info("HunterSpawner: Created new Hunter component");
			}

			// Set player reference
			if (spawnedHunterComponent is not null)
			{
				spawnedHunterComponent.PlayerReference = player;
				Log.Info("HunterSpawner: Set player reference on Hunter");
			}

			spawnedHunters.Add(spawnedHunter);
			Log.Info($"HunterSpawner: Spawned hunter at distance {distance:F0}. Total: {spawnedHunters.Count}/{MaxHunters}");
		}
		catch (System.Exception ex)
		{
			Log.Error($"HunterSpawner: Exception in spawn logic: {ex.Message}\n{ex.StackTrace}");
		}
	}

	public int GetAliveHunterCount()
	{
		// Validate list integrity
		spawnedHunters.RemoveAll(h => h is null || !h.Active);
		return spawnedHunters.Count;
	}

	void OnPlayerEnteredSafeRoom(SafeRoom safeRoom)
	{
		IsSafeZoneActive = true;
		Log.Info($"HunterSpawner: Safe zone activated - spawning stopped");
	}

	void OnPlayerExitedSafeRoom(SafeRoom safeRoom)
	{
		IsSafeZoneActive = false;
		Log.Info($"HunterSpawner: Safe zone deactivated - spawning resumed");
	}

	protected override void OnDestroy()
	{
		// Unsubscribe from SafeRoom events
		SafeRoom.OnPlayerEntered -= OnPlayerEnteredSafeRoom;
		SafeRoom.OnPlayerExited -= OnPlayerExitedSafeRoom;
	}
}
