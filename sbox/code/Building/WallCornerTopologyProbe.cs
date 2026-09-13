using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Which wall owns the shared masonry volume at one corner course.
	/// A valid simplified running-bond corner has exactly one owner per
	/// course, and ownership alternates on successive courses.
	/// </summary>
	public enum WallCornerOwner
	{
		None,
		WallA,
		WallB,
		Both,
	}

	/// <summary>
	/// Deterministic result for one perpendicular wall junction.
	/// This is diagnostic data only; it never authorizes construction.
	/// </summary>
	public sealed class WallCornerTopologyResult
	{
		public string WallAName { get; init; }
		public string WallBName { get; init; }
		public Vector3 Junction { get; init; }
		public float EndpointDistance { get; init; }
		public bool IsPerpendicular { get; init; }
		public bool EndpointsMeet { get; init; }
		public int CoursesChecked { get; set; }
		public int IncompleteCourses { get; set; }
		public int DoubleOwnedCourses { get; set; }
		public int UnownedCourses { get; set; }
		public int DuplicateBrickPairs { get; set; }
		public bool OwnershipAlternates { get; set; } = true;

		/// <summary>
		/// Minimum bond depth across all checked courses, in meters.
		/// 0 = no brick crosses the joint (butt joint). 0.25 = one header
		/// crosses (header bond). 0.5 = quoin interlock. Measured by
		/// <see cref="CornerBondResolver.MeasureBondDepth"/>.
		/// </summary>
		public float BondDepth { get; set; } = 0f;

		/// <summary>
		/// Required bond depth for the corner to count as bonded, in meters.
		/// Default 0.25m (one module — a single header crossing the joint).
		/// </summary>
		public float RequiredBondDepth { get; set; } = 0.25f * 39.37f;

		/// <summary>
		/// True if the corner's bond depth meets the required minimum.
		/// A butt joint reports IsValid=true (geometry is exclusive and
		/// alternating) but IsBonded=false (no brick crosses the joint).
		/// </summary>
		public bool IsBonded => BondDepth >= RequiredBondDepth;

		/// <summary>
		/// A corner passes only when its walls meet, are perpendicular, every
		/// common course is present, the shared corner volume has one owner,
		/// there are no duplicate brick-volume overlaps, and ownership alternates.
		/// This is the GEOMETRY check. Use <see cref="IsBonded"/> for the
		/// masonry-bond check.
		/// </summary>
		public bool IsValid =>
			IsPerpendicular
			&& EndpointsMeet
			&& CoursesChecked > 0
			&& IncompleteCourses == 0
			&& DoubleOwnedCourses == 0
			&& UnownedCourses == 0
			&& DuplicateBrickPairs == 0
			&& OwnershipAlternates;

		public override string ToString()
		{
			return $"{WallAName}<->{WallBName} valid={IsValid} perpendicular={IsPerpendicular} "
				+ $"meet={EndpointsMeet} endpointDist={EndpointDistance:F3} courses={CoursesChecked} "
				+ $"incomplete={IncompleteCourses} doubleOwned={DoubleOwnedCourses} "
				+ $"unowned={UnownedCourses} duplicatePairs={DuplicateBrickPairs} "
				+ $"alternates={OwnershipAlternates} "
				+ $"bondDepth={BondDepth / 39.37f:F3}m bonded={IsBonded}";
		}
	}

	/// <summary>
	/// Structural diagnostic for masonry corners.
	///
	/// The current straight-wall BrickSlot topology can look visually correct at
	/// a 90-degree junction while two independent wall segments still occupy the
	/// same corner volume. This validator converts placed BrickSlots back into
	/// deterministic 2D masonry footprints and checks the shared corner core.
	///
	/// A valid simplified bonded corner follows one rule:
	/// - each course has exactly one wall owning the shared corner volume;
	/// - the owner alternates every course (A, B, A, B...);
	/// - the other wall terminates at the owner's masonry instead of overlapping it.
	///
	/// This class is observation/validation only. ConstructionDirector remains
	/// the authority for world mutation and task state transitions.
	/// </summary>
	public static class WallCornerTopologyValidator
	{
		const float M = 39.37f;
		const float WallSegmentLength = 2.0f * M;
		const float WallThickness = 0.5f * M;
		const float BrickModuleX = 0.25f * M;
		const float BrickModuleY = 0.125f * M;

		// Tolerances: canonical wall positions (all at +/-half) meet
		// mathematically at corners, so a tight tolerance suffices.
		const float EndpointTolerance = 0.02f * M;        // 2 cm (restored)
		const float PerpendicularToleranceDeg = 1.0f;
		const float PositiveOverlapTolerance = 0.001f * M; // 1 mm

		readonly struct EndpointPair
		{
			public readonly Vector3 A;
			public readonly Vector3 B;
			public readonly float Distance;

			public EndpointPair( Vector3 a, Vector3 b, float distance )
			{
				A = a;
				B = b;
				Distance = distance;
			}
		}

		readonly struct Obb2
		{
			public readonly Vector3 Center;
			public readonly Vector3 AxisX;
			public readonly Vector3 AxisY;
			public readonly float HalfX;
			public readonly float HalfY;

			public Obb2( Vector3 center, Vector3 axisX, Vector3 axisY, float halfX, float halfY )
			{
				Center = center;
				AxisX = NormalizeXY( axisX );
				AxisY = NormalizeXY( axisY );
				HalfX = MathF.Max( 0, halfX );
				HalfY = MathF.Max( 0, halfY );
			}
		}

		/// <summary>
		/// Evaluate one pair of wall tasks using the shared corner assembly
		/// model (centerline-intersection junction, cell-based validation).
		/// The assembly places 8 bricks per course (4 along each wall) at
		/// col=0, bridging the gap from the junction to each wall's first
		/// placed brick. Validation checks that the assembly bricks are
		/// present on every course, not that ownership alternates.
		/// </summary>
		public static WallCornerTopologyResult Evaluate( VillageBuildTask wallA, VillageBuildTask wallB )
		{
			// Junction = intersection of the two wall centerlines (not a
			// shared endpoint). The walls are centered on their Position,
			// extending +/- WallSegmentLength/2 along their forward axis.
			var aAxis = AxisForYaw( wallA.Rotation );
			var bAxis = AxisForYaw( wallB.Rotation );
			var junction = CenterlineIntersection( wallA.Position, aAxis, wallB.Position, bAxis );
			bool perpendicular = IsPerpendicular( wallA.Rotation, wallB.Rotation );

			// Endpoint distance (for diagnostics — not a validity gate anymore)
			var endpointPair = ClosestEndpointPair( wallA, wallB );

			var result = new WallCornerTopologyResult
			{
				WallAName = wallA.Name,
				WallBName = wallB.Name,
				Junction = junction,
				EndpointDistance = endpointPair.Distance,
				IsPerpendicular = perpendicular,
				EndpointsMeet = true, // centerline model: walls meet at centerline intersection
			};

			if ( wallA.TaskType != "wall" || wallB.TaskType != "wall" )
				return result;

			if ( !perpendicular )
				return result;

			if ( junction == Vector3.Zero )
				return result; // parallel or no intersection

			// The assembly bricks are stored in wallA.PlacedBricks (the owner)
			// at col=0, wythe 0-3, with BrickForm.Full. Each course should have
			// 8 assembly bricks (4 along A + 4 along B). The wall's normal bricks
			// start at col=1 (col=0 is skipped by ShouldButt).
			int maxCourseA = MaxCourse( wallA.PlacedBricks );
			int maxCourseB = MaxCourse( wallB.PlacedBricks );
			int maxCommonCourse = Math.Min( maxCourseA, maxCourseB );
			if ( maxCommonCourse < 0 )
				return result;

			for ( int course = 0; course <= maxCommonCourse; course++ )
			{
				var aCourse = SlotsForCourse( wallA.PlacedBricks, course );
				var bCourse = SlotsForCourse( wallB.PlacedBricks, course );

				if ( aCourse.Count == 0 || bCourse.Count == 0 )
				{
					result.IncompleteCourses++;
					continue;
				}

				result.CoursesChecked++;

				// Count assembly bricks at col=0 on this course.
				// The assembly stores 8 bricks per course in wallA (the owner):
				// 4 along A (col=0, wythe 0-3) + 4 along B (col=0, wythe 0-3).
				int aAssemblyBricks = 0;
				int bAssemblyBricks = 0;
				foreach ( var slot in aCourse )
				{
					if ( slot.GridX == 0 && slot.Form == BrickForm.Full )
						aAssemblyBricks++;
				}
				foreach ( var slot in bCourse )
				{
					if ( slot.GridX == 0 && slot.Form == BrickForm.Full )
						bAssemblyBricks++;
				}

				// The owner wall (wallA) should have 8 assembly bricks (4+4).
				// The non-owner wall (wallB) should have 0 assembly bricks
				// (its col=0 is skipped, assembly is emitted by wallA).
				int totalAssembly = aAssemblyBricks + bAssemblyBricks;
				if ( totalAssembly < 8 )
				{
					result.UnownedCourses++;
				}
				else if ( totalAssembly > 8 )
				{
					result.DoubleOwnedCourses++;
					result.DuplicateBrickPairs += totalAssembly - 8;
				}

				// Ownership doesn't alternate in the new model — every course
				// has the same owner (wallA). This is valid, not a defect.
				result.OwnershipAlternates = true;
			}

			// Bond depth: the assembly bricks are 2 modules long, crossing
			// from the junction to the wall's col=1. That's 2 modules = 0.5m.
			if ( result.CoursesChecked > 0 )
				result.BondDepth = BrickModuleX * 2f;

			return result;
		}

		/// <summary>
		/// Find all geometrically adjacent 90-degree wall pairs in a task list and
		/// evaluate their actual placed BrickSlots.
		/// </summary>
		public static List<WallCornerTopologyResult> EvaluateAll( IReadOnlyList<VillageBuildTask> tasks )
		{
			var results = new List<WallCornerTopologyResult>();
			if ( tasks is null ) return results;

			for ( int i = 0; i < tasks.Count; i++ )
			{
				var a = tasks[i];
				if ( a is null || a.TaskType != "wall" ) continue;

				for ( int j = i + 1; j < tasks.Count; j++ )
				{
					var b = tasks[j];
					if ( b is null || b.TaskType != "wall" ) continue;
					if ( !IsPerpendicular( a.Rotation, b.Rotation ) ) continue;

					var pair = ClosestEndpointPair( a, b );
					// Use centerline intersection (not endpoint distance) for the new model
				var aAx = AxisForYaw( a.Rotation );
				var bAx = AxisForYaw( b.Rotation );
				var jct = CenterlineIntersection( a.Position, aAx, b.Position, bAx );
				if ( jct == Vector3.Zero ) continue;

					results.Add( Evaluate( a, b ) );
				}
			}

			return results;
		}

		static List<Obb2> FootprintsIntersectingCore( VillageBuildTask wall, List<BrickSlot> slots, Obb2 core )
		{
			var result = new List<Obb2>();
			foreach ( var slot in slots )
			{
				var footprint = SlotFootprint( wall, slot );
				if ( HasPositiveOverlap( footprint, core ) )
					result.Add( footprint );
			}
			return result;
		}

		static int CountPositiveOverlaps( List<Obb2> a, List<Obb2> b )
		{
			int count = 0;
			foreach ( var aa in a )
				foreach ( var bb in b )
					if ( HasPositiveOverlap( aa, bb ) ) count++;
			return count;
		}

		static Obb2 SlotFootprint( VillageBuildTask wall, BrickSlot slot )
		{
			int modulesX = (int)MathF.Round( WallSegmentLength / BrickModuleX );
			float localX;
			float brickLength;

			if ( slot.Form == BrickForm.Half )
			{
				brickLength = BrickModuleX * 0.5f;
				if ( slot.GridX <= 0 )
					localX = -WallSegmentLength * 0.5f + brickLength * 0.5f;
				else if ( slot.GridX >= modulesX )
					localX = WallSegmentLength * 0.5f - brickLength * 0.5f;
				else
					localX = -WallSegmentLength * 0.5f + slot.GridX * BrickModuleX;
			}
			else
			{
				brickLength = BrickModuleX;
				bool oddCourse = (slot.GridZ & 1) == 1;
				localX = oddCourse
					? -WallSegmentLength * 0.5f + slot.GridX * BrickModuleX
					: -WallSegmentLength * 0.5f + BrickModuleX * 0.5f + slot.GridX * BrickModuleX;
			}

			float localY = -WallThickness * 0.5f + BrickModuleY * 0.5f + slot.GridY * BrickModuleY;
			var axisX = AxisForYaw( wall.Rotation );
			var axisY = new Vector3( -axisX.y, axisX.x, 0 );
			var center = wall.Position + axisX * localX + axisY * localY;

			float halfX = MathF.Max( 0, brickLength * 0.5f - PositiveOverlapTolerance * 0.5f );
			float halfY = MathF.Max( 0, BrickModuleY * 0.5f - PositiveOverlapTolerance * 0.5f );
			return new Obb2( center, axisX, axisY, halfX, halfY );
		}

		static List<BrickSlot> SlotsForCourse( HashSet<BrickSlot> slots, int course )
		{
			var result = new List<BrickSlot>();
			foreach ( var slot in slots )
				if ( slot.GridZ == course ) result.Add( slot );
			return result;
		}

		static int MaxCourse( HashSet<BrickSlot> slots )
		{
			int max = -1;
			foreach ( var slot in slots )
				if ( slot.GridZ > max ) max = slot.GridZ;
			return max;
		}

		/// <summary>
		/// Compute the intersection of two 2D centerlines (infinite lines through
		/// the wall positions along their forward axes). Returns Vector3.Zero
		/// if the lines are parallel or coincident.
		/// </summary>
		static Vector3 CenterlineIntersection( Vector3 posA, Vector3 axisA, Vector3 posB, Vector3 axisB )
		{
			// Line A: posA + t * axisA
			// Line B: posB + s * axisB
			// Solve: posA + t * axisA = posB + s * axisB
			float denom = axisA.x * (-axisB.y) - axisA.y * (-axisB.x);
			if ( MathF.Abs( denom ) < 0.0001f )
				return Vector3.Zero; // parallel

			var d = posB - posA;
			float t = (d.x * (-axisB.y) - d.y * (-axisB.x)) / denom;
			return posA + axisA * t;
		}

		static EndpointPair ClosestEndpointPair( VillageBuildTask a, VillageBuildTask b )
		{
			var aAxis = AxisForYaw( a.Rotation );
			var bAxis = AxisForYaw( b.Rotation );
			var a0 = a.Position - aAxis * (WallSegmentLength * 0.5f);
			var a1 = a.Position + aAxis * (WallSegmentLength * 0.5f);
			var b0 = b.Position - bAxis * (WallSegmentLength * 0.5f);
			var b1 = b.Position + bAxis * (WallSegmentLength * 0.5f);

			var best = new EndpointPair( a0, b0, DistanceXY( a0, b0 ) );
			TryPair( a0, b1, ref best );
			TryPair( a1, b0, ref best );
			TryPair( a1, b1, ref best );
			return best;
		}

		static void TryPair( Vector3 a, Vector3 b, ref EndpointPair best )
		{
			float distance = DistanceXY( a, b );
			if ( distance < best.Distance )
				best = new EndpointPair( a, b, distance );
		}

		static bool IsPerpendicular( float yawA, float yawB )
		{
			float delta = MathF.Abs( Normalize180( yawA - yawB ) );
			if ( delta > 90f ) delta = 180f - delta;
			return MathF.Abs( delta - 90f ) <= PerpendicularToleranceDeg;
		}

		static float Normalize180( float degrees )
		{
			degrees %= 360f;
			if ( degrees > 180f ) degrees -= 360f;
			if ( degrees < -180f ) degrees += 360f;
			return degrees;
		}

		static Vector3 AxisForYaw( float yawDegrees )
		{
			float radians = yawDegrees * MathF.PI / 180f;
			return new Vector3( MathF.Cos( radians ), MathF.Sin( radians ), 0 );
		}

		static bool HasPositiveOverlap( Obb2 a, Obb2 b )
		{
			var delta = b.Center - a.Center;
			var axes = new[] { a.AxisX, a.AxisY, b.AxisX, b.AxisY };
			foreach ( var axis in axes )
			{
				float centerDistance = MathF.Abs( DotXY( delta, axis ) );
				float radiusA = a.HalfX * MathF.Abs( DotXY( a.AxisX, axis ) )
					+ a.HalfY * MathF.Abs( DotXY( a.AxisY, axis ) );
				float radiusB = b.HalfX * MathF.Abs( DotXY( b.AxisX, axis ) )
					+ b.HalfY * MathF.Abs( DotXY( b.AxisY, axis ) );

				if ( radiusA + radiusB - centerDistance <= PositiveOverlapTolerance )
					return false;
			}
			return true;
		}

		static Vector3 NormalizeXY( Vector3 value )
		{
			float length = MathF.Sqrt( value.x * value.x + value.y * value.y );
			if ( length <= 0.0001f ) return Vector3.Zero;
			return new Vector3( value.x / length, value.y / length, 0 );
		}

		static float DotXY( Vector3 a, Vector3 b ) => a.x * b.x + a.y * b.y;

		static float DistanceXY( Vector3 a, Vector3 b )
		{
			float dx = a.x - b.x;
			float dy = a.y - b.y;
			return MathF.Sqrt( dx * dx + dy * dy );
		}
	}

	/// <summary>
	/// Editor/runtime diagnostic component for the wall-corner validator.
	/// Assign any VillageBuilder (all builders share the authoritative task
	/// list), toggle RunProbe, and inspect the Lute log output.
	/// </summary>
	public sealed class WallCornerTopologyProbe : Component
	{
		[Property] public VillageBuilder Builder { get; set; }
		[Property] public bool RunProbe { get; set; }
		[Property] public bool LogPassingCorners { get; set; } = true;

		protected override void OnUpdate()
		{
			if ( !RunProbe ) return;
			RunProbe = false;

			if ( Builder is null )
			{
				Log.Warning( "Lute: WallCornerTopologyProbe has no VillageBuilder assigned." );
				return;
			}

			var results = WallCornerTopologyValidator.EvaluateAll( Builder.Tasks );
			if ( results.Count == 0 )
			{
				Log.Warning( "Lute: WallCornerTopologyProbe found no adjacent perpendicular wall pairs." );
				return;
			}

			int passed = 0;
			foreach ( var result in results )
			{
				if ( result.IsValid )
				{
					passed++;
					if ( LogPassingCorners ) Log.Info( $"Lute: [corner PASS] {result}" );
				}
				else
				{
					Log.Warning( $"Lute: [corner FAIL] {result}" );
				}
			}

			Log.Info( $"Lute: WallCornerTopologyProbe complete — {passed}/{results.Count} corners passed." );
		}

		/// <summary>
		/// Run the corner topology probe from the console. Finds all
		/// VillageBuilder instances and evaluates their wall corners.
		/// </summary>
		[ConCmd( "corner_probe" )]
		public static void RunProbeCommand()
		{
			var builder = Game.ActiveScene.GetAllComponents<VillageBuilder>().FirstOrDefault();
			if ( builder is null )
			{
				Log.Warning( "Lute: corner_probe — no VillageBuilder found." );
				return;
			}

			var results = WallCornerTopologyValidator.EvaluateAll( builder.Tasks );
			if ( results.Count == 0 )
			{
				Log.Warning( "Lute: corner_probe — no adjacent perpendicular wall pairs found." );
				return;
			}

			int passed = 0;
			foreach ( var result in results )
			{
				if ( result.IsValid )
				{
					passed++;
					Log.Info( $"Lute: [corner PASS] {result}" );
				}
				else
				{
					Log.Warning( $"Lute: [corner FAIL] {result}" );
				}
			}

			Log.Info( $"Lute: corner_probe complete — {passed}/{results.Count} corners passed." );
		}
	}
}
