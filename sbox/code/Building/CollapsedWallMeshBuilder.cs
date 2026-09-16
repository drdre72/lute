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
	///
	/// WINDING: All face windings match the canonical outward winding from
	/// <see cref="BrickMeshBuilder.BuildSingleBrick"/> (CCW when viewed
	/// from outside). This ensures correct backface culling.
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

			// ── 2. Determine front/back wythes from GridSlot.GridY ──
			frontGridY = localPlacements.Min( p => p.gridY );
			backGridY = localPlacements.Max( p => p.gridY );

			// ── 3. Solid recessed core (centered on local origin) ──
			// Winding matches canonical BuildSingleBrick outward winding.
			float cx0 = minX - envCenter.x, cx1 = maxX - envCenter.x;
			float cy0 = minY + CoreInset - envCenter.y, cy1 = maxY - CoreInset - envCenter.y;
			float cz0 = minZ - envCenter.z, cz1 = maxZ - envCenter.z;
			AddSolidBox( mesh, cx0, cy0, cz0, cx1, cy1, cz1, coreMaterial );

			// ── 4. Front brick skin (GridY == frontGridY, y- face) ──
			// Emit per-brick face quads preserving actual placement X/Z,
			// half-bricks, and running-bond offsets from SpatialRegistry.
			foreach ( var (center, size, yaw, gy) in localPlacements )
			{
				if ( gy != frontGridY )
					continue;
				var localRot = Rotation.FromYaw( yaw );
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

			// ── 6. End caps (centered on local origin, canonical winding) ──
			float ex0 = minX - envCenter.x, ex1 = maxX - envCenter.x;
			float ey0 = minY - envCenter.y, ey1 = maxY - envCenter.y;
			float ez0 = minZ - envCenter.z, ez1 = maxZ - envCenter.z;
			// Left end (x-) — canonical: v3,v0,v4,v7
			AddCapFace( mesh, ex0, ey1, ez0, ex0, ey0, ez0, ex0, ey0, ez1, ex0, ey1, ez1, brickMaterial );
			// Right end (x+) — canonical: v1,v2,v6,v5
			AddCapFace( mesh, ex1, ey0, ez0, ex1, ey1, ez0, ex1, ey1, ez1, ex1, ey0, ez1, brickMaterial );
			// Top (z+) — canonical: v4,v5,v6,v7
			AddCapFace( mesh, ex0, ey0, ez1, ex1, ey0, ez1, ex1, ey1, ez1, ex0, ey1, ez1, brickMaterial );
			// Bottom (z-) — canonical: v0,v3,v2,v1
			AddCapFace( mesh, ex0, ey0, ez0, ex0, ey1, ez0, ex1, ey1, ez0, ex1, ey0, ez0, brickMaterial );

			// ── 7. Generate UVs after all geometry ──
			mesh.ComputeFaceTextureParametersFromCoordinates();

			return mesh;
		}

		/// <summary>
		/// Add a solid box (6 faces) with canonical outward winding matching
		/// BrickMeshBuilder.BuildSingleBrick. All coordinates should already
		/// be centered on local origin.
		///
		/// Canonical vertex mapping (x0=x-, x1=x+, y0=y-, y1=y+, z0=z-, z1=z+):
		///   v0=(x0,y0,z0) v1=(x1,y0,z0) v2=(x1,y1,z0) v3=(x0,y1,z0)
		///   v4=(x0,y0,z1) v5=(x1,y0,z1) v6=(x1,y1,z1) v7=(x0,y1,z1)
		///
		/// Canonical face winding (CCW from outside):
		///   bottom: v0,v3,v2,v1   top: v4,v5,v6,v7
		///   front:  v0,v1,v5,v4    back:  v2,v3,v7,v6
		///   left:   v3,v0,v4,v7    right: v1,v2,v6,v5
		/// </summary>
		static void AddSolidBox( PolygonMesh mesh,
			float x0, float y0, float z0, float x1, float y1, float z1,
			Material material )
		{
			var faces = new List<FaceHandle>();
			// bottom (z-): v0,v3,v2,v1
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z0 ) ) ) );
			// top (z+): v4,v5,v6,v7
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z1 ) ) ) );
			// front (y-): v0,v1,v5,v4
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x0, y0, z1 ) ) ) );
			// back (y+): v2,v3,v7,v6
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x1, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z1 ) ) ) );
			// left (x-): v3,v0,v4,v7
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x0, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x0, y0, z1 ) ),
				mesh.AddVertex( new Vector3( x0, y1, z1 ) ) ) );
			// right (x+): v1,v2,v6,v5
			faces.Add( mesh.AddFace(
				mesh.AddVertex( new Vector3( x1, y0, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z0 ) ),
				mesh.AddVertex( new Vector3( x1, y1, z1 ) ),
				mesh.AddVertex( new Vector3( x1, y0, z1 ) ) ) );

			if ( material is not null )
				mesh.AssignMaterialToFaces( faces, material );
		}

		/// <summary>
		/// Add a single face quad of a brick (not the full cuboid).
		/// faceIndex: 0=bottom, 1=top, 2=front(y-), 3=back(y+), 4=left(x-), 5=right(x+)
		/// Winding matches canonical BuildSingleBrick outward winding.
		/// </summary>
		static void AddBrickFaceQuad( PolygonMesh mesh, Vector3 center, Vector3 size, Rotation localRot, int faceIndex, Material material )
		{
			float hx = size.x * 0.5f;
			float hy = size.y * 0.5f;
			float hz = size.z * 0.5f;

			Vector3[] localCorners = new Vector3[4];
			switch ( faceIndex )
			{
				case 0: // bottom (z-): v0,v3,v2,v1
					localCorners[0] = new Vector3( -hx, -hy, -hz );
					localCorners[1] = new Vector3( -hx,  hy, -hz );
					localCorners[2] = new Vector3(  hx,  hy, -hz );
					localCorners[3] = new Vector3(  hx, -hy, -hz );
					break;
				case 1: // top (z+): v4,v5,v6,v7
					localCorners[0] = new Vector3( -hx, -hy,  hz );
					localCorners[1] = new Vector3(  hx, -hy,  hz );
					localCorners[2] = new Vector3(  hx,  hy,  hz );
					localCorners[3] = new Vector3( -hx,  hy,  hz );
					break;
				case 2: // front (y-): v0,v1,v5,v4
					localCorners[0] = new Vector3( -hx, -hy, -hz );
					localCorners[1] = new Vector3(  hx, -hy, -hz );
					localCorners[2] = new Vector3(  hx, -hy,  hz );
					localCorners[3] = new Vector3( -hx, -hy,  hz );
					break;
				case 3: // back (y+): v2,v3,v7,v6
					localCorners[0] = new Vector3(  hx,  hy, -hz );
					localCorners[1] = new Vector3( -hx,  hy, -hz );
					localCorners[2] = new Vector3( -hx,  hy,  hz );
					localCorners[3] = new Vector3(  hx,  hy,  hz );
					break;
				case 4: // left (x-): v3,v0,v4,v7
					localCorners[0] = new Vector3( -hx,  hy, -hz );
					localCorners[1] = new Vector3( -hx, -hy, -hz );
					localCorners[2] = new Vector3( -hx, -hy,  hz );
					localCorners[3] = new Vector3( -hx,  hy,  hz );
					break;
				case 5: // right (x+): v1,v2,v6,v5
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
		/// Add a flat cap face with 4 explicit corners (canonical outward winding).
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
