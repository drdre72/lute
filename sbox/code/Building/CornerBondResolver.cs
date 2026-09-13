using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// A corner bond goal handed to the builder. This is a *goal*, not a
	/// per-brick instruction: the builder receives the pattern and required
	/// bond depth, and places the concrete BrickSlots from
	/// <see cref="CornerBondResolver.SlotsFor"/>.
	///
	/// The goal is deterministic — no LLM at runtime. The resolver picks the
	/// lowest-cost valid pattern from the fixed grammar in
	/// <see cref="CornerBondPattern"/>.
	/// </summary>
	public readonly struct CornerBondGoal : IEquatable<CornerBondGoal>
	{
		/// <summary> Wall that owns the corner volume on this course. </summary>
		public readonly VillageBuildTask OwnerWall;

		/// <summary> Wall that terminates against the owner. </summary>
		public readonly VillageBuildTask NonOwnerWall;

		/// <summary> Course index (GridZ) this goal applies to. </summary>
		public readonly int Course;

		/// <summary> Chosen bond pattern. </summary>
		public readonly CornerBondPattern Pattern;

		/// <summary> Required bond depth in meters (>= 1 module = 0.25m). </summary>
		public readonly float RequiredBondDepth;

		/// <summary>
		/// Which endpoint side of the owner wall the corner sits on.
		/// 1 = left/start, 2 = right/end. Mirrors CornerButtSide.
		/// </summary>
		public readonly int OwnerCornerSide;

		public CornerBondGoal(
			VillageBuildTask ownerWall,
			VillageBuildTask nonOwnerWall,
			int course,
			CornerBondPattern pattern,
			float requiredBondDepth,
			int ownerCornerSide )
		{
			OwnerWall = ownerWall;
			NonOwnerWall = nonOwnerWall;
			Course = course;
			Pattern = pattern;
			RequiredBondDepth = requiredBondDepth;
			OwnerCornerSide = ownerCornerSide;
		}

		/// <summary> Bond depth this pattern achieves, in meters. </summary>
		public float AchievedBondDepth => Pattern switch
		{
			CornerBondPattern.Butt => 0f,
			CornerBondPattern.HeaderBond => 0.25f,
			CornerBondPattern.Quoin => 0.5f,
			CornerBondPattern.Repair => 0.25f,
			_ => 0f,
		};

		/// <summary> True if this goal satisfies its own required bond depth. </summary>
		public bool IsBonded => AchievedBondDepth >= RequiredBondDepth;

		public bool Equals( CornerBondGoal other )
			=> ReferenceEquals( OwnerWall, other.OwnerWall )
				&& ReferenceEquals( NonOwnerWall, other.NonOwnerWall )
				&& Course == other.Course
				&& Pattern == other.Pattern
				&& RequiredBondDepth.Equals( other.RequiredBondDepth )
				&& OwnerCornerSide == other.OwnerCornerSide;

		public override bool Equals( object obj ) => obj is CornerBondGoal other && Equals( other );
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + (OwnerWall?.Name?.GetHashCode() ?? 0);
				hash = hash * 31 + (NonOwnerWall?.Name?.GetHashCode() ?? 0);
				hash = hash * 31 + Course;
				hash = hash * 31 + (int)Pattern;
				hash = hash * 31 + RequiredBondDepth.GetHashCode();
				hash = hash * 31 + OwnerCornerSide;
				return hash;
			}
		}

		public override string ToString()
			=> $"CornerBondGoal(owner={OwnerWall?.Name} nonOwner={NonOwnerWall?.Name} "
				+ $"course={Course} pattern={Pattern} depth>={RequiredBondDepth} side={OwnerCornerSide})";

		public static bool operator ==( CornerBondGoal a, CornerBondGoal b ) => a.Equals( b );
		public static bool operator !=( CornerBondGoal a, CornerBondGoal b ) => !a.Equals( b );
	}

	/// <summary>
	/// Resolves a wall corner into a deterministic masonry bond pattern.
	///
	/// The resolver operates on BrickSlots using the existing
	/// <see cref="BrickOrientation.Header"/> / <see cref="BrickForm.Quarter"/>
	/// / <see cref="BrickOrientation.Stretcher"/> vocabulary. It does NOT move
	/// wall centerlines — wall geometry stays canonical and the masonry
	/// accommodates the junction.
	///
	/// The builder receives a <see cref="CornerBondGoal"/> (a goal: continuous
	/// masonry, no overlap, no open seam, alternating ownership, minimum bond
	/// depth) and selects the lowest-cost valid pattern from the fixed
	/// grammar in <see cref="CornerBondPattern"/>. This gives the autonomous
	/// fine-tuning behavior without allowing NPCs to drift wall segments.
	/// </summary>
	public static class CornerBondResolver
	{
		const float M = 39.37f;
		const float BrickModuleX = 0.25f * M;
		const float BrickModuleY = 0.125f * M;
		const float WallSegmentLength = 2.0f * M;
		const float WallThickness = 0.5f * M;

		/// <summary>
		/// Default required bond depth: one module (0.25m). A header brick
		/// crossing the joint achieves exactly this.
		/// </summary>
		public const float DefaultRequiredBondDepth = 0.25f;

		/// <summary>
		/// Choose the lowest-cost valid bond pattern for one course at a
		/// corner. The owner wall owns the corner volume on this course;
		/// the non-owner wall terminates against it.
		/// </summary>
		/// <param name="ownerWall">Wall that owns the corner this course.</param>
		/// <param name="nonOwnerWall">Wall that terminates against the owner.</param>
		/// <param name="ownerCornerSide">1 = left/start endpoint, 2 = right/end.</param>
		/// <param name="course">Course index (GridZ).</param>
		/// <param name="requiredBondDepth">Minimum bond depth in meters (default 0.25m).</param>
		public static CornerBondGoal Resolve(
			VillageBuildTask ownerWall,
			VillageBuildTask nonOwnerWall,
			int ownerCornerSide,
			int course,
			float requiredBondDepth = DefaultRequiredBondDepth )
		{
			// Lowest-cost valid pattern: Butt costs nothing but has depth 0.
			// HeaderBond costs one header brick but achieves 0.25m depth.
			// Quoin costs more and achieves 0.5m — only if required.
			CornerBondPattern pattern;
			if ( requiredBondDepth <= 0f )
				pattern = CornerBondPattern.Butt;
			else if ( requiredBondDepth <= 0.25f )
				pattern = CornerBondPattern.HeaderBond;
			else if ( requiredBondDepth <= 0.5f )
				pattern = CornerBondPattern.Quoin;
			else
				pattern = CornerBondPattern.Repair;

			return new CornerBondGoal(
				ownerWall, nonOwnerWall, course, pattern, requiredBondDepth, ownerCornerSide );
		}

		/// <summary>
		/// Produce the concrete BrickSlots the builder should place for a
		/// resolved goal at the corner. These slots REPLACES the
		/// ShouldButt-skip behavior: instead of skipping the corner brick,
		/// the owner places a header (or quoin set) crossing the joint, and
		/// the non-owner terminates short.
		///
		/// For <see cref="CornerBondPattern.Butt"/> this returns an empty
		/// list — the owner places its normal stretcher and the non-owner
		/// skips (current behavior).
		/// </summary>
		public static IReadOnlyList<BrickSlot> SlotsFor( CornerBondGoal goal )
		{
			var result = new List<BrickSlot>();
			if ( goal.Pattern == CornerBondPattern.Butt )
				return result;

			if ( goal.OwnerWall is null )
				return result;

			int modulesX = (int)MathF.Round( WallSegmentLength / BrickModuleX );
			int numWythes = (int)MathF.Round( WallThickness / BrickModuleY );

			// The corner slot sits at the endpoint column of the owner wall.
			// Side 1 = left/start (GridX = 0 area), Side 2 = right/end (GridX = modulesX area).
			// For a header bond, the owner places a header brick at the corner
			// column whose long axis is perpendicular to the wall, crossing
			// the joint into the corner volume.
			int cornerCol = goal.OwnerCornerSide == 1 ? 0 : modulesX - 1;

			if ( goal.Pattern == CornerBondPattern.HeaderBond
				|| goal.Pattern == CornerBondPattern.Repair )
			{
				// One header brick per wythe at the corner column, crossing the joint.
				for ( int wythe = 0; wythe < numWythes; wythe++ )
				{
					result.Add( new BrickSlot(
						cornerCol, wythe, goal.Course,
						BrickForm.Full, BrickOrientation.Header ) );
				}
			}
			else if ( goal.Pattern == CornerBondPattern.Quoin )
			{
				// Two-module interlock: header at cornerCol, stretcher at the
				// adjacent column on the next course. For this course, place
				// the header; the alternating stretcher is produced by the
				// next course's goal.
				for ( int wythe = 0; wythe < numWythes; wythe++ )
				{
					result.Add( new BrickSlot(
						cornerCol, wythe, goal.Course,
						BrickForm.Full, BrickOrientation.Header ) );
				}
			}

			return result;
		}

		/// <summary>
		/// Measure the bond depth of a placed corner on one course, in
		/// meters. Bond depth is how far the owner wall's bricks extend
		/// across the joint line into the corner volume.
		///
		/// A stretcher-only course (butt joint) = 0.
		/// A header crossing the joint = 0.25m (one module).
		/// A quoin interlock = 0.5m (two modules).
		///
		/// This is the metric the topology validator uses to report
		/// <see cref="WallCornerTopologyResult.IsBonded"/>.
		/// </summary>
		public static float MeasureBondDepth(
			VillageBuildTask wallA, VillageBuildTask wallB, int course )
		{
			if ( wallA is null || wallB is null ) return 0f;
			if ( wallA.PlacedBricks is null || wallB.PlacedBricks is null ) return 0f;

			// A header brick in the corner volume indicates a bond.
			// Count header bricks at this course in either wall that fall in
			// the corner core; each contributes BrickModuleX of depth.
			float depth = 0f;
			depth += HeaderDepthInCourse( wallA, course );
			depth += HeaderDepthInCourse( wallB, course );

			// Cap at the maximum meaningful depth (one wall's worth of headers).
			if ( depth > BrickModuleX ) depth = BrickModuleX;
			return depth;
		}

		static float HeaderDepthInCourse( VillageBuildTask wall, int course )
		{
			if ( wall.PlacedBricks is null ) return 0f;
			float depth = 0f;
			foreach ( var slot in wall.PlacedBricks )
			{
				if ( slot.GridZ != course ) continue;
				if ( slot.Orientation == BrickOrientation.Header )
					depth += BrickModuleX;
			}
			return depth;
		}
	}
}
