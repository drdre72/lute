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
		const float CoreInset = 0.020f * M; // ~20mm — deep enough for joints to read as grooves

		/// <summary> How far each skin brick's bevel strips recede inward from the face plane. </summary>
		const float SkinDepth = CoreInset;

		/// <summary> Chamfer inset per edge on a skin brick's proud face. </summary>
		const float SkinBevel = 0.012f * M; // ~12mm

		/// <summary> Pixel size of the single_brick_* texture set (one tile per brick face). </summary>
		const float SkinTexSize = 256f;

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

			int missingSlots = 0;
			foreach ( var p in wallBricks )
			{
				var localCenter = wallRotInv * (p.Position - wallPos);
				float localYaw = p.Yaw - wallRotation;
				// -1 sentinel = no grid metadata. A silent default of 0 can
				// leave an exterior brick with no skin face, baked permanently
				// into the static mesh — fail loud and resolve by geometry.
				int gy;
				if ( p.GridSlot is { } slot )
				{
					gy = slot.GridY;
				}
				else
				{
					gy = -1;
					if ( missingSlots++ < 8 )
						Log.Warning( $"Lute: CollapsedWallMeshBuilder — WallBrick '{p.EntityId}' in '{p.ParentAssembly}' has no GridSlot; wythe will be inferred from geometry." );
				}
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

			if ( missingSlots > 8 )
				Log.Warning( $"Lute: CollapsedWallMeshBuilder — {missingSlots} WallBrick placements lacked GridSlot in this bake." );

			// ── 2. Determine front/back wythes from GridSlot.GridY ──
			// Bricks with unknown wythe (-1) can't participate in extrema
			// detection — a bogus value would corrupt min/max.
			var knownGy = localPlacements.Where( p => p.gridY >= 0 ).ToList();
			if ( knownGy.Count > 0 )
			{
				frontGridY = knownGy.Min( p => p.gridY );
				backGridY = knownGy.Max( p => p.gridY );
			}
			else
			{
				// No grid metadata at all: resolve every brick by geometry.
				frontGridY = 0;
				backGridY = 1;
			}

			// ── 3. Solid recessed core (centered on local origin) ──
			// Winding matches canonical BuildSingleBrick outward winding.
			float cx0 = minX - envCenter.x, cx1 = maxX - envCenter.x;
			float cy0 = minY + CoreInset - envCenter.y, cy1 = maxY - CoreInset - envCenter.y;
			float cz0 = minZ - envCenter.z, cz1 = maxZ - envCenter.z;
			AddSolidBox( mesh, cx0, cy0, cz0, cx1, cy1, cz1, coreMaterial );

			// Skin faces are collected here so their explicit texture
			// parameters can be applied after the global UV pass at the end.
			var skinFaces = new List<(FaceHandle face, Vector4 axisU, Vector4 axisV, Vector2 scale)>();
			// Seed from wall position+rotation so two identical walls don't
			// repeat the same per-brick texture-offset sequence.
			int uvSeed = Math.Abs( (int)wallPos.x * 7 + (int)wallPos.y * 13 + (int)wallPos.z * 3 + (int)wallRotation );

			// ── 4. Front brick skin (GridY == frontGridY, y- face) ──
			// Emit per-brick beveled skin boxes preserving actual placement
			// X/Z, half-bricks, and running-bond offsets from SpatialRegistry.
			foreach ( var (center, size, yaw, gy) in localPlacements )
			{
				if ( EffectiveGridY( gy, center.y, minY, maxY, frontGridY, backGridY ) != frontGridY )
					continue;
				var localRot = Rotation.FromYaw( yaw );
				var centeredCenter = center - envCenter;
				var faces = AddBeveledSkinBrick( mesh, centeredCenter, size, localRot, sign: -1f, brickMaterial );
				AddSkinTexParams( skinFaces, faces, size, localRot, mirror: false, uvSeed++ );
				frontSkinFaces++;
			}

			// ── 5. Back brick skin (GridY == backGridY, y+ face) ──
			foreach ( var (center, size, yaw, gy) in localPlacements )
			{
				if ( EffectiveGridY( gy, center.y, minY, maxY, frontGridY, backGridY ) != backGridY )
					continue;
				var localRot = Rotation.FromYaw( yaw );
				var centeredCenter = center - envCenter;
				var backFaces = AddBeveledSkinBrick( mesh, centeredCenter, size, localRot, sign: +1f, brickMaterial );
				AddSkinTexParams( skinFaces, backFaces, size, localRot, mirror: true, uvSeed++ );
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

			// Re-apply explicit face-aligned mapping on skin bricks — the
			// global call above falls back to a fixed XY projection for any
			// face whose UVs it cannot reconstruct, which smears the texture.
			// One single_brick tile is mapped per brick face.
			foreach ( var (face, axisU, axisV, scale) in skinFaces )
				mesh.SetFaceTextureParameters( face, axisU, axisV, scale );

			return mesh;
		}

		/// <summary>
		/// Resolve a brick's wythe index. A real gridY always wins; a brick
		/// with no grid metadata (-1) is assigned to the geometrically
		/// nearest exterior wythe. Erring toward skinning is safe: an
		/// interior brick that gets a spurious skin face is hidden inside
		/// the wall, while an exterior brick missing its face is a hole.
		/// </summary>
		static int EffectiveGridY( int gy, float centerY, float minY, float maxY, int frontGridY, int backGridY )
			=> gy >= 0 ? gy : (centerY - minY <= maxY - centerY ? frontGridY : backGridY);

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
		/// Add a beveled skin brick: a proud inset face at the wall plane plus
		/// 4 bevel strips receding inward to the base ring at SkinDepth.
		/// sign: -1 = front (y- face), +1 = back (y+ face).
		/// Returns the 5 face handles (index 0 = proud face, 1..4 = bevel
		/// strips in ring-edge order (0,1),(1,2),(2,3),(3,0)).
		///
		/// The uniform inset makes adjacent bevel strips share the corner
		/// diagonal exactly — 4 quads, no corner gaps. Joints read as V-groove
		/// channels with the recessed core showing through as mortar.
		/// </summary>
		static List<FaceHandle> AddBeveledSkinBrick( PolygonMesh mesh, Vector3 center, Vector3 size, Rotation localRot, float sign, Material material )
		{
			float hx = size.x * 0.5f;
			float hy = size.y * 0.5f;
			float hz = size.z * 0.5f;

			float bevel = Math.Min( SkinBevel, 0.4f * Math.Min( hx, hz ) );
			float depth = Math.Min( SkinDepth, hy ); // never recede past the brick's own back
			float yf = sign * hy;
			float yb = sign * ( hy - depth );
			float sx = hx - bevel, sz = hz - bevel;

			// Proud ring (inset rect at the wall plane) + base ring (full rect, recessed).
			var p = new Vector3[4]
			{
				new Vector3( -sx, yf, -sz ),
				new Vector3(  sx, yf, -sz ),
				new Vector3(  sx, yf,  sz ),
				new Vector3( -sx, yf,  sz ),
			};
			var b = new Vector3[4]
			{
				new Vector3( -hx, yb, -hz ),
				new Vector3(  hx, yb, -hz ),
				new Vector3(  hx, yb,  hz ),
				new Vector3( -hx, yb,  hz ),
			};
			for ( int i = 0; i < 4; i++ )
			{
				p[i] = localRot * p[i] + center;
				b[i] = localRot * b[i] + center;
			}

			var faces = new List<FaceHandle>( 5 );
			if ( sign < 0f )
			{
				// Front (y-): canonical v0,v1,v5,v4 winding — CCW viewed from -y.
				faces.Add( AddQuad( mesh, p[0], p[1], p[2], p[3] ) );
				// Strip for ring edge (a->b): (P_b, P_a, B_a, B_b).
				faces.Add( AddQuad( mesh, p[1], p[0], b[0], b[1] ) );
				faces.Add( AddQuad( mesh, p[2], p[1], b[1], b[2] ) );
				faces.Add( AddQuad( mesh, p[3], p[2], b[2], b[3] ) );
				faces.Add( AddQuad( mesh, p[0], p[3], b[3], b[0] ) );
			}
			else
			{
				// Back (y+): mirrored — reverse every winding.
				faces.Add( AddQuad( mesh, p[1], p[0], p[3], p[2] ) );
				faces.Add( AddQuad( mesh, b[1], b[0], p[0], p[1] ) );
				faces.Add( AddQuad( mesh, b[2], b[1], p[1], p[2] ) );
				faces.Add( AddQuad( mesh, b[3], b[2], p[2], p[3] ) );
				faces.Add( AddQuad( mesh, b[0], b[3], p[3], p[0] ) );
			}

			if ( material is not null )
				mesh.AssignMaterialToFaces( faces, material );

			return faces;
		}

		static FaceHandle AddQuad( PolygonMesh mesh, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3 )
		{
			return mesh.AddFace(
				mesh.AddVertex( v0 ),
				mesh.AddVertex( v1 ),
				mesh.AddVertex( v2 ),
				mesh.AddVertex( v3 ) );
		}

		/// <summary>
		/// Compute face-aligned texture parameters for one skin brick's faces:
		/// U along the brick's length, V up, one single_brick tile per face,
		/// plus a deterministic per-brick offset so identical bricks don't
		/// sample identical crops. mirror flips U for back faces so the
		/// texture isn't mirrored when viewed from outside.
		/// </summary>
		static void AddSkinTexParams(
			List<(FaceHandle face, Vector4 axisU, Vector4 axisV, Vector2 scale)> skinFaces,
			List<FaceHandle> faces, Vector3 size, Rotation localRot, bool mirror, int uvSeed )
		{
			var axisU = localRot * new Vector3( mirror ? -1f : 1f, 0f, 0f );
			float offU = (uvSeed * 73) % SkinTexSize;
			float offV = (uvSeed * 151) % SkinTexSize;
			var u4 = new Vector4( axisU, offU );
			var v4 = new Vector4( new Vector3( 0f, 0f, 1f ), offV );
			var scale = new Vector2( size.x / SkinTexSize, size.z / SkinTexSize );
			foreach ( var f in faces )
				skinFaces.Add( (f, u4, v4, scale) );
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
