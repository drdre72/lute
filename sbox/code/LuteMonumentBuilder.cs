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

		BuildPlazaFloor( root );
		BuildCentralWell( root );
		BuildMarketStalls( root );
		BuildInnerRingFloor( root );
		BuildCraftingStations( root );
		BuildNPCHousing( root );
		BuildCurtainWall( root );
		BuildGatehouses( root );
		BuildBridges( root );
		BuildMoat( root );
		BuildWatchtowers( root );
		BuildLighting( root );

		Log.Info( "Lute: Neutral Market monument built (square whitebox)." );
		return root;
	}

	// --- Geometry helpers ---

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
	void BuildPlazaFloor( GameObject root )
	{
		// plane_large.vmdl base = 100000×100000. Scale to 2×PlazaHalfWidth.
		float s = (PlazaHalfWidth * 2f) / 100000f;
		var floor = CreatePrimitive( root, "PlazaFloor", "models/dev/plane_large.vmdl",
			Vector3.Zero, Rotation.Identity, new Vector3( s, s, 1f ),
			material: "materials/dev/gray_75.vmat" );
		AddBoxCollider( floor, new Vector3( 100000f, 100000f, 1f ) );
	}

	/// <summary> 3.1: Central well/fountain — visual anchor at dead center. </summary>
	void BuildCentralWell( GameObject root )
	{
		// sphere.vmdl base = 64 diameter. Scale to ~4m radius (~8m diameter).
		float wellRadius = 4f * M;
		float sc = wellRadius / 32f;
		CreatePrimitive( root, "CentralWell", "models/dev/sphere.vmdl",
			new Vector3( 0, 0, wellRadius * 0.3f ), Rotation.Identity,
			new Vector3( sc, sc, sc * 0.4f ),
			tint: new Color( 0.2f, 0.4f, 0.8f ) );
	}

	/// <summary> 3.1: Market stalls arranged in 4 quadrants with color-coded awnings. </summary>
	void BuildMarketStalls( GameObject root )
	{
		var awningColors = new[] {
			new Color( 0.8f, 0.2f, 0.2f ),  // NW: red (general goods)
			new Color( 0.2f, 0.4f, 0.8f ),  // NE: blue (weapons/armor)
			new Color( 0.2f, 0.7f, 0.3f ),  // SE: green (food)
			new Color( 0.9f, 0.9f, 0.9f ),  // SW: white (exotic/rare)
		};

		// 4 stalls per quadrant = 16 total. Each = counter + awning.
		float stallSpacing = 12f * M;
		float stallW = 3f * M;
		float stallD = 1.5f * M;
		float stallH = 1.2f * M;

		for ( int quad = 0; quad < 4; quad++ )
		{
			// Quadrant offset signs
			float sx = (quad == 0 || quad == 3) ? -1f : 1f;  // west/east
			float sy = (quad == 0 || quad == 1) ? 1f : -1f;  // north/south

			for ( int i = 0; i < 4; i++ )
			{
				int row = i / 2;
				int col = i % 2;
				float x = sx * (15f * M + col * stallSpacing);
				float y = sy * (15f * M + row * stallSpacing);

				// Counter
				float counterScale = new Vector3( stallW / 50f, stallD / 50f, stallH / 50f ).Length;
				var counter = CreatePrimitive( root, $"Stall_{quad}_{i}_Counter",
					"models/dev/box.vmdl",
					new Vector3( x, y, stallH * 0.5f ), Rotation.Identity,
					new Vector3( stallW / 50f, stallD / 50f, stallH / 50f ),
					material: "materials/dev/gray_50.vmat" );
				AddBoxCollider( counter, new Vector3( 50f, 50f, 50f ) );

				// Awning (thin tilted box above counter)
				float awningH = 2.8f * M;
				var awning = CreatePrimitive( root, $"Stall_{quad}_{i}_Awning",
					"models/dev/box.vmdl",
					new Vector3( x, y, awningH ), Rotation.Identity,
					new Vector3( stallW / 50f, stallD / 50f, 0.15f ),
					tint: awningColors[quad] );
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
			var pos = EdgeCenter( midRadius, side );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );
			var seg = CreatePrimitive( root, $"InnerRingFloor_{side}",
				"models/dev/box.vmdl",
				pos, rot, new Vector3( segScale, ringScale, 0.02f ),
				material: "materials/dev/gray_50.vmat" );
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

				// Bench top
				var bench = CreatePrimitive( root, $"Workbench_{idx}",
					"models/dev/box.vmdl",
					benchPos + new Vector3( 0, 0, benchH ),
					Rotation.Identity,
					new Vector3( benchW / 50f, benchD / 50f, 0.3f / 50f * 50f ),
					material: "materials/dev/gray_50.vmat" );
				AddBoxCollider( bench, new Vector3( 50f, 50f, 50f ) );

				// 4 legs (simplified — just 4 thin boxes)
				float legH = benchH;
				float legScale = legW / 50f;
				float legZ = legH * 0.5f;
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
						material: "materials/dev/gray_25.vmat" );
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
					edgeMid + offsetVec + new Vector3( 0, 0, houseH * 0.5f ),
					Rotation.Identity,
					new Vector3( houseScale, houseScale, houseH / 50f ),
					material: "materials/dev/gray_25.vmat" );
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

		// 4 wall segments (one per edge)
		for ( int side = 0; side < 4; side++ )
		{
			var pos = EdgeCenter( WallOuterHalfWidth, side ) + new Vector3( 0, 0, WallHeight * 0.5f );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );

			// Skip the center portion of each edge — the gatehouse fills that gap.
			// Build two half-walls per edge with a gate gap in the middle.
			float gateGap = 6f * M;  // gate tunnel width
			float halfLen = (segLen - gateGap) * 0.5f;

			for ( int half = 0; half < 2; half++ )
			{
				float along = (half == 0 ? -1f : 1f) * (halfLen * 0.5f + gateGap * 0.5f);
				var offsetVec = side == 0 || side == 2
					? new Vector3( along, 0, 0 )
					: new Vector3( 0, along, 0 );

				var wall = CreatePrimitive( root, $"Wall_{side}_{half}",
					"models/dev/box.vmdl",
					pos + offsetVec, rot,
					new Vector3( halfLen / 50f, wallThickness / 50f, WallHeight / 50f ),
					material: "materials/dev/gray_50.vmat" );
				AddBoxCollider( wall, new Vector3( 50f, 50f, 50f ) );
			}
		}

		// 4 corner blocks
		float cornerSize = 6f * M;
		float cornerScale = cornerSize / 50f;
		for ( int corner = 0; corner < 4; corner++ )
		{
			var pos = CornerPos( WallOuterHalfWidth, corner ) + new Vector3( 0, 0, WallHeight * 0.5f );
			var cornerGo = CreatePrimitive( root, $"WallCorner_{corner}",
				"models/dev/box.vmdl",
				pos, Rotation.Identity,
				new Vector3( cornerScale, cornerScale, WallHeight / 50f ),
				material: "materials/dev/gray_50.vmat" );
			AddBoxCollider( cornerGo, new Vector3( 50f, 50f, 50f ) );
		}
	}

	/// <summary> 3.5: Gatehouses — 4 gate tunnels with flanking boxes and ceiling. </summary>
	void BuildGatehouses( GameObject root )
	{
		float wallThickness = 2f * M;
		float gateGap = 6f * M;  // matches the gap left in BuildCurtainWall
		float gateHouseDepth = wallThickness * 2f;  // deeper than the wall
		float ceilingH = WallHeight;
		float ceilingThickness = 1f * M;

		for ( int side = 0; side < 4; side++ )
		{
			var pos = EdgeCenter( WallOuterHalfWidth, side ) + new Vector3( 0, 0, 0 );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );

			// Ceiling (murder hole slab above the gate tunnel)
			CreatePrimitive( root, $"GateCeiling_{side}",
				"models/dev/box.vmdl",
				pos + new Vector3( 0, 0, ceilingH + ceilingThickness * 0.5f ),
				rot,
				new Vector3( gateGap / 50f, gateHouseDepth / 50f, ceilingThickness / 50f ),
				material: "materials/dev/gray_25.vmat" );
		}
	}

	/// <summary> 3.5: Bridges — 4 spanning the moat to the wall. </summary>
	void BuildBridges( GameObject root )
	{
		float bridgeLen = MoatOuterHalfWidth - WallOuterHalfWidth;
		float bridgeW = 6f * M;
		float bridgeThick = 1f * M;
		float midRadius = (WallOuterHalfWidth + MoatOuterHalfWidth) * 0.5f;

		for ( int side = 0; side < 4; side++ )
		{
			var pos = EdgeCenter( midRadius, side ) + new Vector3( 0, 0, bridgeThick * 0.5f );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );

			var bridge = CreatePrimitive( root, $"Bridge_{side}",
				"models/dev/box.vmdl",
				pos, rot,
				new Vector3( bridgeLen / 50f, bridgeW / 50f, bridgeThick / 50f ),
				material: "materials/dev/gray_75.vmat" );
			AddBoxCollider( bridge, new Vector3( 50f, 50f, 50f ) );
		}
	}

	/// <summary> 3.4: Moat — 4 straight segments + 4 corners, sunk below ground. </summary>
	void BuildMoat( GameObject root )
	{
		float moatWidth = MoatOuterHalfWidth - WallOuterHalfWidth;
		float segLen = MoatOuterHalfWidth * 2f;
		float midRadius = (WallOuterHalfWidth + MoatOuterHalfWidth) * 0.5f;

		// 4 straight segments
		for ( int side = 0; side < 4; side++ )
		{
			var pos = EdgeCenter( midRadius, side ) + new Vector3( 0, 0, -MoatDepth * 0.5f );
			var rot = new Angles( 0, EdgeYaw( side ), 0 );

			CreatePrimitive( root, $"Moat_{side}",
				"models/dev/box.vmdl",
				pos, rot,
				new Vector3( segLen / 50f, moatWidth / 50f, MoatDepth / 50f ),
				material: "materials/dev/black_cheap.vmat" );
		}

		// 4 corners
		float cornerSize = moatWidth;
		float cornerScale = cornerSize / 50f;
		for ( int corner = 0; corner < 4; corner++ )
		{
			var pos = CornerPos( midRadius, corner ) + new Vector3( 0, 0, -MoatDepth * 0.5f );
			CreatePrimitive( root, $"MoatCorner_{corner}",
				"models/dev/box.vmdl",
				pos, Rotation.Identity,
				new Vector3( cornerScale, cornerScale, MoatDepth / 50f ),
				material: "materials/dev/black_cheap.vmat" );
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

				var towerPos = gatePos + offsetVec + new Vector3( 0, 0, towerH * 0.5f );

				var tower = CreatePrimitive( root, $"Tower_{side}_{t}",
					"models/dev/box.vmdl",
					towerPos, Rotation.Identity,
					new Vector3( towerScale, towerScale, towerH / 50f ),
					material: "materials/dev/gray_25.vmat" );
				AddBoxCollider( tower, new Vector3( 50f, 50f, 50f ) );

				// Guard torch light at tower top
				AddPointLight( root, $"TowerLight_{side}_{t}",
					towerPos + new Vector3( 0, 0, towerH * 0.5f ),
					warmLight, 400f );
			}
		}
	}

	/// <summary> Market lighting — warm lanterns in the plaza quadrants. </summary>
	void BuildLighting( GameObject root )
	{
		var warmLight = new Color( 0.9f, 0.6f, 0.3f );
		float lightHeight = 5f * M;
		float lightOffset = 30f * M;

		// 4 lanterns, one per plaza quadrant
		var positions = new[] {
			new Vector3( -lightOffset, lightOffset, lightHeight ),   // NW
			new Vector3( lightOffset, lightOffset, lightHeight ),    // NE
			new Vector3( lightOffset, -lightOffset, lightHeight ),   // SE
			new Vector3( -lightOffset, -lightOffset, lightHeight ),  // SW
		};

		for ( int i = 0; i < 4; i++ )
		{
			AddPointLight( root, $"PlazaLight_{i}", positions[i], warmLight, 600f );
		}
	}
}
