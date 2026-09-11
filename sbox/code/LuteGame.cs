using System;
using System.Threading.Tasks;

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

		// Enable the scene's NavMesh. The scene file may serialize the
		// IsEnabled property under either "Enabled" or "IsEnabled", and
		// neither key has reliably stuck across editor versions — so we
		// force it on here. Setting IsEnabled=true calls NavMesh.Init(),
		// but in game mode Scene.Nav_Update() won't auto-call NavMesh.Load()
		// (that only happens in editor), so we also kick off generation
		// explicitly. NavMesh.Generate() builds tiles from the world's
		// static colliders (PlazaFloor, walls, etc.).
		if ( Scene.NavMesh is not null && !Scene.NavMesh.IsEnabled )
		{
			Scene.NavMesh.IsEnabled = true;
			Log.Info( "Lute: NavMesh enabled by LuteGame (was disabled in scene)." );
			_ = GenerateNavMeshAsync();
		}

		// Build the Sanctuary Realm (Temple of Time).
		var world = Components.GetOrCreate<LuteWorld>();
		var sanctuary = world.Build();
		TimePortalSpawn = sanctuary;

		// Create the HUD root (ScreenPanel) + compass.
		// ScreenPanel renders UI to the screen; PanelComponents (like the
		// compass) only execute on the client, so this is safe on server too.
		var hudGo = Scene.CreateObject( true );
		hudGo.Name = "HUD";
		hudGo.SetParent( GameObject );
		hudGo.AddComponent<ScreenPanel>();
		hudGo.AddComponent<LuteCompass>();
		hudGo.AddComponent<LuteAgentChat>();
		Log.Info( "Lute: HUD + compass + agent chat created." );
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	/// <summary>
	/// Generate the NavMesh from the scene's static colliders. Called
	/// fire-and-forget after enabling the NavMesh in <see cref="OnStart"/>.
	/// NavMesh.Generate is async — it builds tiles across multiple frames.
	/// The builder NPCs' NavMeshAgent components will pick up the generated
	/// mesh once tiles are ready; until then they fall back to direct steering.
	/// </summary>
	async Task GenerateNavMeshAsync()
	{
		try
		{
			var generated = await Scene.NavMesh.Generate( Scene.PhysicsWorld );
			Log.Info( $"Lute: NavMesh generation {(generated ? "complete" : "failed")}." );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Lute: NavMesh generation threw: {ex.Message}" );
		}
	}
}
