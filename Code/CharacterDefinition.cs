using System.Collections.Generic;

/// <summary>
/// Cosmetic character options. All stats are identical — purely visual.
/// ClothingPaths: .vmdl model paths for citizen clothing items.
/// These are applied as bone-merged SkinnedModelRenderers on top of the base citizen model.
/// </summary>
public sealed class CharacterDefinition
{
	public string   Name          { get; init; }
	public string   Description   { get; init; }
	public string   ModelPath     { get; init; }
	public string[] ClothingPaths { get; init; } = System.Array.Empty<string>();

	/// <summary>Pipe-joined clothing paths for network sync.</summary>
	public string ClothingConfig => string.Join( "|", ClothingPaths );

	public static readonly System.Collections.Generic.List<CharacterDefinition> All = new System.Collections.Generic.List<CharacterDefinition>
	{
		new CharacterDefinition
		{
			Name        = "Tourist",
			Description = "Just on vacation. Completely unprepared.",
			ModelPath   = "models/citizen_human/citizen_human_male.vmdl",
			ClothingPaths = new string[]
			{
				"models/citizen_clothes/shirt/Hawaiian_Shirt/Model/hawaiian_shirt_m_human.vmdl",
				"models/citizen_clothes/shorts/summer_shorts/Model/summer_shorts_m_human.vmdl",
				"models/citizen_clothes/shoes/Sneakers/Models/sneakers_m_human.vmdl",
				"models/citizen_clothes/hat/Baseball_Cap/Model/baseball_cap_blue_white_m_human.vmdl",
			}
		},
		new CharacterDefinition
		{
			Name        = "Port Worker",
			Description = "Knows the layout. Practical and tough.",
			ModelPath   = "models/citizen_human/citizen_human_female.vmdl",
			ClothingPaths = new string[]
			{
				"models/citizen_clothes/shirt/Flannel_Shirt/Models/flannel_shirt_f_human.vmdl",
				"models/citizen_clothes/trousers/CargoPants/Models/cargo_pants.vmdl",
				"models/citizen_clothes/shoes/Boots/Models/black_boots.vmdl",
				"models/citizen_clothes/hat/Hard_Hat/Models/hard_hat.vmdl",
			}
		},
		new CharacterDefinition
		{
			Name        = "Cruise Employee",
			Description = "Knows the ship inside out.",
			ModelPath   = "models/citizen_human/citizen_human_male.vmdl",
			ClothingPaths = new string[]
			{
				"models/citizen_clothes/shirt/Polo_Shirt/Models/polo_shirt_white_m_human.vmdl",
				"models/citizen_clothes/trousers/SmartTrousers/smarttrousers_m_human.vmdl",
				"models/citizen_clothes/shoes/FlatShoes/Models/flat_shoes_m_human.vmdl",
			}
		},
		new CharacterDefinition
		{
			Name        = "Stowaway",
			Description = "Mysterious background. Asks no questions.",
			ModelPath   = "models/citizen_human/citizen_human_male.vmdl",
			ClothingPaths = new string[]
			{
				"models/citizen_clothes/jacket/Hoodie/Models/hoodie_black_m_human.vmdl",
				"models/citizen_clothes/trousers/Jeans/Models/jeans_black_m_human.vmdl",
				"models/citizen_clothes/shoes/Trainers/Models/trainers_m_human.vmdl",
				"models/citizen_clothes/hat/Baseball_Cap/Model/baseball_cap_black_m_human.vmdl",
			}
		},
	};

	public static CharacterDefinition Get( int index )
	{
		if ( All.Count == 0 ) return null;
		return All[System.Math.Clamp( index, 0, All.Count - 1 )];
	}

	public int Index => All.IndexOf( this );
}
