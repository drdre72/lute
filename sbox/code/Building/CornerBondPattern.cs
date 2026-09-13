using System;

namespace Lute.Building
{
	/// <summary>
	/// Masonry bond pattern for a wall corner. The CornerBondResolver selects
	/// the lowest-cost valid pattern that satisfies the corner goal
	/// (continuous masonry, no overlap, no open seam, alternating ownership,
	/// minimum bond depth).
	///
	/// This is the grammar the builder may choose from — it does NOT move wall
	/// centerlines. Wall geometry stays canonical (N: +half, S: -half,
	/// E: +half, W: -half); the masonry system accommodates the junction.
	/// </summary>
	public enum CornerBondPattern
	{
		/// <summary>
		/// No bond — alternating occupancy, stretchers only. The owner wall
		/// fills the corner volume; the non-owner terminates against it.
		/// BondDepth = 0 (no brick crosses the joint). This is the current
		/// behavior and produces a butt joint.
		/// </summary>
		Butt,

		/// <summary>
		/// Owner course places a header brick whose long axis is perpendicular
		/// to the wall, crossing the joint into the corner volume. The
		/// non-owner wall terminates short against the header. BondDepth =
		/// 1 module (0.25m). Produces a visible interlock on every owner
		/// course.
		/// </summary>
		HeaderBond,

		/// <summary>
		/// Alternating header/stretcher quoins forming a traditional
		/// interlocking corner two modules deep. BondDepth = 2 modules
		/// (0.5m). Reserved for after HeaderBond is verified.
		/// </summary>
		Quoin,

		/// <summary>
		/// Builder-selected repair pattern from the grammar (lowest-cost
		/// valid option). Used when a corner fails its bond goal and the
		/// builder must choose a correction from the fixed grammar.
		/// </summary>
		Repair,
	}
}
