using System;
using System.Numerics;

namespace Lute.Core.Spatial;

/// <summary>
/// Deterministic quantized spatial key for ownership, indexing, and
/// reservations. Continuous <see cref="Vector3"/> positions are NOT
/// authoritative for simulation identity — they are quantized into a
/// stable integer grid cell so float equality / hash instability can
/// never affect task ownership or reservation deduplication.
/// </summary>
public readonly record struct SpatialKey( int X, int Y, int Z )
{
	/// <summary>
	/// Quantize a continuous world position into a grid cell of the
	/// given size. Uses floor so negative coordinates map to the cell
	/// that contains them (not the cell toward zero).
	/// </summary>
	public static SpatialKey Quantize( Vector3 position, float cellSize )
	{
		return new(
			(int)MathF.Floor( position.X / cellSize ),
			(int)MathF.Floor( position.Y / cellSize ),
			(int)MathF.Floor( position.Z / cellSize )
		);
	}

	/// <summary> Quantize using only the horizontal axes (X, Y). Z is 0. </summary>
	public static SpatialKey QuantizeHorizontal( Vector3 position, float cellSize )
	{
		return new(
			(int)MathF.Floor( position.X / cellSize ),
			(int)MathF.Floor( position.Y / cellSize ),
			0
		);
	}
}
