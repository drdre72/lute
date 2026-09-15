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
		// NOTE: We enable the NavMesh here but defer generation until
		// after LuteWorld.Build() completes — the village terrain and
		// resource sources are spawned during Build(), and the NavMesh
		// must include their colliders or village-area NPCs spawn off-mesh.
		if ( Scene.NavMesh is not null && !Scene.NavMesh.IsEnabled )
		{
			Scene.NavMesh.IsEnabled = true;
			Log.Info( "Lute: NavMesh enabled by LuteGame (generation deferred until world build completes)." );
		}

		// Initialize the junction resolver registry (corners, gates,
		// doorways, floor/wall, roof/wall, etc.). Each junction type
		// registers its resolver here.
		Lute.Building.JunctionResolverInit.Initialize();

		// Add the single global simulation ticker so shared static
		// clocks (SpatialBlackboard, ConstructionEventBus,
		// BuilderLivenessRegistry) advance exactly once per frame,
		// independent of builder population. Per-NPC clock advancement
		// was removed from VillageBuilderController.OnFixedUpdate().
		var tickerGo = Scene.CreateObject( true );
		tickerGo.Name = "LuteSimulationTicker";
		tickerGo.SetParent( GameObject );
		tickerGo.AddComponent<Lute.Building.LuteSimulationTicker>();

		// Add the production settlement planner — drives adaptive
		// settlement planning (need evaluation → structure requests →
		// Surveyor site selection). This replaces the need-evaluation
		// loop that was previously inside Gate3Benchmark.
		var plannerGo = Scene.CreateObject( true );
		plannerGo.Name = "SettlementPlanner";
		plannerGo.SetParent( GameObject );
		plannerGo.AddComponent<Lute.Building.SettlementPlanner>();

		// Add the production logistics planner — drives the closed-loop
		// economy by creating haul jobs for unsatisfied task material
		// requirements. This replaces the SupplyUnsatisfiedTasks() call
		// that was previously inside Gate3Benchmark.
		var logisticsGo = Scene.CreateObject( true );
		logisticsGo.Name = "LogisticsPlanner";
		logisticsGo.SetParent( GameObject );
		logisticsGo.AddComponent<Lute.Building.LogisticsPlanner>();

		// Add the production planner — demand-driven crafting supply.
		// When a task needs a crafted material (Plank, Brick, Timber)
		// that no stockpile has, this creates haul jobs to bring raw
		// inputs to the appropriate workstation stockpile so crafters
		// can produce the finished good. Move 7: resource pressure
		// generates production orders; production doesn't just pick the
		// nearest source.
		var productionGo = Scene.CreateObject( true );
		productionGo.Name = "ProductionPlanner";
		productionGo.SetParent( GameObject );
		productionGo.AddComponent<Lute.Building.ProductionPlanner>();

		// Add the completion effects manager — applies gameplay effects
		// when a structure finishes building. A completed sawmill spawns
		// a CraftingBench (Sawmill) workstation, increasing plank/timber
		// production capacity. A completed cottage increases housing.
		// Move 8: completed structures change the settlement's capacity.
		var effectsGo = Scene.CreateObject( true );
		effectsGo.Name = "CompletionEffectsManager";
		effectsGo.SetParent( GameObject );
		effectsGo.AddComponent<Lute.Building.CompletionEffectsManager>();

		// Add the representation collapser — Move 9. Collapses completed
		// wall segments from per-brick GameObjects into a single static
		// GameObject (one ModelRenderer + one BoxCollider). Prevents
		// brick-by-brick GameObject counts from becoming the scale
		// ceiling for large settlements.
		var collapserGo = Scene.CreateObject( true );
		collapserGo.Name = "RepresentationCollapser";
		collapserGo.SetParent( GameObject );
		collapserGo.AddComponent<Lute.Building.RepresentationCollapser>();

		// Initialize the profession/capability catalog so task eligibility
		// is derived from capabilities, not role strings (PR #6 §3).
		// Clear first so a fresh play session picks up any newly-added
		// professions (static state persists across sessions in S&Box).
		Lute.Building.CapabilityRegistry.Clear();
		Lute.Building.CapabilityRegistry.InitializeDefaults();

		// Spawn bootstrap resource sources and stockyard so the resource
		// loop is testable with placeholder visuals (PR #6 §5, §17).
		// Use the village center (same as Gate 3 benchmark) so sources
		// are just outside the village perimeter, not 20k units away.
		Lute.Building.ResourceBootstrap.Initialize( new Vector3( -400f * 39.37f, -400f * 39.37f, 0f ) );

		// Build the Sanctuary Realm (Temple of Time).
		var world = Components.GetOrCreate<LuteWorld>();
		var sanctuary = world.Build();
		TimePortalSpawn = sanctuary;

		// Now that the world (terrain, structures, resource sources) is
		// built, generate the NavMesh so it covers the village area.
		// Village-area NPCs spawn at ~(-15748,-15748) — without this
		// post-build generation they'd be off-mesh and unable to path.
		if ( Scene.NavMesh is not null && Scene.NavMesh.IsEnabled )
		{
			_ = GenerateNavMeshAsync();
		}

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

	protected override void OnUpdate()
	{
		// Drive deterministic NPC communication: observe construction state
		// and emit world-fact messages through the NLP pipeline.
		// (BuilderLivenessRegistry is now ticked by LuteSimulationTicker,
		// not here — so liveness timing is population-independent.)
		Lute.Building.WorldFactProvider.Tick();
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
			// The full-scene Generate() calculates bounds from ALL physics
			// bodies, which with a 3km × 3km ground plane produces ~3700+
			// tiles and hangs. Use tight CustomBounds around the village
			// center only — that's where haulers/crafters/surveyor operate.
			// Sanctuary builder NPCs near origin already have navmesh from
			// the scene's initial generation.
			var M = 39.37f;
			var villageCenter = new Vector3( -400f * M, -400f * M, 0f );
			float radius = 300f * M; // 300m radius — ~24 tiles per axis

			var navBounds = new BBox(
				villageCenter - new Vector3( radius, radius, 100f * M ),
				villageCenter + new Vector3( radius, radius, 200f * M ) );

			Scene.NavMesh.CustomBounds = true;
			Scene.NavMesh.Bounds = navBounds;
			Log.Info( $"Lute: NavMesh — generating village navmesh (radius {radius / M:F0}m, bounds {navBounds})." );

			var generated = await Scene.NavMesh.Generate( Scene.PhysicsWorld );
			Log.Info( $"Lute: NavMesh generation {(generated ? "complete" : "failed")} (village bounds)." );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Lute: NavMesh generation threw: {ex.Message}" );
		}
	}
}
