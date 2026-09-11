using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Result of a <see cref="BlueprintValidator"/> check. Carries typed
	/// <see cref="ValidationIssue"/>s (machine-readable) plus a legacy
	/// free-text <see cref="Issues"/> list for back-compat with existing
	/// console code. AI agents should prefer <see cref="TypedIssues"/>
	/// over <see cref="Issues"/>.
	/// </summary>
	public class BlueprintValidationResult
	{
		/// <summary> True if no errors (warnings are OK). </summary>
		public bool IsValid { get; set; } = true;

		/// <summary> Number of error-level issues (make IsValid false). </summary>
		public int ErrorCount { get; set; }

		/// <summary> Number of warning-level issues (don't block execution). </summary>
		public int WarningCount { get; set; }

		/// <summary> Number of info-level issues (purely diagnostic). </summary>
		public int InfoCount { get; set; }

		/// <summary> Human-readable issue descriptions (legacy). </summary>
		public List<string> Issues { get; set; } = new();

		/// <summary> Typed, machine-readable issues. AI agents read this. </summary>
		public List<ValidationIssue> TypedIssues { get; set; } = new();

		/// <summary> Short summary for console output. </summary>
		public string Summary => $"valid={IsValid}, errors={ErrorCount}, warnings={WarningCount}, info={InfoCount}";

		/// <summary> Add a typed error (also pushed to legacy Issues). </summary>
		public void AddError( string msg, string code = null, List<int> pieceIds = null,
			string suggestedFix = null, bool autoFixable = false )
		{
			Issues.Add( $"ERROR: {msg}" );
			ErrorCount++;
			IsValid = false;
			TypedIssues.Add( new ValidationIssue( IssueSeverity.Error,
				code ?? IssueCode.Nan, msg, pieceIds, suggestedFix, autoFixable ) );
		}

		/// <summary> Add a typed warning (also pushed to legacy Issues). </summary>
		public void AddWarning( string msg, string code = null, List<int> pieceIds = null,
			string suggestedFix = null, bool autoFixable = false )
		{
			Issues.Add( $"WARN: {msg}" );
			WarningCount++;
			TypedIssues.Add( new ValidationIssue( IssueSeverity.Warning,
				code ?? IssueCode.Overlap, msg, pieceIds, suggestedFix, autoFixable ) );
		}

		/// <summary> Add a typed info issue (diagnostic only). </summary>
		public void AddInfo( string msg, string code = null, List<int> pieceIds = null )
		{
			InfoCount++;
			TypedIssues.Add( new ValidationIssue( IssueSeverity.Info,
				code ?? "INFO", msg, pieceIds ) );
		}
	}

	/// <summary>
	/// Validates a <see cref="Blueprint"/> before execution. Checks for:
	///
	/// - **Piece bounds**: no NaN/infinity, no piece absurdly far from origin.
	/// - **Duplicate pieces**: two pieces at the same position with the same type.
	/// - **Structural integrity**: walls should have floor support below them.
	/// - **Dimension constraints**: structure fits within expected bounds,
	///   wall height and floor thickness are sane.
	///
	/// This runs after a producer generates a blueprint and before the
	/// executor places pieces. It catches producer bugs (bad math, missing
	/// floors, overlapping geometry) early — before they become invisible
	/// in-world problems that a vision-less agent can't diagnose.
	/// </summary>
	public static class BlueprintValidator
	{
		/// <summary>
		/// Maximum reasonable distance from origin for any piece (inches).
		/// ~50000in = ~1270m. Anything beyond this is almost certainly a
		/// producer math error.
		/// </summary>
		const float MaxDistanceFromOrigin = 50000f;

		/// <summary>
		/// Maximum reasonable structure footprint (inches per side).
		/// ~20000in = ~508m. St. Peter's Basilica is ~190m long, so this
		/// leaves headroom for monument-scale structures.
		/// </summary>
		const float MaxFootprint = 20000f;

		/// <summary>
		/// Tolerance for duplicate-piece detection (inches). Pieces closer
		/// than this are considered overlapping.
		/// </summary>
		const float OverlapTolerance = 1f;

		/// <summary>
		/// Maximum number of overlap warnings to report before stopping.
		/// Prevents console flooding when many pieces share a position
		/// (e.g. a deserialization bug that resets all positions to 0,0,0).
		/// </summary>
		const int MaxOverlapWarnings = 10;

		/// <summary>
		/// Validate a blueprint. Returns a result with issues listed.
		/// Does not modify the blueprint.
		/// </summary>
		public static BlueprintValidationResult Validate( Blueprint bp )
		{
			var result = new BlueprintValidationResult();

			if ( bp == null )
			{
				result.AddError( "Blueprint is null." );
				return result;
			}

			if ( bp.Pieces.Count == 0 )
			{
				result.AddWarning( "Blueprint has no pieces." );
				return result;
			}

			// 1. Per-piece bounds check
			CheckPieceBounds( bp, result );

			// 2. Duplicate / overlapping pieces
			CheckOverlaps( bp, result );

			// 3. Structural integrity — walls need floor support
			CheckStructuralIntegrity( bp, result );

			// 4. Dimension constraints
			CheckDimensions( bp, result );

			return result;
		}

		/// <summary>
		/// Validate and log the result to the S&Box console. Returns
		/// true if valid (no errors).
		/// </summary>
		public static bool ValidateAndLog( Blueprint bp, string context = "" )
		{
			var result = Validate( bp );
			var prefix = string.IsNullOrEmpty( context ) ? "" : $"[{context}] ";

			Log.Info( $"Lute: {prefix}Blueprint validation — {result.Summary}." );

			foreach ( var issue in result.Issues )
				Log.Info( $"  {issue}" );

			return result.IsValid;
		}

		// ── Check 1: Piece bounds ──

		static void CheckPieceBounds( Blueprint bp, BlueprintValidationResult result )
		{
			for ( int i = 0; i < bp.Pieces.Count; i++ )
			{
				var p = bp.Pieces[i];
				var pos = p.Position;

				// NaN or infinity
				if ( float.IsNaN( pos.x ) || float.IsNaN( pos.y ) || float.IsNaN( pos.z ) ||
					 float.IsInfinity( pos.x ) || float.IsInfinity( pos.y ) || float.IsInfinity( pos.z ) )
				{
					result.AddError( $"Piece #{i} ({p.PieceType}) has invalid position {pos}.",
						IssueCode.Nan, new List<int> { i } );
					continue;
				}

				// Distance from origin
				float dist = pos.Length;
				if ( dist > MaxDistanceFromOrigin )
				{
					result.AddError( $"Piece #{i} ({p.PieceType}) is {dist:F0}in from origin - exceeds max {MaxDistanceFromOrigin}.",
						IssueCode.OutOfBounds, new List<int> { i } );
				}

				// Negative Z for non-floor pieces (walls/roofs should be above ground)
				if ( pos.z < -bp.FloorThickness * 2 && p.PieceType != "FLOOR" )
				{
					result.AddWarning( $"Piece #{i} ({p.PieceType}) at z={pos.z:F1} is below ground.",
						IssueCode.BelowGround, new List<int> { i } );
				}
			}
		}

		// ── Check 2: Overlapping pieces ──

		static void CheckOverlaps( Blueprint bp, BlueprintValidationResult result )
		{
			// Group pieces by approximate position (quantize to overlap tolerance)
			var seen = new Dictionary<long, List<int>>();
			float q = OverlapTolerance;

			for ( int i = 0; i < bp.Pieces.Count; i++ )
			{
				var p = bp.Pieces[i];
				// Quantize position to a grid key
				long key = Quantize( p.Position, q );

				if ( !seen.TryGetValue( key, out var bucket ) )
				{
					bucket = new List<int>();
					seen[key] = bucket;
				}
				bucket.Add( i );
			}

			int overlapWarnings = 0;
			foreach ( var kvp in seen )
			{
				if ( kvp.Value.Count <= 1 )
					continue;

				// Check if any pair in the bucket has the same piece type
				for ( int a = 0; a < kvp.Value.Count; a++ )
				{
					for ( int b = a + 1; b < kvp.Value.Count; b++ )
					{
						if ( overlapWarnings >= MaxOverlapWarnings )
						{
							result.AddWarning( $"... ({MaxOverlapWarnings} overlap warnings shown, more may exist).",
							IssueCode.Overlap );
							return;
						}

						var pa = bp.Pieces[kvp.Value[a]];
						var pb = bp.Pieces[kvp.Value[b]];

						if ( pa.PieceType == pb.PieceType )
						{
							float dist = Vector3.DistanceBetween( pa.Position, pb.Position );
							if ( dist < OverlapTolerance )
							{
								result.AddWarning( $"Pieces #{kvp.Value[a]} and #{kvp.Value[b]} ({pa.PieceType}) overlap at {pa.Position} (dist={dist:F2}).",
								IssueCode.Overlap, new List<int> { kvp.Value[a], kvp.Value[b] } );
								overlapWarnings++;
							}
						}
					}
				}
			}
		}

		static long Quantize( Vector3 v, float q )
		{
			// Pack quantized x,y,z into a single long key
			int x = (int)( v.x / q );
			int y = (int)( v.y / q );
			int z = (int)( v.z / q );
			return ( (long)x << 42 ) ^ ( (long)y << 21 ) ^ (long)z;
		}

		// ── Check 3: Structural integrity ──

		static void CheckStructuralIntegrity( Blueprint bp, BlueprintValidationResult result )
		{
			// Build a set of floor positions for quick lookup
			var floorPositions = new HashSet<long>();
			foreach ( var p in bp.Pieces )
			{
				if ( p.PieceType == "FLOOR" )
					floorPositions.Add( Quantize( p.Position, OverlapTolerance ) );
			}

			// Check that walls have floor support at their base
			int unsupportedWalls = 0;
			foreach ( var p in bp.Pieces )
			{
				if ( p.PieceType != "WALL" )
					continue;

				// A wall at position (x, y, z) should have a floor at (x, y, z) or nearby
				// (walls and floors can be at the same z in the grid system).
				bool supported = floorPositions.Contains( Quantize( p.Position, OverlapTolerance ) );

				// Also check adjacent floor cells (wall might sit on edge of floor)
				if ( !supported )
				{
					float cs = bp.CellSize;
					supported = floorPositions.Contains( Quantize( p.Position + new Vector3( cs, 0, 0 ), OverlapTolerance ) ) ||
								floorPositions.Contains( Quantize( p.Position + new Vector3( -cs, 0, 0 ), OverlapTolerance ) ) ||
								floorPositions.Contains( Quantize( p.Position + new Vector3( 0, cs, 0 ), OverlapTolerance ) ) ||
								floorPositions.Contains( Quantize( p.Position + new Vector3( 0, -cs, 0 ), OverlapTolerance ) );
				}

				if ( !supported )
					unsupportedWalls++;
			}

			if ( unsupportedWalls > 0 )
			{
				result.AddWarning( $"{unsupportedWalls} wall(s) have no adjacent floor support (may float).",
				IssueCode.FloatingWall, null, "Add floor pieces beneath unsupported walls", true );
			}

			// Check that there's at least one floor
			if ( floorPositions.Count == 0 && bp.Pieces.Any( p => p.PieceType == "WALL" ) )
			{
				result.AddError( "Blueprint has walls but no floor pieces - structure will float.",
			IssueCode.MissingFloor );
			}
		}

		// ── Check 4: Dimension constraints ──

		static void CheckDimensions( Blueprint bp, BlueprintValidationResult result )
		{
			if ( bp.Pieces.Count == 0 )
				return;

			// Compute bounding box
			float minX = float.MaxValue, maxX = float.MinValue;
			float minY = float.MaxValue, maxY = float.MinValue;
			float minZ = float.MaxValue, maxZ = float.MinValue;

			foreach ( var p in bp.Pieces )
			{
				if ( p.Position.x < minX ) minX = p.Position.x;
				if ( p.Position.x > maxX ) maxX = p.Position.x;
				if ( p.Position.y < minY ) minY = p.Position.y;
				if ( p.Position.y > maxY ) maxY = p.Position.y;
				if ( p.Position.z < minZ ) minZ = p.Position.z;
				if ( p.Position.z > maxZ ) maxZ = p.Position.z;
			}

			float width = maxX - minX;
			float depth = maxY - minY;
			float height = maxZ - minZ;

			// Footprint check
			if ( width > MaxFootprint || depth > MaxFootprint )
			{
				result.AddError( $"Structure footprint {width:F0}x{depth:F0}in exceeds max {MaxFootprint}.",
			IssueCode.FootprintExceeded );
			}

			// Height sanity (no structure should be taller than ~500m)
			if ( height > 20000f )
			{
				result.AddWarning( $"Structure height {height:F0}in (~{height/39.37f:F0}m) is unusually tall.",
			IssueCode.HeightExceeded );
			}

			// Wall height sanity
			if ( bp.WallHeight < 50f || bp.WallHeight > 1000f )
			{
				result.AddWarning( $"WallHeight {bp.WallHeight:F0}in is outside typical range (50-1000).",
			IssueCode.BadWallHeight );
			}

			// Floor thickness sanity
			if ( bp.FloorThickness < 1f || bp.FloorThickness > 100f )
			{
				result.AddWarning( $"FloorThickness {bp.FloorThickness:F0}in is outside typical range (1-100).",
			IssueCode.BadFloorThickness );
			}

			// Empty type check
			int emptyType = bp.Pieces.Count( p => string.IsNullOrEmpty( p.PieceType ) );
			if ( emptyType > 0 )
			{
				result.AddError( $"{emptyType} piece(s) have empty PieceType.",
			IssueCode.EmptyType );
			}
		}
	}
}
