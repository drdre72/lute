using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;

/// <summary>
/// A builder NPC that constructs block structures using MeshComponent +
/// PolygonMesh, then walks onto them to validate collision. This gives
/// the agent a physics-verification signal it can't get from raycasts
/// alone — the NPC either stands on the blocks or falls through.
///
/// The NPC uses the citizen body (same as the player) so collision
/// results are representative of real gameplay. Dressed in workwear:
/// flannel shirt, cargo pants, army boots, short scruffy hair.
/// </summary>
public sealed class LuteBuilderNpc : Component
{
	[RequireComponent] public PlayerController Controller { get; set; }

	/// <summary> NPC state for the build-and-test sequence. </summary>
	public enum NpcState
	{
		Idle,
		Building,
		Walking,
		Verifying,
		Done
	}

	[Property] public NpcState State { get; set; } = NpcState.Idle;

	/// <summary>
	/// When true, the NPC ignores its build/test state machine and instead
	/// walks toward <see cref="AgentTarget"/> every frame. This is the
	/// "live tunnel" the agent uses to drive the body through MCP
	/// (set_component on AgentControlled + AgentTarget). Speed scales with
	/// <see cref="AgentSpeed"/>.
	/// </summary>
	[Property, Group( "Agent" ), Title( "Agent Controlled" )]
	public bool AgentControlled { get; set; } = false;

	/// <summary> World position the agent is steering toward right now. </summary>
	[Property, Group( "Agent" ), Title( "Agent Target" )]
	public Vector3 AgentTarget { get; set; }

	/// <summary> Move speed in units/sec when agent-driven (default ~5 m/s). </summary>
	[Property, Group( "Agent" ), Title( "Agent Speed" )]
	public float AgentSpeed { get; set; } = 5f * 39.37f;

	/// <summary> Stop distance from the target (units). </summary>
	[Property, Group( "Agent" ), Title( "Agent Stop Radius" )]
	public float AgentStopRadius { get; set; } = 60f;

	/// <summary>
	/// Set this to a non-zero world position to instantly teleport the NPC
	/// there. The teleport fires once in OnUpdate, then the field is reset
	/// to zero. This lets the agent "see" any spot in the world by
	/// repositioning Merlyn via MCP (set_component on AgentTeleportTo)
	/// without walking him there. Useful for inspecting remote geometry,
	/// gates, towers, etc.
	/// </summary>
	[Property, Group( "Agent" ), Title( "Agent Teleport To" )]
	public Vector3 AgentTeleportTo { get; set; }

	private float _stateTimer = 0f;
	private Vector3 _verifyStartPos;
	private List<GameObject> _blocks = new();
	private Vector3 _platformCenter;

	// A small test platform: 3x1 blocks, each 2m x 2m x 0.5m
	private static readonly Vector3 BlockSize = new( 2f * 39.37f, 2f * 39.37f, 0.5f * 39.37f );
	private static readonly int PlatformBlocks = 3;

	protected override void OnStart()
	{
		Log.Info( $"Lute: BuilderNpc '{GameObject.Name}' started at {WorldPosition}." );

		// Dress the NPC in workwear.
		DressAsBuilder();
	}

	protected override void OnUpdate()
	{
		// Teleport takes priority over everything — fires once, then clears.
		if ( AgentTeleportTo != Vector3.Zero )
		{
			var target = AgentTeleportTo;
			AgentTeleportTo = Vector3.Zero;
			WorldPosition = target;
			var body = Controller?.GetComponent<Rigidbody>();
			if ( body.IsValid() )
			{
				body.Velocity = Vector3.Zero;
				body.AngularVelocity = Vector3.Zero;
			}
			Log.Info( $"Lute: [AgentTeleport] moved to {target} (now at {WorldPosition})." );
		}

		// Live agent tunnel takes priority over the autonomous test sequence.
		if ( AgentControlled )
		{
			AgentTick();
			return;
		}

		// Run the build-and-test sequence as a simple state machine.
		switch ( State )
		{
			case NpcState.Idle:
			{
				_stateTimer += Time.Delta;
				if ( _stateTimer > 1f )
				{
					State = NpcState.Building;
					_stateTimer = 0f;
					BuildTestPlatform();
				}
				break;
			}

			case NpcState.Building:
			{
				_stateTimer += Time.Delta;
				if ( _stateTimer > 0.5f )
				{
					State = NpcState.Walking;
					_stateTimer = 0f;
					WalkToPlatform();
				}
				break;
			}

			case NpcState.Walking:
			{
				_stateTimer += Time.Delta;

				var pos = WorldPosition;
				var dist = Vector3.DistanceBetween( pos.WithZ( 0 ), _platformCenter.WithZ( 0 ) );
				var isOnGround = Controller.GroundObject.IsValid();

				if ( _stateTimer < 0.5f )
					_verifyStartPos = pos;

				// Move toward the platform via Rigidbody velocity
				var toPlatform = (_platformCenter - pos).WithZ( 0 );
				if ( toPlatform.Length > 1f )
				{
					var body = Controller?.GetComponent<Rigidbody>();
					if ( body.IsValid() )
						body.Velocity = toPlatform.Normal * 300f;
				}

				// Log once per second
				if ( MathF.Floor( _stateTimer ) > MathF.Floor( _stateTimer - Time.Delta ) )
				{
					Log.Info( $"Lute: BuilderNpc walking — pos={pos}, distToPlatform={dist:F1}, grounded={isOnGround}, timer={_stateTimer:F1}" );
				}

				if ( dist < 50f || _stateTimer > 10f )
				{
					State = NpcState.Verifying;
					_stateTimer = 0f;
					var body = Controller?.GetComponent<Rigidbody>();
					if ( body.IsValid() ) body.Velocity = Vector3.Zero;
				}
				break;
			}

			case NpcState.Verifying:
			{
				_stateTimer += Time.Delta;
				var groundCheck = Controller.GroundObject.IsValid();
				var currentPos = WorldPosition;
				var fallDelta = _verifyStartPos.z - currentPos.z;

				// Log once per second
				if ( MathF.Floor( _stateTimer ) > MathF.Floor( _stateTimer - Time.Delta ) )
				{
					Log.Info( $"Lute: BuilderNpc verify — pos={currentPos}, grounded={groundCheck}, fallDelta={fallDelta:F1} units ({fallDelta / 39.37f:F2} m), timer={_stateTimer:F1}" );
				}

				if ( _stateTimer > 2f )
				{
					var success = groundCheck && fallDelta < 50f;
					Log.Info( $"Lute: BuilderNpc build test {(success ? "PASSED" : "FAILED")} — grounded={groundCheck}, fell={fallDelta / 39.37f:F2} m" );
					State = NpcState.Done;
				}
				break;
			}

			case NpcState.Done:
			{
				StopWalking();
				break;
			}
		}
	}

	/// <summary>
	/// Per-frame agent-driven movement. Walks toward <see cref="AgentTarget"/>
	/// using the Rigidbody velocity, faces the heading, and logs periodically
	/// so the agent (reading the console via MCP) can confirm it is actually
	/// moving. Stops within <see cref="AgentStopRadius"/>.
	/// </summary>
	void AgentTick()
	{
		var pos = WorldPosition;
		var toTarget = (AgentTarget - pos).WithZ( 0 );
		var dist = toTarget.Length;
		var grounded = Controller.GroundObject.IsValid();

		var body = Controller?.GetComponent<Rigidbody>();

		if ( dist > AgentStopRadius )
		{
			// Face the heading
			Controller.EyeAngles = new Angles( 0, toTarget.EulerAngles.yaw, 0 );

			if ( body.IsValid() )
				body.Velocity = toTarget.Normal * AgentSpeed;
		}
		else if ( body.IsValid() )
		{
			body.Velocity = Vector3.Zero;
		}

		// Log once per second so the agent can track progress through MCP.
		if ( MathF.Floor( Time.Now ) > MathF.Floor( Time.Now - Time.Delta ) )
		{
			Log.Info( $"Lute: [AgentDrive] pos={pos} target={AgentTarget} dist={dist:F1} ({dist / 39.37f:F2} m) grounded={grounded} vel={(body.IsValid() ? body.Velocity.Length : 0):F0}" );
		}
	}

	/// <summary>
	/// Dress the NPC as a builder: flannel shirt, cargo pants, army boots,
	/// short scruffy brown hair. Tinted slightly darker than default for
	/// visual distinction from the player.
	/// </summary>
	void DressAsBuilder()
	{
		var dresser = Components.GetOrCreate<Dresser>();
		dresser.Source = Dresser.ClothingSource.Manual;
		dresser.ManualHeight = 0.5f;  // average height
		dresser.ManualTint = 0.3f;    // slightly darker skin
		dresser.ManualAge = 0.6f;     // middle-aged
		dresser.ApplyHeightScale = true;

		// Find the body renderer
		var body = GetComponentInChildren<SkinnedModelRenderer>();
		if ( body.IsValid() )
		{
			dresser.BodyTarget = body;
		}

		// Load clothing items. Paths relative to the citizen addon.
		var clothingPaths = new[]
		{
			"models/citizen_clothes/hair/hair_shortscruffy/hair_shortscruffy_brown.clothing",
			"models/citizen_clothes/shirt/Flannel_Shirt/Flannel_Shirt.clothing",
			"models/citizen_clothes/trousers/CargoPants/cargo_pants_army.clothing",
			"models/citizen_clothes/shoes/Boots/army_boots.clothing",
		};

		dresser.Clothing.Clear();

		foreach ( var path in clothingPaths )
		{
			var clothing = ResourceLibrary.Get<Clothing>( path );
			if ( clothing is not null )
			{
				dresser.Clothing.Add( new ClothingContainer.ClothingEntry( clothing ) );
				Log.Info( $"Lute: BuilderNpc dressed — loaded '{path}'." );
			}
			else
			{
				Log.Warning( $"Lute: BuilderNpc clothing not found: '{path}'." );
			}
		}

		// Apply the clothing asynchronously
		_ = dresser.Apply();

		Log.Info( $"Lute: BuilderNpc dressed with {dresser.Clothing.Count} items." );
	}

	/// <summary>
	/// Build a small test platform of 3 blocks in a row, each 2m x 2m x 0.5m,
	/// placed 5m in front of the NPC. Each block is a GameObject with a
	/// MeshComponent (rendering + collision in one component).
	/// </summary>
	void BuildTestPlatform()
	{
		_platformCenter = WorldPosition + WorldRotation.Forward * (5f * 39.37f);
		_platformCenter = _platformCenter.WithZ( 0.25f * 39.37f ); // top of block at 0.25m

		Log.Info( $"Lute: BuilderNpc building platform at {_platformCenter}, block size={BlockSize}." );

		for ( int i = 0; i < PlatformBlocks; i++ )
		{
			// Offset each block along the NPC's right vector so they form a row
			var offset = WorldRotation.Right * (i - 1) * BlockSize.x;
			var blockPos = _platformCenter + offset;

			var block = PlaceBlock( blockPos, BlockSize, "materials/dev/gray_75.vmat" );
			block.Name = $"BuilderBlock_{i}";
			_blocks.Add( block );

			Log.Info( $"Lute: BuilderNpc placed block {i} at {blockPos} (world pos {block.WorldPosition})." );
		}

		Log.Info( $"Lute: BuilderNpc platform built — {_blocks.Count} blocks." );
	}

	/// <summary>
	/// Place a single block (box mesh) at the given position with the given
	/// size and material. Returns the new GameObject.
	/// </summary>
	GameObject PlaceBlock( Vector3 worldPos, Vector3 size, string materialPath )
	{
		// Create disabled so the MeshComponent's OnEnabledInternal fires AFTER
		// we set Mesh — at runtime (!Scene.IsEditor) the Mesh setter's
		// RebuildMesh bails, so the Model (render + collision) only builds when
		// OnEnabledInternal calls RebuildRenderMesh. If enabled first with
		// Mesh=null, the block is invisible and non-solid.
		var go = Scene.CreateObject( false );
		go.Name = "BuilderBlock";
		go.WorldPosition = worldPos;
		go.WorldRotation = Rotation.Identity;

		// Use a MeshComponent — it provides both rendering and collision.
		// PolygonMesh is built as a simple box from 8 vertices + 6 faces.
		var meshComp = go.AddComponent<MeshComponent>();
		meshComp.Collision = MeshComponent.CollisionType.Mesh; // concave, exact

		// Build a box PolygonMesh centered at origin (the GameObject's
		// position becomes the world position).
		var mesh = new PolygonMesh();
		var half = size * 0.5f;

		// 8 vertices of a box
		var v0 = mesh.AddVertex( new Vector3( -half.x, -half.y, -half.z ) );
		var v1 = mesh.AddVertex( new Vector3(  half.x, -half.y, -half.z ) );
		var v2 = mesh.AddVertex( new Vector3(  half.x,  half.y, -half.z ) );
		var v3 = mesh.AddVertex( new Vector3( -half.x,  half.y, -half.z ) );
		var v4 = mesh.AddVertex( new Vector3( -half.x, -half.y,  half.z ) );
		var v5 = mesh.AddVertex( new Vector3(  half.x, -half.y,  half.z ) );
		var v6 = mesh.AddVertex( new Vector3(  half.x,  half.y,  half.z ) );
		var v7 = mesh.AddVertex( new Vector3( -half.x,  half.y,  half.z ) );

		// 6 faces (outward-facing winding)
		mesh.AddFace( v0, v3, v2, v1 ); // bottom (-Z)
		mesh.AddFace( v4, v5, v6, v7 ); // top (+Z)
		mesh.AddFace( v0, v1, v5, v4 ); // front (-Y)
		mesh.AddFace( v1, v2, v6, v5 ); // right (+X)
		mesh.AddFace( v2, v3, v7, v6 ); // back (+Y)
		mesh.AddFace( v3, v0, v4, v7 ); // left (-X)

		// Apply material to all faces at once
		var material = Material.Load( materialPath );
		if ( material is not null )
		{
			var allFaces = new List<FaceHandle>();
			for ( int f = 0; f < 6; f++ )
			{
				allFaces.Add( mesh.FaceHandleFromIndex( f ) );
			}
			mesh.AssignMaterialToFaces( allFaces, material );
		}

		meshComp.Mesh = mesh;

		// Enable now — OnEnabledInternal fires, RebuildRenderMesh builds the
		// Model (render + physics) from the now-set Mesh.
		go.Enabled = true;

		Log.Info( $"Lute: BuilderNpc block mesh built — material={materialPath}." );

		return go;
	}

	/// <summary>
	/// Walk toward the platform center using the PlayerController's
	/// built-in movement. We set the EyeAngles to face the platform and
	/// simulate forward input.
	/// </summary>
	void WalkToPlatform()
	{
		// Face the platform
		var toPlatform = (_platformCenter - WorldPosition).WithZ( 0 );
		if ( toPlatform.Length > 1f )
		{
			Controller.EyeAngles = new Angles( 0, toPlatform.EulerAngles.yaw, 0 );
		}

		// Move forward — we'll set the wish direction each frame in OnUpdate
		// by writing to the Rigidbody velocity. Actually, the PlayerController
		// reads Input.AnalogMove, which we can't fake for an NPC. Instead,
		// we'll move via the Rigidbody directly.
		Log.Info( "Lute: BuilderNpc walking to platform." );
	}

	/// <summary>
	/// Stop all movement.
	/// </summary>
	void StopWalking()
	{
		var body = Controller?.GetComponent<Rigidbody>();
		if ( body.IsValid() )
		{
			body.Velocity = Vector3.Zero;
		}
	}
}
