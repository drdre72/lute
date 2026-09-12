using System;
using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary>
	/// Wall segment construction state machine.
	///
	/// Planned → BrickLaying → FinalizationEligible → Finalized
	///                                          ↗
	/// Deconstructing ← Finalized
	/// </summary>
	public enum WallSegmentState
	{
		/// <summary> Blueprint exists, no bricks placed yet. </summary>
		Planned,

		/// <summary> Bricks are being placed by builders. </summary>
		BrickLaying,

		/// <summary> Structural query satisfied — NPC may request finalization. </summary>
		FinalizationEligible,

		/// <summary> Director approved finalization — representation collapsed to one mesh. </summary>
		Finalized,

		/// <summary> NPC requested deconstruction — working course expanding to bricks. </summary>
		Deconstructing,
	}

	/// <summary>
	/// NPC craftsmanship traits that determine when a builder considers
	/// a wall "ready to finalize." These are deterministic per-NPC values,
	/// NOT LLM-driven. Two NPCs can disagree about whether a wall is
	/// finished without violating the ConstructionDirector authority model.
	/// </summary>
	public struct CraftsmanshipTraits
	{
		/// <summary> Minimum brick coverage ratio (0-1) to consider finalizing. </summary>
		public float RequiredCoverage;

		/// <summary> Maximum alignment error (in S&Box units) before the NPC fixes bricks. </summary>
		public float MaxAlignmentError;

		/// <summary> If true, the NPC requires the top course to be fully complete. </summary>
		public bool RequiresFullTopCourse;

		/// <summary> Maximum mortar gap variance before the NPC fixes bricks. </summary>
		public float AcceptableMortarVariance;

		/// <summary> Apprentice: lenient (98% coverage, no top course requirement). </summary>
		public static CraftsmanshipTraits Apprentice => new()
		{
			RequiredCoverage = 0.98f,
			MaxAlignmentError = 5f,
			RequiresFullTopCourse = false,
			AcceptableMortarVariance = 0.15f,
		};

		/// <summary> Mason: moderate (99% coverage, top course required). </summary>
		public static CraftsmanshipTraits Mason => new()
		{
			RequiredCoverage = 0.99f,
			MaxAlignmentError = 2f,
			RequiresFullTopCourse = true,
			AcceptableMortarVariance = 0.10f,
		};

		/// <summary> Master mason: strict (100% coverage, full top course, tight alignment). </summary>
		public static CraftsmanshipTraits MasterMason => new()
		{
			RequiredCoverage = 1.0f,
			MaxAlignmentError = 1f,
			RequiresFullTopCourse = true,
			AcceptableMortarVariance = 0.05f,
		};
	}

	/// <summary>
	/// Structural query result for a wall segment. This is the
	/// authoritative check — NOT brick count. The NPC asks "does what
	/// I've built satisfy the definition of this wall?"
	/// </summary>
	public struct WallStructuralQuery
	{
		/// <summary> Ratio of placed bricks to planned bricks (0-1). </summary>
		public float Coverage;

		/// <summary> True if the bottom course is complete (foundation supported). </summary>
		public bool FoundationSupported;

		/// <summary> True if all courses are continuous (no gaps within a row). </summary>
		public bool CoursesContinuous;

		/// <summary> True if corner bricks are bonded (running bond at corners). </summary>
		public bool RequiredCornersBonded;

		/// <summary> True if the top course is fully complete. </summary>
		public bool TopCourseComplete;

		/// <summary> True if there are no illegal gaps (missing bricks that break structure). </summary>
		public bool NoIllegalGap;

		/// <summary> True if no pending structural pieces remain. </summary>
		public bool NoPendingStructuralPieces;

		/// <summary>
		/// Director-level safety check: can this wall legally be finalized?
		/// This is the hard minimum imposed by ConstructionDirector.
		/// </summary>
		public bool CanDirectorFinalize =>
			Coverage >= 0.95f
			&& FoundationSupported
			&& CoursesContinuous
			&& NoIllegalGap
			&& NoPendingStructuralPieces;

		/// <summary>
		/// NPC-level check: does this NPC consider the work ready to submit?
		/// Uses the NPC's own craftsmanship traits.
		/// </summary>
		public bool CanNpcFinalize( CraftsmanshipTraits traits )
		{
			if ( Coverage < traits.RequiredCoverage ) return false;
			if ( !FoundationSupported ) return false;
			if ( !CoursesContinuous ) return false;
			if ( traits.RequiresFullTopCourse && !TopCourseComplete ) return false;
			if ( !NoIllegalGap ) return false;
			return NoPendingStructuralPieces;
		}
	}
}
