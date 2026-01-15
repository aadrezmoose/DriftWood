public static class MyEditorMenu
{
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
}
