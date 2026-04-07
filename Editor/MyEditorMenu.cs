public static class MyEditorMenu
{
	/// <summary>
	/// Scans the active scene and rewires all commonly-lost inspector references.
	/// Run this from Editor → My Project → Rewire Scene Components after a hot-reload wipes properties.
	/// </summary>
	[Menu( "Editor", "My Project/Rewire Scene Components" )]
	public static void RewireComponents()
	{
		var scene = Game.ActiveScene;
		if ( scene == null )
		{
			Log.Warning( "[Rewire] No active scene." );
			return;
		}

		int fixed_count = 0;

		// ── Helpers ──────────────────────────────────────────────────────
		T Find<T>() where T : Component => scene.GetAllComponents<T>().FirstOrDefault();

		void Set<TComp, TVal>( TComp comp, string propName, TVal value, string label )
			where TComp : Component
		{
			if ( comp == null || value == null ) return;
			var prop = typeof(TComp).GetProperty( propName );
			if ( prop == null ) return;
			var current = prop.GetValue( comp );
			if ( current != null ) return; // already set — don't overwrite intentional assignments
			prop.SetValue( comp, value );
			Log.Info( $"[Rewire] {typeof(TComp).Name}.{propName} → {label}" );
			fixed_count++;
		}

		// ── Locate key scene objects ──────────────────────────────────────
		var playerMovement  = Find<PlayerMovement>();
		var playerGO        = playerMovement?.GameObject;
		var enemySpawner    = Find<EnemySpawner>();
		var aiDirector      = Find<AIDirector>();
		var tankSpawner     = Find<TankSpawner>();
		var hunterSpawner   = Find<HunterSpawner>();
		var spitterSpawner  = Find<SpitterSpawner>();
		var chargerSpawner  = Find<ChargerSpawner>();
		var cameraMovement  = Find<CameraMovement>();

		// Head / Body are child GameObjects — find by name
		GameObject headGO = null, bodyGO = null;
		if ( playerGO != null )
		{
			foreach ( var child in playerGO.Children )
			{
				var name = child.Name.ToLower();
				if ( headGO == null && name.Contains( "head" ) ) headGO = child;
				if ( bodyGO == null && name.Contains( "body" ) ) bodyGO = child;
			}
		}

		// ── AIDirector ────────────────────────────────────────────────────
		if ( aiDirector != null )
		{
			Set( aiDirector, "EnemySpawner",   enemySpawner,   "EnemySpawner"   );
			Set( aiDirector, "TankSpawner",    tankSpawner,    "TankSpawner"    );
			Set( aiDirector, "HunterSpawner",  hunterSpawner,  "HunterSpawner"  );
			Set( aiDirector, "SpitterSpawner", spitterSpawner, "SpitterSpawner" );
			Set( aiDirector, "ChargerSpawner", chargerSpawner, "ChargerSpawner" );
			Set( aiDirector, "Player",         playerGO,       "Player"         );
		}

		// ── EnemySpawner ──────────────────────────────────────────────────
		if ( enemySpawner != null )
		{
			Set( enemySpawner, "PlayerReference", playerGO,    "Player"     );
			Set( enemySpawner, "Director",        aiDirector,  "AIDirector" );
		}

		// ── CameraMovement ────────────────────────────────────────────────
		if ( cameraMovement != null )
		{
			Set( cameraMovement, "Player", playerMovement, "PlayerMovement" );
			Set( cameraMovement, "Head",   headGO,         "Head"           );
			Set( cameraMovement, "Body",   bodyGO,         "Body"           );
		}

		// ── DirectorDebugHUD ──────────────────────────────────────────────
		var debugHUD = Find<DirectorDebugHUD>();
		if ( debugHUD != null )
			Set( debugHUD, "Director", aiDirector, "AIDirector" );

		// ── PlayerMovement ────────────────────────────────────────────────
		if ( playerMovement != null )
		{
			Set( playerMovement, "Head", headGO, "Head" );
			Set( playerMovement, "Body", bodyGO, "Body" );
		}

		// ── Done ──────────────────────────────────────────────────────────
		if ( fixed_count == 0 )
			Log.Info( "[Rewire] All references already assigned — nothing to fix." );
		else
			Log.Info( $"[Rewire] Done. Fixed {fixed_count} missing reference(s). Save the scene to persist." );
	}

	[Menu("Editor", "My Project/My Menu Option")]
	public static void OpenMyMenu()
	{
		EditorUtility.DisplayDialog("It worked!", "This is being called from your library's editor code!");
	}

	[Menu("Editor", "My Project/Test Damage/Deal 10 Damage")]
	public static void TestDamage10()
	{
		var player = Game.ActiveScene.GetAllComponents<HealthComponent>().FirstOrDefault();
		if ( player != null )
		{
			player.TakeDamage( 10f );
			Log.Info( $"Applied 10 damage. Health: {player.CurrentHealth}/{player.MaxHealth}" );
		}
		else
		{
			Log.Warning( "No HealthComponent found in active scene." );
		}
	}

	[Menu("Editor", "My Project/Test Damage/Deal 25 Damage")]
	public static void TestDamage25()
	{
		var player = Game.ActiveScene.GetAllComponents<HealthComponent>().FirstOrDefault();
		if ( player != null )
		{
			player.TakeDamage( 25f );
			Log.Info( $"Applied 25 damage. Health: {player.CurrentHealth}/{player.MaxHealth}" );
		}
		else
		{
			Log.Warning( "No HealthComponent found in active scene." );
		}
	}

	[Menu("Editor", "My Project/Test Damage/Deal 50 Damage")]
	public static void TestDamage50()
	{
		var player = Game.ActiveScene.GetAllComponents<HealthComponent>().FirstOrDefault();
		if ( player != null )
		{
			player.TakeDamage( 50f );
			Log.Info( $"Applied 50 damage. Health: {player.CurrentHealth}/{player.MaxHealth}" );
		}
		else
		{
			Log.Warning( "No HealthComponent found in active scene." );
		}
	}

	[Menu("Editor", "My Project/Test Damage/Kill Player")]
	public static void TestKill()
	{
		var player = Game.ActiveScene.GetAllComponents<HealthComponent>().FirstOrDefault();
		if ( player != null )
		{
			player.Die();
			Log.Info( "Player killed." );
		}
		else
		{
			Log.Warning( "No HealthComponent found in active scene." );
		}
	}

	[Menu("Editor", "My Project/Test Damage/Heal 20")]
	public static void TestHeal20()
	{
		var player = Game.ActiveScene.GetAllComponents<HealthComponent>().FirstOrDefault();
		if ( player != null )
		{
			player.Heal( 20f );
			Log.Info( $"Healed 20. Health: {player.CurrentHealth}/{player.MaxHealth}" );
		}
		else
		{
			Log.Warning( "No HealthComponent found in active scene." );
		}
	}

	[Menu("Editor", "My Project/Test Damage/Heal 50")]
	public static void TestHeal50()
	{
		var player = Game.ActiveScene.GetAllComponents<HealthComponent>().FirstOrDefault();
		if ( player != null )
		{
			player.Heal( 50f );
			Log.Info( $"Healed 50. Health: {player.CurrentHealth}/{player.MaxHealth}" );
		}
		else
		{
			Log.Warning( "No HealthComponent found in active scene." );
		}
	}

	[Menu("Editor", "My Project/Test Damage/Revive Player")]
	public static void TestRevive()
	{
		var player = Game.ActiveScene.GetAllComponents<HealthComponent>().FirstOrDefault();
		if ( player != null )
		{
			player.Revive();
			Log.Info( $"Player revived. Health: {player.CurrentHealth}/{player.MaxHealth}" );
		}
		else
		{
			Log.Warning( "No HealthComponent found in active scene." );
		}
	}

	[Menu("Editor", "My Project/Test Enemy/Spawn Test Enemy")]
	public static void SpawnTestEnemy()
	{
		var player = Game.ActiveScene.GetAllComponents<PlayerMovement>().FirstOrDefault();
		if ( player == null )
		{
			Log.Warning( "No player found in scene. Can't spawn enemy." );
			return;
		}

		// Create enemy GameObject with randomized spawn position
		var enemyObj = new GameObject();
		enemyObj.Name = "TestEnemy";
		var spawnOffset = player.GameObject.Transform.Rotation.Forward * 300f;
		// Add some random left/right spread so multiple spawns don't stack
		spawnOffset += player.GameObject.Transform.Rotation.Right * (Game.Random.Float(-150f, 150f));
		enemyObj.Transform.Position = player.GameObject.Transform.Position + spawnOffset + Vector3.Up * 50f;

		// Add ModelRenderer with a basic model (cube for now)
		var renderer = enemyObj.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/infoterryfinal/terrystart.vmdl_c" );
		renderer.Tint = Color.Red;

		// Add collider
		var collider = enemyObj.AddComponent<BoxCollider>();
		collider.Scale = new Vector3( 50, 50, 100 );

		// Add CharacterController for movement
		var controller = enemyObj.AddComponent<CharacterController>();
		controller.Radius = 25f;
		controller.Height = 100f;

		// Add HealthComponent
		var health = enemyObj.AddComponent<HealthComponent>();
		health.MaxHealth = 100f;
		health.CurrentHealth = 100f;
		health.IsPlayer = false;

		// Add Enemy AI component
		var enemy = enemyObj.AddComponent<Enemy>();
		enemy.DetectionRange = 600f;
		enemy.AttackDamage = 10f;
		enemy.PatrolSpeed = 100f;
		enemy.ChaseSpeed = 200f;
		enemy.StaggerDuration = 0.35f;
		enemy.AttackCooldown = 1.5f;

		// Create head child GameObject for headshot zone
		var headObj = new GameObject();
		headObj.Name = "EnemyHead";
		headObj.Parent = enemyObj;
		headObj.Transform.LocalPosition = Vector3.Up * 50f; // Position at head height
		headObj.Transform.LocalScale = Vector3.One * 0.6f;

		// Add collider for head
		var headCollider = headObj.AddComponent<BoxCollider>();
		headCollider.Scale = new Vector3( 30, 30, 35 );
		headCollider.Tags.Add( "nosight" ); // Exclude from LOS traces

		// Add HeadshotZone component
		var headshot = headObj.AddComponent<HeadshotZone>();
		headshot.DamageMultiplier = 2.0f;

		Log.Info( $"Spawned test enemy with headshot zone at {enemyObj.Transform.Position}" );
	}

	[ConCmd("test_enemy_damage")]
	public static void TestEnemyDamage(float damage = 10f)
	{
		var enemy = Game.ActiveScene.GetAllComponents<HealthComponent>().FirstOrDefault();
		enemy?.TakeDamage(damage);
		Log.Info($"Enemy health: {enemy?.CurrentHealth}/{enemy?.MaxHealth}");
	}
}
