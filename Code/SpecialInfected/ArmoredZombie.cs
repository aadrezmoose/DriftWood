using Sandbox;

/// <summary>
/// ArmoredZombie — add this component alongside HealthComponent on an enemy
/// to make it resistant to body shots. Headshots bypass the armor entirely.
///
/// Armor works by listening to OnDamageTakenWithAttacker and immediately healing
/// back a portion of the damage when the attacker is NOT a HeadshotZone.
/// </summary>
public sealed class ArmoredZombie : Component
{
	/// <summary>
	/// Fraction of body-shot damage negated by armor (0 = no armor, 1 = invulnerable to body shots).
	/// Default 0.6 = 60% reduction on body shots.
	/// </summary>
	[Property] public float ArmorReduction { get; set; } = 0.6f;

	private HealthComponent health;

	protected override void OnAwake()
	{
		health = Components.Get<HealthComponent>();

		if ( health is null )
		{
			// Try searching descendants / ancestors in case the component lives on a different node
			health = Components.GetInDescendantsOrSelf<HealthComponent>()
			      ?? Components.GetInAncestorsOrSelf<HealthComponent>();
		}

		if ( health is null )
		{
			Log.Warning( "ArmoredZombie: No HealthComponent found on this GameObject or nearby — armor won't function" );
			return;
		}

		health.OnDamageTakenWithAttacker += OnDamageTaken;
		Log.Info( $"ArmoredZombie: Active with {ArmorReduction * 100f:F0}% body-shot reduction" );
	}

	void OnDamageTaken( float damageAmount, GameObject attacker )
	{
		if ( attacker is null || health is null || health.IsDead ) return;

		// Check whether the damage came from a HeadshotZone ancestry
		bool isHeadshot = IsHeadshotAttacker( attacker );
		if ( isHeadshot )
		{
			// Full damage — armour doesn't protect the head
			return;
		}

		// Body shot: heal back the armor-absorbed portion to create net reduction
		float absorbed = damageAmount * ArmorReduction;
		if ( absorbed > 0f )
		{
			health.Heal( absorbed );
			Log.Info( $"ArmoredZombie: Absorbed {absorbed:F1} damage ({ArmorReduction * 100f:F0}% body-shot reduction)" );
		}
	}

	/// <summary>
	/// Returns true if the attacker GameObject has a HeadshotZone in its
	/// component hierarchy (self or ancestors), indicating the shot was registered
	/// by the headshot collision zone.
	/// </summary>
	bool IsHeadshotAttacker( GameObject attacker )
	{
		if ( attacker is null ) return false;

		// Check the attacker's own components
		var hzSelf = attacker.Components.Get<HeadshotZone>();
		if ( hzSelf is not null ) return true;

		// Walk up the ancestor chain
		var hzAncestor = attacker.Components.GetInAncestorsOrSelf<HeadshotZone>();
		return hzAncestor is not null;
	}

	protected override void OnDestroy()
	{
		if ( health is not null )
			health.OnDamageTakenWithAttacker -= OnDamageTaken;
	}
}
