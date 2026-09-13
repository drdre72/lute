using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Classification of a placement failure — determines which
	/// corrections the self-repair loop should try.
	/// </summary>
	public enum PlacementFailureType
	{
		/// <summary> Unknown failure. </summary>
		Unknown,
		/// <summary> Off-grid position — snap to nearest grid cell. </summary>
		OffGrid,
		/// <summary> Wrong rotation — try 90° increments. </summary>
		WrongRotation,
		/// <summary> Occupied by another structure — shift one cell. </summary>
		Occupied,
		/// <summary> Too close to another structure — shift further. </summary>
		TooClose,
		/// <summary> No valid position found after all corrections — defer. </summary>
		Unrecoverable,
	}

	/// <summary>
	/// Result of a self-repair attempt.
	/// </summary>
	public sealed class SelfRepairResult
	{
		public bool Success { get; init; }
		public Vector3 CorrectedPosition { get; init; }
		public float CorrectedRotation { get; init; }
		public PlacementFailureType FailureType { get; init; }
		public string Reason { get; init; }
		public int AttemptsUsed { get; init; }

		public static SelfRepairResult Ok( Vector3 pos, float rot, int attempts )
			=> new() { Success = true, CorrectedPosition = pos, CorrectedRotation = rot, AttemptsUsed = attempts };

		public static SelfRepairResult Fail( PlacementFailureType type, string reason, int attempts )
			=> new() { Success = false, FailureType = type, Reason = reason, AttemptsUsed = attempts };
	}

	/// <summary>
	/// Bounded deterministic recovery loop for construction placements.
	///
	/// When a placement fails validation, this class tries a bounded set
	/// of approved corrections before giving up:
	///   1. Snap to nearest grid cell
	///   2. Rotate 90° (up to 3 times)
	///   3. Shift one cell in each cardinal direction (N/S/E/W)
	///   4. Shift two cells if one-cell shift fails
	///   5. Defer (return Unrecoverable)
	///
	/// No free-form AI movement. All corrections are deterministic and
	/// bounded. The loop never exceeds MaxAttempts total tries.
	/// </summary>
	public static class ConstructionSelfRepair
	{
		const float M = 39.37f;
		const float BrickModuleX = 0.25f * M;
		const float BrickModuleY = 0.125f * M;
		const int MaxAttempts = 16;

		/// <summary>
		/// Try to find a valid placement near the proposed position.
		/// Returns a SelfRepairResult with the corrected position/rotation
		/// or a failure classification.
		/// </summary>
		public static SelfRepairResult TryRecover(
			Vector3 proposedPos, float proposedRotation, Vector3 size,
			Func<Vector3, float, bool> isValid, string blockerReason = null )
		{
			int attempts = 0;

			// 0. Check if the original is actually valid (no correction needed)
			if ( isValid( proposedPos, proposedRotation ) )
				return SelfRepairResult.Ok( proposedPos, proposedRotation, 0 );

			// 1. Snap to nearest grid cell
			var snapped = SnapToGrid( proposedPos );
			if ( isValid( snapped, proposedRotation ) )
				return SelfRepairResult.Ok( snapped, proposedRotation, ++attempts );

			// 2. Try 90° rotations
			for ( int r = 1; r <= 3; r++ )
			{
				float rot = (proposedRotation + r * 90f) % 360f;
				if ( isValid( snapped, rot ) )
					return SelfRepairResult.Ok( snapped, rot, ++attempts );
			}

			// 3. Shift one cell in each cardinal direction
			var directions = new Vector3[]
			{
				new( BrickModuleX, 0, 0 ),   // East
				new( -BrickModuleX, 0, 0 ),  // West
				new( 0, BrickModuleX, 0 ),   // North
				new( 0, -BrickModuleX, 0 ),   // South
			};

			foreach ( var dir in directions )
			{
				attempts++;
				if ( attempts > MaxAttempts ) break;
				var shifted = snapped + dir;
				if ( isValid( shifted, proposedRotation ) )
					return SelfRepairResult.Ok( shifted, proposedRotation, attempts );
			}

			// 4. Shift two cells in each cardinal direction
			foreach ( var dir in directions )
			{
				attempts++;
				if ( attempts > MaxAttempts ) break;
				var shifted = snapped + dir * 2f;
				if ( isValid( shifted, proposedRotation ) )
					return SelfRepairResult.Ok( shifted, proposedRotation, attempts );
			}

			// 5. Try diagonal shifts (one cell in two directions)
			var diagonals = new Vector3[]
			{
				new( BrickModuleX, BrickModuleX, 0 ),
				new( BrickModuleX, -BrickModuleX, 0 ),
				new( -BrickModuleX, BrickModuleX, 0 ),
				new( -BrickModuleX, -BrickModuleX, 0 ),
			};
			foreach ( var dir in diagonals )
			{
				attempts++;
				if ( attempts > MaxAttempts ) break;
				var shifted = snapped + dir;
				if ( isValid( shifted, proposedRotation ) )
					return SelfRepairResult.Ok( shifted, proposedRotation, attempts );
			}

			// 6. All corrections exhausted — classify and defer
			var failureType = ClassifyFailure( blockerReason );
			return SelfRepairResult.Fail( failureType,
				$"unrecoverable after {attempts} attempts (blocker: {blockerReason ?? "unknown"})",
				attempts );
		}

		/// <summary>
		/// Snap a world position to the nearest brick module grid cell.
		/// </summary>
		public static Vector3 SnapToGrid( Vector3 pos )
		{
			float snapX = MathF.Round( pos.x / BrickModuleX ) * BrickModuleX;
			float snapY = MathF.Round( pos.y / BrickModuleY ) * BrickModuleY;
			return new Vector3( snapX, snapY, pos.z );
		}

		/// <summary>
		/// Classify a placement failure from the blocker reason string.
		/// </summary>
		static PlacementFailureType ClassifyFailure( string reason )
		{
			if ( string.IsNullOrEmpty( reason ) )
				return PlacementFailureType.Unknown;

			var lower = reason.ToLowerInvariant();
			if ( lower.Contains( "occupied" ) || lower.Contains( "blocked" ) )
				return PlacementFailureType.Occupied;
			if ( lower.Contains( "close" ) || lower.Contains( "clearance" ) )
				return PlacementFailureType.TooClose;
			if ( lower.Contains( "rotation" ) || lower.Contains( "orientation" ) )
				return PlacementFailureType.WrongRotation;
			if ( lower.Contains( "grid" ) || lower.Contains( "snap" ) )
				return PlacementFailureType.OffGrid;
			return PlacementFailureType.Unknown;
		}
	}
}
