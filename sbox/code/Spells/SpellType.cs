/// <summary>
/// The eight Lute spell schools. Each maps to a building/world tool,
/// an incantation, and a cast animation (see the Lute spell book).
/// </summary>
public enum SpellType
{
	Fire,      // place_prop, spawn_torch       - "Ignis!"
	Ice,       // sculpt_terrain (raise/lower)  - "Glacies!"
	Earth,     // sculpt_terrain (flatten)      - "Terra!"
	Wind,      // sculpt_terrain (smooth)       - "Ventus!"
	Lightning, // scatter_on_terrain            - "Fulmen!"
	Arcane,    // build_structure               - "Aedifico!"
	Shadow,    // remove/delete                 - "Umbra!"
	Heal,      // default/utility               - "Sancto!"
}
