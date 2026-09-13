using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox;

namespace Lute.Building
{
	public sealed class ReferenceCornerTest : Component
	{
		const float M = 39.37f;
		const float BoxModelNativeSize = 50f;

		static readonly Vector3 BrickBodySize = new( 0.24f * M, 0.115f * M, 0.055f * M );
		static readonly Vector3 BrickModuleSize = new( 0.25f * M, 0.125f * M, 0.0625f * M );

		const float BrickModuleX = 0.25f * M;
		const float BrickModuleY = 0.125f * M;
		const float BrickModuleZ = 0.0625f * M;
		const float WallSegmentLength = 2f * M;
		const float WallThickness = 0.5f * M;
		const float WallHeight = 2f * M;

		enum CornerButtSide { None, Left, Right }
		enum CornerButtCourses { None, Even, Odd }

		[Property] public bool RunTest { get; set; } = false;
		[Property] public Vector3 CornerOrigin { get; set; } = new( 5000, 5000, 0 );
		[Property] public string WallMaterial { get; set; } = "materials/medieval/castle_wall.vmat";
		[Property] public bool InstantBuild { get; set; } = true;

		private GameObject _root;
		private int _passed;
		private int _failed;

		protected override void OnUpdate()
		{
			if ( RunTest )
			{
				RunTest = false;
				_ = RunReferenceCornerTest();
			}
		}

		async Task RunReferenceCornerTest()
		{
			Log.Info( "Lute: ReferenceCornerTest START ========================================" );
			_passed = 0;
			_failed = 0;

			_root = Scene.CreateObject( true );
			_root.Name = "ReferenceCornerTest";
			_root.WorldPosition = CornerOrigin;

			var posA = CornerOrigin - new Vector3( WallSegmentLength * 0.5f, 0, 0 );
			var posB = CornerOrigin + new Vector3( 0, WallSegmentLength * 0.5f, 0 );

			var taskA = new VillageBuildTask
			{
				Name = "RefWallA",
				TaskType = "wall",
				Position = posA,
				Rotation = 0,
				Priority = 0,
			};

			var taskB = new VillageBuildTask
			{
				Name = "RefWallB",
				TaskType = "wall",
				Position = posB,
				Rotation = 90,
				Priority = 0,
			};

			Log.Info( "Lute: ReferenceCornerTest Stage 1: Build two perpendicular walls (alternating ownership)" );
			int fullPieces = ComputeExpectedPieces();
			Log.Info( $"  Full pieces per wall (no butt): {fullPieces}" );

			await BuildWall( taskA, "A", CornerButtSide.Right, CornerButtCourses.Odd );
			await BuildWall( taskB, "B", CornerButtSide.Left, CornerButtCourses.Even );

			int numRows = (int)MathF.Round( WallHeight / BrickModuleZ );
			int numWythes = (int)MathF.Round( WallThickness / BrickModuleY );
			int oddCourses = numRows / 2;
			int evenCourses = (numRows + 1) / 2;
			// Wall A butts on odd courses: skips 2 bricks per wythe (last full + right half)
			int expectedA = fullPieces - oddCourses * numWythes * 2;
			// Wall B butts on even courses: skips 1 brick per wythe (first full stretcher)
			int expectedB = fullPieces - evenCourses * numWythes;
			Log.Info( $"  Expected A pieces (butt odd {oddCourses}c x {numWythes}w x 2 = {oddCourses*numWythes*2}): {expectedA}" );
			Log.Info( $"  Expected B pieces (butt even {evenCourses}c x {numWythes}w = {evenCourses*numWythes}): {expectedB}" );

			Check( "Wall A piece count", taskA.PiecesPlaced == expectedA, $"{taskA.PiecesPlaced} vs {expectedA}" );
			Check( "Wall B piece count", taskB.PiecesPlaced == expectedB, $"{taskB.PiecesPlaced} vs {expectedB}" );

			Log.Info( "Lute: ReferenceCornerTest Stage 2: Verify corner geometry" );

			var aAxis = AxisForYaw( 0 );
			var aRight = posA + aAxis * (WallSegmentLength * 0.5f);
			Check( "Wall A right endpoint at corner",
				MathF.Abs( aRight.x - CornerOrigin.x ) < 0.01f && MathF.Abs( aRight.y - CornerOrigin.y ) < 0.01f,
				$"{aRight} vs {CornerOrigin}" );

			var bAxis = AxisForYaw( 90 );
			var bLeft = posB - bAxis * (WallSegmentLength * 0.5f);
			Check( "Wall B left endpoint at corner",
				MathF.Abs( bLeft.x - CornerOrigin.x ) < 0.01f && MathF.Abs( bLeft.y - CornerOrigin.y ) < 0.01f,
				$"{bLeft} vs {CornerOrigin}" );

			Log.Info( "Lute: ReferenceCornerTest Stage 3: WallCornerTopologyValidator" );
			var result = WallCornerTopologyValidator.Evaluate( taskA, taskB );
			Log.Info( $"  Result: {result}" );

			Check( "Walls are perpendicular", result.IsPerpendicular, $"perpendicular={result.IsPerpendicular}" );
			Check( "Endpoints meet", result.EndpointsMeet, $"endpointDist={result.EndpointDistance:F3}" );

			Log.Info( $"  CoursesChecked={result.CoursesChecked}" );
			Log.Info( $"  IncompleteCourses={result.IncompleteCourses}" );
			Log.Info( $"  DoubleOwnedCourses={result.DoubleOwnedCourses}" );
			Log.Info( $"  UnownedCourses={result.UnownedCourses}" );
			Log.Info( $"  DuplicateBrickPairs={result.DuplicateBrickPairs}" );
			Log.Info( $"  OwnershipAlternates={result.OwnershipAlternates}" );
			Log.Info( $"  IsValid={result.IsValid}" );

			Check( "Courses were checked (both walls built)", result.CoursesChecked > 0, $"courses={result.CoursesChecked}" );
			Check( "No double-owned courses", result.DoubleOwnedCourses == 0, $"doubleOwned={result.DoubleOwnedCourses}" );
			Check( "No unowned courses", result.UnownedCourses == 0, $"unowned={result.UnownedCourses}" );
			Check( "No duplicate brick pairs", result.DuplicateBrickPairs == 0, $"duplicatePairs={result.DuplicateBrickPairs}" );
			Check( "Ownership alternates", result.OwnershipAlternates, $"alternates={result.OwnershipAlternates}" );
			Check( "Corner is VALID", result.IsValid, $"valid={result.IsValid}" );

			Log.Info( "Lute: ReferenceCornerTest REPORT ======================================" );
			Log.Info( $"  PASSED: {_passed}" );
			Log.Info( $"  FAILED: {_failed}" );
			if ( _failed == 0 )
				Log.Info( "  RESULT: ALL CHECKS PASSED." );
			else
				Log.Warning( $"  RESULT: {_failed} CHECK(S) FAILED - see above." );
			Log.Info( "Lute: ReferenceCornerTest END ========================================" );
		}

		int ComputeExpectedPieces()
		{
			int modulesX = (int)MathF.Round( WallSegmentLength / BrickModuleX );
			int numRows = (int)MathF.Round( WallHeight / BrickModuleZ );
			int numWythes = (int)MathF.Round( WallThickness / BrickModuleY );
			int evenRows = (numRows + 1) / 2;
			int oddRows = numRows / 2;
			return (modulesX * evenRows + (modulesX + 1) * oddRows) * numWythes;
		}

		async Task BuildWall( VillageBuildTask task, string label,
			CornerButtSide buttSide = CornerButtSide.None,
			CornerButtCourses buttCourses = CornerButtCourses.None )
		{
			float segLen = WallSegmentLength;
			float wallH = WallHeight;
			float wallDepth = WallThickness;
			float brickLen = BrickBodySize.x;
			float brickDepth = BrickBodySize.y;
			float brickH = BrickBodySize.z;

			int modulesX = (int)MathF.Round( segLen / BrickModuleX );
			int numRows = (int)MathF.Round( wallH / BrickModuleZ );
			int numWythes = (int)MathF.Round( wallDepth / BrickModuleY );
			int evenRows = (numRows + 1) / 2;
			int oddRows = numRows / 2;
			int totalBricks = (modulesX * evenRows + (modulesX + 1) * oddRows) * numWythes;
			task.TotalPieces = totalBricks;

			float cos = (float)Math.Cos( task.Rotation * Math.PI / 180 );
			float sin = (float)Math.Sin( task.Rotation * Math.PI / 180 );

			Vector3 RotateLocal( Vector3 local )
			{
				return task.Position + new Vector3(
					local.x * cos - local.y * sin,
					local.x * sin + local.y * cos,
					local.z );
			}

			bool ShouldButt( bool isLeftEdge, bool isRightEdge, int row )
			{
				if ( buttSide == CornerButtSide.None || buttCourses == CornerButtCourses.None )
					return false;
				bool isOdd = (row % 2 == 1);
				bool buttOnOdd = buttCourses == CornerButtCourses.Odd;
				bool courseButts = isOdd == buttOnOdd;
				if ( !courseButts ) return false;
				if ( buttSide == CornerButtSide.Left && isLeftEdge ) return true;
				if ( buttSide == CornerButtSide.Right && isRightEdge ) return true;
				return false;
			}

			int brickIdx = 0;
			int skipCount = 0;
			for ( int wythe = 0; wythe < numWythes; wythe++ )
			{
				float yCenter = -wallDepth * 0.5f + BrickModuleY * 0.5f + wythe * BrickModuleY;
				for ( int row = 0; row < numRows; row++ )
				{
					float z = row * BrickModuleZ;
					bool isOdd = (row % 2 == 1);
					float halfLen = BrickModuleX * 0.5f;

					if ( isOdd )
					{
						// Left half-brick (col 0)
						if ( ShouldButt( true, false, row ) )
							skipCount++;
						else
						{
							float lx = -segLen * 0.5f + halfLen * 0.5f;
							var pos = RotateLocal( new Vector3( lx, yCenter, z ) );
							SpawnBrick( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ) );
							task.PiecesPlaced = brickIdx + 1;
							task.PlacedBricks.Add( BrickSlot.HalfStretcher( 0, wythe, row ) );
							brickIdx++;
							if ( !InstantBuild ) await Task.DelaySeconds( 0.01f );
						}

						for ( int col = 1; col < modulesX; col++ )
						{
							// On odd courses, the last full stretcher (col=modulesX-1)
							// also overlaps the corner core, so butt it too.
							bool isRightEdgeFull = (col == modulesX - 1);
							if ( ShouldButt( false, isRightEdgeFull, row ) )
							{
								skipCount++;
								continue;
							}
							float x = -segLen * 0.5f + col * BrickModuleX;
							var pos = RotateLocal( new Vector3( x, yCenter, z ) );
							SpawnBrick( pos, new Vector3( brickLen, brickDepth, brickH ) );
							task.PiecesPlaced = brickIdx + 1;
							task.PlacedBricks.Add( BrickSlot.Stretcher( col, wythe, row ) );
							brickIdx++;
							if ( !InstantBuild ) await Task.DelaySeconds( 0.01f );
						}

						// Right half-brick (col modulesX)
						if ( ShouldButt( false, true, row ) )
							skipCount++;
						else
						{
							float rx = segLen * 0.5f - halfLen * 0.5f;
							var pos = RotateLocal( new Vector3( rx, yCenter, z ) );
							SpawnBrick( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ) );
							task.PiecesPlaced = brickIdx + 1;
							task.PlacedBricks.Add( BrickSlot.HalfStretcher( modulesX, wythe, row ) );
							brickIdx++;
							if ( !InstantBuild ) await Task.DelaySeconds( 0.01f );
						}
					}
					else
					{
						for ( int col = 0; col < modulesX; col++ )
						{
							bool isLeft = (col == 0);
							bool isRight = (col == modulesX - 1);
							if ( ShouldButt( isLeft, isRight, row ) )
							{
								skipCount++;
								continue;
							}

							float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
							var pos = RotateLocal( new Vector3( x, yCenter, z ) );
							SpawnBrick( pos, new Vector3( brickLen, brickDepth, brickH ) );
							task.PiecesPlaced = brickIdx + 1;
							task.PlacedBricks.Add( BrickSlot.Stretcher( col, wythe, row ) );
							brickIdx++;
							if ( !InstantBuild ) await Task.DelaySeconds( 0.01f );
						}
					}
				}
			}
			Log.Info( $"  Wall {label}: built {brickIdx} bricks, {task.PlacedBricks.Count} slots, skipped {skipCount} (buttSide={buttSide} buttCourses={buttCourses})." );
		}

		static int task_counter = 0;

		void SpawnBrick( Vector3 worldPos, Vector3 size )
		{
			worldPos = worldPos.WithZ( worldPos.z + size.z * 0.5f );
			var go = Scene.CreateObject( false );
			go.Name = $"RefCorner_brick_{task_counter++}";
			go.SetParent( _root );
			go.WorldPosition = worldPos;
			var renderer = go.AddComponent<ModelRenderer>();
			renderer.Model = Cloud.Model( "facepunch.brick_single_04" );
			if ( renderer.Model is not null )
			{
				var modelSize = renderer.Model.Bounds.Size;
				if ( modelSize.x > 0 && modelSize.y > 0 && modelSize.z > 0 )
				{
					bool isHalf = size.x < BrickBodySize.x * 0.75f;
					var renderSize = isHalf
						? new Vector3( BrickModuleX * 0.5f, BrickModuleY, BrickModuleZ )
						: BrickModuleSize;
					go.WorldScale = renderSize / modelSize;
				}
				else
				{
					go.WorldScale = size / BoxModelNativeSize;
				}
			}
			else
			{
				go.WorldScale = size / BoxModelNativeSize;
			}
			go.Enabled = true;
		}

		void Check( string name, bool ok, string detail )
		{
			if ( ok )
			{
				_passed++;
				Log.Info( $"  [PASS] {name}" + (string.IsNullOrEmpty( detail ) ? "" : $" — {detail}") );
			}
			else
			{
				_failed++;
				Log.Warning( $"  [FAIL] {name}" + (string.IsNullOrEmpty( detail ) ? "" : $" — {detail}") );
			}
		}

		static Vector3 AxisForYaw( float yawDegrees )
		{
			float radians = yawDegrees * MathF.PI / 180f;
			return new Vector3( MathF.Cos( radians ), MathF.Sin( radians ), 0 );
		}
	}
}
