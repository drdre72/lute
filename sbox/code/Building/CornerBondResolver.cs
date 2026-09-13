using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// One cell of the shared 2x2 corner core, in module coordinates relative
	/// to the junction. U runs along Wall A's length axis, V runs along Wall
	/// B's length axis. Each cell is BrickModuleX (0.25m) on a side.
	/// </summary>
	public readonly struct CornerCell : IEquatable<CornerCell>
	{
		public readonly int U;
		public readonly int V;
		public readonly int Course;

		public CornerCell( int u, int v, int course )
		{
			U = u; V = v; Course = course;
		}

		public bool Equals( CornerCell other ) => U == other.U && V == other.V && Course == other.Course;
		public override bool Equals( object o ) => o is CornerCell c && Equals( c );
		public override int GetHashCode() => (U * 31 + V) * 31 + Course;
		public override string ToString() => $"CornerCell(u={U} v={V} course={Course})";
		public static bool operator ==( CornerCell a, CornerCell b ) => a.Equals( b );
		public static bool operator !=( CornerCell a, CornerCell b ) => !a.Equals( b );
	}

	/// <summary>
	/// One concrete brick placement inside the shared corner core. Records
	/// the actual world transform (not just a logical BrickSlot tag) so the
	/// validator can compare rendered geometry against the cell model.
	/// </summary>
	public readonly struct CornerBrickPlacement
	{
		public readonly BrickSlot Slot;
		public readonly Vector3 WorldCenter;
		public readonly float WorldYaw;
		public readonly Vector3 WorldSize;
		public readonly CornerCell[] OccupiedCells;

		public CornerBrickPlacement(
			BrickSlot slot, Vector3 worldCenter, float worldYaw,
			Vector3 worldSize, CornerCell[] occupiedCells )
		{
			Slot = slot;
			WorldCenter = worldCenter;
			WorldYaw = worldYaw;
			WorldSize = worldSize;
			OccupiedCells = occupiedCells;
		}

		public override string ToString()
			=> $"CornerBrick(slot={Slot} center={WorldCenter} yaw={WorldYaw:F1} size={WorldSize} cells={OccupiedCells?.Length ?? 0})";
	}

	/// <summary>
	/// Authoritative plan for one course of the shared corner assembly.
	/// Two ordinary full bricks cover the 2x2 cell core; their long axes
	/// alternate between Wall A (even courses) and Wall B (odd courses).
	/// </summary>
	public sealed class CornerAssemblyPlan
	{
		public string CornerId { get; init; }
		public VillageBuildTask WallA { get; init; }
		public VillageBuildTask WallB { get; init; }
		public Vector3 Junction { get; init; }
		public Vector3 InwardA { get; init; }
		public Vector3 InwardB { get; init; }
		public int Course { get; init; }
		public IReadOnlyList<CornerBrickPlacement> Placements { get; init; }
		/// <summary>True when this course's bricks align with Wall A; false for Wall B.</summary>
		public bool AlignsWithA => (Course & 1) == 0;
	}

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
		/// Resolve the shared corner assembly plan for one course. The
		/// junction and inward vectors are derived from canonical wall
		/// geometry — wall centerlines are NOT moved. Two ordinary full
		/// bricks cover the 2x2 cell core (0.25m x 0.25m); their long axes
		/// alternate between Wall A (even courses) and Wall B (odd courses).
		///
		/// Returns null if the two walls do not share a corner endpoint
		/// within the standard endpoint tolerance (~2cm).
		/// </summary>
		public static CornerAssemblyPlan TryResolveCorner(
			VillageBuildTask wallA, VillageBuildTask wallB,
			int course, string cornerId = null )
		{
			if ( wallA is null || wallB is null ) return null;
			if ( !TryGetJunctionAndInward( wallA, wallB,
				out var junction, out var inwardA, out var inwardB ) )
				return null;

			var placements = PlacementsFor(
				junction, inwardA, inwardB, course );

			cornerId ??= $"corner_{wallA.Name}_{wallB.Name}";
			return new CornerAssemblyPlan
			{
				CornerId = cornerId,
				WallA = wallA,
				WallB = wallB,
				Junction = junction,
				InwardA = inwardA,
				InwardB = inwardB,
				Course = course,
				Placements = placements,
			};
		}

		/// <summary>
		/// Generate the two full-brick placements that cover the 2x2 cell
		/// shared core for one course. The bricks are placed on the module
		/// lattice using the body size for the rendered box, so the body
		/// sits flush inside the module cell (no exterior overhang).
		/// </summary>
		public static IReadOnlyList<CornerBrickPlacement> PlacementsFor(
			Vector3 junction, Vector3 inwardA, Vector3 inwardB, int course )
		{
			// The corner assembly fills BOTH walls' gaps on every course.
			// Each wall's first brick (col=0) is 1 module from the junction,
			// and the wall skips col=0 (ShouldButt). The assembly places 2
			// bricks per wall per course: 2 along Wall A + 2 along Wall B.
			// Each brick is 2 modules long (covering the 1-module gap from
			// the wall endpoint to the junction + 1 module at the junction).
			// The 4 wythes are centered on each wall's centerline.
			//
			// This produces a solid L-shaped corner with no gaps on either
			// wall, on every course (no alternating stair-step).

			float brickLen = BrickModuleX * 2f;
			float brickDepth = BrickModuleY;
			float brickH = 0.0625f * M;
			var worldSize = new Vector3( brickLen, brickDepth, brickH );

			var result = new List<CornerBrickPlacement>( 8 );

			// Wall A bricks: 4 wythes along Wall B's depth, long axis = inwardA.
			AddWallBricks( result, junction, inwardA, inwardB, course, true, brickLen, brickDepth, brickH, worldSize );
			// Wall B bricks: 4 wythes along Wall A's depth, long axis = inwardB.
			AddWallBricks( result, junction, inwardB, inwardA, course, false, brickLen, brickDepth, brickH, worldSize );

			return result;
		}

		static void AddWallBricks(
			List<CornerBrickPlacement> result,
			Vector3 junction, Vector3 longAxis, Vector3 shortAxis,
			int course, bool isWallA,
			float brickLen, float brickDepth, float brickH, Vector3 worldSize )
		{
			float yaw = MathF.Atan2( longAxis.y, longAxis.x );
			// Center at col=0 position (1 module from junction toward wall).
			var col0Center = junction - longAxis * BrickModuleX;

			for ( int i = 0; i < 4; i++ )
			{
				var center = col0Center
					+ shortAxis * (brickDepth * (i - 1.5f));

				var cells = new CornerCell[2];
				if ( isWallA )
				{
					cells[0] = new CornerCell( 0, i, course );
					cells[1] = new CornerCell( 1, i, course );
				}
				else
				{
					cells[0] = new CornerCell( i, 0, course );
					cells[1] = new CornerCell( i, 1, course );
				}

				var slot = new BrickSlot(
					/*col*/ isWallA ? 0 : i,
					/*wythe*/ isWallA ? i : 0,
					course,
					BrickForm.Full, BrickOrientation.Stretcher );

				result.Add( new CornerBrickPlacement(
					slot, center, yaw, worldSize, cells ) );
			}
		}

		/// <summary>
		/// Derive the junction point and the two inward unit vectors from
		/// the canonical wall geometry. Returns false if the two walls do
		/// not share an endpoint within the endpoint tolerance (~2cm).
		/// </summary>
		static bool TryGetJunctionAndInward(
			VillageBuildTask wallA, VillageBuildTask wallB,
			out Vector3 junction, out Vector3 inwardA, out Vector3 inwardB )
		{
			junction = default;
			inwardA = default;
			inwardB = default;

			// Rotation is yaw in degrees. Forward (in S&Box) is +X rotated by
			// yaw around Z. Wall endpoints: Position (start) and
			// Position + forward * WallSegmentLength (end).
			static Vector3 ForwardOf( float yawDeg )
			{
				float r = yawDeg * MathF.PI / 180f;
				return new Vector3( MathF.Cos( r ), MathF.Sin( r ), 0 );
			}

			// The junction is the intersection of the two wall centerlines,
			// NOT a shared endpoint. Walls are perpendicular, so their
			// centerlines (infinite lines through Position along Forward)
			// intersect at exactly one point. This handles the case where
			// wall Position is the start endpoint and the walls meet at
			// their start corners (the typical Lute village layout).
			//
			// Wall A: point = a0 + t * fa,  Wall B: point = b0 + s * fb
			// Solve: a0 + t*fa = b0 + s*fb  (perpendicular, so fa . fb = 0)
			//   t = ((b0 - a0) . fa) / (fa . fa)
			//   s = ((a0 - b0) . fb) / (fb . fb)
			var a0 = wallA.Position;
			var fa = ForwardOf( wallA.Rotation );
			var b0 = wallB.Position;
			var fb = ForwardOf( wallB.Rotation );

			// Verify perpendicularity (within 1 degree).
			float dot = Vector3.Dot( fa, fb );
			if ( MathF.Abs( dot ) > 0.02f ) return false; // not perpendicular

			float faLenSq = Vector3.Dot( fa, fa );
			float fbLenSq = Vector3.Dot( fb, fb );
			if ( faLenSq < 1e-6f || fbLenSq < 1e-6f ) return false;

			float t = Vector3.Dot( b0 - a0, fa ) / faLenSq;
			// float s = Vector3.Dot( a0 - b0, fb ) / fbLenSq;

			junction = a0 + fa * t;
			junction.z = 0;

			// Inward = direction from the wall's far endpoint toward the
			// junction. If the junction is past the wall's end, inward is
			// along the wall; if before the start, inward is reversed.
			var a1 = a0 + fa * WallSegmentLength;
			inwardA = (junction - a1).Normal;
			if ( Vector3.DistanceBetween( a0, junction ) <
				Vector3.DistanceBetween( a1, junction ) )
				inwardA = (junction - a0).Normal;

			var b1 = b0 + fb * WallSegmentLength;
			inwardB = (junction - b1).Normal;
			if ( Vector3.DistanceBetween( b0, junction ) <
				Vector3.DistanceBetween( b1, junction ) )
				inwardB = (junction - b0).Normal;

			// Flatten to XY (walls are horizontal).
			inwardA = new Vector3( inwardA.x, inwardA.y, 0 ).Normal;
			inwardB = new Vector3( inwardB.x, inwardB.y, 0 ).Normal;
			return true;
		}

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
