using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Builds the finalized (collapsed) wall mesh from authoritative
	/// SpatialRegistry placements. Produces a solid recessed core +
	/// front/back visible brick skin + closed ends/caps — NOT a pile
	/// of full cuboids. This eliminates open mortar joints, interior
	/// wythe geometry, and see-through gaps while preserving the
	/// running-bond silhouette from actual placements.
	///
	/// The bond pattern is derived from actual SpatialRegistry
	/// placements (each brick's world position, size, yaw), not
	/// regenerated from wall dimensions. The core fills the complete
	/// structural envelope so mortar joints appear as recessed
	/// masonry, not open air.
	///
	/// Only exterior visible faces are emitted:
	///   - Front skin: front face (y-) of front-wythe bricks
	///   - Back skin: back face (y+) of back-wythe bricks
	///   - End caps: left/right/top/bottom of the envelope
	/// Interior wythe faces are NOT emitted.
	/// </summary>
	public static class CollapsedWallMeshBuilder
	{
		const float M = 39.37f;

		/// <summary> How far the recessed core is inset from the brick skin surface. </summary>
		const float CoreInset = 0.005f * M; // ~5mm, just enough to create a visible recess

		/// <summary>
		/// Build the collapsed wall mesh from SpatialRegistry placements.
		/// Returns the mesh and the computed wall-local envelope (for collider).
		/// </summary>
		public static PolygonMesh Build(
			List<StructuralPlacement> placements,
			Vector3 wallPos,
			float wallRotation,
			Material brickMaterial,
			Material coreMaterial,
			out BBox envelope )
		{
			var mesh = new PolygonMesh();
			envelope = new BBox();

			if ( placements == null || placements.Count == 0 )
				return mesh;

			var wallRot = Rotation.FromYaw( wallRotation );
			var wallRotInv = wallRot.Inverse;

			// ── 1. Transform all placements to wall-local space ──
			var localPlacements = new List<(Vector3 center, Vector3 size, float yaw)>();
			float minX = float.MaxValue, maxX = float.MinValue;
			float minY = float.MaxValue, maxY = float.MinValue;
			float minZ = float.MaxValue, maxZ = float.MinValue;

			foreach ( var p in placements )
			{
				var localCenter = wallRotInv * (p.Position - wallPos);
				float localYaw = p.Yaw - wallRotation;
				localPlacements.Add( (localCenter, p.Size, localYaw) );

				float hx = p.Size.x * 0.5f;
				float hy = p.Size.y * 0.5f;
				float hz = p.Size.z * 0.5f;
				// For rotated bricks, use AABB of the rotated OBB
				var localRot = Rotation.FromYaw( localYaw );
				var corners = new Vector3[8];
				corners[0] = localRot * new Vector3( -hx, -hy, -hz ) + localCenter;
				corners[1] = localRot * new Vector3(  hx, -hy, -hz ) + localCenter;
				corners[2] = localRot * new Vector3(  hx,  hy, -hz ) + localCenter;
				corners[3] = localRot * new Vector3( -hx,  hy, -hz ) + localCenter;
				corners[4] = localRot * new Vector3( -hx, -hy,  hz ) + localCenter;
				corners[5] = localRot * new Vector3(  hx, -hy,  hz ) + localCenter;
				corners[6] = localRot * new Vector3(  hx,  hy,  hz ) + localCenter;
				corners[7] = localRot * new Vector3( -hx,  hy,  hz ) + localCenter;
				foreach ( var c in corners )
				{
					if ( c.x < minX ) minX = c.x;
					if ( c.x > maxX ) maxX = c.x;
					if ( c.y < minY ) minY = c.y;
					if ( c.y > maxY ) maxY = c.y;
					if ( c.z < minZ ) minZ = c.z;
					if ( c.z > maxZ ) maxZ = c.z;
				}
			}

			envelope = new BBox( new Vector3( minX, minY, minZ ), new Vector3( maxX, maxY, maxZ ) );

			// ── 2. Solid recessed core ──
			// Fills the complete structural envelope, inset slightly from
			// the front and back brick skin surfaces so mortar joints appear
			// as recessed masonry, not open air.
			float coreMinX = minX, coreMaxX = maxX;
			float coreMinY = minY + CoreInset, coreMaxY = maxY - CoreInset;
			float coreMinZ = minZ, coreMaxZ = maxZ;
			AddSolidBox( mesh, coreMinX, coreMinY, coreMinZ, coreMaxX, coreMaxY, coreMaxZ, coreMaterial );

			// ── 3. Front brick skin (y- face of front-wythe bricks) ──
			// The front wythe is the one with the smallest Y (most negative).
			// We emit only the front-facing quad (y-) of each brick in that wythe.
			float frontY = minY;
			float frontSkinY = frontY; // the surface where front bricks sit
			foreach ( var (center, size, yaw) in localPlacements )
			{
				// Only emit front skin for bricks in the front wythe
				float brickFrontY = center.y - size.y * 0.5f;
				if ( System.MathF.Abs( brickFrontY - frontY ) > 0.01f * M )
					continue;

				var localRot = Rotation.FromYaw( yaw );
				AddBrickFaceQuad( mesh, center, size, localRot, faceIndex: 2, brickMaterial ); // faceIndex 2 = front (y-)
			}

			// ── 4. Back brick skin (y+ face of back-wythe bricks) ──
			float backY = maxY;
			foreach ( var (center, size, yaw) in localPlacements )
			{
				float brickBackY = center.y + size.y * 0.5f;
				if ( System.MathF.Abs( brickBackY - backY ) > 0.01f * M )
					continue;

				var localRot = Rotation.FromYaw( yaw );
				AddBrickFaceQuad( mesh, center, size, localRot, faceIndex: 3, brickMaterial ); // faceIndex 3 = back (y+)
			}

			// ── 5. End caps (left, right, top, bottom) ──
			// Close the wall envelope so there are no open ends.
			// Left end (x-) - viewed from left (looking toward x+), CCW
			AddCapFace( mesh, minX, minY, minZ, minX, minY, maxZ, minX, maxY, maxZ, minX, maxY, minZ, brickMaterial );
			// Right end (x+) - viewed from right (looking toward x-), CCW
			AddCapFace( mesh, maxX, minY, minZ, maxX, maxY, minZ, maxX, maxY, maxZ, maxX, minY, maxZ, brickMaterial );
			// Top (z+) - viewed from above (looking down), CCW
			AddCapFace( mesh, minX, minY, maxZ, maxX, minY, maxZ, maxX, maxY, maxZ, minX, maxY, maxZ, brickMaterial );
			// Bottom (z-) - viewed from below (looking up), CCW
			AddCapFace( mesh, minX, minY, minZ, minX, maxY, minZ, maxX, maxY, minZ, maxX, minY, minZ, brickMaterial );

			// ── 6. Generate UVs after all geometry ──
			mesh.ComputeFaceTextureParametersFromCoordinates();

			return mesh;
		}

		/// <summary>
		/// Add a solid box (6 faces) to the mesh with the given material.
		/// </summary>
		static void AddSolidBox( PolygonMesh mesh,
			float x0, float y0, float z0, float x1, float y1, float z1,
			Material material )
		{
			var faces = new List<FaceHandle>();
			// Bottom (z-) - viewed from below (looking up), CCW
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z0 ) ) ) );
			// Top (z+) - viewed from above (looking down), CCW
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z1 ) ) ) );
			// Front (y-) - viewed from front (looking toward y+), CCW
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z0 ) ) ) );
			// Back (y+) - viewed from back (looking toward y-), CCW
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z1 ) ) ) );
			// Left (x-) - viewed from left (looking toward x+), CCW
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x0, y0, z1 ) ) ) );
			// Right (x+) - viewed from right (looking toward x-), CCW
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x1, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z0 ) ) ) );

			if ( material is not null )
				mesh.AssignMaterialToFaces( faces, material );
		}

		/// <summary>
		/// Add a single face quad of a brick (not the full cuboid).
		/// faceIndex: 0=bottom, 1=top, 2=front(y-), 3=back(y+), 4=left(x-), 5=right(x+)
		/// The face is in the brick's local frame, then rotated + translated.
		/// </summary>
		static void AddBrickFaceQuad( PolygonMesh mesh, Vector3 center, Vector3 size, Rotation localRot, int faceIndex, Material material )
		{
			float hx = size.x * 0.5f;
			float hy = size.y * 0.5f;
			float hz = size.z * 0.5f;

			Vector3[] localCorners = new Vector3[4];
			switch ( faceIndex )
			{
				case 0: // bottom (z-)
					localCorners[0] = new Vector3( -hx, -hy, -hz );
					localCorners[1] = new Vector3(  hx, -hy, -hz );
					localCorners[2] = new Vector3(  hx,  hy, -hz );
					localCorners[3] = new Vector3( -hx,  hy, -hz );
					break;
				case 1: // top (z+)
					localCorners[0] = new Vector3( -hx, -hy,  hz );
					localCorners[1] = new Vector3(  hx, -hy,  hz );
					localCorners[2] = new Vector3(  hx,  hy,  hz );
					localCorners[3] = new Vector3( -hx,  hy,  hz );
					break;
				case 2: // front (y-) - CCW from outside (looking toward y+)
					localCorners[0] = new Vector3( -hx, -hy, -hz );
					localCorners[1] = new Vector3( -hx, -hy,  hz );
					localCorners[2] = new Vector3(  hx, -hy,  hz );
					localCorners[3] = new Vector3(  hx, -hy, -hz );
					break;
				case 3: // back (y+)
					localCorners[0] = new Vector3( -hx,  hy, -hz );
					localCorners[1] = new Vector3(  hx,  hy, -hz );
					localCorners[2] = new Vector3(  hx,  hy,  hz );
					localCorners[3] = new Vector3( -hx,  hy,  hz );
					break;
				case 4: // left (x-)
					localCorners[0] = new Vector3( -hx, -hy, -hz );
					localCorners[1] = new Vector3( -hx,  hy, -hz );
					localCorners[2] = new Vector3( -hx,  hy,  hz );
					localCorners[3] = new Vector3( -hx, -hy,  hz );
					break;
				case 5: // right (x+)
					localCorners[0] = new Vector3(  hx, -hy, -hz );
					localCorners[1] = new Vector3(  hx,  hy, -hz );
					localCorners[2] = new Vector3(  hx,  hy,  hz );
					localCorners[3] = new Vector3(  hx, -hy,  hz );
					break;
				default:
					return;
			}

			// Transform to wall-local space
			for ( int i = 0; i < 4; i++ )
				localCorners[i] = localRot * localCorners[i] + center;

			var face = mesh.AddFace(
				mesh.AddVertex( localCorners[0] ),
				mesh.AddVertex( localCorners[1] ),
				mesh.AddVertex( localCorners[2] ),
				mesh.AddVertex( localCorners[3] ) );

			if ( material is not null )
				mesh.AssignMaterialToFaces( new List<FaceHandle> { face }, material );
		}

		/// <summary>
		/// Add a flat cap face with 4 explicit corners (CCW from outside).
		/// </summary>
		static void AddCapFace( PolygonMesh mesh,
			float x0, float y0, float z0,
			float x1, float y1, float z1,
			float x2, float y2, float z2,
			float x3, float y3, float z3,
			Material material )
		{
			var face = mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x2, y2, z2 ) ),
				mesh.AddVertex( new Vector3( x3, y3, z3 ) ) );

			if ( material is not null )
				mesh.AssignMaterialToFaces( new List<FaceHandle> { face }, material );
		}
	}
}
