namespace Lute.Building;

using HalfEdgeMesh;

/// <summary>
/// Generates brick-pattern geometry for walls and structures.
/// Instead of a flat box with a stretched texture, this creates a mesh
/// with proper brick dimensions, mortar gaps, and offset rows.
/// </summary>
public static class BrickMeshBuilder
{
	// Standard medieval brick dimensions (in meters, converted to engine units)
	const float M = 39.37f;

	/// <summary> Brick length (long side). </summary>
	public const float BrickLength = 0.25f * M;   // ~10 inches
	/// <summary> Brick width (depth). </summary>
	public const float BrickWidth = 0.12f * M;   // ~4.7 inches
	/// <summary> Brick height (course height). </summary>
	public const float BrickHeight = 0.065f * M; // ~2.5 inches
	/// <summary> Mortar joint thickness. </summary>
	public const float MortarGap = 0.01f * M;    // ~0.4 inches

	/// <summary>
	/// Build a brick wall mesh. Creates individual brick faces with
	/// offset rows (running bond pattern) and mortar gaps.
	/// </summary>
	/// <param name="size">Wall dimensions (width x depth x height)</param>
	/// <param name="material">Material for the bricks</param>
	/// <param name="mortarMaterial">Material for mortar joints (optional)</param>
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

				float y0 = -halfH + row * (actualBrickH + MortarGap);
				float y1 = y0 + actualBrickH;

				// Front face brick (+Y)
				AddBrickFace( mesh, x0, halfD, y0, x1, halfD, y1, halfD + 0.01f, material );

				// Back face brick (-Y)
				AddBrickFace( mesh, x1, -halfD, y0, x0, -halfD, y1, -halfD - 0.01f, material );
			}
		}

		// Build top and bottom faces as simple caps
		AddCapFace( mesh, -halfW, -halfD, halfH, halfW, halfD, halfH, material );     // top
		AddCapFace( mesh, -halfW, -halfD, -halfH, halfW, halfD, -halfH, material );   // bottom

		// Build left and right side faces
		AddCapFace( mesh, -halfW, -halfD, -halfH, -halfW, halfD, halfH, material );    // left
		AddCapFace( mesh, halfW, -halfD, -halfH, halfW, halfD, halfH, material );     // right

		return mesh;
	}

	/// <summary>
	/// Add a single brick face (a quad on the front or back of the wall).
	/// Slightly raised to create depth/texture variation.
	/// </summary>
	static void AddBrickFace( PolygonMesh mesh,
		float x0, float y0, float z0,
		float x1, float y1, float z1,
		float depth, Material material )
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
