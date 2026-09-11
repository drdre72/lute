using System;
using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary> Integer 2D grid position. Kept in one place to avoid duplicate-type errors across files. </summary>
	public struct Vector2Int
	{
		public int X;
		public int Y;
		public Vector2Int( int x, int y ) { X = x; Y = y; }
		public override string ToString() => $"{X},{Y}";
	}

	/// <summary> Integer rectangle. </summary>
	public struct RectInt
	{
		public int X, Y, Width, Height;
		public RectInt( int x, int y, int w, int h ) { X = x; Y = y; Width = w; Height = h; }
	}

	/// <summary>
	/// BSP-style room-subdivided building layout generator. A higher
	/// WealthFactor produces more interior walls (more rooms)
	/// instead of just a bigger box. Output is a dictionary of grid cell
	/// -> piece type ("WALL", "FLOOR", "DOOR").
	///
	/// The perimeter pass runs first and is untouched by subdivision, so
	/// exterior walls/door logic from the original single-room version
	/// still works. Subdivision only touches the interior rect, and
	/// <c>depth</c> (not room count directly) scales with wealth — depth
	/// compounds (each level roughly doubles room count) and gives a
	/// natural falloff instead of needing to hand-tune a room count.
	/// </summary>
	public class BuildingGrammar
	{
		// Rooms smaller than this on either axis are never split further.
		private const int MinRoomSize = 3;

		private readonly Random _rng;

		/// <summary>
		/// Number of unreachable rooms that <see cref="EnsureReachability"/> repaired
		/// during the last <see cref="GenerateLayout"/> call. 0 = layout was already
		/// fully connected. Read this after calling GenerateLayout to log/verify.
		/// </summary>
		public int LastRepairCount { get; private set; }

		/// <summary>
		/// Construct with a shared RNG (visual variety per building). Pass
		/// a seeded <see cref="Random"/> if you need deterministic/reproducible
		/// layouts (e.g. for a specific quest building).
		/// </summary>
		public BuildingGrammar( Random rng = null )
		{
			_rng = rng ?? new Random();
		}

		/// <summary>
		/// Generate a layout of the given base size scaled by wealthFactor.
		/// Cell values: "WALL", "FLOOR", "DOOR".
		/// </summary>
		public Dictionary<Vector2Int, string> GenerateLayout( int baseWidth, int baseHeight, float wealthFactor )
		{
			var layout = new Dictionary<Vector2Int, string>();
			int width = (int)(baseWidth * wealthFactor);
			int height = (int)(baseHeight * wealthFactor);
			if ( width < 3 ) width = 3;
			if ( height < 3 ) height = 3;

			// Outer perimeter
			for ( int x = 0; x < width; x++ )
			{
				for ( int y = 0; y < height; y++ )
				{
					var pos = new Vector2Int( x, y );
					layout[pos] = (x == 0 || x == width - 1 || y == 0 || y == height - 1)
						? "WALL"
						: "FLOOR";
				}
			}

			// Front door
			layout[new Vector2Int( width / 2, 0 )] = "DOOR";

			// Recursion depth scales with wealth: richer NPCs get more subdivided homes.
			// 1.0-1.9 -> 0 splits (single room), 2.0-2.9 -> ~1 split, 3.0+ -> 2+ splits
			int maxDepth = wealthFactor >= 3f ? 3 : wealthFactor >= 2f ? 2 : wealthFactor >= 1.5f ? 1 : 0;

			var interior = new RectInt( 1, 1, width - 2, height - 2 );
			SubdivideAndCarve( interior, maxDepth, layout );

			// Safety net: flood-fill from the front door and auto-repair any
			// rooms that BSP subdivision walled off. Each BSP split adds one
			// doorway connecting its two children, which should form a spanning
			// tree, but edge cases (a doorway landing on a perimeter wall,
			// overlapping splits) can still isolate a room. The repair carves
			// a DOOR into the wall between each unreachable FLOOR region and
			// the nearest reachable cell.
			LastRepairCount = EnsureReachability( layout, width, height );

			return layout;
		}

		private void SubdivideAndCarve( RectInt area, int depth, Dictionary<Vector2Int, string> layout )
		{
			bool canSplitH = area.Height >= MinRoomSize * 2 + 1;
			bool canSplitV = area.Width >= MinRoomSize * 2 + 1;

			if ( depth <= 0 || (!canSplitH && !canSplitV) )
				return; // leaf room — leave as open FLOOR, already carved by perimeter pass

			// Prefer splitting the longer axis so rooms stay roughly square.
			bool splitHorizontally = canSplitH && (!canSplitV || area.Height > area.Width);

			if ( splitHorizontally )
			{
				int splitY = area.Y + MinRoomSize + _rng.Next( area.Height - MinRoomSize * 2 );
				for ( int x = area.X; x < area.X + area.Width; x++ )
					layout[new Vector2Int( x, splitY )] = "WALL";

				// Doorway through the new partition so rooms stay reachable
				int doorX = area.X + _rng.Next( area.Width );
				layout[new Vector2Int( doorX, splitY )] = "DOOR";

				SubdivideAndCarve( new RectInt( area.X, area.Y, area.Width, splitY - area.Y ), depth - 1, layout );
				SubdivideAndCarve( new RectInt( area.X, splitY + 1, area.Width, area.Y + area.Height - splitY - 1 ), depth - 1, layout );
			}
			else
			{
				int splitX = area.X + MinRoomSize + _rng.Next( area.Width - MinRoomSize * 2 );
				for ( int y = area.Y; y < area.Y + area.Height; y++ )
					layout[new Vector2Int( splitX, y )] = "WALL";

				int doorY = area.Y + _rng.Next( area.Height );
				layout[new Vector2Int( splitX, doorY )] = "DOOR";

				SubdivideAndCarve( new RectInt( area.X, area.Y, splitX - area.X, area.Height ), depth - 1, layout );
				SubdivideAndCarve( new RectInt( splitX + 1, area.Y, area.X + area.Width - splitX - 1, area.Height ), depth - 1, layout );
		}
	}

		/// <summary>
		/// Flood-fill from the front door and auto-repair any rooms that BSP
		/// subdivision walled off. Returns the count of unreachable regions
		/// that were repaired (0 = layout was already fully connected).
		/// </summary>
		int EnsureReachability( Dictionary<Vector2Int, string> layout, int width, int height )
		{
			// Find the front door as the flood-fill seed. If it's missing
			// (shouldn't happen), fall back to the first FLOOR cell.
			Vector2Int seed = new( width / 2, 0 );
			if ( !layout.TryGetValue( seed, out var seedVal ) || seedVal == "WALL" )
			{
				for ( int y = 1; y < height - 1; y++ )
				{
					for ( int x = 1; x < width - 1; x++ )
					{
						var p = new Vector2Int( x, y );
						if ( layout.TryGetValue( p, out var v ) && v != "WALL" )
						{
							seed = p;
							x = width; y = height; // break both loops
						}
					}
				}
			}

			// Flood-fill: mark all cells reachable from the seed. A cell is
			// passable if it's FLOOR or DOOR. WALL is impassable.
			var reachable = new HashSet<Vector2Int>();
			var queue = new Queue<Vector2Int>();
			queue.Enqueue( seed );
			reachable.Add( seed );

			var dirs = new[] { new Vector2Int( 1, 0 ), new Vector2Int( -1, 0 ), new Vector2Int( 0, 1 ), new Vector2Int( 0, -1 ) };

			while ( queue.Count > 0 )
			{
				var cur = queue.Dequeue();
				foreach ( var d in dirs )
				{
					var n = new Vector2Int( cur.X + d.X, cur.Y + d.Y );
					if ( n.X < 0 || n.X >= width || n.Y < 0 || n.Y >= height )
						continue;
					if ( reachable.Contains( n ) )
						continue;
					if ( !layout.TryGetValue( n, out var nv ) || nv == "WALL" )
						continue;
					reachable.Add( n );
					queue.Enqueue( n );
				}
			}

			// Find all FLOOR cells that weren't reached. Group them into
			// contiguous unreachable regions (flood-fill again, but only
			// among unreached FLOOR/DOOR cells).
			var unreached = new HashSet<Vector2Int>();
			for ( int y = 1; y < height - 1; y++ )
			{
				for ( int x = 1; x < width - 1; x++ )
				{
					var p = new Vector2Int( x, y );
					if ( reachable.Contains( p ) )
						continue;
					if ( layout.TryGetValue( p, out var v ) && v != "WALL" )
						unreached.Add( p );
				}
			}

			if ( unreached.Count == 0 )
				return 0; // already fully connected

			// For each unreachable region, find a wall cell that borders a
			// reachable cell and carve a DOOR through it. This reconnects
			// the region to the main layout.
			int repaired = 0;
			var visited = new HashSet<Vector2Int>();

			foreach ( var start in unreached )
			{
				if ( visited.Contains( start ) )
					continue;

				// Flood-fill this unreachable region, collecting its cells
				// and searching for a bordering wall to carve.
				var region = new List<Vector2Int>();
				var rq = new Queue<Vector2Int>();
				rq.Enqueue( start );
				visited.Add( start );

				Vector2Int? carveTarget = null;

				while ( rq.Count > 0 )
				{
					var cur = rq.Dequeue();
					region.Add( cur );

					foreach ( var d in dirs )
					{
						var n = new Vector2Int( cur.X + d.X, cur.Y + d.Y );
						if ( n.X < 0 || n.X >= width || n.Y < 0 || n.Y >= height )
							continue;

						// Check if this neighbor is a wall bordering a
						// reachable cell � if so, it's a carve candidate.
						if ( carveTarget is null && layout.TryGetValue( n, out var wv ) && wv == "WALL" )
						{
							foreach ( var d2 in dirs )
							{
								var nn = new Vector2Int( n.X + d2.X, n.Y + d2.Y );
								if ( reachable.Contains( nn ) )
								{
									// Don't carve perimeter walls (they'd
									// open a hole to the outside).
									bool isPerimeter = n.X == 0 || n.X == width - 1 || n.Y == 0 || n.Y == height - 1;
									if ( !isPerimeter )
									{
										carveTarget = n;
									}
									break;
								}
							}
						}

						if ( unreached.Contains( n ) && !visited.Contains( n ) )
						{
							visited.Add( n );
							rq.Enqueue( n );
						}
					}
				}

				// Carve a doorway if we found a bordering wall.
				if ( carveTarget is not null )
				{
					layout[carveTarget.Value] = "DOOR";
					repaired++;
				}
			}

			if ( repaired > 0 )
			{
				// Could log here, but BuildingGrammar is engine-agnostic.
				// The caller (NPCBuilder) can log if it wants.
			}

			return repaired;
		}
	}
}
