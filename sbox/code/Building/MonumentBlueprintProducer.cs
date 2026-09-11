using System;
using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary>
	/// Massing-level specification for a monument-scale structure.
	/// Defines the major architectural volumes (nave, dome, towers, etc.)
	/// as parametric blocks. The <see cref="MonumentBlueprintProducer"/>
	/// converts this into a <see cref="Blueprint"/> piece list.
	///
	/// This is the "sketch" layer — it captures the overall form and
	/// proportions of a famous structure (St. Peter's Basilica, the
	/// Parthenon, etc.) without modeling every column or window. The
	/// resulting blueprint gives a vision-less agent a verifiable
	/// geometric footprint: bounds, piece counts, volume distribution.
	/// </summary>
	public class MonumentMassing
	{
		/// <summary> Display name (e.g. "StPetersBasilica"). </summary>
		public string Name { get; set; } = "Monument";

		/// <summary> World origin for the monument. </summary>
		public Vector3 Origin { get; set; }

		/// <summary> Yaw rotation (degrees). </summary>
		public float Rotation { get; set; }

		/// <summary> Cell size for the piece grid (inches). </summary>
		public float CellSize { get; set; } = 100f;

		/// <summary> Default wall height per story (inches). </summary>
		public float StoryHeight { get; set; } = 200f;

		/// <summary> Default floor thickness (inches). </summary>
		public float FloorThickness { get; set; } = 10f;

		/// <summary> Default wall material. </summary>
		public string WallMaterial { get; set; } = "materials/medieval/stone_wall.vmat";

		/// <summary> Default floor material. </summary>
		public string FloorMaterial { get; set; } = "materials/medieval/plaza.vmat";

		/// <summary> Default roof material. </summary>
		public string RoofMaterial { get; set; } = "materials/medieval/roof.vmat";

		/// <summary> Default column material. </summary>
		public string ColumnMaterial { get; set; } = "materials/medieval/stone_tower.vmat";

		/// <summary> Major volumes that compose the monument. </summary>
		public List<MonumentVolume> Volumes { get; set; } = new();

		/// <summary>
		/// Add a volume to the massing. Returns this for chaining.
		/// </summary>
		public MonumentMassing AddVolume( MonumentVolume vol )
		{
			Volumes.Add( vol );
			return this;
		}
	}

	/// <summary>
	/// A single major architectural volume in a monument massing.
	/// Each volume becomes a set of floor/wall/roof pieces in the blueprint.
	/// </summary>
	public class MonumentVolume
	{
		/// <summary> Display name (e.g. "Nave", "Dome", "WestTower"). </summary>
		public string Name { get; set; }

		/// <summary> Position relative to the monument origin (inches). </summary>
		public Vector3 Offset { get; set; }

		/// <summary> Width in cells (X axis). </summary>
		public int WidthCells { get; set; }

		/// <summary> Depth in cells (Y axis). </summary>
		public int DepthCells { get; set; }

		/// <summary> Number of stories (floors). </summary>
		public int Stories { get; set; } = 1;

		/// <summary> Volume type — controls how pieces are generated. </summary>
		public MonumentVolumeType Type { get; set; } = MonumentVolumeType.Box;

		/// <summary> Roof type for this volume. </summary>
		public MonumentRoofType RoofType { get; set; } = MonumentRoofType.Flat;

		/// <summary> Roof pitch in degrees (for gabled/hipped). </summary>
		public float RoofPitch { get; set; } = 30f;

		/// <summary> Material override (empty = use massing default). </summary>
		public string WallMaterialOverride { get; set; } = "";

		/// <summary> Floor material override. </summary>
		public string FloorMaterialOverride { get; set; } = "";

		/// <summary> Roof material override. </summary>
		public string RoofMaterialOverride { get; set; } = "";

		/// <summary> Dome radius in cells (for Dome type). </summary>
		public int DomeRadiusCells { get; set; } = 3;

		/// <summary> Column spacing in cells (0 = no columns). </summary>
		public int ColumnSpacing { get; set; } = 0;
	}

	/// <summary> Volume shape types. </summary>
	public enum MonumentVolumeType
	{
		/// <summary> Rectangular box (nave, transept, apse base). </summary>
		Box,

		/// <summary> Circular dome (drum + dome). </summary>
		Dome,

		/// <summary> Tower (tall narrow box with battlements). </summary>
		Tower,

		/// <summary> Colonnade (row of columns, no walls). </summary>
		Colonnade,
	}

	/// <summary> Roof types for monument volumes. </summary>
	public enum MonumentRoofType
	{
		Flat,
		Gabled,
		Hipped,
		Dome,
		Battlement,
	}

	/// <summary>
	/// Converts a <see cref="MonumentMassing"/> spec into a <see cref="Blueprint"/>
	/// piece list. This is the producer for monument-scale structures —
	/// it generates the same Blueprint format that BuildingGrammar and
	/// StyleGrammar produce, so the same validator, serializer, and
	/// executor pipeline works for monuments.
	///
	/// Two generation paths:
	/// - <see cref="Generate"/>: direct massing-to-pieces (the original
	///   path; kept for back-compat and for cases where the architectural
	///   grammar is not needed).
	/// - <see cref="GenerateFromGrammar"/>: massing -> architectural
	///   grammar (element tree) -> detail grammar (pieces). This is the
	///   Phase 4 path the professor recommended: instead of jumping
	///   straight from volumes to 14k pieces, decompose into bays,
	///   columns, arches, windows, drum/rings/lantern first, then emit.
	/// </summary>
	public static class MonumentBlueprintProducer
	{
		/// <summary>
		/// Generate a Blueprint from a MonumentMassing spec.
		/// Each volume contributes floor, wall, and roof pieces.
		/// </summary>
		public static Blueprint Generate( MonumentMassing massing )
		{
			var bp = new Blueprint
			{
				Name = massing.Name,
				Origin = massing.Origin,
				Rotation = massing.Rotation,
				CellSize = massing.CellSize,
				WallHeight = massing.StoryHeight,
				FloorThickness = massing.FloorThickness,
				WallMaterial = massing.WallMaterial,
				FloorMaterial = massing.FloorMaterial,
				RoofMaterial = massing.RoofMaterial,
				ColumnMaterial = massing.ColumnMaterial,
			};

			foreach ( var vol in massing.Volumes )
				GenerateVolume( bp, massing, vol );

			return bp;
		}

		/// <summary>
		/// Generate a Blueprint by first decomposing the massing into an
		/// architectural element tree (<see cref="MonumentGrammar"/>),
		/// then emitting pieces from that tree (the detail grammar). This
		/// is the Phase 4 path: Massing -> ArchitecturalGrammar ->
		/// DetailGrammar -> Blueprint.
		///
		/// The resulting blueprint carries provenance noting the grammar
		/// path, and is registered with <see cref="BlueprintRegistry"/>.
		/// </summary>
		public static Blueprint GenerateFromGrammar( MonumentMassing massing )
		{
			var bp = new Blueprint
			{
				Id = $"{massing.Name}_grammar",
				Name = massing.Name,
				Origin = massing.Origin,
				Rotation = massing.Rotation,
				CellSize = massing.CellSize,
				WallHeight = massing.StoryHeight,
				FloorThickness = massing.FloorThickness,
				WallMaterial = massing.WallMaterial,
				FloorMaterial = massing.FloorMaterial,
				RoofMaterial = massing.RoofMaterial,
				ColumnMaterial = massing.ColumnMaterial,
				Provenance = new BlueprintProvenance
				{
					Producer = "MonumentBlueprintProducer.GenerateFromGrammar",
					Preset = massing.Name,
					Parameters = new Dictionary<string, string>
					{
						["path"] = "massing->grammar->detail",
						["volumes"] = massing.Volumes.Count.ToString(),
					},
				},
			};

			var trees = MonumentGrammar.Decompose( massing );
			foreach ( var tree in trees )
				EmitElement( bp, massing, tree, volOffset: tree.Offset );

			bp.ComputeHash();
			BlueprintRegistry.Register( bp );
			return bp;
		}

		/// <summary>
		/// Recursively emit pieces for an <see cref="ArchitecturalElement"/>
		/// and its children. This is the detail grammar: it translates the
		/// architectural element tree into concrete BlueprintPieces.
		/// </summary>
		static void EmitElement( Blueprint bp, MonumentMassing m, ArchitecturalElement el, Vector3 volOffset )
		{
			float cs = m.CellSize;

			switch ( el.Kind )
			{
				case ArchitecturalElementKind.Floor:
					EmitSlab( bp, m, el, volOffset, "FLOOR", m.FloorMaterial );
					break;

				case ArchitecturalElementKind.Wall:
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = volOffset + el.Offset,
						PieceType = "WALL",
						Material = string.IsNullOrEmpty( el.MaterialOverride ) ? m.WallMaterial : el.MaterialOverride,
						Rotation = el.Rotation,
					} );
					break;

				case ArchitecturalElementKind.Column:
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = volOffset + el.Offset,
						PieceType = "COLUMN",
						Material = m.ColumnMaterial,
						Rotation = el.Rotation,
					} );
					break;

				case ArchitecturalElementKind.Arch:
				case ArchitecturalElementKind.Architrave:
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = volOffset + el.Offset,
						PieceType = "ARCH",
						Material = string.IsNullOrEmpty( el.MaterialOverride ) ? m.WallMaterial : el.MaterialOverride,
						Rotation = el.Rotation,
					} );
					break;

				case ArchitecturalElementKind.Window:
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = volOffset + el.Offset,
						PieceType = "WINDOW",
						Material = string.IsNullOrEmpty( el.MaterialOverride ) ? m.WallMaterial : el.MaterialOverride,
						Rotation = el.Rotation,
					} );
					break;

				case ArchitecturalElementKind.Door:
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = volOffset + el.Offset,
						PieceType = "DOOR",
						Material = string.IsNullOrEmpty( el.MaterialOverride ) ? m.WallMaterial : el.MaterialOverride,
						Rotation = el.Rotation,
					} );
					break;

				case ArchitecturalElementKind.Roof:
				case ArchitecturalElementKind.Merlon:
				case ArchitecturalElementKind.Pediment:
				case ArchitecturalElementKind.Cornice:
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = volOffset + el.Offset,
						PieceType = "ROOF",
						Material = string.IsNullOrEmpty( el.MaterialOverride ) ? m.RoofMaterial : el.MaterialOverride,
						Rotation = el.Rotation,
					} );
					break;

				case ArchitecturalElementKind.Drum:
					// Drum = ring of wall pieces around the circumference
					EmitDrum( bp, m, el, volOffset );
					break;

				case ArchitecturalElementKind.Rib:
					// Dome ring = ring of roof pieces
					EmitRing( bp, m, el, volOffset );
					break;

				case ArchitecturalElementKind.Lantern:
					// Lantern = small box on top
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = volOffset + el.Offset,
						PieceType = "COLUMN",
						Material = m.ColumnMaterial,
						Rotation = 0,
					} );
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = volOffset + el.Offset + new Vector3( 0, 0, el.Height ),
						PieceType = "ROOF",
						Material = m.RoofMaterial,
						Rotation = 0,
					} );
					break;

				case ArchitecturalElementKind.Bay:
				case ArchitecturalElementKind.Volume:
				case ArchitecturalElementKind.Statue:
					// Container/decorative — no direct piece, recurse into children
					break;
			}

			// Recurse into children
			foreach ( var child in el.Children )
				EmitElement( bp, m, child, volOffset );
		}

		static void EmitSlab( Blueprint bp, MonumentMassing m, ArchitecturalElement el, Vector3 volOffset, string type, string mat )
		{
			float cs = m.CellSize;
			int wCells = Math.Max( 1, (int)( el.Width / cs + 0.5f ) );
			int dCells = Math.Max( 1, (int)( el.Depth / cs + 0.5f ) );
			for ( int x = 0; x < wCells; x++ )
				for ( int y = 0; y < dCells; y++ )
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = volOffset + el.Offset + new Vector3( x * cs, y * cs, 0 ),
						PieceType = type,
						Material = mat,
						Rotation = 0,
					} );
		}

		static void EmitDrum( Blueprint bp, MonumentMassing m, ArchitecturalElement el, Vector3 volOffset )
		{
			float cs = m.CellSize;
			int r = Math.Max( 1, (int)( el.Width / ( 2 * cs ) + 0.5f ) );
			int stories = Math.Max( 1, (int)( el.Height / m.StoryHeight + 0.5f ) );
			for ( int s = 0; s < stories; s++ )
			{
				float z = s * m.StoryHeight;
				for ( int x = -r; x <= r; x++ )
					for ( int y = -r; y <= r; y++ )
					{
						int d2 = x * x + y * y;
						if ( d2 <= r * r && d2 > ( r - 1 ) * ( r - 1 ) )
							bp.Pieces.Add( new BlueprintPiece
							{
								Position = volOffset + el.Offset + new Vector3( x * cs, y * cs, z ),
								PieceType = "WALL",
								Material = m.WallMaterial,
								Rotation = 0,
							} );
					}
			}
		}

		static void EmitRing( Blueprint bp, MonumentMassing m, ArchitecturalElement el, Vector3 volOffset )
		{
			float cs = m.CellSize;
			int r = Math.Max( 1, (int)( el.Width / ( 2 * cs ) + 0.5f ) );
			for ( int x = -r; x <= r; x++ )
				for ( int y = -r; y <= r; y++ )
				{
					int d2 = x * x + y * y;
					if ( d2 <= r * r && d2 > ( r - 1 ) * ( r - 1 ) )
						bp.Pieces.Add( new BlueprintPiece
						{
							Position = volOffset + el.Offset + new Vector3( x * cs, y * cs, 0 ),
							PieceType = "ROOF",
							Material = m.RoofMaterial,
							Rotation = 0,
						} );
				}
		}

		static void GenerateVolume( Blueprint bp, MonumentMassing massing, MonumentVolume vol )
		{
			switch ( vol.Type )
			{
				case MonumentVolumeType.Box:
					GenerateBox( bp, massing, vol );
					break;
				case MonumentVolumeType.Dome:
					GenerateDome( bp, massing, vol );
					break;
				case MonumentVolumeType.Tower:
					GenerateTower( bp, massing, vol );
					break;
				case MonumentVolumeType.Colonnade:
					GenerateColonnade( bp, massing, vol );
					break;
			}
		}

		static string WallMat( MonumentMassing m, MonumentVolume v ) =>
			string.IsNullOrEmpty( v.WallMaterialOverride ) ? m.WallMaterial : v.WallMaterialOverride;

		static string FloorMat( MonumentMassing m, MonumentVolume v ) =>
			string.IsNullOrEmpty( v.FloorMaterialOverride ) ? m.FloorMaterial : v.FloorMaterialOverride;

		static string RoofMat( MonumentMassing m, MonumentVolume v ) =>
			string.IsNullOrEmpty( v.RoofMaterialOverride ) ? m.RoofMaterial : v.RoofMaterialOverride;

		// ── Box volume: rectangular building with floors, perimeter walls, roof ──

		static void GenerateBox( Blueprint bp, MonumentMassing massing, MonumentVolume vol )
		{
			float cs = massing.CellSize;
			float sh = massing.StoryHeight;
			float ft = massing.FloorThickness;
			var origin = vol.Offset;

			for ( int story = 0; story < vol.Stories; story++ )
			{
				float z = story * sh;

				// Floor
				for ( int x = 0; x < vol.WidthCells; x++ )
				{
					for ( int y = 0; y < vol.DepthCells; y++ )
					{
						bp.Pieces.Add( new BlueprintPiece
						{
							Position = origin + new Vector3( x * cs, y * cs, z ),
							PieceType = "FLOOR",
							Material = FloorMat( massing, vol ),
							Rotation = 0,
						} );
					}
				}

				// Perimeter walls
				for ( int x = 0; x < vol.WidthCells; x++ )
				{
					// North wall (y=0)
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = origin + new Vector3( x * cs, 0, z ),
						PieceType = "WALL",
						Material = WallMat( massing, vol ),
						Rotation = 0,
					} );
					// South wall (y=max)
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = origin + new Vector3( x * cs, (vol.DepthCells - 1) * cs, z ),
						PieceType = "WALL",
						Material = WallMat( massing, vol ),
						Rotation = 0,
					} );
				}

				for ( int y = 1; y < vol.DepthCells - 1; y++ )
				{
					// East wall (x=0)
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = origin + new Vector3( 0, y * cs, z ),
						PieceType = "WALL",
						Material = WallMat( massing, vol ),
						Rotation = 0,
					} );
					// West wall (x=max)
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = origin + new Vector3( (vol.WidthCells - 1) * cs, y * cs, z ),
						PieceType = "WALL",
						Material = WallMat( massing, vol ),
						Rotation = 0,
					} );
				}
			}

			// Roof
			GenerateRoof( bp, massing, vol, vol.Stories * sh );
		}

		// ── Dome volume: drum (cylinder of walls) + dome roof ──

		static void GenerateDome( Blueprint bp, MonumentMassing massing, MonumentVolume vol )
		{
			float cs = massing.CellSize;
			float sh = massing.StoryHeight;
			var origin = vol.Offset;
			int r = vol.DomeRadiusCells;

			// Drum floor
			for ( int x = -r; x <= r; x++ )
			{
				for ( int y = -r; y <= r; y++ )
				{
					if ( x * x + y * y <= r * r )
					{
						bp.Pieces.Add( new BlueprintPiece
						{
							Position = origin + new Vector3( x * cs, y * cs, 0 ),
							PieceType = "FLOOR",
							Material = FloorMat( massing, vol ),
							Rotation = 0,
						} );
					}
				}
			}

			// Drum walls (ring of wall pieces at radius r)
			for ( int story = 0; story < vol.Stories; story++ )
			{
				float z = story * sh;
				for ( int x = -r; x <= r; x++ )
				{
					for ( int y = -r; y <= r; y++ )
					{
						int distSq = x * x + y * y;
						if ( distSq <= r * r && distSq > ( r - 1 ) * ( r - 1 ) )
						{
							bp.Pieces.Add( new BlueprintPiece
							{
								Position = origin + new Vector3( x * cs, y * cs, z ),
								PieceType = "WALL",
								Material = WallMat( massing, vol ),
								Rotation = 0,
							} );
						}
					}
				}
			}

			// Dome roof — concentric rings shrinking toward the top
			float domeZ = vol.Stories * sh;
			int rings = Math.Max( 2, r );
			for ( int ring = 0; ring < rings; ring++ )
			{
				float ringZ = domeZ + ( ring / (float)rings ) * sh * 0.5f;
				int ringR = r - ( ring * r / rings );
				if ( ringR < 1 ) ringR = 1;

				for ( int x = -ringR; x <= ringR; x++ )
				{
					for ( int y = -ringR; y <= ringR; y++ )
					{
						if ( x * x + y * y <= ringR * ringR &&
							 x * x + y * y > ( ringR - 1 ) * ( ringR - 1 ) )
						{
							bp.Pieces.Add( new BlueprintPiece
							{
								Position = origin + new Vector3( x * cs, y * cs, ringZ ),
								PieceType = "ROOF",
								Material = RoofMat( massing, vol ),
								Rotation = 0,
							} );
						}
					}
				}
			}

			// Finial at the top
			bp.Pieces.Add( new BlueprintPiece
			{
				Position = origin + new Vector3( 0, 0, domeZ + sh * 0.6f ),
				PieceType = "COLUMN",
				Material = massing.ColumnMaterial,
				Rotation = 0,
			} );
		}

		// ── Tower volume: tall narrow box with battlement roof ──

		static void GenerateTower( Blueprint bp, MonumentMassing massing, MonumentVolume vol )
		{
			float cs = massing.CellSize;
			float sh = massing.StoryHeight;
			var origin = vol.Offset;

			for ( int story = 0; story < vol.Stories; story++ )
			{
				float z = story * sh;

				// Floor
				for ( int x = 0; x < vol.WidthCells; x++ )
				{
					for ( int y = 0; y < vol.DepthCells; y++ )
					{
						bp.Pieces.Add( new BlueprintPiece
						{
							Position = origin + new Vector3( x * cs, y * cs, z ),
							PieceType = "FLOOR",
							Material = FloorMat( massing, vol ),
							Rotation = 0,
						} );
					}
				}

				// Perimeter walls (all 4 sides)
				for ( int x = 0; x < vol.WidthCells; x++ )
				{
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = origin + new Vector3( x * cs, 0, z ),
						PieceType = "WALL",
						Material = WallMat( massing, vol ),
						Rotation = 0,
					} );
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = origin + new Vector3( x * cs, (vol.DepthCells - 1) * cs, z ),
						PieceType = "WALL",
						Material = WallMat( massing, vol ),
						Rotation = 0,
					} );
				}
				for ( int y = 1; y < vol.DepthCells - 1; y++ )
				{
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = origin + new Vector3( 0, y * cs, z ),
						PieceType = "WALL",
						Material = WallMat( massing, vol ),
						Rotation = 0,
					} );
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = origin + new Vector3( (vol.WidthCells - 1) * cs, y * cs, z ),
						PieceType = "WALL",
						Material = WallMat( massing, vol ),
						Rotation = 0,
					} );
				}
			}

			// Battlement roof — merlons (raised sections) around the perimeter
			float roofZ = vol.Stories * sh;
			for ( int x = 0; x < vol.WidthCells; x += 2 )
			{
				bp.Pieces.Add( new BlueprintPiece
				{
					Position = origin + new Vector3( x * cs, 0, roofZ ),
					PieceType = "ROOF",
					Material = RoofMat( massing, vol ),
					Rotation = 0,
				} );
				bp.Pieces.Add( new BlueprintPiece
				{
					Position = origin + new Vector3( x * cs, (vol.DepthCells - 1) * cs, roofZ ),
					PieceType = "ROOF",
					Material = RoofMat( massing, vol ),
					Rotation = 0,
				} );
			}
			for ( int y = 0; y < vol.DepthCells; y += 2 )
			{
				bp.Pieces.Add( new BlueprintPiece
				{
					Position = origin + new Vector3( 0, y * cs, roofZ ),
					PieceType = "ROOF",
					Material = RoofMat( massing, vol ),
					Rotation = 0,
				} );
				bp.Pieces.Add( new BlueprintPiece
				{
					Position = origin + new Vector3( (vol.WidthCells - 1) * cs, y * cs, roofZ ),
					PieceType = "ROOF",
					Material = RoofMat( massing, vol ),
					Rotation = 0,
				} );
			}
		}

		// ── Colonnade: row of columns, no walls ──

		static void GenerateColonnade( Blueprint bp, MonumentMassing massing, MonumentVolume vol )
		{
			float cs = massing.CellSize;
			float sh = massing.StoryHeight;
			var origin = vol.Offset;
			int spacing = Math.Max( 1, vol.ColumnSpacing );

			// Floor strip
			for ( int x = 0; x < vol.WidthCells; x++ )
			{
				bp.Pieces.Add( new BlueprintPiece
				{
					Position = origin + new Vector3( x * cs, 0, 0 ),
					PieceType = "FLOOR",
					Material = FloorMat( massing, vol ),
					Rotation = 0,
				} );
			}

			// Columns at regular spacing
			for ( int story = 0; story < vol.Stories; story++ )
			{
				float z = story * sh;
				for ( int x = 0; x < vol.WidthCells; x += spacing )
				{
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = origin + new Vector3( x * cs, 0, z ),
						PieceType = "COLUMN",
						Material = massing.ColumnMaterial,
						Rotation = 0,
					} );
				}
			}

			// Architrave (flat roof beam on top)
			float roofZ = vol.Stories * sh;
			for ( int x = 0; x < vol.WidthCells; x++ )
			{
				bp.Pieces.Add( new BlueprintPiece
				{
					Position = origin + new Vector3( x * cs, 0, roofZ ),
					PieceType = "ROOF",
					Material = RoofMat( massing, vol ),
					Rotation = 0,
				} );
			}
		}

		// ── Roof generation for box volumes ──

		static void GenerateRoof( Blueprint bp, MonumentMassing massing, MonumentVolume vol, float baseZ )
		{
			float cs = massing.CellSize;

			switch ( vol.RoofType )
			{
				case MonumentRoofType.Flat:
					// Flat roof: floor pieces on top
					for ( int x = 0; x < vol.WidthCells; x++ )
						for ( int y = 0; y < vol.DepthCells; y++ )
							bp.Pieces.Add( new BlueprintPiece
							{
								Position = vol.Offset + new Vector3( x * cs, y * cs, baseZ ),
								PieceType = "ROOF",
								Material = RoofMat( massing, vol ),
								Rotation = 0,
							} );
					break;

				case MonumentRoofType.Gabled:
					// Gabled roof: triangular profile along the width
					int halfW = vol.WidthCells / 2;
					float pitchHeight = ( vol.WidthCells * cs / 2f ) * MathF.Tan( vol.RoofPitch * MathF.PI / 180f );
					for ( int x = 0; x < vol.WidthCells; x++ )
					{
						int distFromCenter = Math.Abs( x - halfW );
						float z = baseZ + ( 1f - (float)distFromCenter / halfW ) * pitchHeight;
						for ( int y = 0; y < vol.DepthCells; y++ )
							bp.Pieces.Add( new BlueprintPiece
							{
								Position = vol.Offset + new Vector3( x * cs, y * cs, z ),
								PieceType = "ROOF",
								Material = RoofMat( massing, vol ),
								Rotation = 0,
							} );
					}
					break;

				case MonumentRoofType.Hipped:
					// Hipped roof: slopes on all 4 sides — pyramid
					int halfMax = Math.Max( vol.WidthCells, vol.DepthCells ) / 2;
					float hipHeight = ( halfMax * cs ) * MathF.Tan( vol.RoofPitch * MathF.PI / 180f );
					for ( int x = 0; x < vol.WidthCells; x++ )
					{
						for ( int y = 0; y < vol.DepthCells; y++ )
						{
							int dx = Math.Abs( x - vol.WidthCells / 2 );
							int dy = Math.Abs( y - vol.DepthCells / 2 );
							int dist = Math.Max( dx, dy );
							float z = baseZ + ( 1f - (float)dist / halfMax ) * hipHeight;
							bp.Pieces.Add( new BlueprintPiece
							{
								Position = vol.Offset + new Vector3( x * cs, y * cs, z ),
								PieceType = "ROOF",
								Material = RoofMat( massing, vol ),
								Rotation = 0,
							} );
						}
					}
					break;

				case MonumentRoofType.Battlement:
					// Battlement: merlons every other cell around perimeter
					for ( int x = 0; x < vol.WidthCells; x += 2 )
					{
						bp.Pieces.Add( new BlueprintPiece
						{
							Position = vol.Offset + new Vector3( x * cs, 0, baseZ ),
							PieceType = "ROOF",
							Material = RoofMat( massing, vol ),
							Rotation = 0,
						} );
						bp.Pieces.Add( new BlueprintPiece
						{
							Position = vol.Offset + new Vector3( x * cs, (vol.DepthCells - 1) * cs, baseZ ),
							PieceType = "ROOF",
							Material = RoofMat( massing, vol ),
							Rotation = 0,
						} );
					}
					for ( int y = 0; y < vol.DepthCells; y += 2 )
					{
						bp.Pieces.Add( new BlueprintPiece
						{
							Position = vol.Offset + new Vector3( 0, y * cs, baseZ ),
							PieceType = "ROOF",
							Material = RoofMat( massing, vol ),
							Rotation = 0,
						} );
						bp.Pieces.Add( new BlueprintPiece
						{
							Position = vol.Offset + new Vector3( (vol.WidthCells - 1) * cs, y * cs, baseZ ),
							PieceType = "ROOF",
							Material = RoofMat( massing, vol ),
							Rotation = 0,
						} );
					}
					break;

				case MonumentRoofType.Dome:
					// Dome roof on a box base — reuse dome generation
					var domeVol = new MonumentVolume
					{
						Offset = vol.Offset + new Vector3(
							( vol.WidthCells - 1 ) * cs / 2f,
							( vol.DepthCells - 1 ) * cs / 2f, 0 ),
						DomeRadiusCells = Math.Min( vol.WidthCells, vol.DepthCells ) / 2,
						Stories = 1,
						WallMaterialOverride = vol.WallMaterialOverride,
						RoofMaterialOverride = vol.RoofMaterialOverride,
					};
					GenerateDome( bp, massing, domeVol );
					break;
			}
		}

		// ── Preset: St. Peter's Basilica (massing level) ──

		/// <summary>
		/// Create a massing spec for St. Peter's Basilica at the massing level.
		/// The real basilica is ~190m long with a central dome, flanking towers,
		/// a long nave, and Bernini's colonnade embracing the piazza.
		///
		/// This is a simplified massing — correct proportions and major volumes,
		/// not every column and window. Scale: ~1 cell = 2.5m (100in).
		/// </summary>
		public static MonumentMassing StPetersBasilica( Vector3 origin, float rotation = 0 )
		{
			float cs = 100f; // 2.54m per cell
			float sh = 300f; // ~7.6m per story (taller than village buildings)

			var m = new MonumentMassing
			{
				Name = "StPetersBasilica",
				Origin = origin,
				Rotation = rotation,
				CellSize = cs,
				StoryHeight = sh,
				WallMaterial = "materials/medieval/stone_wall.vmat",
				FloorMaterial = "materials/medieval/plaza.vmat",
				RoofMaterial = "materials/medieval/roof.vmat",
				ColumnMaterial = "materials/medieval/stone_tower.vmat",
			};

			// Nave: long rectangular body (38 cells wide × 76 cells long = ~96m × 193m)
			m.AddVolume( new MonumentVolume
			{
				Name = "Nave",
				Offset = new Vector3( 0, 0, 0 ),
				WidthCells = 38,
				DepthCells = 76,
				Stories = 3,
				Type = MonumentVolumeType.Box,
				RoofType = MonumentRoofType.Gabled,
				RoofPitch = 20f,
			} );

			// Crossing + Dome: central dome over the crossing
			m.AddVolume( new MonumentVolume
			{
				Name = "Dome",
				Offset = new Vector3( 18 * cs, 38 * cs, 0 ),
				DomeRadiusCells = 12,
				Stories = 2, // drum height
				Type = MonumentVolumeType.Dome,
			} );

			// West façade (front of the basilica)
			m.AddVolume( new MonumentVolume
			{
				Name = "Facade",
				Offset = new Vector3( 0, -2 * cs, 0 ),
				WidthCells = 38,
				DepthCells = 2,
				Stories = 3,
				Type = MonumentVolumeType.Box,
				RoofType = MonumentRoofType.Flat,
			} );

			// North tower
			m.AddVolume( new MonumentVolume
			{
				Name = "NorthTower",
				Offset = new Vector3( -2 * cs, -2 * cs, 0 ),
				WidthCells = 4,
				DepthCells = 4,
				Stories = 5,
				Type = MonumentVolumeType.Tower,
			} );

			// South tower
			m.AddVolume( new MonumentVolume
			{
				Name = "SouthTower",
				Offset = new Vector3( 36 * cs, -2 * cs, 0 ),
				WidthCells = 4,
				DepthCells = 4,
				Stories = 5,
				Type = MonumentVolumeType.Tower,
			} );

			// Bernini's colonnade — north arm (embracing the piazza)
			m.AddVolume( new MonumentVolume
			{
				Name = "ColonnadeNorth",
				Offset = new Vector3( -20 * cs, -10 * cs, 0 ),
				WidthCells = 20,
				DepthCells = 1,
				Stories = 1,
				Type = MonumentVolumeType.Colonnade,
				ColumnSpacing = 2,
			} );

			// Bernini's colonnade — south arm
			m.AddVolume( new MonumentVolume
			{
				Name = "ColonnadeSouth",
				Offset = new Vector3( 38 * cs, -10 * cs, 0 ),
				WidthCells = 20,
				DepthCells = 1,
				Stories = 1,
				Type = MonumentVolumeType.Colonnade,
				ColumnSpacing = 2,
			} );

			// Apse (east end)
			m.AddVolume( new MonumentVolume
			{
				Name = "Apse",
				Offset = new Vector3( 14 * cs, 76 * cs, 0 ),
				WidthCells = 10,
				DepthCells = 6,
				Stories = 3,
				Type = MonumentVolumeType.Box,
				RoofType = MonumentRoofType.Hipped,
				RoofPitch = 25f,
			} );

			return m;
		}
	}
}
