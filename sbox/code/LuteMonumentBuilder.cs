/// <summary>
/// Procedurally generates the Neutral Market monument — the game's first
/// fixed-location POI (Rust-style monument). A square, fortified market
/// keep belonging to an independent merchant's guild: neutral ground where
/// any faction can trade, craft, and rest.
///
/// Layout (Variant B: Square) per Monument_Spec_Neutral_Market_Square.md §2a:
/// concentric square rings — central plaza, inner ring (workbenches + housing),
/// wall band (curtain wall + battlements), moat — with 4 bridges/gates at
/// N/E/S/W and 8 watchtowers flanking the gates.
///
/// This is a whitebox blockout: correct scale and layout using dev primitives
/// (box.vmdl, sphere.vmdl, plane_large.vmdl) and dev materials. No bespoke
/// art, no NPCs, no interior detail — just the shell and zoning.
/// </summary>
public sealed class LuteMonumentBuilder : Component
{
	const float M = 39.37f; // S&Box units per meter (Source engine convention)

	// Seeded random for procedural variation — deterministic so the monument
	// looks the same every build (no RNG between sessions).
 readonly System.Random _rng = new( 1337 );

	/// <summary> World-space center of the monument. </summary>
	[Property] public Vector3 Center { get; set; } = new Vector3( 15000, 15000, 0 );

	/// <summary> Half-width of the central plaza (from center to edge). </summary>
	[Property] public float PlazaHalfWidth { get; set; } = 60f * M;

	/// <summary> Half-width to the outer edge of the inner ring. </summary>
	[Property] public float InnerRingOuter { get; set; } = 110f * M;

	/// <summary> Half-width to the outer face of the curtain wall. </summary>
	[Property] public float WallOuterHalfWidth { get; set; } = 140f * M;

	/// <summary> Half-width to the outer edge of the moat. </summary>
	[Property] public float MoatOuterHalfWidth { get; set; } = 170f * M;

	/// <summary> Height of the curtain wall. </summary>
	[Property] public float WallHeight { get; set; } = 12f * M;

	/// <summary> Depth of the moat below ground level. </summary>
	[Property] public float MoatDepth { get; set; } = 3f * M;

	/// <summary>
	/// Builds the monument under <paramref name="parent"/> and returns the root.
	/// </summary>
	public GameObject Build( GameObject parent )
	{
		var root = Scene.CreateObject( true );
		root.Name = "NeutralMarket";
		root.SetParent( parent );
		root.WorldPosition = Center;

		BuildFoundation( root );
		BuildPlazaFloor( root );
		BuildCentralWell( root );
		BuildMarketStalls( root );
		BuildInnerRingFloor( root );
		BuildCraftingStations( root );
		BuildNPCHousing( root );
		BuildCurtainWall( root );
		BuildBattlements( root );
		BuildGatehouses( root );
		BuildBridges( root );
		BuildMoat( root );
		BuildMarketTerrain( root );
		BuildWatchtowers( root );
		BuildTowerBattlements( root );
		BuildLighting( root );
		BuildVisualDetails( root );

		Log.Info( "Lute: Neutral Market monument built (detailed whitebox)." );
		return root;
	}

	// --- Material constants (custom PBR textures, differentiated by surface type) ---

	const string MatStoneWall   = "materials/medieval/stone_wall.vmat";   // walls, corners, gatehouse (high tiling)
	const string MatStoneTower  = "materials/medieval/stone_tower.vmat";  // towers (perfect brick scale)
	const string MatStoneDetail = "materials/medieval/stone_detail.vmat"; // merlons, well rim, jambs (low tiling)
	const string MatPlaza       = "materials/medieval/plaza.vmat";        // plaza floor, inner ring, bridges
	const string MatWood        = "materials/medieval/wood.vmat";         // stalls, workbenches, well posts
	const string MatWoodHouse   = "materials/medieval/wood_house.vmat";   // houses (higher tiling)
	const string MatMetal       = "materials/medieval/metal.vmat";        // portcullis bars
	const string MatRoof        = "materials/medieval/roof.vmat";         // awnings, well roof
	const string MatWater       = "materials/medieval/moat_water.vmat";      // moat (dark blue, reflective)

	// --- Geometry helpers ---

	/// <summary> Returns a slightly jittered copy of a color for per-instance variation. </summary>
	Color TintJitter( Color baseColor, float amount = 0.08f )
	{
		float r = MathX.Clamp( baseColor.r + (float)(_rng.NextDouble() - 0.5) * amount, 0, 1 );
		float g = MathX.Clamp( baseColor.g + (float)(_rng.NextDouble() - 0.5) * amount, 0, 1 );
		float b = MathX.Clamp( baseColor.b + (float)(_rng.NextDouble() - 0.5) * amount, 0, 1 );
		return new Color( r, g, b );
	}

	/// <summary> Returns a small random offset in the range [-amount, +amount]. </summary>
	float Jitter( float amount = 1f ) => (float)(_rng.NextDouble() - 0.5) * 2f * amount;

	/// <summary> World position at the center of one edge of a square ring. </summary>
	Vector3 EdgeCenter( float halfWidth, int side )
	{
		return side switch
		{
			0 => new Vector3( 0, halfWidth, 0 ),   // North
			1 => new Vector3( halfWidth, 0, 0 ),   // East
			2 => new Vector3( 0, -halfWidth, 0 ),  // South
			_ => new Vector3( -halfWidth, 0, 0 ),  // West
		};
	}

	/// <summary> Yaw rotation for a wall segment running along one edge. </summary>
	float EdgeYaw( int side ) => side * 90f;

	/// <summary> Corner position (0=NW, 1=NE, 2=SE, 3=SW). </summary>
	Vector3 CornerPos( float halfWidth, int corner )
	{
		return corner switch
		{
			0 => new Vector3( -halfWidth, halfWidth, 0 ),   // NW
			1 => new Vector3( halfWidth, halfWidth, 0 ),    // NE
			2 => new Vector3( halfWidth, -halfWidth, 0 ),   // SE
			_ => new Vector3( -halfWidth, -halfWidth, 0 ),  // SW
		};
	}

	GameObject CreatePrimitive( GameObject parent, string name, string modelPath,
		Vector3 localPos, Rotation rotation, Vector3 scale,
		Color tint = default, string material = null )
	{
		var go = Scene.CreateObject( true );
		go.Name = name;
		go.SetParent( parent );
		go.WorldPosition = parent.WorldPosition + localPos;
		if ( rotation != default ) go.WorldRotation = rotation;
		if ( scale != default ) go.WorldScale = scale;

		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( modelPath );
		if ( tint != default ) renderer.Tint = tint;
		if ( material != null ) renderer.MaterialOverride = Material.Load( material );

		return go;
	}

	/// <summary> Adds a static BoxCollider. 'size' is the absolute box size in
	/// local units (NOT a 0-1 multiplier). For box.vmdl (native 50³), pass
	/// (50,50,50) to match the visual exactly. Final world size = size × WorldScale. </summary>
	BoxCollider AddBoxCollider( GameObject go, Vector3 size )
	{
		var collider = go.AddComponent<BoxCollider>();
		collider.Scale = size;
		collider.Static = true;
		return collider;
	}

	GameObject AddPointLight( GameObject parent, string name, Vector3 localPos,
		Color color, float radius, bool shadows = false )
	{
		var lightGo = Scene.CreateObject( true );
		lightGo.Name = name;
		lightGo.SetParent( parent );
		lightGo.WorldPosition = parent.WorldPosition + localPos;
		var light = lightGo.AddComponent<PointLight>();
		light.LightColor = color;
		light.Radius = radius;
		light.Shadows = shadows;
		return lightGo;
	}

	// --- Build methods (spec §2a + §3) ---

	/// <summary> 3.1: Central plaza floor — large flat square at ground level. </summary>
	/// <summary> 3.0: Foundation — solid block under the market and walls,
	/// rising to bridge height so the whole market sits on a raised platform. </summary>
	void BuildFoundation( GameObject root )
	{
		// Foundation spans the wall area + margin, top above the moat water.
		// Extra ~16m (8m each side) to cover wall thickness and corner towers.
		float foundHalf = WallOuterHalfWidth + 8f * M;
		float foundTopZ = 2.5f * M;  // above moat water (1.5m) and bridge (1.6m)
		float foundThick = foundTopZ;  // from z=0 to top
		float foundCenterZ = foundThick * 0.5f;

		var foundation = CreatePrimitive( root, "MarketFoundation", "models/dev/box.vmdl",
			new Vector3( 0, 0, foundCenterZ ), Rotation.Identity,
			new Vector3( foundHalf * 2f / 50f, foundHalf * 2f / 50f, foundThick / 50f ),
			material: MatStoneWall );
		AddBoxCollider( foundation, new Vector3( 50f, 50f, 50f ) );
	}

	/// <summary> 3.1: Plaza floor — flat surface on top of the foundation. </summary>
	void BuildPlazaFloor( GameObject root )
	{
		// Use a thin box instead of plane_large (single-sided plane) to ensure
		// the floor renders from all angles and doesn't z-fight with the
		// foundation top. Box base = 50×50×50, scale to 2×PlazaHalfWidth.
		float plazaScale = (PlazaHalfWidth * 2f) / 50f;
		var floor = CreatePrimitive( root, "PlazaFloor", "models/dev/box.vmdl",
			new Vector3( 0, 0, 2.5f * M + 1f ), Rotation.Identity,
			new Vector3( plazaScale, plazaScale, 0.02f ),
			material: MatPlaza );
		AddBoxCollider( floor, new Vector3( PlazaHalfWidth * 2f, PlazaHalfWidth * 2f, 1f ) );
	}

	/// <summary> 3.1: Central well/fountain — stone rim, water surface, wooden roof posts. </summary>
	void BuildCentralWell( GameObject root )
	{
		float wellRadius = 4f * M;
		float rimH = 1.2f * M;
		float rimThick = 0.6f * M;
		float plazaTopZ = 2.5f * M + 1f;  // top of plaza floor — well sits on this

		// Stone rim — outer ring (hollow cylinder approximated as a short, wide box ring)
		// Use a slightly squashed sphere for the rim base, then a water disc inside.
		float rimSc = wellRadius / 32f;
		CreatePrimitive( root, "WellRim", "models/dev/sphere.vmdl",
			new Vector3( 0, 0, plazaTopZ + rimH * 0.5f ), Rotation.Identity,
			new Vector3( rimSc, rimSc, rimH / 64f ),
			material: MatStoneDetail );

		// Water surface (dark disc at rim top)
		float waterSc = (wellRadius - rimThick) / 32f;
		CreatePrimitive( root, "WellWater", "models/dev/sphere.vmdl",
			new Vector3( 0, 0, plazaTopZ + rimH * 0.9f ), Rotation.Identity,
			new Vector3( waterSc, waterSc, 0.02f ),
			material: MatWater );

		// 4 wooden roof posts at cardinal points around the rim
		float postH = 4f * M;
		float postW = 0.3f * M;
		float postSc = postW / 50f;
		float postOffset = wellRadius + rimThick * 0.5f;
		var postPositions = new[] {
			new Vector3(  postOffset, 0, plazaTopZ + postH * 0.5f ),
			new Vector3( -postOffset, 0, plazaTopZ + postH * 0.5f ),
			new Vector3( 0,  postOffset, plazaTopZ + postH * 0.5f ),
			new Vector3( 0, -postOffset, plazaTopZ + postH * 0.5f ),
		};
		for ( int i = 0; i < 4; i++ )
		{
			CreatePrimitive( root, $"WellPost_{i}",
				"models/dev/box.vmdl",
				postPositions[i], Rotation.Identity,
				new Vector3( postSc, postSc, postH / 50f ),
				material: MatWood );
		}

		// Roof — flat square covering the well, resting on posts
		float roofW = (wellRadius + rimThick) * 2.2f;
		float roofT = 0.4f * M;
		CreatePrimitive( root, "WellRoof",
			"models/dev/box.vmdl",
			new Vector3( 0, 0, plazaTopZ + postH + roofT * 0.5f ), Rotation.Identity,
			new Vector3( roofW / 50f, roofW / 50f, roofT / 50f ),
			material: MatRoof );
	}

	/// <summary> 3.1: Market stalls arranged in 4 quadrants with color-coded tilted awnings. </summary>
	void BuildMarketStalls( GameObject root )
	{
		var awningColors = new[] {
			new Color( 0.8f, 0.2f, 0.2f ),  // NW: red (general goods)
			new Color( 0.2f, 0.4f, 0.8f ),  // NE: blue (weapons/armor)
			new Color( 0.2f, 0.7f, 0.3f ),  // SE: green (food)
			new Color( 0.9f, 0.9f, 0.9f ),  // SW: white (exotic/rare)
		};

		// 4 stalls per quadrant = 16 total. Each = counter + 2 posts + tilted awning.
		float stallSpacing = 12f * M;
		float stallW = 3f * M;
		float stallD = 1.5f * M;
		float stallH = 1.2f * M;
		float postH = 2.8f * M;
		float postW = 0.15f * M;
		float awningTilt = 12f; // degrees of pitch for rain runoff
		float plazaTopZ = 2.5f * M + 1f;  // top of plaza floor
		for ( int quad = 0; quad < 4; quad++ )
		{
			// Quadrant offset signs
			float sx = (quad == 0 || quad == 3) ? -1f : 1f;  // west/east
			float sy = (quad == 0 || quad == 1) ? 1f : -1f;  // north/south

			var quadColor = awningColors[quad];

			for ( int i = 0; i < 4; i++ )
			{
				int row = i / 2;
				int col = i % 2;
				float x = sx * (15f * M + col * stallSpacing);
				float y = sy * (15f * M + row * stallSpacing);

				// Per-stall variation: counter height jitter and awning color variation
				float thisStallH = stallH + Jitter( 0.1f * M );
				var thisAwningColor = TintJitter( quadColor, 0.12f );

				// Counter (wood)
				var counter = CreatePrimitive( root, $"Stall_{quad}_{i}_Counter",
					"models/dev/box.vmdl",
					new Vector3( x, y, plazaTopZ + thisStallH * 0.5f ), Rotation.Identity,
					new Vector3( stallW / 50f, stallD / 50f, thisStallH / 50f ),
					material: MatWood, tint: TintJitter( new Color( 0.7f, 0.5f, 0.3f ), 0.05f ) );
				AddBoxCollider( counter, new Vector3( 50f, 50f, 50f ) );

				// 2 vertical support posts at front corners
				float postSc = postW / 50f;
				float postFwd = stallD * 0.45f;
				var postPositions = new[] {
					new Vector3( x + stallW * 0.4f, y + postFwd, plazaTopZ + postH * 0.5f ),
					new Vector3( x - stallW * 0.4f, y + postFwd, plazaTopZ + postH * 0.5f ),
				};
				for ( int p = 0; p < 2; p++ )
				{
					CreatePrimitive( root, $"Stall_{quad}_{i}_Post{p}",
						"models/dev/box.vmdl",
						postPositions[p], Rotation.Identity,
						new Vector3( postSc, postSc, postH / 50f ),
						material: MatWood );
				}

				// Tilted awning — pitched toward the back (away from customer)
				// Tilt around the X axis so the front is higher than the back.
				var awningRot = new Angles( awningTilt, 0, 0 );
				CreatePrimitive( root, $"Stall_{quad}_{i}_Awning",
					"models/dev/box.vmdl",
					new Vector3( x, y, plazaTopZ + postH ), awningRot,
					new Vector3( stallW / 50f, stallD / 50f, 0.1f ),
					material: MatRoof, tint: thisAwningColor );
			}
		}
	}

	/// <summary> 3.2: Inner ring floor — annular square between plaza and wall. </summary>
	void BuildInnerRingFloor( GameObject root )
	{
		float ringWidth = InnerRingOuter - PlazaHalfWidth;
		float midRadius = (PlazaHalfWidth + InnerRingOuter) * 0.5f;
		float segLen = InnerRingOuter * 2f;
		float segScale = segLen / 50f;
		float ringScale = ringWidth / 50f;

		for ( int side = 0; side < 4; side++ )
		{
			var pos = EdgeCenter( midRadius, side ) + new Vector3( 0, 0, 2.5f * M + 1f );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );
			var seg = CreatePrimitive( root, $"InnerRingFloor_{side}",
				"models/dev/box.vmdl",
				pos, rot, new Vector3( segScale, ringScale, 0.02f ),
				material: MatPlaza );
			AddBoxCollider( seg, new Vector3( 50f, 50f, 50f ) );
		}
	}

	/// <summary> 3.2: Crafting stations (8 total, 2 per side). </summary>
	void BuildCraftingStations( GameObject root )
	{
		float midRadius = (PlazaHalfWidth + InnerRingOuter) * 0.5f;
		float benchW = 3f * M;
		float benchD = 1.5f * M;
		float benchH = 1f * M;
		float legW = 0.15f * M;
		float plazaTopZ = 2.5f * M + 1f;  // top of plaza floor

		// Positions along each edge at 1/3 and 2/3 offsets
		float[] offsets = { -InnerRingOuter / 3f, InnerRingOuter / 3f };

		int idx = 0;
		for ( int side = 0; side < 4; side++ )
		{
			var edgeMid = EdgeCenter( midRadius, side );
			// Perpendicular direction along the edge
			float yaw = EdgeYaw( side );
			var along = new Angles( 0, yaw, 0 ).ToRotation();

			foreach ( float offset in offsets )
			{
				// Offset along the edge direction
				var offsetVec = side == 0 || side == 2
					? new Vector3( offset, 0, 0 )   // N/S edges: offset along X
					: new Vector3( 0, offset, 0 );  // E/W edges: offset along Y

				var benchPos = edgeMid + offsetVec;

				// Bench top (wood)
				var bench = CreatePrimitive( root, $"Workbench_{idx}",
					"models/dev/box.vmdl",
					benchPos + new Vector3( 0, 0, plazaTopZ + benchH ),
					Rotation.Identity,
					new Vector3( benchW / 50f, benchD / 50f, 0.3f / 50f * 50f ),
					material: MatWood );
				AddBoxCollider( bench, new Vector3( 50f, 50f, 50f ) );

				// 4 legs (simplified — just 4 thin boxes)
				float legH = benchH;
				float legScale = legW / 50f;
				float legZ = plazaTopZ + legH * 0.5f;
				var legOffsets = new[] {
					new Vector3( benchW * 0.4f, benchD * 0.3f, 0 ),
					new Vector3( -benchW * 0.4f, benchD * 0.3f, 0 ),
					new Vector3( benchW * 0.4f, -benchD * 0.3f, 0 ),
					new Vector3( -benchW * 0.4f, -benchD * 0.3f, 0 ),
				};
				for ( int leg = 0; leg < 4; leg++ )
				{
					CreatePrimitive( root, $"Workbench_{idx}_Leg{leg}",
						"models/dev/box.vmdl",
						benchPos + legOffsets[leg] + new Vector3( 0, 0, legZ ),
						Rotation.Identity,
						new Vector3( legScale, legScale, legH / 50f ),
						material: MatWood );
				}
				idx++;
			}
		}
	}

	/// <summary> 3.2: NPC housing modules (8 total, between crafting stations). </summary>
	void BuildNPCHousing( GameObject root )
	{
		float midRadius = (PlazaHalfWidth + InnerRingOuter) * 0.5f;
		float houseW = 6f * M;
		float houseH = 4f * M;
		float houseScale = houseW / 50f;
		float plazaTopZ = 2.5f * M + 1f;  // top of plaza floor

		// Offset from crafting stations — place at 0 and 1/2 offsets
		float[] offsets = { -InnerRingOuter / 2f, 0f };

		int idx = 0;
		for ( int side = 0; side < 4; side++ )
		{
			var edgeMid = EdgeCenter( midRadius, side );

			foreach ( float offset in offsets )
			{
				var offsetVec = side == 0 || side == 2
					? new Vector3( offset, 0, 0 )
					: new Vector3( 0, offset, 0 );

				var house = CreatePrimitive( root, $"House_{idx}",
					"models/dev/box.vmdl",
					edgeMid + offsetVec + new Vector3( 0, 0, plazaTopZ + houseH * 0.5f ),
					Rotation.Identity,
					new Vector3( houseScale, houseScale, houseH / 50f ),
					material: MatWoodHouse );
				AddBoxCollider( house, new Vector3( 50f, 50f, 50f ) );
				idx++;
			}
		}
	}

	/// <summary> 3.3: Curtain wall — 4 straight segments + 4 corner blocks. </summary>
	void BuildCurtainWall( GameObject root )
	{
		float wallThickness = 2f * M;
		float segLen = WallOuterHalfWidth * 2f;
		float cornerSize = 6f * M;

		// 4 wall segments (one per edge)
		for ( int side = 0; side < 4; side++ )
		{
			var pos = EdgeCenter( WallOuterHalfWidth, side ) + new Vector3( 0, 0, 2.5f * M + WallHeight * 0.5f );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );

			// Skip the center portion of each edge — the gatehouse fills that gap.
			// Build two half-walls per edge with a gate gap in the middle.
			// Shorten each half by cornerSize/2 so walls end at the corner's
			// inner edge instead of its center (prevents overlap/z-fighting).
			float gateGap = 6f * M;  // gate tunnel width
			float halfLen = (segLen - gateGap - cornerSize) * 0.5f;

			for ( int half = 0; half < 2; half++ )
			{
				float along = (half == 0 ? -1f : 1f) * (halfLen * 0.5f + gateGap * 0.5f);
				var offsetVec = side == 0 || side == 2
					? new Vector3( along, 0, 0 )
					: new Vector3( 0, along, 0 );

				// Per-segment variation: slight height jitter and tint
				float wallH = WallHeight + Jitter( 0.3f * M );
				var wallTint = TintJitter( new Color( 0.85f, 0.82f, 0.75f ), 0.06f );

				var wall = CreatePrimitive( root, $"Wall_{side}_{half}",
					"models/dev/box.vmdl",
					pos + offsetVec + new Vector3( 0, 0, (wallH - WallHeight) * 0.5f ), rot,
					new Vector3( halfLen / 50f, wallThickness / 50f, wallH / 50f ),
					material: MatStoneWall, tint: wallTint );
				AddBoxCollider( wall, new Vector3( 50f, 50f, 50f ) );
			}
		}

		// 4 corner blocks — slightly taller than walls, with tint variation
		float cornerScale = cornerSize / 50f;
		for ( int corner = 0; corner < 4; corner++ )
		{
			float cornerH = WallHeight + Jitter( 0.5f * M );
			var pos = CornerPos( WallOuterHalfWidth, corner ) + new Vector3( 0, 0, 2.5f * M + cornerH * 0.5f );
			var cornerGo = CreatePrimitive( root, $"WallCorner_{corner}",
				"models/dev/box.vmdl",
				pos, Rotation.Identity,
				new Vector3( cornerScale, cornerScale, cornerH / 50f ),
				material: MatStoneWall, tint: TintJitter( new Color( 0.8f, 0.77f, 0.7f ), 0.06f ) );
			AddBoxCollider( cornerGo, new Vector3( 50f, 50f, 50f ) );
		}
	}

	/// <summary> 3.5: Gatehouses — 4 gate tunnels with ceiling, side walls, portcullis bars, and gate frame. </summary>
	void BuildGatehouses( GameObject root )
	{
		float wallThickness = 2f * M;
		float gateGap = 6f * M;  // matches the gap left in BuildCurtainWall
		float gateHouseDepth = wallThickness * 2f;  // deeper than the wall
		float ceilingH = WallHeight;
		float ceilingThickness = 1f * M;

		for ( int side = 0; side < 4; side++ )
		{
			var pos = EdgeCenter( WallOuterHalfWidth, side ) + new Vector3( 0, 0, 2.5f * M );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );

			// Ceiling (murder hole slab above the gate tunnel)
			CreatePrimitive( root, $"GateCeiling_{side}",
				"models/dev/box.vmdl",
				pos + new Vector3( 0, 0, ceilingH + ceilingThickness * 0.5f ),
				rot,
				new Vector3( gateGap / 50f, gateHouseDepth / 50f, ceilingThickness / 50f ),
				material: MatStoneWall );

			// Gate frame — 2 vertical jambs + a lintel across the top of the opening
			float jambW = 0.8f * M;
			float jambH = ceilingH;
			float lintelH = 1f * M;
			// Jambs flank the gate opening along the wall length (not the
			// tunnel depth). Use direct world-space offsets — same pattern
			// as BuildCurtainWall — to avoid the yaw-rotation placement bug.
			bool isNSJamb = (side == 0 || side == 2);
			float jambAlong = gateGap * 0.5f + jambW * 0.5f;
			var jambOffsets = new[] {
				isNSJamb
					? new Vector3(  jambAlong, 0, jambH * 0.5f )
					: new Vector3( 0,  jambAlong, jambH * 0.5f ),
				isNSJamb
					? new Vector3( -jambAlong, 0, jambH * 0.5f )
					: new Vector3( 0, -jambAlong, jambH * 0.5f ),
			};
			for ( int j = 0; j < 2; j++ )
			{
				CreatePrimitive( root, $"GateJamb_{side}_{j}",
					"models/dev/box.vmdl",
					pos + jambOffsets[j], rot,
					new Vector3( jambW / 50f, jambW / 50f, jambH / 50f ),
					material: MatStoneWall );
			}
			// Lintel
			CreatePrimitive( root, $"GateLintel_{side}",
				"models/dev/box.vmdl",
				pos + new Vector3( 0, 0, ceilingH + lintelH * 0.5f ), rot,
				new Vector3( (gateGap + jambW * 2f) / 50f, jambW / 50f, lintelH / 50f ),
				material: MatStoneWall );

			// Portcullis — a grid of vertical and horizontal metal bars filling the upper half of the gate
			float barW = 0.2f * M;
			float barSc = barW / 50f;
			float grillH = ceilingH * 0.5f;
			float grillZ = ceilingH * 0.75f;
			int numVerticals = 6;
			int numHorizontals = 3;
			var metalTint = new Color( 0.4f, 0.4f, 0.42f );
			bool isNSGate = (side == 0 || side == 2);  // N/S gates: opening runs along X; E/W gates: along Y

			for ( int v = 0; v < numVerticals; v++ )
			{
				float offset = -gateGap * 0.45f + v * (gateGap * 0.9f / (numVerticals - 1));
				// Space vertical bars across the gate opening (along the wall direction)
				var worldOff = isNSGate
					? new Vector3( offset, 0, grillZ )
					: new Vector3( 0, offset, grillZ );
				CreatePrimitive( root, $"PortcullisV_{side}_{v}",
					"models/dev/box.vmdl",
					pos + worldOff, rot,
					new Vector3( barSc, barSc, grillH / 50f ),
					material: MatMetal, tint: metalTint );
			}
			for ( int h = 0; h < numHorizontals; h++ )
			{
				float z = ceilingH * 0.55f + h * (grillH * 0.4f / (numHorizontals - 1));
				// Horizontal bars span across the gate opening
				var hScale = isNSGate
					? new Vector3( gateGap * 0.9f / 50f, barSc, barSc )
					: new Vector3( barSc, gateGap * 0.9f / 50f, barSc );
				CreatePrimitive( root, $"PortcullisH_{side}_{h}",
					"models/dev/box.vmdl",
					pos + new Vector3( 0, 0, z ), rot,
					hScale,
					material: MatMetal, tint: metalTint );
			}
		}
	}

	/// <summary> 3.5: Bridges — 4 spanning the moat to the wall. </summary>
	void BuildBridges( GameObject root )
	{
		float bridgeLen = MoatOuterHalfWidth - WallOuterHalfWidth;
		float bridgeW = 6f * M;
		float bridgeThick = 1f * M;
		float midRadius = (WallOuterHalfWidth + MoatOuterHalfWidth) * 0.5f;
		// Moat water top is at z=1.5m. Bridge sits just above it at z=1.6m.
		float bridgeZ = 1.6f * M;

		for ( int side = 0; side < 4; side++ )
		{
			var pos = EdgeCenter( midRadius, side ) + new Vector3( 0, 0, bridgeZ + bridgeThick * 0.5f );
			// +90° yaw so the bridge length spans across the moat (radial),
			// not along the wall (tangential).
			var rot = new Angles( 0, EdgeYaw( side ) + 90f, 0 );

			var bridge = CreatePrimitive( root, $"Bridge_{side}",
				"models/dev/box.vmdl",
				pos, rot,
				new Vector3( bridgeLen / 50f, bridgeW / 50f, bridgeThick / 50f ),
				material: MatPlaza );
			AddBoxCollider( bridge, new Vector3( 50f, 50f, 50f ) );
		}
	}

	/// <summary> 3.4: Moat — raised water channel (solid box with water material).
	/// The basin is raised above ground (z=0) to avoid z-fighting with the
	/// WorldGround box. The top surface IS the water — no separate plane. </summary>
	void BuildMoat( GameObject root )
	{
		float moatWidth = MoatOuterHalfWidth - WallOuterHalfWidth;
		float segLen = MoatOuterHalfWidth * 2f;
		float midRadius = (WallOuterHalfWidth + MoatOuterHalfWidth) * 0.5f;

		// Basin extends from -MoatDepth to +1.5m above ground.
		// Top surface (the "water") is at +1.5m — well clear of ground at z=0.
		float basinTopZ = 1.5f * M;
		float basinH = MoatDepth + basinTopZ;
		float basinCenterZ = -MoatDepth + basinH * 0.5f;

		// 4 straight segments — solid box with water material (top = water surface)
		for ( int side = 0; side < 4; side++ )
		{
			var pos = EdgeCenter( midRadius, side ) + new Vector3( 0, 0, basinCenterZ );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );

			CreatePrimitive( root, $"Moat_{side}",
				"models/dev/box.vmdl",
				pos, rot,
				new Vector3( segLen / 50f, moatWidth / 50f, basinH / 50f ),
				material: MatWater );
		}

		// 4 corners
		float cornerSize = moatWidth;
		float cornerScale = cornerSize / 50f;
		for ( int corner = 0; corner < 4; corner++ )
		{
			var pos = CornerPos( midRadius, corner ) + new Vector3( 0, 0, basinCenterZ );
			CreatePrimitive( root, $"MoatCorner_{corner}",
				"models/dev/box.vmdl",
				pos, Rotation.Identity,
				new Vector3( cornerScale, cornerScale, basinH / 50f ),
				material: MatWater );
		}
	}

	/// <summary> 3.5: Market terrain — local terrain patch with a berm ring
	/// surrounding the moat. Ground rises gradually from flat terrain to a
	/// ~3ft peak just outside the moat, then drops steeply into the moat
	/// channel. This hides the floating moat box and blends the transition
	/// from the 3km WorldGround box to the market area. </summary>
	void BuildMarketTerrain( GameObject root )
	{
		// Terrain patch: 500m square, centered on the market.
		float terrainSizeM = 500f;
		float terrainSize = terrainSizeM * M;
		int res = 512;

		// TerrainHeight: 5m gives enough range for 3ft berm + moat depth.
		float terrainHeightM = 5f;
		float terrainHeight = terrainHeightM * M;

		// Base of the terrain sits 1m below ground (z = -1m) so the
		// heightmap can represent both the moat bottom (z=-3m) and the
		// berm peak (z=+0.91m). Ground level (z=0) is at heightmap 1/5.
		float baseZ = -1f * M;

		var go = Scene.CreateObject( true );
		go.Name = "MarketTerrain";
		// Don't parent to root — terrain has its own transform offset.
		// Position at terrain corner so the square is centered on the market.
		go.WorldPosition = Center + new Vector3( -terrainSize * 0.5f, -terrainSize * 0.5f, baseZ );

		var terrain = go.AddComponent<Terrain>();
		terrain.Enabled = false; // force a clean enable cycle
		terrain.Enabled = true;  // OnEnabled() -> Create()

		var storage = new TerrainStorage();
		storage.SetResolution( res );
		storage.TerrainSize = terrainSize;
		storage.TerrainHeight = terrainHeight;

		GenerateBermHeightmap( storage, res, terrainSize, terrainHeight );

		terrain.Storage = storage; // triggers Create() internally
		terrain.Create();         // ensure Create runs
		terrain.SyncGPUTexture();
		terrain.UpdateCollision(
			Terrain.SyncFlags.Height,
			new RectInt( 0, 0, res, res ) );

		Log.Info( "Lute: market terrain berm generated." );

		// Diagnostic: sample a few heightmap values to verify they're written.
		// bermOuterM=200m, worldPerTexel=terrainSize/res=19685/512=38.4 units/texel
		// 200m = 7874 units, so edgeOffset = 7874/38.4 = ~205 texels from center
		int midX = res / 2;
		int midY = res / 2;
		int moatOffset = 175;  // ~170m
		int midBerm = 190;     // ~185m (middle of berm)
		int edgeOffset = 205;  // ~200m (outer edge)
		Log.Info( $"Lute: terrain diag — center={storage.HeightMap[midY * res + midX]}, " +
			$"moatEdge={storage.HeightMap[midY * res + (midX + moatOffset)]}, " +
			$"midBerm={storage.HeightMap[midY * res + (midX + midBerm)]}, " +
			$"bermOuter={storage.HeightMap[midY * res + (midX + edgeOffset)]}, " +
			$"arrayLen={storage.HeightMap.Length}" );
	}

	/// <summary>
	/// Fills the terrain heightmap with a flat surface everywhere except
	/// a berm ring just outside the moat. The berm rises gradually from
	/// flat ground to ~3ft at the moat edge, then drops steeply into the
	/// moat channel.
	/// </summary>
	void GenerateBermHeightmap( TerrainStorage storage, int res, float terrainSize, float terrainHeight )
	{
		// Heights in meters (relative to z=0 ground level).
		const float groundHeightM = 1f;       // z=0 (base is at z=-1m, so 1m up)
		const float bermPeakM = 3f;           // z=2m (~6.5ft above ground)

		// Distances in meters from market center.
		const float moatOuterM = 170f;        // MoatOuterHalfWidth
		const float bermWidthM = 30f;         // berm extends 30m outside moat
		const float bermOuterM = moatOuterM + bermWidthM; // 200m

		float halfSize = terrainSize * 0.5f;
		float worldPerTexel = terrainSize / res;
		float heightScale = (float)ushort.MaxValue;
		float metersPerHeightUnit = terrainHeight / M; // meters per full ushort range

		for ( int y = 0; y < res; y++ )
		{
			for ( int x = 0; x < res; x++ )
			{
				// World position relative to terrain center (which is at market center).
				float wx = x * worldPerTexel - halfSize;
				float wy = y * worldPerTexel - halfSize;

				// Distance from market center in meters (square/Chebyshev —
				// follows the square moat shape, not circular).
				float dxM = MathF.Abs( wx / M );
				float dyM = MathF.Abs( wy / M );
				float distM = MathF.Max( dxM, dyM );

				float heightM;

				if ( distM <= moatOuterM )
				{
					// Inside the moat — flat at ground level (moat box sits on top).
					heightM = groundHeightM;
				}
				else if ( distM >= bermOuterM )
				{
					// Flat ground far from the market.
					heightM = groundHeightM;
				}
				else
				{
					// Berm zone: moatOuterM < distM < bermOuterM.
					// t goes from 0 (at moat edge, peak) to 1 (at berm outer edge, ground).
					float t = (distM - moatOuterM) / bermWidthM;

					// Smooth ease-in-out for a gradual descent from peak to flat.
					float smooth = t * t * (3f - 2f * t);

					// Peak at moat edge (t=0), ground at outer edge (t=1).
					heightM = bermPeakM + (groundHeightM - bermPeakM) * smooth;
				}

				// Convert meters to ushort: heightM is in meters above the
				// terrain base (z=-1m). ushort.MaxValue = terrainHeight (inches).
				float normalized = heightM * M / terrainHeight;
				normalized = normalized.Clamp( 0f, 1f );
				storage.HeightMap[y * res + x] = (ushort)( normalized * heightScale );
			}
		}
	}

	/// <summary> 3.6: Watchtowers — 8 total, flanking each gate. </summary>
	void BuildWatchtowers( GameObject root )
	{
		float towerSize = 6f * M;
		float towerH = 18f * M;  // 1.5× wall height
		float towerScale = towerSize / 50f;
		float towerOffset = 10f * M;  // distance from gate center along the wall

		var warmLight = new Color( 0.9f, 0.6f, 0.3f );

		for ( int side = 0; side < 4; side++ )
		{
			var gatePos = EdgeCenter( WallOuterHalfWidth, side );

			// Two towers flanking the gate, offset along the edge
			for ( int t = 0; t < 2; t++ )
			{
				float along = (t == 0 ? -1f : 1f) * towerOffset;
				var offsetVec = side == 0 || side == 2
					? new Vector3( along, 0, 0 )
					: new Vector3( 0, along, 0 );

				// Per-tower variation: height jitter (±1m) and tint
				float thisTowerH = towerH + Jitter( 1f * M );
				var towerPos = gatePos + offsetVec + new Vector3( 0, 0, 2.5f * M + thisTowerH * 0.5f );

				var tower = CreatePrimitive( root, $"Tower_{side}_{t}",
					"models/dev/box.vmdl",
					towerPos, Rotation.Identity,
					new Vector3( towerScale, towerScale, thisTowerH / 50f ),
					material: MatStoneTower, tint: TintJitter( new Color( 0.82f, 0.78f, 0.7f ), 0.07f ) );
				AddBoxCollider( tower, new Vector3( 50f, 50f, 50f ) );

				// Guard torch light at tower top
				AddPointLight( root, $"TowerLight_{side}_{t}",
					towerPos + new Vector3( 0, 0, thisTowerH * 0.5f ),
					warmLight, 400f );
			}
		}
	}

	/// <summary> Market lighting — warm lanterns in the plaza, torches at gates and well. </summary>
	void BuildLighting( GameObject root )
	{
		var warmLight = new Color( 1.0f, 0.7f, 0.35f );
		var torchLight = new Color( 1.0f, 0.55f, 0.2f );
		float lightHeight = 5f * M;
		float lightOffset = 30f * M;

		// 4 lanterns, one per plaza quadrant — with shadows for atmosphere
		var positions = new[] {
			new Vector3( -lightOffset, lightOffset, lightHeight ),   // NW
			new Vector3( lightOffset, lightOffset, lightHeight ),    // NE
			new Vector3( lightOffset, -lightOffset, lightHeight ),   // SE
			new Vector3( -lightOffset, -lightOffset, lightHeight ),  // SW
		};

		for ( int i = 0; i < 4; i++ )
		{
			AddPointLight( root, $"PlazaLight_{i}", positions[i], warmLight, 800f, shadows: true );
		}

		// Central well light — warm glow from the well area
		AddPointLight( root, "WellLight",
			new Vector3( 0, 0, 3f * M ), torchLight, 500f );

		// Gate torches — warm light at each gate entrance
		for ( int side = 0; side < 4; side++ )
		{
			var gatePos = EdgeCenter( WallOuterHalfWidth, side );
			AddPointLight( root, $"GateTorch_{side}",
				gatePos + new Vector3( 0, 0, WallHeight * 0.7f ),
				torchLight, 600f, shadows: true );
		}
	}

	/// <summary> Crenellations (merlons) along the top of the curtain wall — alternating solid blocks and gaps. </summary>
	void BuildBattlements( GameObject root )
	{
		float merlonW = 1.5f * M;   // width of each solid block (along the wall edge)
		float merlonH = 1.5f * M;   // height above the wall top
		float merlonD = 2f * M;     // depth (wall thickness)
		float gap = 1.5f * M;       // gap between merlons (crenel)
		float segLen = WallOuterHalfWidth * 2f;
		float gateGap = 6f * M;
		float cornerSize = 6f * M;
		float halfLen = (segLen - gateGap - cornerSize) * 0.5f;
		var stoneTint = new Color( 0.7f, 0.65f, 0.58f );

		for ( int side = 0; side < 4; side++ )
		{
			var edgeMid = EdgeCenter( WallOuterHalfWidth, side ) + new Vector3( 0, 0, 2.5f * M + WallHeight + merlonH * 0.5f );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );
			bool isNS = (side == 0 || side == 2);  // N/S walls run along X; E/W walls run along Y

			// Two half-walls per edge (matching BuildCurtainWall's gate gap)
			for ( int half = 0; half < 2; half++ )
			{
				float alongStart = (half == 0 ? -1f : 1f) * (gateGap * 0.5f);
				float alongEnd = alongStart + (half == 0 ? -1f : 1f) * halfLen;

				// Walk along the half-wall placing merlons + gaps
				float cursor = alongStart;
				while ( (half == 0 && cursor > alongEnd) || (half == 1 && cursor < alongEnd) )
				{
					// Merlon center along the wall edge direction
					float merlonCenter = cursor + (half == 0 ? -merlonW * 0.5f : merlonW * 0.5f);
					// Direct world-space offset (same pattern as BuildCurtainWall)
					var worldOff = isNS
						? new Vector3( merlonCenter, 0, 0 )
						: new Vector3( 0, merlonCenter, 0 );

					// Merlon scale: depth (across wall thickness) × width (along wall) × height
					// For N/S walls: X=along wall (merlonW), Y=depth (merlonD)
					// For E/W walls: X=depth (merlonD), Y=along wall (merlonW)
					var merlonScale = isNS
						? new Vector3( merlonW / 50f, merlonD / 50f, merlonH / 50f )
						: new Vector3( merlonD / 50f, merlonW / 50f, merlonH / 50f );

					CreatePrimitive( root, $"Merlon_{side}_{half}_{(int)(cursor * 10)}",
						"models/dev/box.vmdl",
						edgeMid + worldOff, rot,
						merlonScale,
						material: MatStoneDetail, tint: stoneTint );

					cursor += (half == 0 ? -1f : 1f) * (merlonW + gap);
				}
			}
		}

		// Corner merlons (4 corners, slightly larger)
		float cornerMerlonW = 2f * M;
		for ( int corner = 0; corner < 4; corner++ )
		{
			var pos = CornerPos( WallOuterHalfWidth, corner ) + new Vector3( 0, 0, 2.5f * M + WallHeight + merlonH * 0.5f );
			CreatePrimitive( root, $"CornerMerlon_{corner}",
				"models/dev/box.vmdl",
				pos, Rotation.Identity,
				new Vector3( cornerMerlonW / 50f, cornerMerlonW / 50f, merlonH / 50f ),
				material: MatStoneDetail, tint: stoneTint );
		}
	}

	/// <summary> Crenellations on top of the 8 watchtowers — smaller merlons on each tower top. </summary>
	void BuildTowerBattlements( GameObject root )
	{
		float towerSize = 6f * M;
		float towerH = 18f * M;
		float towerOffset = 10f * M;
		float merlonH = 1.5f * M;
		float merlonW = 1f * M;
		var stoneTint = new Color( 0.7f, 0.65f, 0.58f );

		for ( int side = 0; side < 4; side++ )
		{
			var gatePos = EdgeCenter( WallOuterHalfWidth, side );

			for ( int t = 0; t < 2; t++ )
			{
				float along = (t == 0 ? -1f : 1f) * towerOffset;
				var offsetVec = side == 0 || side == 2
					? new Vector3( along, 0, 0 )
					: new Vector3( 0, along, 0 );

				var towerPos = gatePos + offsetVec + new Vector3( 0, 0, 2.5f * M + towerH + merlonH * 0.5f );

				// 4 merlons, one per side of the tower top
				var merlonOffsets = new[] {
					new Vector3( 0,  towerSize * 0.5f, 0 ),
					new Vector3( 0, -towerSize * 0.5f, 0 ),
					new Vector3(  towerSize * 0.5f, 0, 0 ),
					new Vector3( -towerSize * 0.5f, 0, 0 ),
				};
				var merlonScales = new[] {
					new Vector3( towerSize / 50f, merlonW / 50f, merlonH / 50f ),
					new Vector3( towerSize / 50f, merlonW / 50f, merlonH / 50f ),
					new Vector3( merlonW / 50f, towerSize / 50f, merlonH / 50f ),
					new Vector3( merlonW / 50f, towerSize / 50f, merlonH / 50f ),
				};

				for ( int m = 0; m < 4; m++ )
				{
					CreatePrimitive( root, $"TowerMerlon_{side}_{t}_{m}",
						"models/dev/box.vmdl",
						towerPos + merlonOffsets[m], Rotation.Identity,
						merlonScales[m],
						material: MatStoneDetail, tint: stoneTint );
				}
			}
		}
	}

	/// <summary> Visual details — tower banners, market goods, house doors. </summary>
	void BuildVisualDetails( GameObject root )
	{
		BuildTowerBanners( root );
		BuildMarketGoods( root );
		BuildHouseDoors( root );
	}

	/// <summary> Colored banners on poles atop each watchtower. </summary>
	void BuildTowerBanners( GameObject root )
	{
		var bannerColors = new[] {
			new Color( 0.8f, 0.15f, 0.15f ),  // N: red
			new Color( 0.15f, 0.3f, 0.8f ),   // E: blue
			new Color( 0.15f, 0.7f, 0.2f ),   // S: green
			new Color( 0.85f, 0.7f, 0.1f ),   // W: gold
		};

		float towerSize = 6f * M;
		float towerH = 18f * M;
		float towerOffset = 10f * M;
		float merlonH = 1.5f * M;

		for ( int side = 0; side < 4; side++ )
		{
			var gatePos = EdgeCenter( WallOuterHalfWidth, side );

			for ( int t = 0; t < 2; t++ )
			{
				float along = (t == 0 ? -1f : 1f) * towerOffset;
				var offsetVec = side == 0 || side == 2
					? new Vector3( along, 0, 0 )
					: new Vector3( 0, along, 0 );

				var towerTop = gatePos + offsetVec + new Vector3( 0, 0, towerH + merlonH );

				// Banner pole
				float poleH = 3f * M;
				float poleW = 0.15f * M;
				CreatePrimitive( root, $"BannerPole_{side}_{t}",
					"models/dev/box.vmdl",
					towerTop + new Vector3( 0, 0, poleH * 0.5f ), Rotation.Identity,
					new Vector3( poleW / 50f, poleW / 50f, poleH / 50f ),
					material: MatMetal );

				// Banner cloth — flat rectangle hanging from pole top
				float bannerW = 2f * M;
				float bannerH = 1.5f * M;
				CreatePrimitive( root, $"Banner_{side}_{t}",
					"models/dev/box.vmdl",
					towerTop + new Vector3( 0, 0, poleH - bannerH * 0.5f ),
					Rotation.Identity,
					new Vector3( 0.05f / 50f * 50f, bannerW / 50f, bannerH / 50f ),
					material: MatRoof, tint: bannerColors[side] );
			}
		}
	}

	/// <summary> Colored goods boxes on stall counters. </summary>
	void BuildMarketGoods( GameObject root )
	{
		var goodsColors = new[] {
			new Color( 0.7f, 0.5f, 0.2f ),   // amber goods
			new Color( 0.5f, 0.3f, 0.1f ),   // dark leather
			new Color( 0.8f, 0.7f, 0.5f ),   // wheat/grain
			new Color( 0.3f, 0.5f, 0.3f ),   // produce
		};

		float stallSpacing = 12f * M;
		float stallW = 3f * M;
		float stallH = 1.2f * M;
		float goodsW = 0.8f * M;
		float goodsH = 0.4f * M;
		float plazaTopZ = 2.5f * M + 1f;  // top of plaza floor

		for ( int quad = 0; quad < 4; quad++ )
		{
			float sx = (quad == 0 || quad == 3) ? -1f : 1f;
			float sy = (quad == 0 || quad == 1) ? 1f : -1f;

			for ( int i = 0; i < 4; i++ )
			{
				int row = i / 2;
				int col = i % 2;
				float x = sx * (15f * M + col * stallSpacing);
				float y = sy * (15f * M + row * stallSpacing);

				// 2 goods boxes per stall counter
				for ( int g = 0; g < 2; g++ )
				{
					float gx = x + (g == 0 ? -stallW * 0.2f : stallW * 0.2f);
					float gy = y;
					float gz = plazaTopZ + stallH + goodsH * 0.5f;

					CreatePrimitive( root, $"Goods_{quad}_{i}_{g}",
						"models/dev/box.vmdl",
						new Vector3( gx, gy, gz ), Rotation.Identity,
						new Vector3( goodsW / 50f, goodsW / 50f, goodsH / 50f ),
						material: MatWood, tint: goodsColors[(quad + i + g) % goodsColors.Length] );
				}
			}
		}
	}

	/// <summary> Dark door openings on each house. </summary>
	void BuildHouseDoors( GameObject root )
	{
		float midRadius = (PlazaHalfWidth + InnerRingOuter) * 0.5f;
		float houseW = 6f * M;
		float houseH = 4f * M;
		float doorW = 1.2f * M;
		float doorH = 2.2f * M;
		float plazaTopZ = 2.5f * M + 1f;  // top of plaza floor

		float[] offsets = { -InnerRingOuter / 2f, 0f };

		int idx = 0;
		for ( int side = 0; side < 4; side++ )
		{
			var edgeMid = EdgeCenter( midRadius, side );

			foreach ( float offset in offsets )
			{
				var offsetVec = side == 0 || side == 2
					? new Vector3( offset, 0, 0 )
					: new Vector3( 0, offset, 0 );

				var housePos = edgeMid + offsetVec;

				// Door faces inward (toward plaza center)
				var inwardDir = side switch
				{
					0 => new Vector3( 0, -1, 0 ),  // N faces south
					1 => new Vector3( -1, 0, 0 ),  // E faces west
					2 => new Vector3( 0, 1, 0 ),   // S faces north
					_ => new Vector3( 1, 0, 0 ),   // W faces east
				};

				// Place door slightly inset from house center, facing plaza
				var doorPos = housePos + inwardDir * (houseW * 0.5f) + new Vector3( 0, 0, plazaTopZ + doorH * 0.5f );

				CreatePrimitive( root, $"Door_{idx}",
					"models/dev/box.vmdl",
					doorPos, Rotation.Identity,
					new Vector3( doorW / 50f, 0.1f / 50f * 50f, doorH / 50f ),
					material: "materials/dev/black_cheap.vmat",
					tint: new Color( 0.15f, 0.1f, 0.08f ) );

				idx++;
			}
		}
	}
}
