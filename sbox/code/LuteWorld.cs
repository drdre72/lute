using Lute.Building;
using Lute.Npc;

/// <summary>
/// Builds the Lute Sanctuary Realm — the Temple of Time.
/// This is a separate scene/realm where players materialize via the Time Portal
/// (PRD section 1.1). Start minimal: just the portal. Walls, pillars, and
/// architecture will be added iteratively.
///
/// All geometry is spawned programmatically. The portal is purely visual
/// (no collision) so it can never trap the player.
/// </summary>
public sealed class LuteWorld : Component
{
	/// <summary>
	/// Builds the sanctuary world. Returns the root GameObject holding all world geometry.
	/// </summary>
	public GameObject Build()
	{
		// Reset construction runtime state exactly once at world-build entry,
		// BEFORE the static occupancy scan. Static fields persist across play
		// sessions, so stale reservations/task assignments must be cleared.
		// Builders must NOT call Reset() themselves — it would wipe the scan.
		Lute.Building.ConstructionDirector.Reset();

		var root = Scene.CreateObject( true );
		root.Name = "Sanctuary";
		root.WorldPosition = Vector3.Zero;

		// Push the camera's far clip plane out so the monument and ground
		// plane are visible at distance. Default ZFar=10000 (~254m) is too
		// short for a 4.5km world.
		var cam = Scene.Camera;
		if ( cam.IsValid() )
		{
			cam.ZFar = 50000f;  // ~1270m — enough to see the monument at 200m
			Log.Info( $"Lute: camera ZFar set to {cam.ZFar}." );
		}

		// World ground plane — large flat plain at z=0, just below the
		// sanctuary floor (z=2) so stepping off the temple is a tiny step down.
		BuildWorldGround( root );

		// Build the Neutral Market monument (first POI, square whitebox).
		// Placed 200m NE of the sanctuary — close enough to see with the
		// extended far clip, far enough to be a separate location.
		var monument = Components.GetOrCreate<LuteMonumentBuilder>();
		monument.Center = new Vector3( 400f * 39.37f, 400f * 39.37f, 0f );
		monument.Build( root );

		BuildTempleFloor( root );
		BuildHexagonalWalls( root );
		BuildTimePortal( root );
		BuildArchway( root );
		BuildAcropolis( root );
		BuildBuilderNpc( root );
		BuildWatchtower( root );
		BuildTestStructure( root );
		BuildVillage( root );

		// Scan static world geometry and register it in the occupancy
		// ledger so village construction reservations respect pre-existing
		// structures (monument, temple, walls, acropolis, etc.).
		try
		{
			Lute.Building.OccupancyScanner.ScanScene( Scene );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"Lute: OccupancyScanner failed: {ex.Message}" );
		}

		// Spawn NPCs from all SpawnMarkers (both editor-placed and code-spawned
		// by BuildTestStructure/BuildVillage above). Each marker is consumed after spawning.
		NPCSpawner.SpawnAll( Scene, root );

		Log.Info( "Lute: Sanctuary built (world ground + monument + portal + temple floor + hex chamber + builder NPC + watchtower + test structure + village + NPC markers)." );
		return root;
	}

	/// <summary>
	/// Six wall segments arranged in a hexagon around the portal.
	/// Walls are placed at a safe radius so they never overlap the center.
	/// Each wall is a box positioned and rotated to form one edge of the hex.
	/// </summary>
	void BuildHexagonalWalls( GameObject parent )
	{
		const int sides = 6;
		float radius = 1200f;       // distance from center to wall midpoint
		float wallHeight = 600f;    // ~11.4m tall
		float wallThickness = 40f;  // ~0.76m thick

		// box.vmdl base size is 50x50x50. To get desired world size, scale = desired / 50.
		float edgeLength = radius; // hexagon: R = side length
		float wallWidth = edgeLength + wallThickness;

		var modelScale = new Vector3( wallWidth / 50f, wallThickness / 50f, wallHeight / 50f );

		for ( int i = 0; i < sides; i++ )
		{
			// Place walls at edge midpoints: 30°, 90°, 150°, 210°, 270°, 330°
			// (offset 30° from vertex angles so walls sit between vertices)
			float angle = 30f + ( 60f * i );
			float rad = angle * MathF.PI / 180f;

			float x = MathF.Cos( rad ) * radius;
			float y = MathF.Sin( rad ) * radius;

			var pos = new Vector3( x, y, wallHeight * 0.5f );
			// Yaw = angle so the wall's X-axis runs along the hex edge
			var rot = new Angles( 0, angle, 0 );

			var wall = CreatePrimitive( parent, $"Wall_{i}", "models/dev/box.vmdl",
				pos, rot, modelScale, material: "materials/dev/gray_50.vmat" );
			AddBoxCollider( wall, new Vector3( 50f, 50f, 50f ), staticCollider: true );
		}
	}

	/// <summary>
	/// The world ground — a large flat plain at z=0, just below the sanctuary
	/// floor (z=2). Uses a thick box for reliable collision so the player
	/// can't fall through. Stepping off the temple edge is a barely-
	/// noticeable 2-unit step down.
	/// </summary>
	void BuildWorldGround( GameObject parent )
	{
		// 3km × 3km ground, 100m thick — massive collision volume that can't
		// be fallen through. box.vmdl base = 50×50×50.
		float groundSize = 3000f * 39.37f;   // 118,110 units (~3000m)
		float groundThick = 100f * 39.37f;   // 3,937 units (~100m) — very thick

		var ground = CreatePrimitive( parent, "WorldGround", "models/dev/box.vmdl",
			new Vector3( 0, 0, -groundThick * 0.5f ), Rotation.Identity,
			new Vector3( groundSize / 50f, groundSize / 50f, groundThick / 50f ),
			material: "materials/dev/gray_75.vmat" );

		// BoxCollider.Scale is the absolute box size in local units (default 50,
		// matching box.vmdl's native 50³). Final world size = Scale × WorldScale.
		// WorldScale = groundSize/50, so Scale=50 gives 50 × groundSize/50 = groundSize.
		var collider = ground.AddComponent<BoxCollider>();
		collider.Scale = new Vector3( 50f, 50f, 50f );
		collider.Static = true;

		Log.Info( $"Lute: WorldGround collider — pos={ground.WorldPosition}, worldScale={ground.WorldScale}, colliderScale={collider.Scale}, static={collider.Static}" );
	}

	/// <summary>
	/// The temple floor — a large raised stone platform surrounding the portal,
	/// at the same height as the portal pad. Uses a thick box for solid collision.
	/// </summary>
	void BuildTempleFloor( GameObject parent )
	{
		// 100m × 100m × 2m thick platform at z=2 (top surface at z=3, bottom at z=1).
		// The world ground top is at z=0, so there's a 1-unit gap — but the
		// platform's own collider covers z=1..3, and the ground covers z=-100..0,
		// so the player stands on top of whichever is closer.
		float floorSize = 100f * 39.37f;   // 3,937 units (~100m)
		float floorThick = 2f * 39.37f;    // 79 units (~2m)

		var floor = CreatePrimitive( parent, "TempleFloor", "models/dev/box.vmdl",
			new Vector3( 0, 0, 2f - floorThick * 0.5f ), Rotation.Identity,
			new Vector3( floorSize / 50f, floorSize / 50f, floorThick / 50f ),
			material: "materials/dev/gray_50.vmat" );
		var collider = floor.AddComponent<BoxCollider>();
		collider.Scale = new Vector3( 50f, 50f, 50f );
		collider.Static = true;

		Log.Info( $"Lute: TempleFloor collider — pos={floor.WorldPosition}, worldScale={floor.WorldScale}, colliderScale={collider.Scale}, static={collider.Static}" );

		// Outer ring trim — slightly larger, darker border (visual only)
		float trimSize = 110f * 39.37f;
		var trim = CreatePrimitive( parent, "TempleFloorTrim", "models/dev/box.vmdl",
			new Vector3( 0, 0, 0.5f ), Rotation.Identity,
			new Vector3( trimSize / 50f, trimSize / 50f, 1f / 50f ),
			material: "materials/dev/gray_25.vmat" );
	}

	/// <summary>
	/// The central Time Portal — a glowing ring where players materialize
	/// into the world (PRD section 1.1: "The Time Portal").
	/// Purely visual: no collision, just model + light.
	/// </summary>
	void BuildTimePortal( GameObject parent )
	{
		// sphere.vmdl base radius = 32 (64 diameter).
		// Portal pad: flat disk, ~160 units diameter (~3m)
		var pad = CreatePrimitive( parent, "PortalPad", "models/dev/sphere.vmdl",
			new Vector3( 0, 0, 2 ), Scale: new Vector3( 2.5f, 2.5f, 0.1f ),
			tint: new Color( 0.15f, 0.1f, 0.3f ) );

		// Portal egg: ~200 units wide, ~300 tall (~3.8m x 5.7m)
		var ring = CreatePrimitive( parent, "PortalRing", "models/dev/sphere.vmdl",
			new Vector3( 0, 0, 150 ), Scale: new Vector3( 3f, 3f, 4.5f ),
			tint: new Color( 0.3f, 0.5f, 1.0f ) );

		// Portal glow light (omnidirectional blue)
		var lightGo = Scene.CreateObject( true );
		lightGo.Name = "PortalGlow";
		lightGo.SetParent( parent );
		lightGo.WorldPosition = new Vector3( 0, 0, 150 );
		var light = lightGo.AddComponent<PointLight>();
		light.LightColor = new Color( 0.4f, 0.6f, 1.0f );
		light.Radius = 500f;
		light.Shadows = true;
	}

	/// <summary>
	/// Spawns the imported medieval archway (base.fbx → base.vmdl) beside the
	/// temple floor at a known position so its imported scale can be measured.
	/// The model was authored in centimeters, so import_scale=0.3937 was used
	/// in the .vmdl. This logs the model's bounding box for verification.
	/// </summary>
	void BuildArchway( GameObject parent )
	{
		const float M = 39.37f;  // meters → S&Box units

		var go = Scene.CreateObject( true );
		go.Name = "ArchwayTest";
		go.SetParent( parent );

		// Place 30m east of center, on top of the temple floor (z=2).
		go.WorldPosition = new Vector3( 30f * M, 0f, 2f );
		go.WorldRotation = Rotation.Identity;
		go.WorldScale = Vector3.One;

		var renderer = go.AddComponent<ModelRenderer>();
		var model = Model.Load( "models/medieval/base.vmdl" );
		renderer.Model = model;

		// Force a material override to bypass the FBX's broken material names
		// (root.0.0, root.1, etc. — the dots make the engine reject them as
		// invalid resource extensions). This renders the whole model with
		// a single visible material.
		renderer.MaterialOverride = Material.Load( "materials/medieval/archway_stone.vmat" );

		// Log bounding box so we can read the imported scale from the editor log.
		if ( model is not null )
		{
			var bounds = model.Bounds;
			Log.Info( $"Lute: ArchwayTest model='models/medieval/base.vmdl' bounds={bounds} center={bounds.Center} size={bounds.Size} sizeMeters=({bounds.Size.x / M:F2}, {bounds.Size.y / M:F2}, {bounds.Size.z / M:F2})" );
		}
		else
		{
			Log.Warning( "Lute: ArchwayTest model failed to load (models/medieval/base.vmdl)." );
		}
	}

	/// <summary>
	/// Spawns the imported Acropolis monument (CC0 STL → OBJ → .vmdl) to test
	/// its imported scale. The original STL was authored in meters (Blender),
	/// so import_scale=39.37 was used in the .vmdl.
	/// </summary>
	void BuildAcropolis( GameObject parent )
	{
		const float M = 39.37f;

		var go = Scene.CreateObject( true );
		go.Name = "AcropolisTest";
		go.SetParent( parent );

		// Place far from sanctuary (500m north), base flush on the ground (z=0).
		// The Acropolis is ~91m × 37m × 18m — way too big for the temple floor.
		// After import rotation, bounds are X:[-759,698] Y:[-1877,1709] Z:[-189,533]
		// (center at -30,-84,172). Scale 6x for visibility. The model's local
		// base is at z=-189.29; ×6 = -1135.74. Setting the GameObject's world z
		// to +1135.74 puts that base at world z=0 (on the floor).
		go.WorldScale = new Vector3( 6f, 6f, 6f );
		go.WorldPosition = new Vector3( 0f, 500f * M, 0f )
			- new Vector3( -30.40f * 6f, -83.77f * 6f, -189.29f * 6f );  // recenter XY, base flush on floor (z=0)
		go.WorldRotation = Rotation.Identity;

		var renderer = go.AddComponent<ModelRenderer>();
		var model = Model.Load( "models/monuments/acropolis.vmdl" );
		renderer.Model = model;
		renderer.MaterialOverride = Material.Load( "materials/dev/primary_white.vmat" );

		// Add a ModelCollider so the monument is solid. This uses the model's
		// compiled physics data (the PhysicsHullFromRender node in the .vmdl).
		// If the physics node is missing/broken, Model.Physics will be null or
		// have zero parts and the collider will be empty — logged below so we
		// can tell whether similar imported assets will work.
		var collider = go.AddComponent<ModelCollider>();
		collider.Model = model;

		if ( model is not null )
		{
			var bounds = model.Bounds;
			Log.Info( $"Lute: AcropolisTest model='models/monuments/acropolis.vmdl' bounds={bounds} center={bounds.Center} size={bounds.Size} sizeMeters=({bounds.Size.x / M:F2}, {bounds.Size.y / M:F2}, {bounds.Size.z / M:F2})" );

			var phys = model.Physics;
			if ( phys is null )
			{
				Log.Warning( "Lute: AcropolisTest ModelCollider — Model.Physics is null. The .vmdl has no compiled physics data; PhysicsHullFromRender node is missing or failed to compile." );
			}
			else
			{
				Log.Info( $"Lute: AcropolisTest ModelCollider — Model.Physics has {phys.Parts.Count} part(s)." );
				for ( int i = 0; i < phys.Parts.Count; i++ )
				{
					var p = phys.Parts[i];
					Log.Info( $"Lute: AcropolisTest physics part {i}: hulls={p.Hulls.Count} meshes={p.Meshes.Count} spheres={p.Spheres.Count} capsules={p.Capsules.Count} bone='{p.BoneName}'." );
				}
			}
		}
		else
		{
			Log.Warning( "Lute: AcropolisTest model failed to load (models/monuments/acropolis.vmdl)." );
		}
	}

	/// <summary>
	/// Builds a from-scratch multi-tier stone watchtower near the sanctuary
	/// using block primitives (MeshComponent + PolygonMesh). Demonstrates the
	/// building system: foundation, hollow stacked walls, walkable parapet
	/// platform with a doorway, and an external staircase. Every block is a
	/// real mesh collider so the tower is solid and climbable.
	/// Placed 120m east of the sanctuary center (well outside the hex walls).
	/// </summary>
	void BuildWatchtower( GameObject parent )
	{
		const float M = 39.37f;

		var tower = Components.GetOrCreate<LuteWatchtower>();
		tower.Center = new Vector3( 120f * M, 0f, 0f );
		tower.Yaw = 0f;
		tower.Build( parent );
	}

	/// <summary>
	/// Spawns a builder NPC (citizen body) near the sanctuary for block-
	/// building collision testing. The NPC places a small test platform,
	/// walks onto it, and reports grounding status to the log.
	/// </summary>
	void BuildBuilderNpc( GameObject parent )
	{
		const float M = 39.37f;

		var go = Scene.CreateObject( true );
		go.Name = "Merlyn";
		go.SetParent( parent );

		// Place 20m east of the temple center, on the ground. Spawn close to
		// the floor (z=4) so the PlayerController's short ground trace reaches
		// it — spawning at z=64 leaves the body floating and grounding fails.
		go.WorldPosition = new Vector3( 20f * M, 0f, 4f );
		go.WorldRotation = Rotation.Identity;

		// Body — citizen model (same as the player).
		var bodyGo = Scene.CreateObject( true );
		bodyGo.Name = "Body";
		bodyGo.SetParent( go );
		bodyGo.WorldPosition = Vector3.Zero; // relative to parent
		bodyGo.WorldRotation = Rotation.Identity;
		var bodyRenderer = bodyGo.AddComponent<SkinnedModelRenderer>();
		bodyRenderer.Model = Model.Load( "models/citizen/citizen.vmdl" );

		// Colliders — same setup as the player (capsule + box).
		var collidersGo = Scene.CreateObject( true );
		collidersGo.Name = "Colliders";
		collidersGo.SetParent( go );
		var capsule = collidersGo.AddComponent<CapsuleCollider>();
		capsule.Radius = 16f;
		capsule.Start = new Vector3( 0, 0, 0 );
		capsule.End = new Vector3( 0, 0, 72f );
		var box = collidersGo.AddComponent<BoxCollider>();
		box.Scale = new Vector3( 32f, 32f, 72f );

		// Rigidbody for physics.
		var rb = go.AddComponent<Rigidbody>();
		rb.Gravity = true;

		// PlayerController for movement and grounding detection.
		// Disable input — this NPC moves via code, not WASD.
		var controller = go.AddComponent<PlayerController>();
		controller.UseInputControls = false;
		controller.UseCameraControls = false;
		controller.UseAnimatorControls = true;
		controller.Renderer = bodyRenderer;

		// LuteBuilderNpc — the build-and-test logic.
		var npc = go.AddComponent<LuteBuilderNpc>();

		// First-person camera for agent vision. Nested as a child at eye
		// height so it follows Merlyn when he teleports/walks. Not the main
		// camera (IsMainCamera=false) — the agent captures from it
		// explicitly via MCP camera_screenshot with this camera's ID.
		var camGo = Scene.CreateObject( true );
		camGo.Name = "Eyes";
		camGo.SetParent( go );
		// Third-person camera nested as a child. Positioned 1.5 feet (18
		// units) from the back of the head at a 35° angle — behind and
		// above for an over-the-shoulder view. Forward for the citizen
		// model is -Y, so "behind" is +Y.
		camGo.LocalPosition = new Vector3( 0, 15f, 74f );
		camGo.LocalRotation = Rotation.Identity;
		var cam = camGo.AddComponent<CameraComponent>();
		cam.IsMainCamera = false;
		cam.FieldOfView = 90f;
		cam.ZNear = 1f;
		cam.ZFar = 50000f;

		Log.Info( $"Lute: BuilderNpc spawned at {go.WorldPosition} (parent={parent.Name})." );
		Log.Info( $"Lute: BuilderNpc first-person camera 'Eyes' at local z=64, FOV=90." );
	}

	/// <summary>
	/// Spawns code-spawned <see cref="SpawnMarker"/>s for three test builder
	/// NPCs near the sanctuary to verify the room-subdivided building grammar
	/// end-to-end. Three structures side by side at WealthFactor 1.0, 2.0, 3.0
	/// so the agent can compare the room-subdivision tiers via logs/MCP.
	///
	/// Markers are consumed by <see cref="NPCSpawner.SpawnAll"/> after this
	/// returns — the spawner reads each marker's config and spawns the
	/// structure + citizen body + NPCBuilderController. This keeps the
	/// marker system general-purpose: editor-placed markers in the .scene
	/// file would work the same way.
	/// </summary>
	void BuildTestStructure( GameObject parent )
	{
		const float M = 39.37f;

		float[] wealthTiers = { 1.0f, 2.0f, 3.0f };
		for ( int i = 0; i < wealthTiers.Length; i++ )
		{
			var markerGo = Scene.CreateObject( true );
			markerGo.Name = $"TestMarker_W{wealthTiers[i]:F1}";
			markerGo.SetParent( parent );

			// Place 40m south of the temple center, spaced 15m apart along X.
			markerGo.WorldPosition = new Vector3( (i - 1) * 15f * M, -40f * M, 0f );
			markerGo.WorldRotation = Rotation.Identity;

			var marker = markerGo.AddComponent<SpawnMarker>();
			marker.NpcType = "Builder";
			marker.NpcName = $"BuilderNPC_W{wealthTiers[i]:F1}";
			marker.WealthFactor = wealthTiers[i];
			marker.BaseWidth = 4;
			marker.BaseHeight = 4;
			marker.LayoutSeed = 1000 + i;

			Log.Info( $"Lute: TestMarker {i} (wealth={wealthTiers[i]:F1}) placed at {markerGo.WorldPosition}." );
		}
	}

	/// <summary>
	/// Spawns a <see cref="SpawnMarker"/> for a village builder NPC at a
	/// new location ~400m SW of the sanctuary (opposite the Neutral Market
	/// which is ~400m NE). The village builder constructs a full medieval
	/// village (stone walls, gates, roads, ~40 buildings) over ~8 hours
	/// with periodic saves every 10 minutes. On reload, it resumes from
	/// the last save.
	/// </summary>
	void BuildVillage( GameObject parent )
	{
		const float M = 39.37f;

		var markerGo = Scene.CreateObject( true );
		markerGo.Name = "VillageMarker";
		markerGo.SetParent( parent );

		// 400m SW of sanctuary — opposite the market at 400m NE.
		markerGo.WorldPosition = new Vector3( -400f * M, -400f * M, 0f );
		markerGo.WorldRotation = Rotation.Identity;

		var marker = markerGo.AddComponent<SpawnMarker>();
		marker.NpcType = "VillageBuilder";
		marker.NpcName = "VillageBuilderNPC";
		marker.VillageSeed = 42;     // deterministic village layout
		marker.FreshBuild = true;    // clear any existing save — start from nothing
		marker.BuilderCount = 3;     // multi-builder mode: 3 builders share the task list

		Log.Info( $"Lute: VillageMarker placed at {markerGo.WorldPosition} (~400m SW of sanctuary). Fresh build — 3 builders will construct the full village (~270 tasks) from nothing." );
	}

	/// <summary> Creates a GameObject with a ModelRenderer using a primitive model. </summary>
	GameObject CreatePrimitive( GameObject parent, string name, string modelPath,
		Vector3 position, Rotation rotation = default, Vector3 Scale = default,
		Color tint = default, string material = null )
	{
		var go = Scene.CreateObject( true );
		go.Name = name;
		go.SetParent( parent );
		go.WorldPosition = position;
		if ( rotation != default ) go.WorldRotation = rotation;
		if ( Scale != default ) go.WorldScale = Scale;

		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( modelPath );
		if ( tint != default ) renderer.Tint = tint;
		if ( material != null ) renderer.MaterialOverride = Material.Load( material );

		return go;
	}

	/// <summary> Adds a static BoxCollider for collision. </summary>
	BoxCollider AddBoxCollider( GameObject go, Vector3 size, bool staticCollider )
	{
		var collider = go.AddComponent<BoxCollider>();
		collider.Scale = size;
		collider.Static = staticCollider;
		return collider;
	}
}
