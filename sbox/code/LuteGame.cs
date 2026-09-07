/// <summary>
/// Lute game manager. Root component for the Lute MMORPG.
/// Handles game lifecycle and (eventually) the Time Portal spawn loop,
/// soul retrieval state, and the profession/dynamic-quest engine.
/// </summary>
public sealed class LuteGame : Component
{
	/// <summary> Singleton instance of the active game manager. </summary>
	public static LuteGame Instance { get; private set; }

	/// <summary>
	/// The Time Portal spawn point. Per the PRD, every character materializes
	/// into the world via an in-world Time Portal with a brief temporal
	/// invulnerability buffer.
	/// </summary>
	[Property] public GameObject TimePortalSpawn { get; set; }

	protected override void OnStart()
	{
		Instance = this;
		Log.Info( "Lute: Game manager started." );

		// Build the Sanctuary Realm (Temple of Time).
		var world = Components.GetOrCreate<LuteWorld>();
		var sanctuary = world.Build();
		TimePortalSpawn = sanctuary;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}
}
