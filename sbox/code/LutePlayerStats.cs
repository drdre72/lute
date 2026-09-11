/// <summary>
/// Lute player stats — HP, MP, attributes, skills, and deterministic
/// progression. Implements the PRD's polar-opposite attribute web (9 nodes,
/// 3 clusters) and skill web (9 specialization lines).
///
/// Key design from PRD §2:
/// - Zero-RNG: all progression is deterministic, driven by usage
/// - Polar-opposite: investing in one attribute pulls its opposite down
/// - Distance strain: training past 70% costs exponentially more
/// - Skill-driven: growth occurs through direct usage (crafting, combat, etc.)
/// </summary>
public sealed class LutePlayerStats : Component
{
	// ── Vitals ──

	[Property] public float MaxHealth { get; set; } = 100f;
	[Property] public float CurrentHealth { get; set; } = 100f;
	[Property] public float MaxMana { get; set; } = 50f;
	[Property] public float CurrentMana { get; set; } = 50f;
	[Property] public float MaxStamina { get; set; } = 100f;
	[Property] public float CurrentStamina { get; set; } = 100f;

	// ── Level / XP ──

	[Property] public int Level { get; set; } = 1;
	[Property] public float XP { get; set; } = 0f;
	[Property] public int AttributePoints { get; set; } = 0;

	// XP needed for next level — deterministic curve, no RNG
	// Level N requires N * 100 XP cumulative
	public float XPToNextLevel => Level * 100f;

	// ── Attribute Web (9 nodes, PRD §2.1) ──
	// Each attribute has a polar opposite. Investing in one pulls the
	// other down. Values are 0-100, baseline 10.

	[Property, Range( 0f, 100f )] public float Health { get; set; } = 10f;
	[Property, Range( 0f, 100f )] public float Coordination { get; set; } = 10f;
	[Property, Range( 0f, 100f )] public float Constitution { get; set; } = 10f;
	[Property, Range( 0f, 100f )] public float Mana { get; set; } = 10f;
	[Property, Range( 0f, 100f )] public float Stamina { get; set; } = 10f;
	[Property, Range( 0f, 100f )] public float Perception { get; set; } = 10f;
	[Property, Range( 0f, 100f )] public float Agility { get; set; } = 10f;
	[Property, Range( 0f, 100f )] public float Intelligence { get; set; } = 10f;
	[Property, Range( 0f, 100f )] public float Honor { get; set; } = 10f; // No penalty node

	// ── Skill Web (9 lines, PRD §2.2) ──
	// Same polar-opposite mechanic, layered over attributes.

	[Property, Range( 0f, 100f )] public float HeavyWeaponry { get; set; } = 0f;
	[Property, Range( 0f, 100f )] public float Conjuration { get; set; } = 0f;
	[Property, Range( 0f, 100f )] public float Protection { get; set; } = 0f;
	[Property, Range( 0f, 100f )] public float Survival { get; set; } = 0f;
	[Property, Range( 0f, 100f )] public float Tradecraft { get; set; } = 0f;
	[Property, Range( 0f, 100f )] public float Destruction { get; set; } = 0f;
	[Property, Range( 0f, 100f )] public float Athletics { get; set; } = 0f;
	[Property, Range( 0f, 100f )] public float Restoration { get; set; } = 0f;
	[Property, Range( 0f, 100f )] public float Subterfuge { get; set; } = 0f;

	// ── Soul State (PRD §1.1) ──

	public enum SoulStatus
	{
		Alive,      // Normal play
		SoulLocked, // Dead in Permanent Realm — unplayable until retrieved
		Restored,   // Retrieved via Seasonal Realm victory
	}

	[Property] public SoulStatus Soul { get; set; } = SoulStatus.Alive;

	// Position where the player died — for soul retrieval
	[Property] public Vector3 DeathPosition { get; set; }
	[Property] public bool HasDeathPosition { get; set; } = false;

	// ── Polar opposite pairs (PRD §2.1) ──
	// Physical Trinity: Health <-> Coordination, Constitution <-> Mana, Stamina <-> Perception
	// Spiritual Trinity: Agility <-> Health, Honor (no penalty), Intelligence <-> Opposite Nodes

	static readonly (System.Func<LutePlayerStats, float> Get, System.Action<LutePlayerStats, float> Set, string Name)[] AttributePairs =
	{
		// Physical Trinity
		(s => s.Health,      (s, v) => s.Health = v,      "Health"),
		(s => s.Coordination, (s, v) => s.Coordination = v, "Coordination"),
		(s => s.Constitution, (s, v) => s.Constitution = v, "Constitution"),
		(s => s.Mana,         (s, v) => s.Mana = v,         "Mana"),
		(s => s.Stamina,      (s, v) => s.Stamina = v,      "Stamina"),
		(s => s.Perception,   (s, v) => s.Perception = v,  "Perception"),
		// Spiritual Trinity
		(s => s.Agility,      (s, v) => s.Agility = v,      "Agility"),
		(s => s.Intelligence, (s, v) => s.Intelligence = v, "Intelligence"),
		// Honor has no polar opposite (no penalty node)
	};

	// Polar opposite mapping: index -> opposite index
	// Health(0) <-> Coordination(1), Constitution(2) <-> Mana(3),
	// Stamina(4) <-> Perception(5), Agility(6) <-> Health(0),
	// Intelligence(7) <-> Opposite Nodes (special)
	static readonly int[] PolarOpposite = { 1, 0, 3, 2, 5, 4, 0, -1, -1 };

	// ── Distance Strain (PRD §2.1) ──
	// Training past 70% costs exponentially more and doubles the
	// inward contraction rate of the opposite node.
	const float StrainThreshold = 70f;

	/// <summary> Calculate the cost to raise an attribute by 1 point,
	/// accounting for distance strain. Returns the effort cost. </summary>
	public float AttributeCost( int attrIndex )
	{
		var (get, _, _) = AttributePairs[attrIndex];
		float current = get( this );
		if ( current < StrainThreshold )
			return 1f; // Base cost below strain threshold
		// Exponential cost past 70%
		float over = current - StrainThreshold;
		return 1f + (over * over * 0.01f); // Quadratic scaling
	}

	/// <summary> Train an attribute. Increases it by the given amount,
	/// pulling the polar opposite down proportionally. Returns true if
	/// the training was applied. </summary>
	public bool TrainAttribute( int attrIndex, float amount )
	{
		if ( attrIndex < 0 || attrIndex >= AttributePairs.Length )
			return false;

		var (get, set, name) = AttributePairs[attrIndex];
		float current = get( this );
		float cost = AttributeCost( attrIndex );

		// Apply the increase (clamped to 0-100)
		float newValue = Math.Clamp( current + amount, 0f, 100f );
		set( this, newValue );

		// Pull the polar opposite down
		int oppositeIndex = PolarOpposite[attrIndex];
		if ( oppositeIndex >= 0 && oppositeIndex < AttributePairs.Length )
		{
			var (oppGet, oppSet, _) = AttributePairs[oppositeIndex];
			float oppCurrent = oppGet( this );
			// Contraction rate: 1:1 below strain, 2:1 above strain
			float contractionRate = current > StrainThreshold ? 2f : 1f;
			float contraction = amount * 0.5f * contractionRate;
			float newOpp = Math.Max( 0f, oppCurrent - contraction );
			oppSet( this, newOpp );
			Log.Info( $"Lute: trained {name} {current}->{newValue}, "
				+ $"opposite contracted {oppCurrent}->{newOpp}" );
		}
		else
		{
			Log.Info( $"Lute: trained {name} {current}->{newValue} (no opposite)" );
		}

		// Recalculate derived vitals from attributes
		RecalculateVitals();
		return true;
	}

	/// <summary> Recalculate MaxHealth, MaxMana, MaxStamina from attributes.
	/// Health attribute -> MaxHealth, Mana attribute -> MaxMana, etc. </summary>
	public void RecalculateVitals()
	{
		// Base 50 + attribute * 10 (so 10 attribute = 150 HP)
		MaxHealth = 50f + Health * 10f;
		MaxMana = 20f + Mana * 5f;
		MaxStamina = 50f + Stamina * 10f;

		// Clamp current to new max
		CurrentHealth = Math.Min( CurrentHealth, MaxHealth );
		CurrentMana = Math.Min( CurrentMana, MaxMana );
		CurrentStamina = Math.Min( CurrentStamina, MaxStamina );
	}

	// ── XP and Leveling ──

	/// <summary> Award XP. When XP reaches the threshold, level up and
	/// grant attribute points. Deterministic — no RNG. </summary>
	public void AwardXP( float amount )
	{
		XP += amount;
		while ( XP >= XPToNextLevel )
		{
			XP -= XPToNextLevel;
			Level++;
			AttributePoints += 3; // 3 points per level
			Log.Info( $"Lute: level up! Now level {Level}, "
				+ $"{AttributePoints} attribute points available" );
		}
	}

	/// <summary> Spend an attribute point to train an attribute.
	/// Returns true if successful. </summary>
	public bool SpendAttributePoint( int attrIndex )
	{
		if ( AttributePoints <= 0 )
			return false;
		if ( !TrainAttribute( attrIndex, 1f ) )
			return false;
		AttributePoints--;
		return true;
	}

	// ── Skill Training (PRD §2.3) ──
	// Skill growth occurs organically through direct usage.

	/// <summary> Train a skill by usage. Skills also have polar opposites
	/// but the contraction is softer (0.25x instead of 0.5x). </summary>
	public void TrainSkill( string skillName, float amount )
	{
		// Map skill names to their property setters
		var skillMap = new System.Collections.Generic.Dictionary<string, (System.Func<float> get, System.Action<float> set, string opposite)>
		{
			{ "HeavyWeaponry", (() => HeavyWeaponry, v => HeavyWeaponry = v, "Conjuration") },
			{ "Conjuration",   (() => Conjuration,   v => Conjuration = v,   "HeavyWeaponry") },
			{ "Protection",    (() => Protection,    v => Protection = v,    "Survival") },
			{ "Survival",      (() => Survival,      v => Survival = v,      "Protection") },
			{ "Tradecraft",    (() => Tradecraft,    v => Tradecraft = v,    "Destruction") },
			{ "Destruction",   (() => Destruction,   v => Destruction = v,   "Tradecraft") },
			{ "Athletics",     (() => Athletics,     v => Athletics = v,     "Restoration") },
			{ "Restoration",   (() => Restoration,   v => Restoration = v,   "Athletics") },
			{ "Subterfuge",    (() => Subterfuge,    v => Subterfuge = v,    "HeavyWeaponry") },
		};

		if ( !skillMap.TryGetValue( skillName, out var entry ) )
			return;

		float current = entry.get();
		float newValue = Math.Clamp( current + amount, 0f, 100f );
		entry.set( newValue );

		// Soft contraction on opposite skill
		if ( skillMap.TryGetValue( entry.opposite, out var opp ) )
		{
			float oppCurrent = opp.get();
			float contraction = amount * 0.25f;
			opp.set( Math.Max( 0f, oppCurrent - contraction ) );
		}

		Log.Info( $"Lute: skill {skillName} {current}->{newValue}" );
	}

	// ── Damage and Death (PRD §1.1 Soul Retrieval) ──

	/// <summary> Apply damage to the player. Reduces CurrentHealth.
	/// On death, records death position and locks soul. </summary>
	public void TakeDamage( float amount, GameObject attacker = null )
	{
		if ( Soul != SoulStatus.Alive )
			return; // Can't damage a soul-locked character

		CurrentHealth -= amount;
		Log.Info( $"Lute: took {amount} damage from "
			+ (attacker?.Name ?? "unknown") + ", HP now {CurrentHealth}/{MaxHealth}" );

		if ( CurrentHealth <= 0f )
		{
			Die( attacker );
		}
	}

	/// <summary> Player death. Records death position and locks soul. </summary>
	public void Die( GameObject killer = null )
	{
		CurrentHealth = 0f;
		DeathPosition = WorldPosition;
		HasDeathPosition = true;
		Soul = SoulStatus.SoulLocked;
		Log.Info( $"Lute: player died at {DeathPosition}, soul locked. "
			+ "Must be retrieved via Seasonal Realm victory." );
	}

	/// <summary> Restore a soul-locked character (via Seasonal Realm victory). </summary>
	public void RestoreSoul()
	{
		if ( Soul != SoulStatus.SoulLocked )
			return;
		Soul = SoulStatus.Restored;
		CurrentHealth = MaxHealth * 0.5f; // Respawn at half HP
		Log.Info( "Lute: soul restored! Resurrecting at half HP." );
	}

	/// <summary> Heal the player. Cannot exceed MaxHealth. </summary>
	public void Heal( float amount )
	{
		CurrentHealth = Math.Min( MaxHealth, CurrentHealth + amount );
	}

	/// <summary> Spend mana. Returns true if enough mana was available. </summary>
	public bool SpendMana( float amount )
	{
		if ( CurrentMana < amount )
			return false;
		CurrentMana -= amount;
		return true;
	}

	/// <summary> Regenerate mana over time. Called from OnUpdate. </summary>
	public void RegenerateMana( float deltaTime )
	{
		// 1 mana per second + Intelligence bonus
		float regen = 1f + Intelligence * 0.1f;
		CurrentMana = Math.Min( MaxMana, CurrentMana + regen * deltaTime );
	}

	/// <summary> Regenerate stamina over time. Called from OnUpdate. </summary>
	public void RegenerateStamina( float deltaTime )
	{
		// 5 stamina per second + Athletics bonus
		float regen = 5f + Athletics * 0.5f;
		CurrentStamina = Math.Min( MaxStamina, CurrentStamina + regen * deltaTime );
	}

	protected override void OnUpdate()
	{
		// Regenerate mana and stamina over time
		RegenerateMana( Time.Delta );
		RegenerateStamina( Time.Delta );
	}

	protected override void OnStart()
	{
		// Calculate vitals from starting attributes
		RecalculateVitals();
		Log.Info( $"Lute: PlayerStats initialized — "
			+ $"HP {CurrentHealth}/{MaxHealth}, MP {CurrentMana}/{MaxMana}, "
			+ $"STA {CurrentStamina}/{MaxStamina}, Level {Level}" );
	}
}
