using System;
using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary>
	/// A single build task in the village queue. Each task produces one
	/// structure (a wall segment, road section, building, well, etc.)
	/// that the VillageBuilder constructs piece by piece.
	/// </summary>
	public class VillageBuildTask
	{
		/// <summary> World position of the structure's origin. </summary>
		public Vector3 Position;

		/// <summary> Yaw rotation in degrees (facing direction). </summary>
		public float Rotation;

		/// <summary>
		/// Task type: "wall", "gate", "road", "well", "market_square",
			/// "cottage", "shop", "smithy", "tavern", "chapel", "storage",
		/// "guardhouse".
		/// </summary>
		public string TaskType;

		/// <summary> Display name for logs. </summary>
		public string Name;

		/// <summary> Wealth factor for BuildingGrammar layouts (buildings only). </summary>
		public float WealthFactor = 1.0f;

		/// <summary> Base grid width for BuildingGrammar (buildings only). </summary>
		public int BaseWidth = 4;

		/// <summary> Base grid height for BuildingGrammar (buildings only). </summary>
		public int BaseHeight = 4;

		/// <summary> Layout seed for deterministic BuildingGrammar output. </summary>
		public int LayoutSeed = 0;

		/// <summary>
		/// Architectural style for this building. If null, uses plain
		/// BuildingGrammar (no style constraints). If set, uses StyleGrammar
		/// for period-coherent exterior (symmetry, roof, colonnade, windows).
		/// </summary>
		public ArchitecturalStyle Style;

		/// <summary>
		/// Build priority. Lower = built first. Infrastructure (walls,
		/// roads, well) has priority 0, buildings have priority 10+.
		/// </summary>
		public int Priority = 10;

		/// <summary> Runtime: 0=pending, 1=in-progress, 2=complete. </summary>
		public int Status = 0;

		/// <summary> Runtime: number of pieces placed so far (for resume). </summary>
		public int PiecesPlaced = 0;

		/// <summary> Runtime: total pieces in this task's layout. </summary>
		public int TotalPieces = 0;

		/// <summary>
		/// Multi-builder mode: which builder ID is assigned this task.
		/// -1 = not yet assigned (single-builder mode or pre-partition).
		/// Set by VillageBuilder.PartitionTasks during OnStart.
		/// </summary>
		public int BuilderAssignment = -1;
	}

	/// <summary>
	/// Village-scale layout generator. Produces a list of
	/// <see cref="VillageBuildTask"/>s that, when built in priority order,
	/// construct a medieval village with:
	/// - Stone curtain wall with 2 gates (N and S)
	/// - Main road (N-S) + cross streets (E-W)
	/// - Central well and market square
	/// - 30-50 buildings (cottages, shops, smithy, tavern, chapel, etc.)
	///
	/// Scale matches the Neutral Market monument (~280m wall-to-wall).
	/// Designed for an 8-hour build by a single NPC at ~6s per piece.
	/// </summary>
	public class VillageGrammar
	{
		private readonly Random _rng;

		// Scale constants (matching LuteMonumentBuilder)
		private const float M = 39.37f; // units per meter

		/// <summary> Half-width to outer face of the stone wall. </summary>
		public float WallOuterHalfWidth { get; set; } = 140f * M;

		/// <summary> Wall height. </summary>
		public float WallHeight { get; set; } = 8f * M;

		/// <summary> Main road half-width (from center line). </summary>
		public float MainRoadHalfWidth { get; set; } = 5f * M;

		/// <summary> Cross street half-width. </summary>
		public float CrossStreetHalfWidth { get; set; } = 3f * M;

		/// <summary> Grid cell size for BuildingGrammar (inches). </summary>
		public float CellSize { get; set; } = 100f;

		public VillageGrammar( Random rng = null )
		{
			_rng = rng ?? new Random();
		}

		/// <summary>
		/// Generate the full village layout as an ordered list of build tasks.
		/// Tasks are sorted by priority (infrastructure first, then buildings).
		/// </summary>
		public List<VillageBuildTask> GenerateLayout( Vector3 center )
		{
			var tasks = new List<VillageBuildTask>();
			int seedCounter = 1000;

			// ── Priority 0: Stone wall segments ──
			GenerateWallTasks( center, tasks, ref seedCounter );

			// ── Priority 1: Gates (N and S) ──
			GenerateGateTasks( center, tasks, ref seedCounter );

			// ── Priority 2: Roads ──
			GenerateRoadTasks( center, tasks, ref seedCounter );

			// ── Priority 3: Well ──
			tasks.Add( new VillageBuildTask
			{
				Position = center,
				Rotation = 0,
				TaskType = "well",
				Name = "VillageWell",
				Priority = 3,
				LayoutSeed = seedCounter++,
			} );

			// ── Priority 4: Market square ──
			tasks.Add( new VillageBuildTask
			{
				Position = center + new Vector3( 0, 20f * M, 0 ),
				Rotation = 0,
				TaskType = "market_square",
				Name = "VillageMarketSquare",
				Priority = 4,
				LayoutSeed = seedCounter++,
			} );

			// ── Priority 10+: Buildings along roads ──
			GenerateBuildingTasks( center, tasks, ref seedCounter );

			// Sort by priority (infrastructure first), then by distance from center
			tasks.Sort( ( a, b ) =>
			{
				int cmp = a.Priority.CompareTo( b.Priority );
				if ( cmp != 0 ) return cmp;
				// Within same priority, sort by distance from center
				float da = Vector3.DistanceBetween( a.Position, center );
				float db = Vector3.DistanceBetween( b.Position, center );
				return da.CompareTo( db );
			} );

			return tasks;
		}

		void GenerateWallTasks( Vector3 center, List<VillageBuildTask> tasks, ref int seed )
		{
			// Stone curtain wall around the perimeter. Each wall segment
			// is ~10m long. With 280m perimeter per side, that's 28 segments
			// per side x 4 sides = 112 segments. But we skip the gate openings.
			float segLen = 10f * M;
			float half = WallOuterHalfWidth;
			int segsPerSide = (int)( ( half * 2f ) / segLen );

			// North wall (y = +half), skip gate opening at center
			for ( int i = 0; i < segsPerSide; i++ )
			{
				float x = -half + segLen * (i + 0.5f);
				float y = half;
				// Skip the gate opening (center 20m)
				if ( Math.Abs( x ) < 12f * M ) continue;

				tasks.Add( new VillageBuildTask
				{
					Position = center + new Vector3( x, y, 0 ),
					Rotation = 0,
					TaskType = "wall",
					Name = $"Wall_N_{i}",
					Priority = 0,
					LayoutSeed = seed++,
				} );
			}

			// South wall (y = -half), skip gate opening at center
			for ( int i = 0; i < segsPerSide; i++ )
			{
				float x = -half + segLen * (i + 0.5f);
				float y = -half;
				if ( Math.Abs( x ) < 12f * M ) continue;

				tasks.Add( new VillageBuildTask
				{
					Position = center + new Vector3( x, y, 0 ),
					Rotation = 0,
					TaskType = "wall",
					Name = $"Wall_S_{i}",
					Priority = 0,
					LayoutSeed = seed++,
				} );
			}

			// East wall (x = +half)
			for ( int i = 0; i < segsPerSide; i++ )
			{
				float y = -half + segLen * (i + 0.5f);
				float x = half;

				tasks.Add( new VillageBuildTask
				{
					Position = center + new Vector3( x, y, 0 ),
					Rotation = 90,
					TaskType = "wall",
					Name = $"Wall_E_{i}",
					Priority = 0,
					LayoutSeed = seed++,
				} );
			}

			// West wall (x = -half)
			for ( int i = 0; i < segsPerSide; i++ )
			{
				float y = -half + segLen * (i + 0.5f);
				float x = -half;

				tasks.Add( new VillageBuildTask
				{
					Position = center + new Vector3( x, y, 0 ),
					Rotation = 90,
					TaskType = "wall",
					Name = $"Wall_W_{i}",
					Priority = 0,
					LayoutSeed = seed++,
				} );
			}
		}

		void GenerateGateTasks( Vector3 center, List<VillageBuildTask> tasks, ref int seed )
		{
			// North gate
			tasks.Add( new VillageBuildTask
			{
				Position = center + new Vector3( 0, WallOuterHalfWidth, 0 ),
				Rotation = 0,
				TaskType = "gate",
				Name = "Gate_North",
				Priority = 1,
				LayoutSeed = seed++,
			} );

			// South gate
			tasks.Add( new VillageBuildTask
			{
				Position = center + new Vector3( 0, -WallOuterHalfWidth, 0 ),
				Rotation = 180,
				TaskType = "gate",
				Name = "Gate_South",
				Priority = 1,
				LayoutSeed = seed++,
			} );
		}

		void GenerateRoadTasks( Vector3 center, List<VillageBuildTask> tasks, ref int seed )
		{
			float half = WallOuterHalfWidth;
			float roadLen = half * 2f;
			int roadSegs = (int)( roadLen / (10f * M) );

			// Main road N-S (runs through center, connects gates)
			for ( int i = 0; i < roadSegs; i++ )
			{
				float y = -half + 10f * M * (i + 0.5f);
				tasks.Add( new VillageBuildTask
				{
					Position = center + new Vector3( 0, y, 0 ),
					Rotation = 0,
					TaskType = "road",
					Name = $"Road_Main_{i}",
					Priority = 2,
					LayoutSeed = seed++,
				} );
			}

			// Cross streets E-W (2 streets at 1/3 and 2/3 height)
			for ( int street = 0; street < 2; street++ )
			{
				float y = -half * 0.5f + half * street;
				for ( int i = 0; i < roadSegs; i++ )
				{
					float x = -half + 10f * M * (i + 0.5f);
					tasks.Add( new VillageBuildTask
					{
						Position = center + new Vector3( x, y, 0 ),
						Rotation = 90,
						TaskType = "road",
						Name = $"Road_Cross{street}_{i}",
						Priority = 2,
						LayoutSeed = seed++,
					} );
				}
			}
		}

		void GenerateBuildingTasks( Vector3 center, List<VillageBuildTask> tasks, ref int seed )
		{
			float half = WallOuterHalfWidth;
			float inset = 15f * M; // buildings start 15m inside the wall

			// Building types with weights (higher weight = more common)
			var buildingTypes = new[]
			{
				("cottage", 20),    // most common
				("cottage", 20),
				("shop", 5),
				("storage", 5),
				("guardhouse", 3),
			};

			// Special buildings (fixed positions)
			tasks.Add( new VillageBuildTask
			{
				Position = center + new Vector3( 25f * M, 30f * M, 0 ),
				TaskType = "smithy", Name = "Smithy",
				Priority = 10, WealthFactor = 2.0f,
				BaseWidth = 5, BaseHeight = 5,
				LayoutSeed = seed++,
				Style = ArchitecturalStyle.Vernacular,
			} );

			tasks.Add( new VillageBuildTask
			{
				Position = center + new Vector3( -25f * M, 30f * M, 0 ),
				TaskType = "tavern", Name = "Tavern",
				Priority = 10, WealthFactor = 2.5f,
				BaseWidth = 6, BaseHeight = 5,
				LayoutSeed = seed++,
				Style = ArchitecturalStyle.Vernacular,
			} );

			tasks.Add( new VillageBuildTask
			{
				Position = center + new Vector3( 0, -60f * M, 0 ),
				TaskType = "chapel", Name = "Chapel",
				Priority = 10, WealthFactor = 3.0f,
				BaseWidth = 5, BaseHeight = 7,
				LayoutSeed = seed++,
				Style = ArchitecturalStyle.Gothic,
			} );

			// Generate cottages and shops along roads
			// Place buildings in rows parallel to the main road, offset
			// from the road edge. Two columns (east and west of main road).
			float[] rowOffsets = { 15f * M, 35f * M, 55f * M, 75f * M, 100f * M };
			int buildingsPerRow = 6;
			float buildingSpacing = 25f * M;
			int buildingIndex = 0;

			foreach ( float rowOffset in rowOffsets )
			{
				// East side
				for ( int i = 0; i < buildingsPerRow; i++ )
				{
					float y = -half + inset + buildingSpacing * (i + 1);
					if ( Math.Abs( y ) < 15f * M ) continue; // skip center (well/market)
					// Skip buildings on cross-street Y lines (roads at ±half*0.5)
					// Margin = road half-width + building half-size + clearance
					float crossStreetMargin = 5f * M + 2f * M + 2f * M; // 9m total
					if ( Math.Abs( y - (-half * 0.5f) ) < crossStreetMargin ) continue;
					if ( Math.Abs( y - (half * 0.5f) ) < crossStreetMargin ) continue;
					// Skip buildings near special building positions (Chapel, Smithy, Tavern)
					float specialMargin = 15f * M; // 15m clearance from special buildings
					if ( Math.Abs( y - (-60f * M) ) < specialMargin ) continue; // Chapel at y=-60m
					if ( Math.Abs( y - (30f * M) ) < specialMargin ) continue; // Smithy/Tavern at y=30m

					// Pick building type by weight
					var (btype, _) = PickWeighted( buildingTypes );
					float wealth = btype == "cottage" ? 1.0f + (float)(_rng.NextDouble() * 0.5) : 1.5f;

					tasks.Add( new VillageBuildTask
					{
						Position = center + new Vector3( rowOffset, y, 0 ),
						Rotation = 270, // face west (toward road)
						TaskType = btype,
						Name = $"{btype}_{buildingIndex}",
						Priority = 10 + buildingIndex / 10,
						WealthFactor = wealth,
						BaseWidth = 4,
						BaseHeight = 4,
						LayoutSeed = seed++,
						Style = ArchitecturalStyle.Vernacular,
					} );
					buildingIndex++;
				}

				// West side
				for ( int i = 0; i < buildingsPerRow; i++ )
				{
					float y = -half + inset + buildingSpacing * (i + 1);
					if ( Math.Abs( y ) < 15f * M ) continue;
					// Skip buildings on cross-street Y lines (roads at ±half*0.5)
					float crossStreetMargin = 5f * M + 2f * M + 2f * M; // 9m total
					if ( Math.Abs( y - (-half * 0.5f) ) < crossStreetMargin ) continue;
					if ( Math.Abs( y - (half * 0.5f) ) < crossStreetMargin ) continue;
					// Skip buildings near special building positions (Chapel, Smithy, Tavern)
					float specialMargin = 15f * M;
					if ( Math.Abs( y - (-60f * M) ) < specialMargin ) continue; // Chapel at y=-60m
					if ( Math.Abs( y - (30f * M) ) < specialMargin ) continue; // Smithy/Tavern at y=30m

					var (btype, _) = PickWeighted( buildingTypes );
					float wealth = btype == "cottage" ? 1.0f + (float)(_rng.NextDouble() * 0.5) : 1.5f;

					tasks.Add( new VillageBuildTask
					{
						Position = center + new Vector3( -rowOffset, y, 0 ),
						Rotation = 90, // face east (toward road)
						TaskType = btype,
						Name = $"{btype}_{buildingIndex}",
						Priority = 10 + buildingIndex / 10,
						WealthFactor = wealth,
						BaseWidth = 4,
						BaseHeight = 4,
						LayoutSeed = seed++,
						Style = ArchitecturalStyle.Vernacular,
					} );
					buildingIndex++;
				}
			}

			// Buildings along cross streets (north-facing and south-facing)
			float[] crossY = { -half * 0.5f, half * 0.5f };
			foreach ( float cy in crossY )
			{
				for ( int i = 0; i < 8; i++ )
				{
					float x = -half + inset + buildingSpacing * (i + 1);
					if ( Math.Abs( x ) < 15f * M ) continue;

					var (btype, _) = PickWeighted( buildingTypes );
					float wealth = btype == "cottage" ? 1.0f + (float)(_rng.NextDouble() * 0.5) : 1.5f;

					tasks.Add( new VillageBuildTask
					{
						Position = center + new Vector3( x, cy + 12f * M, 0 ),
						Rotation = 180, // face south
						TaskType = btype,
						Name = $"{btype}_{buildingIndex}",
						Priority = 10 + buildingIndex / 10,
						WealthFactor = wealth,
						BaseWidth = 4,
						BaseHeight = 4,
						LayoutSeed = seed++,
						Style = ArchitecturalStyle.Vernacular,
					} );
					buildingIndex++;

					tasks.Add( new VillageBuildTask
					{
						Position = center + new Vector3( x, cy - 12f * M, 0 ),
						Rotation = 0, // face north
						TaskType = btype,
						Name = $"{btype}_{buildingIndex}",
						Priority = 10 + buildingIndex / 10,
						WealthFactor = wealth,
						BaseWidth = 4,
						BaseHeight = 4,
						LayoutSeed = seed++,
						Style = ArchitecturalStyle.Vernacular,
					} );
					buildingIndex++;
				}
			}
		}

		(string, int) PickWeighted( (string, int)[] weighted )
		{
			int total = 0;
			foreach ( var (_, w) in weighted ) total += w;
			int r = _rng.Next( total );
			int acc = 0;
			foreach ( var (name, w) in weighted )
			{
				acc += w;
				if ( r < acc ) return (name, w);
			}
			return weighted[0];
		}
	}
}
