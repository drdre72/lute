using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary>
	/// Severity of a <see cref="ValidationIssue"/>. Errors block execution,
	/// warnings allow it, and info is purely diagnostic.
	/// </summary>
	public enum IssueSeverity
	{
		Info,
		Warning,
		Error
	}

	/// <summary>
	/// Machine-readable classification of a validation issue. Grouped by
	/// category so AI agents can reason over typed diagnostics instead of
	/// parsing prose. See <see cref="ValidationIssue.Code"/> for the
	/// canonical string form (e.g. "STRUCTURAL.FLOATING_WALL").
	/// </summary>
	public static class IssueCode
	{
		// Structural — load path / support
		public const string FloatingWall = "STRUCTURAL.FLOATING_WALL";
		public const string MissingFloor = "STRUCTURAL.MISSING_FLOOR";
		public const string UnsupportedRoof = "STRUCTURAL.UNSUPPORTED_ROOF";

		// Geometry — coordinates / overlap / bounds
		public const string Overlap = "GEOMETRY.OVERLAP";
		public const string OutOfBounds = "GEOMETRY.OUT_OF_BOUNDS";
		public const string Nan = "GEOMETRY.NAN";
		public const string BelowGround = "GEOMETRY.BELOW_GROUND";
		public const string EmptyType = "GEOMETRY.EMPTY_TYPE";

		// Dimension — footprint / height sanity
		public const string FootprintExceeded = "DIMENSION.FOOTPRINT";
		public const string HeightExceeded = "DIMENSION.HEIGHT";
		public const string BadWallHeight = "DIMENSION.WALL_HEIGHT";
		public const string BadFloorThickness = "DIMENSION.FLOOR_THICKNESS";

		// Style — grammar-level coherence
		public const string BayRhythm = "STYLE.BAY_RHYTHM";
		public const string Symmetry = "STYLE.SYMMETRY";

		// Material
		public const string UnknownMaterial = "MATERIAL.UNKNOWN";

		// Performance
		public const string PieceCount = "PERFORMANCE.PIECE_COUNT";
	}

	/// <summary>
	/// A single typed validation issue. Replaces the old free-text
	/// List of strings in BlueprintValidationResult so AI agents can
	/// reason over structured diagnostics: code, affected piece ids,
	/// suggested fix, and whether the fix is auto-applicable.
	/// </summary>
	public class ValidationIssue
	{
		/// <summary> Severity (Info/Warning/Error). Errors block execution. </summary>
		public IssueSeverity Severity { get; set; } = IssueSeverity.Warning;

		/// <summary> Machine-readable code, e.g. "STRUCTURAL.FLOATING_WALL". </summary>
		public string Code { get; set; }

		/// <summary> Human-readable explanation (for logs / debugging). </summary>
		public string Message { get; set; }

		/// <summary> Indices of affected pieces in <see cref="Blueprint.Pieces"/>. </summary>
		public List<int> PieceIds { get; set; } = new();

		/// <summary> Suggested deterministic fix, if any. </summary>
		public string SuggestedFix { get; set; }

		/// <summary> True if <see cref="SuggestedFix"/> can be applied automatically. </summary>
		public bool AutoFixable { get; set; }

		public ValidationIssue() { }

		public ValidationIssue( IssueSeverity severity, string code, string message,
			List<int> pieceIds = null, string suggestedFix = null, bool autoFixable = false )
		{
			Severity = severity;
			Code = code;
			Message = message;
			PieceIds = pieceIds ?? new();
			SuggestedFix = suggestedFix;
			AutoFixable = autoFixable;
		}

		public override string ToString()
		{
			var ids = PieceIds.Count > 0
				? $" pieces=[{string.Join( ",", PieceIds )}]"
				: "";
			var fix = AutoFixable ? $" (auto-fixable: {SuggestedFix})" : "";
			return $"[{Severity}] {Code}: {Message}{ids}{fix}";
		}
	}
}
