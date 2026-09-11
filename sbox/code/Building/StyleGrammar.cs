using System;
using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary>
	/// Architectural style descriptor. Defines the rules that constrain
	/// a <see cref="StyleGrammar"/> so generated buildings read as belonging
	/// to a specific culture/era rather than being random room layouts.
	///
	/// Each style controls:
	/// - Façade symmetry (always symmetric, or allowed asymmetry)
	/// - Bay rhythm (number and width of vertical divisions on the façade)
	/// - Roof type (gabled, hipped, flat)
	/// - Roof pitch (angle in degrees)
	/// - Colonnade rhythm (column spacing for porticoes/arcades)
	/// - Wall material preference (stone, wood, brick)
	/// - Max room width (construction technology constraint)
	/// - Window spacing rule (rhythm along a wall run)
	/// </summary>
	public class ArchitecturalStyle
	{
		public string Name { get; set; }

		/// <summary> Force the façade to be symmetric about its center axis. </summary>
		public bool SymmetricFacade { get; set; } = true;

		/// <summary> Number of bays (vertical divisions) on the façade. 0 = unconstrained. </summary>
		public int BayCount { get; set; } = 3;

		/// <summary> Roof type: "gabled", "hipped", "flat". </summary>
		public string RoofType { get; set; } = "gabled";

		/// <summary> Roof pitch in degrees (0 = flat, 45 = steep gabled). </summary>
		public float RoofPitch { get; set; } = 30f;

		/// <summary> Colonnade rhythm: columns per bay (0 = no colonnade). </summary>
		public int ColumnsPerBay { get; set; } = 0;

		/// <summary> Default wall material. </summary>
		public string WallMaterial { get; set; } = "materials/medieval/stone_wall.vmat";

		/// <summary> Default floor material. </summary>
		public string FloorMaterial { get; set; } = "materials/medieval/plaza.vmat";

		/// <summary> Default roof material. </summary>
		public string RoofMaterial { get; set; } = "materials/medieval/roof.vmat";

		/// <summary> Default column material. </summary>
		public string ColumnMaterial { get; set; } = "materials/medieval/stone_tower.vmat";

		/// <summary> Max room width in cells (construction tech constraint). </summary>
		public int MaxRoomWidth { get; set; } = 6;

		/// <summary> Window spacing: place a window every N cells along a wall. 0 = no windows. </summary>
		public int WindowSpacing { get; set; } = 2;

		/// <summary> Wealth factor multiplier for this style (richer styles = more detail). </summary>
		public float WealthMultiplier { get; set; } = 1.0f;

		// ── Preset styles ──

		/// <summary> Romanesque: thick stone walls, small windows, round arches, symmetric. </summary>
		public static ArchitecturalStyle Romanesque => new()
		{
			Name = "Romanesque",
			SymmetricFacade = true,
			BayCount = 3,
			RoofType = "gabled",
			RoofPitch = 25f,
			ColumnsPerBay = 1,
			WallMaterial = "materials/medieval/stone_wall.vmat",
			FloorMaterial = "materials/medieval/plaza.vmat",
			RoofMaterial = "materials/medieval/roof.vmat",
			ColumnMaterial = "materials/medieval/stone_tower.vmat",
			MaxRoomWidth = 5,
			WindowSpacing = 3,
			WealthMultiplier = 1.2f,
		};

		/// <summary> Gothic: tall proportions, steep roof, large windows, flying buttress feel. </summary>
		public static ArchitecturalStyle Gothic => new()
		{
			Name = "Gothic",
			SymmetricFacade = true,
			BayCount = 5,
			RoofType = "gabled",
			RoofPitch = 45f,
			ColumnsPerBay = 2,
			WallMaterial = "materials/medieval/stone_wall.vmat",
			FloorMaterial = "materials/medieval/plaza.vmat",
			RoofMaterial = "materials/medieval/roof.vmat",
			ColumnMaterial = "materials/medieval/stone_tower.vmat",
			MaxRoomWidth = 4,
			WindowSpacing = 1, // large, frequent windows
			WealthMultiplier = 1.5f,
		};

		/// <summary> Classical: symmetric, colonnaded portico, flat or low roof, grand. </summary>
		public static ArchitecturalStyle Classical => new()
		{
			Name = "Classical",
			SymmetricFacade = true,
			BayCount = 5,
			RoofType = "hipped",
			RoofPitch = 15f,
			ColumnsPerBay = 2,
			WallMaterial = "materials/medieval/stone_wall.vmat",
			FloorMaterial = "materials/medieval/plaza.vmat",
			RoofMaterial = "materials/medieval/roof.vmat",
			ColumnMaterial = "materials/medieval/stone_tower.vmat",
			MaxRoomWidth = 6,
			WindowSpacing = 2,
			WealthMultiplier = 1.8f,
		};

		/// <summary> Vernacular: simple cottage, wood/stone, asymmetric allowed, steep roof. </summary>
		public static ArchitecturalStyle Vernacular => new()
		{
			Name = "Vernacular",
			SymmetricFacade = false,
			BayCount = 0, // unconstrained
			RoofType = "gabled",
			RoofPitch = 40f,
			ColumnsPerBay = 0,
			WallMaterial = "materials/medieval/wood.vmat",
			FloorMaterial = "materials/medieval/wood.vmat",
			RoofMaterial = "materials/medieval/roof.vmat",
			ColumnMaterial = "materials/medieval/wood.vmat",
			MaxRoomWidth = 4,
			WindowSpacing = 3,
			WealthMultiplier = 0.8f,
		};

		/// <summary> Fortress: thick stone, minimal windows, crenellated, functional. </summary>
		public static ArchitecturalStyle Fortress => new()
		{
			Name = "Fortress",
			SymmetricFacade = true,
			BayCount = 0,
			RoofType = "flat",
			RoofPitch = 0f,
			ColumnsPerBay = 0,
			WallMaterial = "materials/medieval/stone_wall.vmat",
			FloorMaterial = "materials/medieval/stone_wall.vmat",
			RoofMaterial = "materials/medieval/stone_wall.vmat",
			ColumnMaterial = "materials/medieval/stone_tower.vmat",
			MaxRoomWidth = 8,
			WindowSpacing = 0, // no windows — defensive
			WealthMultiplier = 1.0f,
		};
	}

	/// <summary>
	/// Style-constrained building generator. Produces a <see cref="Blueprint"/>
	/// that respects period-appropriate architectural rules:
	/// - Symmetric façades (when the style requires it)
	/// - Regular bay rhythm (façade divided into N bays)
	/// - Correct roof type and pitch
	/// - Colonnade placement (columns along a portico/arcade)
	/// - Room widths within construction-tech limits
	/// - Window spacing along walls
	///
	/// This sits as a constraint layer on top of the existing BuildingGrammar
	/// BSP interior logic: BSP still owns "how do the rooms connect," the
	/// StyleGrammar owns "does the outside read as belonging to this world."
	/// </summary>
	public class StyleGrammar
	{
		private readonly Random _rng;
		private const float M = 39.37f;

		public ArchitecturalStyle Style { get; set; }

		public StyleGrammar( ArchitecturalStyle style, Random rng = null )
		{
			Style = style;
			_rng = rng ?? new Random();
		}

		/// <summary>
		/// Generate a complete building blueprint with style-constrained
		/// exterior (façade, roof, colonnade) and BSP-interior rooms.
		/// </summary>
		public Blueprint Generate( Vector3 origin, float rotation,
			int baseWidth, int baseHeight, float wealthFactor,
			float cellSize = 100f, float wallHeight = 200f, float floorThickness = 10f )
		{
			float effectiveWealth = wealthFactor * Style.WealthMultiplier;
			int w = Math.Max( 2, (int)( baseWidth * effectiveWealth ) );
			int h = Math.Max( 2, (int)( baseHeight * effectiveWealth ) );

			// Clamp room dimensions to style's max
			w = Math.Min( w, Style.MaxRoomWidth * 2 );
			h = Math.Min( h, Style.MaxRoomWidth * 2 );

			var bp = new Blueprint
			{
				Name = $"{Style.Name}_Building",
				Origin = origin,
				Rotation = rotation,
				CellSize = cellSize,
				WallHeight = wallHeight,
				FloorThickness = floorThickness,
				WallMaterial = Style.WallMaterial,
				FloorMaterial = Style.FloorMaterial,
				RoofMaterial = Style.RoofMaterial,
			};

			// 1. Interior: use BuildingGrammar for room layout
			var grammar = new BuildingGrammar( _rng );
			var layout = grammar.GenerateLayout( w, h, effectiveWealth );
			AddLayoutToBlueprint( bp, layout, cellSize, wallHeight, floorThickness );

			// 2. Façade: enforce symmetry if required
			if ( Style.SymmetricFacade )
				ApplyFacadeSymmetry( bp, w, h, cellSize );

			// 3. Roof: add roof pieces based on style
			AddRoof( bp, w, h, cellSize, wallHeight );

			// 4. Colonnade: add columns if the style calls for them
			if ( Style.ColumnsPerBay > 0 )
				AddColonnade( bp, w, h, cellSize, wallHeight );

			// 5. Windows: add window openings (short wall stubs) along façades
			if ( Style.WindowSpacing > 0 )
				AddWindows( bp, w, h, cellSize, wallHeight );

			return bp;
		}

		void AddLayoutToBlueprint( Blueprint bp, Dictionary<Vector2Int, string> layout,
			float cellSize, float wallHeight, float floorThickness )
		{
			foreach ( var kvp in layout )
			{
				var piece = new BlueprintPiece
				{
					Position = new Vector3( kvp.Key.X * cellSize, kvp.Key.Y * cellSize, 0 ),
					PieceType = kvp.Value,
					Rotation = 0,
				};

				switch ( kvp.Value )
				{
					case "WALL":
						piece.Material = Style.WallMaterial;
						break;
					case "DOOR":
						piece.Material = Style.WallMaterial;
						break;
					case "FLOOR":
					default:
						piece.Material = Style.FloorMaterial;
						break;
				}

				bp.Pieces.Add( piece );
			}
		}

		/// <summary>
		/// Enforce façade symmetry: ensure the front (south-facing) wall
		/// has a symmetric arrangement of doors and windows. If the BSP
		/// produced an asymmetric front, mirror the door position to the
		/// center and add matching wall pieces on both sides.
		/// </summary>
		void ApplyFacadeSymmetry( Blueprint bp, int w, int h, float cellSize )
		{
			// Find all WALL/DOOR pieces on the front row (y = 0)
			var frontRow = new List<BlueprintPiece>();
			foreach ( var p in bp.Pieces )
			{
				if ( Math.Abs( p.Position.y ) < cellSize * 0.5f )
					frontRow.Add( p );
			}

			// Find the center column
			int centerX = w / 2;

			// Ensure there's a door at or near the center
			bool hasCenterDoor = false;
			foreach ( var p in frontRow )
			{
				if ( p.PieceType == "DOOR" )
				{
					int cellX = (int)( p.Position.x / cellSize + 0.5f );
					if ( cellX == centerX )
					{
						hasCenterDoor = true;
						break;
					}
				}
			}

			if ( !hasCenterDoor && frontRow.Count > 0 )
			{
				// Move the first door to center, or add one
				var door = frontRow.Find( p => p.PieceType == "DOOR" );
				if ( door.PieceType == "DOOR" )
				{
					// Move existing door to center
					int idx = bp.Pieces.IndexOf( door );
					var moved = door;
					moved.Position = new Vector3( centerX * cellSize, 0, 0 );
					bp.Pieces[idx] = moved;
				}
				else
				{
					// Add a center door
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = new Vector3( centerX * cellSize, 0, 0 ),
						PieceType = "DOOR",
						Material = Style.WallMaterial,
						Rotation = 0,
					} );
				}
			}
		}

		/// <summary>
		/// Add a roof based on the style's roof type and pitch.
		/// - Gabled: two sloped planes along the long axis
		/// - Hipped: four sloped planes
		/// - Flat: single flat roof
		/// </summary>
		void AddRoof( Blueprint bp, int w, int h, float cellSize, float wallHeight )
		{
			float roofZ = wallHeight + cellSize * 0.5f; // sit on top of walls
			float roofPitch = Style.RoofPitch * (float)Math.PI / 180f;
			float roofHeight = (w * cellSize * 0.5f) * (float)Math.Tan( roofPitch );
			int ridgeX = w / 2;

			switch ( Style.RoofType )
			{
				case "flat":
					// Single flat roof spanning the building
					for ( int x = 0; x < w; x++ )
					{
						for ( int y = 0; y < h; y++ )
						{
							bp.Pieces.Add( new BlueprintPiece
							{
								Position = new Vector3( x * cellSize, y * cellSize, roofZ ),
								PieceType = "ROOF",
								Material = Style.RoofMaterial,
								Rotation = 0,
							} );
						}
					}
					break;

				case "gabled":
					// Two sloped planes: ridge along the long axis (Y)
					// Left slope (x < center) and right slope (x > center)
					for ( int x = 0; x < w; x++ )
					{
						float distFromRidge = Math.Abs( x - ridgeX ) * cellSize;
						float z = roofZ + roofHeight - distFromRidge * (float)Math.Tan( roofPitch );
						for ( int y = 0; y < h; y++ )
						{
							bp.Pieces.Add( new BlueprintPiece
							{
								Position = new Vector3( x * cellSize, y * cellSize, z ),
								PieceType = "ROOF",
								Material = Style.RoofMaterial,
								Rotation = 0,
							} );
						}
					}
					break;

				case "hipped":
					// Four sloped planes: ridge shrinks to a point at the center
					for ( int x = 0; x < w; x++ )
					{
						for ( int y = 0; y < h; y++ )
						{
							float dx = Math.Abs( x - ridgeX ) * cellSize;
							float dy = Math.Abs( y - h / 2 ) * cellSize;
							float maxDist = Math.Max( dx, dy );
							float z = roofZ + roofHeight - maxDist * (float)Math.Tan( roofPitch );
							bp.Pieces.Add( new BlueprintPiece
							{
								Position = new Vector3( x * cellSize, y * cellSize, z ),
								PieceType = "ROOF",
								Material = Style.RoofMaterial,
								Rotation = 0,
							} );
						}
					}
					break;
			}
		}

		/// <summary>
		/// Add a colonnade (row of columns) along the front façade.
		/// Columns are placed at regular intervals based on BayCount and
		/// ColumnsPerBay.
		/// </summary>
		void AddColonnade( Blueprint bp, int w, int h, float cellSize, float wallHeight )
		{
			if ( Style.BayCount <= 0 || Style.ColumnsPerBay <= 0 )
				return;

			float colSpacing = (w * cellSize) / (Style.BayCount * Style.ColumnsPerBay);
			float colY = -cellSize * 0.5f; // just in front of the façade

			for ( int i = 0; i <= Style.BayCount * Style.ColumnsPerBay; i++ )
			{
				float x = -w * cellSize * 0.5f + i * colSpacing;
				bp.Pieces.Add( new BlueprintPiece
				{
					Position = new Vector3( x, colY, wallHeight * 0.5f ),
					PieceType = "COLUMN",
					Material = Style.ColumnMaterial,
					Rotation = 0,
					Size = new Vector3( cellSize * 0.3f, cellSize * 0.3f, wallHeight ),
				} );
			}
		}

		/// <summary>
		/// Add window openings: short wall stubs along the side walls
		/// at regular intervals. These are rendered as low wall sections
		/// so the opening above is visible (same pattern as DOOR pieces).
		/// </summary>
		void AddWindows( Blueprint bp, int w, int h, float cellSize, float wallHeight )
		{
			if ( Style.WindowSpacing <= 0 )
				return;

			// Windows on the east wall (x = w-1) and west wall (x = 0)
			for ( int y = Style.WindowSpacing; y < h; y += Style.WindowSpacing )
			{
				if ( y >= h - 1 ) break; // don't place at corners

				// East wall window
				bp.Pieces.Add( new BlueprintPiece
				{
					Position = new Vector3( (w - 1) * cellSize, y * cellSize, 0 ),
					PieceType = "DOOR", // reuse door stub for window opening
					Material = Style.WallMaterial,
					Rotation = 0,
				} );

				// West wall window (if symmetric)
				if ( Style.SymmetricFacade )
				{
					bp.Pieces.Add( new BlueprintPiece
					{
						Position = new Vector3( 0, y * cellSize, 0 ),
						PieceType = "DOOR",
						Material = Style.WallMaterial,
						Rotation = 0,
					} );
				}
			}
		}
	}
}
