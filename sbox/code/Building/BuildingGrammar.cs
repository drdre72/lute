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
	}
}
