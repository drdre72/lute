using System.Collections.Generic;
using HalfEdgeMesh;

/// <summary>
/// A multi-tier stone watchtower built entirely from block primitives
/// (MeshComponent + PolygonMesh), placed near the sanctuary. Demonstrates
/// the from-scratch building system: stacked hollow walls, a walkable top
/// platform with a parapet, a doorway, and an external staircase the NPC
/// can climb. Every block is a real collider (MeshComponent.CollisionType.Mesh)
/// so the structure is solid and walkable.
///
/// Layout (meters, Z up):
///   Foundation slab   7×7×1      z 0-1
///   Shaft tier 0      4 walls    z 1-3.5   (south wall split for doorway)
///   Shaft tier 1      4 walls    z 3.5-6
///   Top floor slab     6×6×0.5   z 6-6.5   (walkable parapet floor)
///   Parapet            4 walls    z 6.5-8   (south split for stair entry)
///   External stair     15 steps   south side, 0.44m rise × 0.65m run × 3m wide
/// </summary>
public sealed class LuteWatchtower : Component
{
	/// <summary> Center of the tower base on the ground (z is ignored; base sits at z=0). </summary>
	[Property] public Vector3 Center { get; set; }

	/// <summary> Yaw in degrees to orient the tower + stair. </summary>
	[Property] public float Yaw { get; set; } = 0f;

	private const float M = 39.37f;

	// Tower dimensions (meters)
	private const float Outer = 6f;          // outer wall-to-wall (X and Y)
	private const float WallThickness = 0.5f;
	private const float TierHeight = 2.5f;
	private const int TierCount = 2;
	private const float FoundationSize = 7f;
	private const float FoundationHeight = 1f;
	private const float TopFloorThickness = 0.5f;
	private const float ParapetHeight = 1.5f;
	private const float DoorWidth = 1f;

	// Stair
	private const float StepRise = 0.44f;
	private const float StepRun = 0.65f;
	private const float StepWidth = 3f;
	private const int StepCount = 15;

	private static readonly string StoneMat = "materials/dev/gray_75.vmat";
	private static readonly string DarkStoneMat = "materials/dev/gray_50.vmat";

	private readonly List<GameObject> _blocks = new();

	/// <summary> Build the tower as a child of <paramref name="parent"/>. </summary>
	public void Build( GameObject parent )
	{
		var root = Scene.CreateObject( true );
		root.Name = "Watchtower";
		root.SetParent( parent );
		root.WorldPosition = Center.WithZ( 0f );
		root.WorldRotation = new Angles( 0, Yaw, 0 );

		float half = Outer * 0.5f * M;
		float wallT = WallThickness * M;
		float tierH = TierHeight * M;

		// 1. Foundation slab (slightly larger than the tower footprint)
		var foundationSize = new Vector3( FoundationSize * M, FoundationSize * M, FoundationHeight * M );
		PlaceBox( root, "Tower_Foundation", new Vector3( 0, 0, FoundationHeight * 0.5f * M ), foundationSize, DarkStoneMat );

		// 2. Shaft — stacked tiers of 4 hollow walls
		for ( int tier = 0; tier < TierCount; tier++ )
		{
			float tierBaseZ = (FoundationHeight + tier * TierHeight) * M;
			float tierCenterZ = tierBaseZ + tierH * 0.5f;
			bool hasDoor = (tier == 0); // doorway only on ground tier

			BuildWallsRing( root, tier, tierCenterZ, half, wallT, tierH, hasDoor );
		}

		// 3. Top floor slab (the walkable parapet platform)
		float shaftTopZ = (FoundationHeight + TierCount * TierHeight) * M;
		var topFloorSize = new Vector3( Outer * M, Outer * M, TopFloorThickness * M );
		PlaceBox( root, "Tower_TopFloor", new Vector3( 0, 0, shaftTopZ + TopFloorThickness * 0.5f * M ), topFloorSize, DarkStoneMat );

		// 4. Parapet — 4 low walls around the top, south side split for stair entry
		float parapetBaseZ = shaftTopZ + TopFloorThickness * M;
		BuildWallsRing( root, -1, parapetBaseZ + ParapetHeight * 0.5f * M, half, wallT, ParapetHeight * M, hasDoor: true );

		// 5. External staircase on the south side (−Y), climbing to the parapet floor
		BuildStair( root, half, wallT, shaftTopZ + TopFloorThickness * M );

		Log.Info( $"Lute: Watchtower built at {Center} — {_blocks.Count} blocks, height {(FoundationHeight + TierCount * TierHeight + TopFloorThickness + ParapetHeight):F1}m." );
	}

	/// <summary>
	/// Build the 4 walls of one tier as a hollow square ring. South wall
	/// (−Y face) can be split into two segments to leave a doorway gap.
	/// </summary>
	void BuildWallsRing( GameObject parent, int tierIndex, float centerZ, float half, float wallT, float wallH, bool hasDoor )
	{
		string prefix = tierIndex < 0 ? "Tower_Parapet" : $"Tower_Tier{tierIndex}";

		// North wall (+Y): full length along X
		PlaceBox( parent, $"{prefix}_N", new Vector3( 0, half, centerZ ), new Vector3( Outer * M, wallT, wallH ), StoneMat );

		// South wall (−Y): full length, or split for doorway
		if ( hasDoor )
		{
			float doorHalf = DoorWidth * 0.5f * M;
			float segLen = (Outer * 0.5f - DoorWidth * 0.5f) * M; // length of each split segment
			float segCenter = (segLen * 0.5f) + doorHalf;
			PlaceBox( parent, $"{prefix}_S_L", new Vector3( -segCenter, -half, centerZ ), new Vector3( segLen, wallT, wallH ), StoneMat );
			PlaceBox( parent, $"{prefix}_S_R", new Vector3( segCenter, -half, centerZ ), new Vector3( segLen, wallT, wallH ), StoneMat );
		}
		else
		{
			PlaceBox( parent, $"{prefix}_S", new Vector3( 0, -half, centerZ ), new Vector3( Outer * M, wallT, wallH ), StoneMat );
		}

		// East (+X) and West (−X) walls: length runs along Y, thickness along X
		// (swap X/Y dimensions instead of rotating)
		PlaceBox( parent, $"{prefix}_E", new Vector3( half, 0, centerZ ), new Vector3( wallT, Outer * M, wallH ), StoneMat );
		PlaceBox( parent, $"{prefix}_W", new Vector3( -half, 0, centerZ ), new Vector3( wallT, Outer * M, wallH ), StoneMat );
	}

	/// <summary>
	/// External straight staircase on the south side (−Y), climbing toward
	/// the tower. Each step is a SOLID block from the ground up to its tread
	/// (so the NPC can't walk under it), and steps ascend toward +Y (north)
	/// so the lowest step is farthest south and the highest meets the parapet
	/// floor. Each riser is StepRise (≤0.46m) so the NPC's StepUpHeight
	/// (18u ≈ 0.46m) can climb it one step at a time.
	/// </summary>
	void BuildStair( GameObject parent, float half, float wallT, float topZ )
	{
		float rise = StepRise * M;
		float run = StepRun * M;
		float width = StepWidth * M;

		// The highest step (last) sits just outside the south wall face.
		float topStepY = -half - wallT - run * 0.5f;
		// Each lower step is one run farther south (−Y).
		for ( int i = 0; i < StepCount; i++ )
		{
			// i=0 is the lowest (farthest south), i=StepCount-1 is highest (at tower).
			int heightIndex = i; // step i's tread is rise*(i+1) high
			float stepCenterY = topStepY - run * (StepCount - 1 - i);
			float stepHeight = rise * (heightIndex + 1);
			float stepCenterZ = stepHeight * 0.5f;
			PlaceBox( parent, $"Tower_Step_{i}", new Vector3( 0, stepCenterY, stepCenterZ ),
				new Vector3( width, run, stepHeight ), StoneMat );
		}

		Log.Info( $"Lute: Watchtower stair built — {StepCount} steps, rise={StepRise}m, run={StepRun}m, reaching z={StepRise * StepCount:F2}m (parapet floor at {topZ / M:F2}m)." );
	}

	/// <summary>
	/// Place a single axis-aligned box block: a GameObject with a MeshComponent
	/// whose PolygonMesh is a 6-faced box of the given world-space size, with
	/// concave mesh collision. Position is relative to the tower root (which
	/// is already placed/rotated), so blocks inherit the tower's transform.
	/// </summary>
	GameObject PlaceBox( GameObject parent, string name, Vector3 localPos, Vector3 size, string materialPath )
	{
		// Create disabled so the MeshComponent's OnEnabledInternal fires AFTER
		// we set Mesh — at runtime (!Scene.IsEditor) the Mesh setter's
		// RebuildMesh bails, so the Model only builds when OnEnabledInternal
		// calls RebuildRenderMesh. If the object is enabled first (Mesh=null),
		// the model never builds and the block is invisible + non-solid.
		var go = Scene.CreateObject( false );
		go.Name = name;
		go.SetParent( parent );
		go.LocalPosition = localPos;
		go.LocalRotation = Rotation.Identity;
		go.LocalScale = Vector3.One; // size is baked into the mesh vertices

		var meshComp = go.AddComponent<MeshComponent>();
		meshComp.Collision = MeshComponent.CollisionType.Mesh;

		var mesh = new PolygonMesh();
		var half = size * 0.5f;

		var v0 = mesh.AddVertex( new Vector3( -half.x, -half.y, -half.z ) );
		var v1 = mesh.AddVertex( new Vector3(  half.x, -half.y, -half.z ) );
		var v2 = mesh.AddVertex( new Vector3(  half.x,  half.y, -half.z ) );
		var v3 = mesh.AddVertex( new Vector3( -half.x,  half.y, -half.z ) );
		var v4 = mesh.AddVertex( new Vector3( -half.x, -half.y,  half.z ) );
		var v5 = mesh.AddVertex( new Vector3(  half.x, -half.y,  half.z ) );
		var v6 = mesh.AddVertex( new Vector3(  half.x,  half.y,  half.z ) );
		var v7 = mesh.AddVertex( new Vector3( -half.x,  half.y,  half.z ) );

		mesh.AddFace( v0, v3, v2, v1 ); // bottom (-Z)
		mesh.AddFace( v4, v5, v6, v7 ); // top (+Z)
		mesh.AddFace( v0, v1, v5, v4 ); // front (-Y)
		mesh.AddFace( v1, v2, v6, v5 ); // right (+X)
		mesh.AddFace( v2, v3, v7, v6 ); // back (+Y)
		mesh.AddFace( v3, v0, v4, v7 ); // left (-X)

		var material = Material.Load( materialPath );
		if ( material is not null )
		{
			var allFaces = new List<FaceHandle>();
			for ( int f = 0; f < 6; f++ )
				allFaces.Add( mesh.FaceHandleFromIndex( f ) );
			mesh.AssignMaterialToFaces( allFaces, material );
		}

		meshComp.Mesh = mesh;

		// Now enable — OnEnabledInternal fires, RebuildRenderMesh builds the
		// Model (render + physics) from the now-set Mesh.
		go.Enabled = true;

		_blocks.Add( go );
		return go;
	}
}
