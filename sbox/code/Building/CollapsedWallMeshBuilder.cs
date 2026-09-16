using System;
using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Builds the finalized (collapsed) wall mesh from authoritative
	/// SpatialRegistry placements. Produces a solid recessed core +
	/// front/back visible brick skin + closed ends/caps.
	///
	/// DEFECT 1 FIX: Only <see cref="StructuralType.WallBrick"/> placements
	/// are used for the wall body envelope and exterior skin classification.
	/// <see cref="StructuralType.CornerAssemblyBrick"/> placements are
	/// excluded entirely — they may be rotated relative to the wall and
	/// would corrupt geometric minY/maxY extrema, leaving the core exposed.
	///
	/// Exterior wythes are determined from authoritative
	/// <see cref="BrickSlot.GridY"/> (wythe index), NOT from geometric
	/// position extrema. Front wythe = minimum GridY among WallBrick
	/// placements; back wythe = maximum GridY. This is robust to brick
	/// rotation and placement tolerance.
	///
	/// Per-brick face quads preserve actual X/Z placement, half-bricks,
	/// and running-bond offsets from SpatialRegistry. The core fills the
	/// envelope so mortar joints appear as recessed masonry.
	///
	/// DEFECT 2 FIX: All vertices are centered around the envelope center
	/// so the GameObject can be placed at
	/// <c>wallPos + Rotation.FromYaw(wallRotation) * envelope.Center</c>
	/// without double-translation. The BoxCollider uses envelope.Size with
	/// both mesh and collider centered on local origin.
	/// </summary>
	public static class CollapsedWallMeshBuilder
	{
		const float M = 39.37f;

		/// <summary> How far the recessed core is inset from the brick skin surface. </summary>
		const float CoreInset = 0.005f * M; // ~5mm

		/// <summary>
		/// Build the collapsed wall mesh from WallBrick placements only.
		/// Returns the mesh (centered on local origin) and the computed
		/// wall-local envelope (for collider + GameObject placement).
		///
		/// CornerAssemblyBrick placements are excluded — they are shared
		/// junction objects with their own ownership path.
		/// </summary>
		public static PolygonMesh Build(
			List<StructuralPlacement> placements,
			Vector3 wallPos,
			float wallRotation,
			Material brickMaterial,
			Material coreMaterial,
			out BBox envelope,
			out int frontGridY,
			out int backGridY,
			out int frontSkinFaces,
			out int backSkinFaces )
		{
			var mesh = new PolygonMesh();
			envelope = new BBox();
			frontGridY = -1;
			backGridY = -1;
			frontSkinFaces = 0;
			backSkinFaces = 0;

			// ── Filter: WallBrick only, exclude CornerAssemblyBrick ──
			var wallBricks = placements
				.Where( p => p.SemanticType == StructuralType.WallBrick )
				.ToList();

			if ( wallBricks.Count == 0 )
				return mesh;

			var wallRot = Rotation.FromYaw( wallRotation );
			var wallRotInv = wallRot.Inverse;

			// ── 1. Transform WallBrick placements to wall-local space ──
			var localPlacements = new List<(Vector3 center, Vector3 size, float yaw, int gridY)>();
			float minX = float.MaxValue, maxX = float.MinValue;
			float minY = float.MaxValue, maxY = float.MinValue;
			float minZ = float.MaxValue, maxZ = float.MinValue;

			foreach ( var p in wallBricks )
			{
				var localCenter = wallRotInv * (p.Position - wallPos);
				float localYaw = p.Yaw - wallRotation;
				int gy = p.GridSlot?.GridY ?? 0;
				localPlacements.Add( (localCenter, p.Size, localYaw, gy) );

				// Use AABB of the (possibly rotated) OBB for envelope
				float hx = p.Size.x * 0.5f;
				float hy = p.Size.y * 0.5f;
				float hz = p.Size.z * 0.5f;
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
			var envCenter = envelope.Center;
			var envSize = envelope.Size;

			// ── 2. Determine front/back wythes from GridSlot.GridY ──
			frontGridY = localPlacements.Min( p => p.gridY );
			backGridY = localPlacements.Max( p => p.gridY );

			// ── 3. Solid recessed core (centered on local origin) ──
			float coreMinX = (minX - envCenter.x), coreMaxX = (maxX - envCenter.x);
			float coreMinY = (minY + CoreInset - envCenter.y), coreMaxY = (maxY - CoreInset - envCenter.y);
			float coreMinZ = (minZ - envCenter.z), coreMaxZ = (maxZ - envCenter.z);
			AddSolidBox( mesh, coreMinX, coreMinY, coreMinZ, coreMaxX, coreMaxY, coreMaxZ, coreMaterial );

			// ── 4. Front brick skin (GridY == frontGridY, y- face) ──
			// Emit per-brick face quads preserving actual placement X/Z,
			// half-bricks, and running-bond offsets from SpatialRegistry.
			foreach ( var (center, size, yaw, gy) in localPlacements )
			{
				if ( gy != frontGridY )
					continue;
				var localRot = Rotation.FromYaw( yaw );
				// Center the vertex around envelope center
				var centeredCenter = center - envCenter;
				AddBrickFaceQuad( mesh, centeredCenter, size, localRot, faceIndex: 2, brickMaterial );
				frontSkinFaces++;
			}

			// ── 5. Back brick skin (GridY == backGridY, y+ face) ──
			foreach ( var (center, size, yaw, gy) in localPlacements )
			{
				if ( gy != backGridY )
					continue;
				var localRot = Rotation.FromYaw( yaw );
				var centeredCenter = center - envCenter;
				AddBrickFaceQuad( mesh, centeredCenter, size, localRot, faceIndex: 3, brickMaterial );
				backSkinFaces++;
			}

			// ── 6. End caps (centered on local origin) ──
			float cMinX = minX - envCenter.x, cMaxX = maxX - envCenter.x;
			float cMinY = minY - envCenter.y, cMaxY = maxY - envCenter.y;
			float cMinZ = minZ - envCenter.z, cMaxZ = maxZ - envCenter.z;
			// Left end (x-)
			AddCapFace( mesh, cMinX, cMinY, cMinZ, cMinX, cMinY, cMaxZ, cMinX, cMaxY, cMaxZ, cMinX, cMaxY, cMinZ, brickMaterial );
			// Right end (x+)
			AddCapFace( mesh, cMaxX, cMinY, cMinZ, cMaxX, cMaxY, cMinZ, cMaxX, cMaxY, cMaxZ, cMaxX, cMinY, cMaxZ, brickMaterial );
			// Top (z+)
			AddCapFace( mesh, cMinX, cMinY, cMaxZ, cMaxX, cMinY, cMaxZ, cMaxX, cMaxY, cMaxZ, cMinX, cMaxY, cMaxZ, brickMaterial );
			// Bottom (z-)
			AddCapFace( mesh, cMinX, cMinY, cMinZ, cMinX, cMaxY, cMinZ, cMaxX, cMaxY, cMinZ, cMaxX, cMinY, cMinZ, brickMaterial );

			// ── 7. Generate UVs after all geometry ──
			mesh.ComputeFaceTextureParametersFromCoordinates();

			return mesh;
		}

		/// <summary>
		/// Add a solid box (6 faces) to the mesh with the given material.
		/// All coordinates should already be centered on local origin.
		/// </summary>
		static void AddSolidBox( PolygonMesh mesh,
			float x0, float y0, float z0, float x1, float y1, float z1,
			Material material )
		{
			var faces = new List<FaceHandle>();
			// Bottom (z-) - CCW from below
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z0 ) ) ) );
			// Top (z+) - CCW from above
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z1 ) ) ) );
			// Front (y-) - CCW from front
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z0 ) ) ) );
			// Back (y+) - CCW from back
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z1 ) ) ) );
			// Left (x-) - CCW from left
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x0, y0, z1 ) ) ) );
			// Right (x+) - CCW from right
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
				case 3: // back (y+) - CCW from outside (looking toward y-)
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

			// Transform to wall-local space (centered)
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
