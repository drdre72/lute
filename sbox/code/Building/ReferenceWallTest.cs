using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Canonical Lute reference wall test fixture.
	///
	/// Builds a 2.0m x 2.0m x 0.5m wall using the BrickSlot topology
	/// primitive, then runs the full build -> finalize -> deconstruct
	/// cycle and logs a structured pass/fail report.
	///
	/// The reference wall is:
	///   8 modules long  (25cm pitch  = 2.0m)
	///   32 courses high (6.25cm pitch = 2.0m)
	///   4 wythes deep   (12.5cm pitch = 0.5m)
	///   = 1088 bricks (16 even x 32 + 16 odd x 36)
	///
	/// Trigger by setting <see cref="RunTest"/> to true from the editor
	/// or MCP. The test runs asynchronously and logs every stage.
	/// </summary>
	public sealed class ReferenceWallTest : Component
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

		/// <summary>
		/// Set to true to run the reference wall test. Cleared after the
		/// test completes. Trigger from the editor inspector or MCP.
		/// </summary>
		[Property] public bool RunTest { get; set; } = false;

		/// <summary>
		/// World position for the reference wall origin (bottom-center).
		/// Defaults to a spot near the village center for easy inspection.
		/// </summary>
		[Property] public Vector3 WallOrigin { get; set; } = new( 5000, 5000, 0 );

		/// <summary> Wall material path. </summary>
		[Property] public string WallMaterial { get; set; } = "materials/medieval/castle_wall.vmat";

		/// <summary> If true, place all bricks instantly (no delays). </summary>
		[Property] public bool InstantBuild { get; set; } = true;

		private GameObject _root;
		private int _passed;
		private int _failed;

		protected override void OnUpdate()
		{
			if ( RunTest )
			{
				RunTest = false;
				_ = RunReferenceWallTest();
			}
		}

		async Task RunReferenceWallTest()
		{
			Log.Info( "Lute: ReferenceWallTest START ============================================" );
			_passed = 0;
			_failed = 0;

			// Create a root for the test wall
			_root = Scene.CreateObject( true );
			_root.Name = "ReferenceWallTest";
			_root.WorldPosition = WallOrigin;

			// Build the task
			var task = new VillageBuildTask
			{
				Name = "RefWall",
				TaskType = "wall",
				Position = WallOrigin,
				Rotation = 0,
				Priority = 0,
			};

			// ── Stage 1: Build ──
			Log.Info( "Lute: ReferenceWallTest Stage 1: Build" );
			int expectedPieces = ComputeExpectedPieces();
			Log.Info( $"  Expected pieces: {expectedPieces} (8x32x4, 16 even x 32 + 16 odd x 36)" );

			await BuildReferenceWall( task );

			Check( "Piece count matches expected",
				task.PiecesPlaced == expectedPieces,
				$"{task.PiecesPlaced} placed vs {expectedPieces} expected" );

			Check( "TotalPieces matches PiecesPlaced",
				task.TotalPieces == task.PiecesPlaced,
				$"{task.TotalPieces} vs {task.PiecesPlaced}" );

			Check( "PlacedBricks count matches",
				task.PlacedBricks.Count == expectedPieces,
				$"{task.PlacedBricks.Count} slots vs {expectedPieces} expected" );

			// Verify edge bricks: even course first brick left face at -1.0m
			float segLen = WallSegmentLength;
			float firstEvenCenter = -segLen * 0.5f + BrickModuleX * 0.5f;
			float firstEvenLeftFace = firstEvenCenter - BrickBodySize.x * 0.5f;
			Check( "Even course first brick left face at -segLen/2",
				MathF.Abs( firstEvenLeftFace - (-segLen * 0.5f) ) < 0.001f,
				$"left face at {firstEvenLeftFace:F3}, expected {-segLen * 0.5f:F3}" );

			// Verify odd course fills -1.0 to +1.0
			float halfLen = BrickModuleX * 0.5f;
			float oddLeftHalfCenter = -segLen * 0.5f + halfLen * 0.5f;
			float oddLeftHalfLeftFace = oddLeftHalfCenter - (BrickBodySize.x * 0.5f) * 0.5f;
			Check( "Odd course left half left face at -segLen/2",
				MathF.Abs( oddLeftHalfLeftFace - (-segLen * 0.5f) ) < 0.001f,
				$"left face at {oddLeftHalfLeftFace:F3}, expected {-segLen * 0.5f:F3}" );

			float oddRightHalfCenter = segLen * 0.5f - halfLen * 0.5f;
			float oddRightHalfRightFace = oddRightHalfCenter + (BrickBodySize.x * 0.5f) * 0.5f;
			Check( "Odd course right half right face at +segLen/2",
				MathF.Abs( oddRightHalfRightFace - (segLen * 0.5f) ) < 0.001f,
				$"right face at {oddRightHalfRightFace:F3}, expected {segLen * 0.5f:F3}" );

			// ── Stage 2: Structural query ──
			Log.Info( "Lute: ReferenceWallTest Stage 2: Structural query" );
			int modulesX = (int)MathF.Round( segLen / BrickModuleX );
			int numRows = (int)MathF.Round( WallHeight / BrickModuleZ );
			var query = EvaluateWallStructure( task, modulesX, numRows );
			Log.Info( $"  Coverage={query.Coverage:F3} Foundation={query.FoundationSupported} Courses={query.CoursesContinuous} Corners={query.RequiredCornersBonded} Top={query.TopCourseComplete} NoGap={query.NoIllegalGap}" );

			Check( "Coverage == 1.0", MathF.Abs( query.Coverage - 1.0f ) < 0.001f, $"{query.Coverage:F3}" );
			Check( "FoundationSupported", query.FoundationSupported, "" );
			Check( "CoursesContinuous", query.CoursesContinuous, "" );
			Check( "RequiredCornersBonded", query.RequiredCornersBonded, "" );
			Check( "TopCourseComplete", query.TopCourseComplete, "" );
			Check( "NoIllegalGap", query.NoIllegalGap, "" );
			Check( "CanDirectorFinalize", query.CanDirectorFinalize, "" );

			// ── Stage 3: Finalize ──
			Log.Info( "Lute: ReferenceWallTest Stage 3: Finalize" );
			task.WallState = WallSegmentState.FinalizationEligible;
			bool finalized = FinalizeWall( task );
			Check( "FinalizeWall returned true", finalized, "" );
			Check( "FinalizedMeshGo is not null", task.FinalizedMeshGo is not null, "" );
			Check( "WallState == Finalized", task.WallState == WallSegmentState.Finalized, $"{task.WallState}" );

			// Verify finalized mesh transform is identity (not double-scaled)
			if ( task.FinalizedMeshGo is not null )
			{
				var scale = task.FinalizedMeshGo.WorldScale;
				Check( "Finalized wall scale == Vector3.One",
					MathF.Abs( scale.x - 1f ) < 0.001f && MathF.Abs( scale.y - 1f ) < 0.001f && MathF.Abs( scale.z - 1f ) < 0.001f,
					$"scale={scale}" );
				var pos = task.FinalizedMeshGo.WorldPosition;
				Check( "Finalized wall position == WallOrigin",
					MathF.Abs( pos.x - WallOrigin.x ) < 0.1f && MathF.Abs( pos.y - WallOrigin.y ) < 0.1f && MathF.Abs( pos.z - WallOrigin.z ) < 0.1f,
					$"pos={pos} expected={WallOrigin}" );
			}

			// ── Stage 4: Deconstruct ──
			Log.Info( "Lute: ReferenceWallTest Stage 4: Deconstruct" );
			int slotsBefore = task.PlacedBricks.Count;
			bool deconstructed = DeconstructWall( task );
			Check( "DeconstructWall returned true", deconstructed, "" );
			Check( "WallState == Deconstructing", task.WallState == WallSegmentState.Deconstructing, $"{task.WallState}" );
			int slotsAfter = task.PlacedBricks.Count;
			int topSlots = (numRows - 1) % 2 == 1 ? modulesX + 1 : modulesX;
			Check( "Top course slots removed",
				slotsBefore - slotsAfter == topSlots,
				$"{slotsBefore} -> {slotsAfter} (removed {slotsBefore - slotsAfter}, expected {topSlots})" );

			// ── Report ──
			Log.Info( "Lute: ReferenceWallTest REPORT ==========================================" );
			Log.Info( $"  PASSED: {_passed}" );
			Log.Info( $"  FAILED: {_failed}" );
			if ( _failed == 0 )
				Log.Info( "  RESULT: ALL CHECKS PASSED - reference wall is canonical." );
			else
				Log.Warning( $"  RESULT: {_failed} CHECK(S) FAILED - see above." );
			Log.Info( "Lute: ReferenceWallTest END ==============================================" );
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

		async Task BuildReferenceWall( VillageBuildTask task )
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

			int brickIdx = 0;
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
						// Left half
						{
							float lx = -segLen * 0.5f + halfLen * 0.5f;
							var pos = task.Position + new Vector3( lx, yCenter, z );
							SpawnBrick( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ) );
							task.PiecesPlaced = brickIdx + 1;
							task.PlacedBricks.Add( BrickSlot.HalfStretcher( 0, wythe, row ) );
						}
						brickIdx++;
						if ( !InstantBuild ) await Task.DelaySeconds( 0.01f );

						for ( int col = 1; col < modulesX; col++ )
						{
							float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
							var pos = task.Position + new Vector3( x, yCenter, z );
							SpawnBrick( pos, new Vector3( brickLen, brickDepth, brickH ) );
							task.PiecesPlaced = brickIdx + 1;
							task.PlacedBricks.Add( BrickSlot.Stretcher( col, wythe, row ) );
							brickIdx++;
							if ( !InstantBuild ) await Task.DelaySeconds( 0.01f );
						}

						// Right half
						{
							float rx = segLen * 0.5f - halfLen * 0.5f;
							var pos = task.Position + new Vector3( rx, yCenter, z );
							SpawnBrick( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ) );
							task.PiecesPlaced = brickIdx + 1;
							task.PlacedBricks.Add( BrickSlot.HalfStretcher( modulesX, wythe, row ) );
						}
						brickIdx++;
						if ( !InstantBuild ) await Task.DelaySeconds( 0.01f );
					}
					else
					{
						for ( int col = 0; col < modulesX; col++ )
						{
							float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
							var pos = task.Position + new Vector3( x, yCenter, z );
							SpawnBrick( pos, new Vector3( brickLen, brickDepth, brickH ) );
							task.PiecesPlaced = brickIdx + 1;
							task.PlacedBricks.Add( BrickSlot.Stretcher( col, wythe, row ) );
							brickIdx++;
							if ( !InstantBuild ) await Task.DelaySeconds( 0.01f );
						}
					}
				}
			}
			Log.Info( $"  Built {brickIdx} bricks, {task.PlacedBricks.Count} BrickSlots." );
		}

		void SpawnBrick( Vector3 worldPos, Vector3 size )
		{
			// Lift to base anchor (center of brick)
			worldPos = worldPos.WithZ( worldPos.z + size.z * 0.5f );
			var go = Scene.CreateObject( false );
			go.Name = $"RefWall_brick_{task_counter++}";
			go.SetParent( _root );
			go.WorldPosition = worldPos;
			var renderer = go.AddComponent<ModelRenderer>();
			renderer.Model = Model.Load( "models/medieval/brick.vmdl" );
			go.WorldScale = size / BoxModelNativeSize;
			go.Enabled = true;
		}

		static int task_counter = 0;

		WallStructuralQuery EvaluateWallStructure( VillageBuildTask task, int modulesX, int numRows )
		{
			var q = new WallStructuralQuery();
			q.Coverage = task.TotalPieces > 0 ? (float)task.PlacedBricks.Count / task.TotalPieces : 0f;

			int SlotsForRow( int row ) => (row % 2 == 1) ? modulesX + 1 : modulesX;

			int foundationCount = 0;
			for ( int col = 0; col < modulesX; col++ )
			{
				if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, 0 ) ) ) foundationCount++;
			}
			q.FoundationSupported = foundationCount == modulesX;

			q.CoursesContinuous = true;
			for ( int row = 0; row < numRows; row++ )
			{
				int slots = SlotsForRow( row );
				bool foundGap = false;
				for ( int col = 0; col < slots; col++ )
				{
					if ( !task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, row ) ) && !foundGap )
					{
						for ( int c2 = col + 1; c2 < slots; c2++ )
						{
							if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( c2, 0, row ) ) ) { foundGap = true; break; }
						}
					}
				}
				if ( foundGap ) { q.CoursesContinuous = false; break; }
			}

			int topSlots = SlotsForRow( numRows - 1 );
			int topCount = 0;
			for ( int col = 0; col < topSlots; col++ )
			{
				if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, numRows - 1 ) ) ) topCount++;
			}
			q.TopCourseComplete = topCount == topSlots;

			q.RequiredCornersBonded = true;
			for ( int row = 0; row < numRows; row++ )
			{
				int slots = SlotsForRow( row );
				bool firstFilled = task.PlacedBricks.Contains( BrickSlot.Stretcher( 0, 0, row ) )
					|| task.PlacedBricks.Contains( BrickSlot.HalfStretcher( 0, 0, row ) );
				bool lastFilled = task.PlacedBricks.Contains( BrickSlot.Stretcher( slots - 1, 0, row ) )
					|| task.PlacedBricks.Contains( BrickSlot.HalfStretcher( slots - 1, 0, row ) );
				if ( !firstFilled || !lastFilled ) { q.RequiredCornersBonded = false; break; }
			}

			q.NoIllegalGap = q.CoursesContinuous;
			if ( q.NoIllegalGap )
			{
				for ( int row = 1; row < numRows; row++ )
				{
					bool rowHas = false, prevHas = false;
					int slots = SlotsForRow( row );
					int prevSlots = SlotsForRow( row - 1 );
					for ( int col = 0; col < slots; col++ ) { if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, row ) ) ) rowHas = true; }
					for ( int col = 0; col < prevSlots; col++ ) { if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, row - 1 ) ) ) prevHas = true; }
					if ( rowHas && !prevHas ) { q.NoIllegalGap = false; break; }
				}
			}
			q.NoPendingStructuralPieces = task.PiecesPlaced >= task.TotalPieces;
			return q;
		}

		bool FinalizeWall( VillageBuildTask task )
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

			var vertices = new List<Vertex>();
			var indices = new List<int>();
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
						AddBrickToMesh( vertices, indices, -segLen * 0.5f + halfLen * 0.5f, yCenter, z, brickLen * 0.5f, brickDepth, brickH );
						for ( int col = 1; col < modulesX; col++ )
						{
							float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
							AddBrickToMesh( vertices, indices, x, yCenter, z, brickLen, brickDepth, brickH );
						}
						AddBrickToMesh( vertices, indices, segLen * 0.5f - halfLen * 0.5f, yCenter, z, brickLen * 0.5f, brickDepth, brickH );
					}
					else
					{
						for ( int col = 0; col < modulesX; col++ )
						{
							float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
							AddBrickToMesh( vertices, indices, x, yCenter, z, brickLen, brickDepth, brickH );
						}
					}
				}
			}

			var mesh = new Mesh();
			mesh.Material = Material.Load( WallMaterial );
#pragma warning disable CS0618
			mesh.CreateVertexBuffer( vertices.Count, Vertex.Layout, vertices );
#pragma warning restore CS0618
			mesh.CreateIndexBuffer( indices.Count, indices );
			mesh.Bounds = new BBox( new Vector3( -segLen * 0.5f, -wallDepth * 0.5f, 0 ), new Vector3( segLen * 0.5f, wallDepth * 0.5f, wallH ) );
			var model = Model.Builder.AddMesh( mesh ).Create();

			var wallGo = Scene.CreateObject( false );
			wallGo.Name = "RefWall_finalized";
			wallGo.SetParent( _root );
			wallGo.WorldPosition = task.Position;
			wallGo.WorldScale = Vector3.One;
			var renderer = wallGo.AddComponent<ModelRenderer>();
			renderer.Model = model;
			var collider = wallGo.AddComponent<BoxCollider>();
			collider.Scale = new Vector3( BoxModelNativeSize, BoxModelNativeSize, BoxModelNativeSize );
			wallGo.Enabled = true;
			task.FinalizedMeshGo = wallGo;

			// Destroy individual brick GameObjects
			int destroyed = 0;
			foreach ( var child in _root.Children )
			{
				if ( child.Name != null && child.Name.StartsWith( "RefWall_brick_" ) ) { child.Destroy(); destroyed++; }
			}
			task.WallState = WallSegmentState.Finalized;
			Log.Info( $"  Finalized: collapsed {destroyed} bricks to 1 mesh." );
			return true;
		}

		void AddBrickToMesh( List<Vertex> vertices, List<int> indices, float cx, float cy, float cz, float sx, float sy, float sz )
		{
			float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f;
			int baseIdx = vertices.Count;
			var p = new Vector3[8];
			p[0] = new Vector3( cx - hx, cy - hy, cz ); p[1] = new Vector3( cx + hx, cy - hy, cz );
			p[2] = new Vector3( cx + hx, cy + hy, cz ); p[3] = new Vector3( cx - hx, cy + hy, cz );
			p[4] = new Vector3( cx - hx, cy - hy, cz + sz ); p[5] = new Vector3( cx + hx, cy - hy, cz + sz );
			p[6] = new Vector3( cx + hx, cy + hy, cz + sz ); p[7] = new Vector3( cx - hx, cy + hy, cz + sz );
			var uv0 = new Vector4( 0, 0, 0, 0 );
			var uv1 = new Vector4( 1, 0, 0, 0 );
			var uv2 = new Vector4( 1, 1, 0, 0 );
			var uv3 = new Vector4( 0, 1, 0, 0 );
			vertices.Add( new Vertex( p[3], uv0, Color32.White ) ); vertices.Add( new Vertex( p[2], uv1, Color32.White ) ); vertices.Add( new Vertex( p[6], uv2, Color32.White ) ); vertices.Add( new Vertex( p[7], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[1], uv0, Color32.White ) ); vertices.Add( new Vertex( p[0], uv1, Color32.White ) ); vertices.Add( new Vertex( p[4], uv2, Color32.White ) ); vertices.Add( new Vertex( p[5], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[0], uv0, Color32.White ) ); vertices.Add( new Vertex( p[3], uv1, Color32.White ) ); vertices.Add( new Vertex( p[7], uv2, Color32.White ) ); vertices.Add( new Vertex( p[4], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[2], uv0, Color32.White ) ); vertices.Add( new Vertex( p[1], uv1, Color32.White ) ); vertices.Add( new Vertex( p[5], uv2, Color32.White ) ); vertices.Add( new Vertex( p[6], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[7], uv0, Color32.White ) ); vertices.Add( new Vertex( p[6], uv1, Color32.White ) ); vertices.Add( new Vertex( p[5], uv2, Color32.White ) ); vertices.Add( new Vertex( p[4], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[0], uv0, Color32.White ) ); vertices.Add( new Vertex( p[1], uv1, Color32.White ) ); vertices.Add( new Vertex( p[2], uv2, Color32.White ) ); vertices.Add( new Vertex( p[3], uv3, Color32.White ) );
			for ( int face = 0; face < 6; face++ ) { int i = baseIdx + face * 4; indices.Add( i ); indices.Add( i + 1 ); indices.Add( i + 2 ); indices.Add( i ); indices.Add( i + 2 ); indices.Add( i + 3 ); }
		}

		bool DeconstructWall( VillageBuildTask task )
		{
			float segLen = WallSegmentLength;
			float wallH = WallHeight;
			float wallDepth = WallThickness;
			float brickLen = BrickBodySize.x;
			float brickDepth = BrickBodySize.y;
			float brickH = BrickBodySize.z;
			int modulesX = (int)MathF.Round( segLen / BrickModuleX );
			int numRows = (int)MathF.Round( wallH / BrickModuleZ );

			if ( task.FinalizedMeshGo is not null ) { task.FinalizedMeshGo.Destroy(); task.FinalizedMeshGo = null; }

			int topRow = numRows - 1;
			bool isOdd = (topRow % 2 == 1);
			float halfLen = BrickModuleX * 0.5f;
			float yCenter = -wallDepth * 0.5f + BrickModuleY * 0.5f;
			float z = topRow * BrickModuleZ;

			if ( isOdd )
			{
				var pos = task.Position + new Vector3( -segLen * 0.5f + halfLen * 0.5f, yCenter, z );
				SpawnBrick( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ) );
				task.PlacedBricks.Remove( BrickSlot.HalfStretcher( 0, 0, topRow ) );
				for ( int col = 1; col < modulesX; col++ )
				{
					float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
					pos = task.Position + new Vector3( x, yCenter, z );
					SpawnBrick( pos, new Vector3( brickLen, brickDepth, brickH ) );
					task.PlacedBricks.Remove( BrickSlot.Stretcher( col, 0, topRow ) );
				}
				pos = task.Position + new Vector3( segLen * 0.5f - halfLen * 0.5f, yCenter, z );
				SpawnBrick( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ) );
				task.PlacedBricks.Remove( BrickSlot.HalfStretcher( modulesX, 0, topRow ) );
			}
			else
			{
				for ( int col = 0; col < modulesX; col++ )
				{
					float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
					var pos = task.Position + new Vector3( x, yCenter, z );
					SpawnBrick( pos, new Vector3( brickLen, brickDepth, brickH ) );
					task.PlacedBricks.Remove( BrickSlot.Stretcher( col, 0, topRow ) );
				}
			}
			task.WallState = WallSegmentState.Deconstructing;
			Log.Info( $"  Deconstructed: expanded top course (row {topRow})." );
			return true;
		}

		void Check( string name, bool passed, string detail )
		{
			if ( passed )
			{
				_passed++;
				Log.Info( $"  [PASS] {name}" + (string.IsNullOrEmpty( detail ) ? "" : $" ({detail})") );
			}
			else
			{
				_failed++;
				Log.Warning( $"  [FAIL] {name}" + (string.IsNullOrEmpty( detail ) ? "" : $" ({detail})") );
			}
		}
	}
}
