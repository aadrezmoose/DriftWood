using Sandbox;

/// <summary>
/// Persisted player preferences. Saved to FileSystem.Data as JSON.
/// </summary>
public static class GameSettings
{
	public const float SensMin = 0.1f;
	public const float SensMax = 5.0f;
	public const float FOVMin  = 60f;
	public const float FOVMax  = 120f;

	private const string SavePath = "driftwood_settings.json";

	private static float _sensitivity       = 1.0f;
	private static float _fov               = 90f;
	private static int   _characterIndex    = 0;

	public static float Sensitivity
	{
		get => _sensitivity;
		set { _sensitivity = System.Math.Clamp( value, SensMin, SensMax ); Save(); }
	}

	public static float FOV
	{
		get => _fov;
		set { _fov = System.Math.Clamp( value, FOVMin, FOVMax ); Save(); }
	}

	public static int SelectedCharacterIndex
	{
		get => _characterIndex;
		set { _characterIndex = System.Math.Clamp( value, 0, CharacterDefinition.All.Count - 1 ); Save(); }
	}

	public static CharacterDefinition SelectedCharacter => CharacterDefinition.Get( _characterIndex );

	public static void Load()
	{
		var data = FileSystem.Data.ReadJson<SettingsData>( SavePath, new SettingsData() );
		_sensitivity    = System.Math.Clamp( data.Sensitivity, SensMin, SensMax );
		_fov            = System.Math.Clamp( data.FOV,         FOVMin,  FOVMax  );
		_characterIndex = System.Math.Clamp( data.CharacterIndex, 0, CharacterDefinition.All.Count - 1 );
	}

	private static void Save()
	{
		FileSystem.Data.WriteJson( SavePath, new SettingsData
		{
			Sensitivity    = _sensitivity,
			FOV            = _fov,
			CharacterIndex = _characterIndex
		} );
	}

	private class SettingsData
	{
		public float Sensitivity    { get; set; } = 1.0f;
		public float FOV            { get; set; } = 90f;
		public int   CharacterIndex { get; set; } = 0;
	}
}
