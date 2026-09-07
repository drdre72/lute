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
		var root = Scene.CreateObject( true );
		root.Name = "Sanctuary";
		root.WorldPosition = Vector3.Zero;

		// Procedural terrain first — the temple sits on top of it.
		var terrainGen = Components.GetOrCreate<LuteTerrainGenerator>();
		terrainGen.Build( root );

		BuildTempleFloor( root );
		BuildHexagonalWalls( root );
		BuildTimePortal( root );

		Log.Info( "Lute: Sanctuary built (terrain + portal + temple floor + hex chamber)." );
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
			AddBoxCollider( wall, new Vector3( 1f, 1f, 1f ), staticCollider: true );
		}
	}

	/// <summary>
	/// The temple floor — a large raised stone platform surrounding the portal,
	/// at the same height as the portal pad. This is the ground level of the
	/// Temple of Time. The default landscape plane sits slightly below this.
	/// </summary>
	void BuildTempleFloor( GameObject parent )
	{
		// plane_large.vmdl base = 100000x100000. Scale 0.05 = 5000x5000 units (~95m)
		var floor = CreatePrimitive( parent, "TempleFloor", "models/dev/plane_large.vmdl",
			new Vector3( 0, 0, 2 ), Scale: new Vector3( 0.05f, 0.05f, 1f ),
			material: "materials/dev/gray_50.vmat" );
		AddBoxCollider( floor, new Vector3( 1f, 1f, 1f ), staticCollider: true );

		// Outer ring trim — slightly larger, darker border
		var trim = CreatePrimitive( parent, "TempleFloorTrim", "models/dev/plane_large.vmdl",
			new Vector3( 0, 0, 0 ), Scale: new Vector3( 0.055f, 0.055f, 1f ),
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
