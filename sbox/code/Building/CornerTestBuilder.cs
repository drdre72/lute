using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.Building
{
	public static class CornerTestBuilder
	{
		const float M = 39.37f;
		const float BrickModuleX = 0.25f * M;
		const float BrickModuleY = 0.125f * M;
		const float BrickModuleZ = 0.0625f * M;
		const float WallSegmentLength = 2f * M;
		const float WallThickness = 0.5f * M;
		static readonly Vector3 BrickBodySize = new( 0.24f * M, 0.115f * M, 0.055f * M );

		// Test corner origin (world position of the junction).
		static Vector3 _junction;
		static GameObject _root;

		/// <summary>
		/// Build a standalone SW corner test: two perpendicular walls + the
		/// shared corner assembly, instantly (no NPC, no delays). Used for
		/// fast geometry iteration.
		///
		/// Usage: corner_test_build [x y z] — defaults to (-16417,-16417,0)
		/// </summary>
		[ConCmd( "corner_test_build" )]
		public static void Build( float x = -16417f, float y = -16417f, float z = 0f )
		{
			_junction = new Vector3( x, y, z );
			var scene = Game.ActiveScene;

			// Clear any previous test
			if ( _root != null && _root.IsValid )
			{
				_root.Destroy();
				_root = null;
			}
			_root = scene.CreateObject( true );
			_root.Name = "CornerTest";
			_root.WorldPosition = _junction;

			int modulesX = 8;
			int numWythes = 4;
			int numRows = 32;
			float segLen = WallSegmentLength;
			float wallDepth = WallThickness;
			float groundZ = z;
			string mat = "materials/medieval/castle_wall.vmat";

			// Wall A: runs east from junction (rotation 0).
			// Wall center is segLen/2 east of the junction, so the wall's
			// left edge is at the junction (matching the real village).
			var wallAPos = _junction + new Vector3( segLen * 0.5f, 0, 0 );
			var wallARot = 0f; // facing east
			// Wall B: runs north from junction (rotation 90).
			var wallBPos = _junction + new Vector3( 0, segLen * 0.5f, 0 );
			var wallBRot = 90f; // facing north

			Log.Info( $"Lute: [corner_test] junction={_junction} wallA_pos={wallAPos} wallB_pos={wallBPos}" );

			// Build Wall A (east-running)
			BuildWall( wallAPos, wallARot, modulesX, numWythes, numRows, segLen, wallDepth, groundZ, mat, "Wall_A", isLeftCorner: true );
			// Build Wall B (north-running)
			BuildWall( wallBPos, wallBRot, modulesX, numWythes, numRows, segLen, wallDepth, groundZ, mat, "Wall_B", isLeftCorner: true );

			// Build the corner assembly
			BuildCornerAssembly( _junction, new Vector3( -1, 0, 0 ), new Vector3( 0, -1, 0 ), numRows, groundZ, mat );

			Log.Info( $"Lute: [corner_test] done — 2 walls + corner assembly at {_junction}" );
		}

		/// <summary>
		/// Clear the test corner.
		/// </summary>
		[ConCmd( "corner_test_clear" )]
		public static void Clear()
		{
			if ( _root != null && _root.IsValid )
			{
				_root.Destroy();
				_root = null;
				Log.Info( "Lute: [corner_test] cleared." );
			}
		}

		static void BuildWall(
			Vector3 pos, float yawDeg, int modulesX, int numWythes, int numRows,
			float segLen, float wallDepth, float groundZ, string mat,
			string name, bool isLeftCorner )
		{
			float rad = yawDeg * MathF.PI / 180f;
			float cos = MathF.Cos( rad );
			float sin = MathF.Sin( rad );
			Vector3 RotateLocal( Vector3 local )
				=> pos + new Vector3( local.x * cos - local.y * sin, local.x * sin + local.y * cos, local.z );

			int brickIdx = 0;
			for ( int wythe = 0; wythe < numWythes; wythe++ )
			{
				float yCenter = -wallDepth * 0.5f + BrickModuleY * 0.5f + wythe * BrickModuleY;
				for ( int row = 0; row < numRows; row++ )
				{
					float z = groundZ + row * BrickModuleZ;
					bool isOdd = (row % 2 == 1);
					if ( isOdd )
					{
						float halfLen = BrickModuleX * 0.5f;
						// Left half — skip if corner
						float lx = -segLen * 0.5f + halfLen * 0.5f;
						if ( isLeftCorner )
						{
							brickIdx++;
							continue;
						}
						SpawnBrick( RotateLocal( new Vector3( lx, yCenter, z ) ),
							new Vector3( BrickModuleX * 0.5f, BrickModuleY, BrickModuleZ ),
							mat, yawDeg, _root, $"{name}_half_{brickIdx}", BrickForm.Half );
						brickIdx++;

						// Full bricks
						for ( int col = 1; col < modulesX; col++ )
						{
							float x = -segLen * 0.5f + col * BrickModuleX;
							SpawnBrick( RotateLocal( new Vector3( x, yCenter, z ) ),
								new Vector3( BrickModuleX, BrickModuleY, BrickModuleZ ),
								mat, yawDeg, _root, $"{name}_full_{brickIdx}" );
							brickIdx++;
						}

						// Right half
						float rx = segLen * 0.5f - halfLen * 0.5f;
						SpawnBrick( RotateLocal( new Vector3( rx, yCenter, z ) ),
							new Vector3( BrickModuleX * 0.5f, BrickModuleY, BrickModuleZ ),
							mat, yawDeg, _root, $"{name}_rhalf_{brickIdx}", BrickForm.Half );
						brickIdx++;
					}
					else
					{
						for ( int col = 0; col < modulesX; col++ )
						{
							if ( isLeftCorner && col == 0 )
							{
								brickIdx++;
								continue; // skip corner column
							}
							float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
							SpawnBrick( RotateLocal( new Vector3( x, yCenter, z ) ),
								new Vector3( BrickModuleX, BrickModuleY, BrickModuleZ ),
								mat, yawDeg, _root, $"{name}_even_{brickIdx}" );
							brickIdx++;
						}
					}
				}
			}
			Log.Info( $"Lute: [corner_test] {name} built {brickIdx} bricks at pos={pos} yaw={yawDeg}" );
		}

		static void BuildCornerAssembly(
			Vector3 junction, Vector3 inwardA, Vector3 inwardB,
			int numRows, float groundZ, string mat )
		{
			int count = 0;
			for ( int course = 0; course < numRows; course++ )
			{
				float z = groundZ + course * BrickModuleZ;
				var placements = CornerBondResolver.PlacementsFor( junction, inwardA, inwardB, course );
				foreach ( var p in placements )
				{
					var center = p.WorldCenter;
					center.z = z + BrickModuleZ * 0.5f; // base anchor
					SpawnBrick( center, p.WorldSize, mat,
						p.WorldYaw * 180f / MathF.PI, _root,
						$"corner_c{course}_{count}", BrickForm.Full,
						BrickModuleX * 2f ); // 2 modules long
					count++;
				}
			}
			Log.Info( $"Lute: [corner_test] corner assembly built {count} bricks ({numRows} courses)" );
		}

		static void SpawnBrick( Vector3 worldPos, Vector3 size, string mat,
			float yawDeg, GameObject parent, string name,
			BrickForm form = BrickForm.Full, float? overrideLength = null )
		{
			var go = parent.Scene.CreateObject( false );
			go.Name = $"CT_{name}";
			go.SetParent( parent );
			go.WorldPosition = worldPos;
			if ( yawDeg != 0f ) go.WorldRotation = Rotation.FromYaw( yawDeg );

			var renderer = go.AddComponent<ModelRenderer>();
			renderer.Model = Cloud.Model( "facepunch.brick_single_04" );

			if ( renderer.Model is not null )
			{
				var modelSize = renderer.Model.Bounds.Size;
				if ( modelSize.x > 0 && modelSize.y > 0 && modelSize.z > 0 )
				{
					float length = overrideLength ?? form switch
					{
						BrickForm.Half    => BrickModuleX * 0.5f,
						BrickForm.Quarter => BrickModuleX * 0.25f,
						_                 => BrickModuleX
					};
					var renderSize = new Vector3( length, BrickModuleY, BrickModuleZ );
					go.WorldScale = renderSize / modelSize;
				}
				else
					go.WorldScale = size / 50f;
			}
			else
				go.WorldScale = size / 50f;

			go.Enabled = true;
		}
	}
}
