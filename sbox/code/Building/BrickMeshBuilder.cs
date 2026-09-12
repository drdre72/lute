namespace Lute.Building;

using HalfEdgeMesh;

/// <summary>
/// Generates brick-pattern geometry for walls and structures.
/// Instead of a flat box with a stretched texture, this creates a mesh
/// with proper brick dimensions, mortar gaps, and offset rows.
/// Each brick face is raised slightly outward to create visible relief
/// and shadow lines between bricks.
/// </summary>
public static class BrickMeshBuilder
{
	// Standard medieval brick dimensions (in meters, converted to engine units)
	const float M = 39.37f;

	/// <summary> Brick length (long side). </summary>
	public const float BrickLength = 0.5f * M;    // ~20 inches (large, visible)
	/// <summary> Brick width (depth). </summary>
	public const float BrickWidth = 0.25f * M;   // ~10 inches
	/// <summary> Brick height (course height). </summary>
	public const float BrickHeight = 0.2f * M;   // ~8 inches (large, visible)
	/// <summary> Mortar joint thickness. </summary>
	public const float MortarGap = 0.04f * M;    // ~1.6 inches (clearly visible)
	/// <summary> How far bricks protrude from the mortar base (relief). </summary>
	const float BrickRelief = 0.15f * M;        // ~6 inches (clearly visible)

	/// <summary>
	/// Build a brick wall mesh. Creates individual brick faces with
	/// offset rows (running bond pattern) and mortar gaps.
	/// Bricks are raised slightly to create visible relief and shadow lines.
	/// </summary>
	public static PolygonMesh BuildBrickWall(
		Vector3 size,
		Material material,
		Material mortarMaterial = null )
	{
		var mesh = new PolygonMesh();

		float wallW = size.x;
		float wallD = size.y;
		float wallH = size.z;

		// Calculate how many bricks fit
		float brickWWithGap = BrickLength + MortarGap;
		float brickHWithGap = BrickHeight + MortarGap;

		int bricksPerRow = Math.Max( 1, (int)(wallW / brickWWithGap) );
		int numRows = Math.Max( 1, (int)(wallH / brickHWithGap) );

		// Adjust brick size to fill the wall evenly
		float actualBrickW = (wallW - MortarGap * (bricksPerRow - 1)) / bricksPerRow;
		float actualBrickH = (wallH - MortarGap * (numRows - 1)) / numRows;

		float halfW = wallW * 0.5f;
		float halfD = wallD * 0.5f;
		float halfH = wallH * 0.5f;

		// The mortar base is at the wall surface (halfD).
		// Bricks protrude outward by BrickRelief.
		float frontBase = halfD;
		float frontBrick = halfD + BrickRelief;
		float backBase = -halfD;
		float backBrick = -halfD - BrickRelief;

		// Build front face (facing +Y) and back face (facing -Y) with brick pattern
		for ( int row = 0; row < numRows; row++ )
		{
			// Running bond: offset every other row by half a brick
			float rowOffset = (row % 2 == 1) ? actualBrickW * 0.5f : 0f;

			for ( int col = 0; col < bricksPerRow; col++ )
			{
				float x0 = -halfW + col * (actualBrickW + MortarGap) + rowOffset;
				float x1 = x0 + actualBrickW;
				if ( x1 > halfW ) x1 = halfW; // clip to wall edge

				float z0 = -halfH + row * (actualBrickH + MortarGap);
				float z1 = z0 + actualBrickH;

				// Front face brick (+Y) — raised outward with side faces
				AddProtrudedBrick( mesh, x0, frontBase, frontBrick, z0, x1, z1, material );

				// Back face brick (-Y) — raised outward with side faces
				AddProtrudedBrick( mesh, x1, -frontBase, backBrick, z0, x0, z1, material );
			}
		}

		// Build mortar base (the recessed surface between bricks)
		// Front mortar
		AddMortarQuad( mesh, -halfW, frontBase, -halfH, halfW, frontBase, halfH, mortarMaterial ?? material );
		// Back mortar
		AddMortarQuad( mesh, halfW, backBase, -halfH, -halfW, backBase, halfH, mortarMaterial ?? material );

		// Build top and bottom faces as simple caps
		AddCapFace( mesh, -halfW, -halfD, halfH, halfW, halfD, halfH, material );     // top
		AddCapFace( mesh, -halfW, -halfD, -halfH, halfW, halfD, -halfH, material );   // bottom

		// Build left and right side faces
		AddCapFace( mesh, -halfW, -halfD, -halfH, -halfW, halfD, halfH, material );    // left
		AddCapFace( mesh, halfW, -halfD, -halfH, halfW, halfD, halfH, material );     // right

		return mesh;
	}

	/// <summary>
	/// Add a protruded brick with front face and side faces.
	/// The brick face is at `brickY`, the mortar base is at `baseY`.
	/// Side faces connect the brick face to the mortar base, creating
	/// visible edges and shadow lines.
	/// </summary>
	static void AddProtrudedBrick( PolygonMesh mesh,
		float x0, float baseY, float brickY, float z0,
		float x1, float z1,
		Material material )
	{
		// Front face (the brick surface)
		var v0 = mesh.AddVertex( new Vector3( x0, brickY, z0 ) );
		var v1 = mesh.AddVertex( new Vector3( x1, brickY, z0 ) );
		var v2 = mesh.AddVertex( new Vector3( x1, brickY, z1 ) );
		var v3 = mesh.AddVertex( new Vector3( x0, brickY, z1 ) );
		var frontFace = mesh.AddFace( v0, v1, v2, v3 );

		// Top side face (connects brick top edge to mortar base)
		var v4 = mesh.AddVertex( new Vector3( x0, baseY, z1 ) );
		var v5 = mesh.AddVertex( new Vector3( x1, baseY, z1 ) );
		mesh.AddFace( v3, v2, v5, v4 );

		// Bottom side face
		var v6 = mesh.AddVertex( new Vector3( x0, baseY, z0 ) );
		var v7 = mesh.AddVertex( new Vector3( x1, baseY, z0 ) );
		mesh.AddFace( v0, v6, v7, v1 );

		// Left side face
		mesh.AddFace( v0, v3, v4, v6 );

		// Right side face
		mesh.AddFace( v1, v7, v5, v2 );

		if ( material is not null )
		{
			var faces = new List<FaceHandle> { frontFace };
			mesh.AssignMaterialToFaces( faces, material );
		}
	}

	/// <summary>
	/// Add a flat mortar quad at the base wall surface (recessed behind bricks).
	/// </summary>
	static void AddMortarQuad( PolygonMesh mesh,
		float x0, float y, float z0,
		float x1, float y1, float z1,
		Material material )
	{
		var v0 = mesh.AddVertex( new Vector3( x0, y, z0 ) );
		var v1 = mesh.AddVertex( new Vector3( x1, y, z0 ) );
		var v2 = mesh.AddVertex( new Vector3( x1, y1, z1 ) );
		var v3 = mesh.AddVertex( new Vector3( x0, y1, z1 ) );

		var face = mesh.AddFace( v0, v1, v2, v3 );
		if ( material is not null )
		{
			mesh.AssignMaterialToFaces( new List<FaceHandle> { face }, material );
		}
	}

	/// <summary>
	/// Add a simple cap face (quad) for top/bottom/sides.
	/// </summary>
	static void AddCapFace( PolygonMesh mesh,
		float x0, float y0, float z0,
		float x1, float y1, float z1,
		Material material )
	{
		var v0 = mesh.AddVertex( new Vector3( x0, y0, z0 ) );
		var v1 = mesh.AddVertex( new Vector3( x1, y0, z0 ) );
		var v2 = mesh.AddVertex( new Vector3( x1, y1, z1 ) );
		var v3 = mesh.AddVertex( new Vector3( x0, y1, z1 ) );

		var face = mesh.AddFace( v0, v1, v2, v3 );
		if ( material is not null )
		{
			mesh.AssignMaterialToFaces( new List<FaceHandle> { face }, material );
		}
	}

	/// <summary>
	/// Build a single brick (for inventory drops, crafting previews, etc.)
	/// </summary>
	public static PolygonMesh BuildSingleBrick( Material material = null )
	{
		var mesh = new PolygonMesh();
		var half = new Vector3( BrickLength * 0.5f, BrickWidth * 0.5f, BrickHeight * 0.5f );

		var v0 = mesh.AddVertex( new Vector3( -half.x, -half.y, -half.z ) );
		var v1 = mesh.AddVertex( new Vector3(  half.x, -half.y, -half.z ) );
		var v2 = mesh.AddVertex( new Vector3(  half.x,  half.y, -half.z ) );
		var v3 = mesh.AddVertex( new Vector3( -half.x,  half.y, -half.z ) );
		var v4 = mesh.AddVertex( new Vector3( -half.x, -half.y,  half.z ) );
		var v5 = mesh.AddVertex( new Vector3(  half.x, -half.y,  half.z ) );
		var v6 = mesh.AddVertex( new Vector3(  half.x,  half.y,  half.z ) );
		var v7 = mesh.AddVertex( new Vector3( -half.x,  half.y,  half.z ) );

		var faces = new List<FaceHandle>();
		faces.Add( mesh.AddFace( v0, v3, v2, v1 ) ); // bottom
		faces.Add( mesh.AddFace( v4, v5, v6, v7 ) ); // top
		faces.Add( mesh.AddFace( v0, v1, v5, v4 ) ); // front
		faces.Add( mesh.AddFace( v1, v2, v6, v5 ) ); // right
		faces.Add( mesh.AddFace( v2, v3, v7, v6 ) ); // back
		faces.Add( mesh.AddFace( v3, v0, v4, v7 ) ); // left

		if ( material is not null )
		{
			mesh.AssignMaterialToFaces( faces, material );
		}

		return mesh;
	}
}
