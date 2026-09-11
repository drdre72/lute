using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Art pass system for the medieval village. Runs after the village
	/// finishes building with primitives, and replaces each building task
	/// (not each piece) with an appropriate castle_kit model.
	///
	/// The village builder creates pieces as GameObjects with MeshComponent
	/// (PolygonMesh boxes) named "Village_{taskName}_{pieceIndex}".
	/// A single cottage task may produce 7+ pieces (4 walls, floor, roof, door).
	///
	/// This pass:
	///   1. Scans the MedievalVillage hierarchy for MeshComponent objects
	///   2. Groups pieces by task name (e.g. all Village_cottage_7_* -> cottage_7)
	///   3. Calculates the bounding box of each task group
	///   4. Selects an appropriate castle_kit model per task type
	///   5. Spawns ONE model per task at the group's center, scaled to fit
	///   6. Removes all primitive pieces for that task
	///   7. Scatters scaled props as set-dressing
	/// </summary>
	public static class VillageArtPass
	{
		static int _replaced;
		static int _adjusted;
		static int _failed;
		static int _props;
		static int _skipped;

		/// <summary>
		/// Maps task type keyword to a castle_kit model path.
		/// Returns null for building types — those stay as primitives.
		/// </summary>
		static string GetModelForTaskType( string taskType )
		{
			// Outer perimeter walls — use castle wall model
			if ( taskType.StartsWith( "wall_" ) )
				return "models/castle_kit/wall.vmdl";

			// Gates
			if ( taskType.Contains( "gate" ) )
				return "models/castle_kit/gate.vmdl";

			// Towers / guardhouses
			if ( taskType.Contains( "tower" ) || taskType.Contains( "guardhouse" ) )
				return "models/castle_kit/tower-square.vmdl";

			// Roads and market square — flat ground
			if ( taskType.Contains( "road" ) || taskType.Contains( "market" ) )
				return "models/castle_kit/ground.vmdl";

			// Well — small structure
			if ( taskType.Contains( "well" ) )
				return "models/castle_kit/wall-corner.vmdl";

			// Bridges
			if ( taskType.Contains( "bridge" ) )
				return "models/castle_kit/bridge-straight.vmdl";

			// Buildings (cottages, shops, smithy, tavern, storage, chapel)
			// Keep as primitives — castle_kit has no house models.
			// A future asset pack can replace these.
			return null;
		}

		/// <summary>
		/// Prop models and their scale multipliers.
		/// Castle kit models are authored at ~100m, so we scale them way down.
		/// A 5m tree = 5/100 = 0.05 scale. A 2m rock = 2/100 = 0.02 scale.
		/// </summary>
		static readonly (string path, float scale)[] PropModels =
		{
			("models/castle_kit/flag.vmdl",             0.03f),  // ~3m flag
			("models/castle_kit/flag-banner-short.vmdl", 0.03f),  // ~3m banner
			("models/castle_kit/tree-small.vmdl",        0.05f),  // ~5m tree
			("models/castle_kit/tree-large.vmdl",        0.04f),  // ~4m tree (large model scaled smaller)
			("models/castle_kit/rocks-small.vmdl",       0.02f),  // ~2m rocks
			("models/castle_kit/rocks-large.vmdl",       0.015f), // ~1.5m rocks
		};

		/// <summary>
		/// Run the art pass on the village.
		/// </summary>
		public static void RunArtPass()
		{
			_replaced = 0;
			_adjusted = 0;
			_failed = 0;
			_props = 0;
			_skipped = 0;

			Log.Info( "[VillageArtPass] Starting art pass..." );

			var villageRoot = FindVillageRoot();
			if ( villageRoot == null )
			{
				Log.Error( "[VillageArtPass] No MedievalVillage root found!" );
				return;
			}

			// Collect all MeshComponent objects under the village root
			var meshObjects = new List<GameObject>();
			CollectMeshObjects( villageRoot, meshObjects );

			Log.Info( $"[VillageArtPass] Found {meshObjects.Count} mesh objects in village" );

			if ( meshObjects.Count == 0 )
			{
				Log.Warning( "[VillageArtPass] No mesh objects found!" );
				return;
			}

			// Group pieces by task name
			// e.g. "Village_cottage_7_3436" -> task "cottage_7"
			var taskGroups = new Dictionary<string, List<GameObject>>();
			foreach ( var obj in meshObjects )
			{
				var taskName = ParseTaskName( obj.Name );
				if ( !taskGroups.ContainsKey( taskName ) )
					taskGroups[taskName] = new List<GameObject>();
				taskGroups[taskName].Add( obj );
			}

			Log.Info( $"[VillageArtPass] Grouped into {taskGroups.Count} task groups" );

			// Log type breakdown
			var byType = new Dictionary<string, int>();
			foreach ( var kvp in taskGroups )
			{
				var type = ParseTaskType( kvp.Key );
				if ( !byType.ContainsKey( type ) ) byType[type] = 0;
				byType[type] += kvp.Value.Count;
			}
			Log.Info( "[VillageArtPass] Pieces by type:" );
			foreach ( var kvp in byType.OrderBy( k => k.Key ) )
				Log.Info( $"  {kvp.Key}: {kvp.Value} pieces in {taskGroups.Count(g => ParseTaskType(g.Key) == kvp.Key)} tasks" );

			// Replace each task group with a single model
			foreach ( var kvp in taskGroups )
			{
				ReplaceTaskGroup( kvp.Key, kvp.Value );
			}

			// Scatter props
			ScatterProps( villageRoot, taskGroups );

			Log.Info( "[VillageArtPass] Art pass complete:" );
			Log.Info( $"  Replaced: {_replaced} task groups with castle_kit models" );
			Log.Info( $"  Adjusted: {_adjusted} transforms for collision fit" );
			Log.Info( $"  Skipped:  {_skipped} groups (no matching model)" );
			Log.Info( $"  Failed:   {_failed} replacements" );
			Log.Info( $"  Props:    {_props} set-dressing props scattered" );
		}

		static void CollectMeshObjects( GameObject obj, List<GameObject> list )
		{
			if ( obj == null || !obj.Enabled )
				return;

			var mesh = obj.GetComponent<Sandbox.MeshComponent>();
			if ( mesh != null )
			{
				list.Add( obj );
				return;
			}

			foreach ( var child in obj.Children.ToList() )
				CollectMeshObjects( child, list );
		}

		static GameObject FindVillageRoot()
		{
			return Game.ActiveScene
				?.GetAllObjects( true )
				?.FirstOrDefault( o => o.Name == "MedievalVillage" );
		}

		/// <summary>
		/// Parse the task name from a Village piece name.
		/// "Village_cottage_7_3436" -> "cottage_7"
		/// "Village_Wall_N_10_5" -> "Wall_N_10"
		/// </summary>
		static string ParseTaskName( string name )
		{
			if ( string.IsNullOrEmpty( name ) )
				return "unknown";

			var cleaned = name.StartsWith( "Village_" ) ? name.Substring( 8 ) : name;
			var parts = cleaned.Split( '_' );

			// Rebuild everything except the last segment (which is the piece index)
			var result = new List<string>();
			for ( int i = 0; i < parts.Length; i++ )
			{
				if ( i == parts.Length - 1 && parts[i].Length > 0 && char.IsDigit( parts[i][0] ) )
					break;
				result.Add( parts[i] );
			}

			return string.Join( "_", result );
		}

		/// <summary>
		/// Parse the task type keyword from a task name.
		/// "cottage_7" -> "cottage"
		/// "Wall_N_10" -> "wall_n"
		/// </summary>
		static string ParseTaskType( string taskName )
		{
			var parts = taskName.Split( '_' );
			var typePart = "";
			foreach ( var p in parts )
			{
				if ( p.Length > 0 && char.IsDigit( p[0] ) )
					break;
				typePart += (typePart.Length > 0 ? "_" : "") + p;
			}
			return typePart.ToLowerInvariant();
		}

		/// <summary>
		/// Replace an entire task group (all pieces of one building/structure)
		/// with a single castle_kit model positioned at the group's center.
		/// </summary>
		static void ReplaceTaskGroup( string taskName, List<GameObject> pieces )
		{
			if ( pieces.Count == 0 )
				return;

			var taskType = ParseTaskType( taskName );
			var modelPath = GetModelForTaskType( taskType );
			if ( modelPath == null )
			{
				_skipped += pieces.Count;
				return;
			}

			// Calculate the center position of all pieces
			var center = Vector3.Zero;
			var validCount = 0;
			foreach ( var p in pieces )
			{
				if ( p != null && p.IsValid )
				{
					center += p.WorldPosition;
					validCount++;
				}
			}
			if ( validCount == 0 ) return;
			center /= validCount;

			// Load the target model
			var model = Sandbox.Model.Load( modelPath );
			if ( model == null )
			{
				Log.Warning( $"[VillageArtPass] Failed to load '{modelPath}' for task '{taskName}'" );
				_failed += pieces.Count;
				return;
			}

			// Get model bounds
			var modelBounds = model.Bounds;
			var modelSize = modelBounds.Size;
			var modelCenter = modelBounds.Center;

			// Calculate the spread of pieces to determine target size
			var minPos = new Vector3( float.MaxValue, float.MaxValue, float.MaxValue );
			var maxPos = new Vector3( float.MinValue, float.MinValue, float.MinValue );
			foreach ( var p in pieces )
			{
				if ( p == null || !p.IsValid ) continue;
				minPos = Vector3.Min( minPos, p.WorldPosition );
				maxPos = Vector3.Max( maxPos, p.WorldPosition );
			}
			var targetSize = maxPos - minPos;

			// Calculate scale to fit the model to the building footprint
			var scaleAdj = 1f;
			if ( modelSize.x > 0 && modelSize.y > 0 )
			{
				// Use the larger of X/Y to determine scale
				var targetFootprint = MathF.Max( targetSize.x, targetSize.y );
				var modelFootprint = MathF.Max( modelSize.x, modelSize.y );
				if ( modelFootprint > 0 )
				{
					scaleAdj = targetFootprint / modelFootprint;
					// For walls, add 10% to close gaps between segments
					if ( taskType.StartsWith( "wall_" ) )
						scaleAdj *= 1.1f;
					// Clamp to reasonable range
					scaleAdj = Math.Clamp( scaleAdj, 0.01f, 10f );
				}
			}

			// Calculate position: place model so its base (mins.z) sits at ground level
			// The model bounds mins.z is typically 0, so we just use minPos.z
			var newPos = center;
			newPos.z = minPos.z - (modelBounds.Mins.z * scaleAdj);

			var needsAdjust = MathF.Abs( scaleAdj - 1f ) > 0.01f;

			// Spawn the new model
			var newGo = Game.ActiveScene.CreateObject( true );
			newGo.Name = $"ArtPass_{taskName}";
			var parent = pieces[0]?.Parent;
			if ( parent != null && parent.IsValid )
				newGo.SetParent( parent );
			newGo.WorldPosition = newPos;
			newGo.WorldRotation = Rotation.Identity;
			newGo.WorldScale = scaleAdj;

			var newMr = newGo.AddComponent<Sandbox.ModelRenderer>();
			newMr.Model = model;

			// Add collision from the model
			var newCollider = newGo.AddComponent<Sandbox.ModelCollider>();
			newCollider.Model = model;

			// Remove all primitive pieces for this task
			foreach ( var p in pieces )
			{
				if ( p != null && p.IsValid )
					p.Destroy();
			}

			_replaced++;
			if ( _replaced <= 30 || needsAdjust )
			{
				_adjusted++;
				if ( _adjusted <= 40 )
				{
					Log.Info( $"[VillageArtPass] {taskName} ({pieces.Count} pieces) -> {modelPath}" );
					Log.Info( $"  center: {center}  targetSize: {targetSize}  modelSize: {modelSize}" );
					Log.Info( $"  scale: {scaleAdj:F4}  pos: {newPos}" );
				}
			}
		}

		static void ScatterProps( GameObject villageRoot, Dictionary<string, List<GameObject>> taskGroups )
		{
			if ( taskGroups.Count == 0 )
				return;

			var rng = new Random( 42 ); // deterministic seed
			var buildingTasks = taskGroups.Keys
				.Where( k => {
					var t = ParseTaskType( k );
					return t.Contains( "cottage" ) || t.Contains( "shop" ) ||
						   t.Contains( "smithy" ) || t.Contains( "tavern" );
				} )
				.ToList();

			if ( buildingTasks.Count == 0 )
				return;

			var propCount = Math.Min( buildingTasks.Count, 20 );

			// Calculate building bounding boxes for collision avoidance
			var buildingBounds = new List<(Vector3 min, Vector3 max)>();
			foreach ( var taskName in buildingTasks )
			{
				var pieces = taskGroups[taskName];
				var min = new Vector3( float.MaxValue, float.MaxValue, float.MaxValue );
				var max = new Vector3( float.MinValue, float.MinValue, float.MinValue );
				foreach ( var p in pieces )
				{
					if ( p == null || !p.IsValid ) continue;
					min = Vector3.Min( min, p.WorldPosition );
					max = Vector3.Max( max, p.WorldPosition );
				}
				if ( min.x < float.MaxValue )
				{
					// Add margin around building
					var margin = 200f; // 5m margin
					buildingBounds.Add( (min - new Vector3(margin, margin, 0),
										max + new Vector3(margin, margin, 0)));
				}
			}

			for ( int i = 0; i < propCount; i++ )
			{
				var taskName = buildingTasks[rng.Next( buildingTasks.Count )];
				var pieces = taskGroups[taskName];
				if ( pieces.Count == 0 ) continue;

				// Find a valid piece to position near
				var targetPiece = pieces.FirstOrDefault( p => p != null && p.IsValid );
				if ( targetPiece == null ) continue;

				var (propName, propScale) = PropModels[rng.Next( PropModels.Length )];
				var model = Sandbox.Model.Load( propName );
				if ( model == null ) continue;

				// Find a position OUTSIDE all building bounds
				var propPos = targetPiece.WorldPosition;
				for ( int attempt = 0; attempt < 10; attempt++ )
				{
					// Place further away from the building center
					var angle = (float)(rng.NextDouble() * Math.PI * 2);
					var dist = 600f + (float)rng.NextDouble() * 400f; // 15-25m away
					var offset = new Vector3(
						(float)Math.Cos( angle ) * dist,
						(float)Math.Sin( angle ) * dist,
						0 );
					var candidate = targetPiece.WorldPosition + offset;

					// Check if this position is outside all building bounds
					var inside = false;
					foreach ( var (bmin, bmax) in buildingBounds )
					{
						if ( candidate.x >= bmin.x && candidate.x <= bmax.x &&
							 candidate.y >= bmin.y && candidate.y <= bmax.y )
						{
							inside = true;
							break;
						}
					}

					if ( !inside )
					{
						propPos = candidate;
						break;
					}
				}

				var go = Game.ActiveScene.CreateObject( true );
				go.Name = $"Prop_{propName.Split( '/' ).Last().Replace( ".vmdl", "" )}_{i}";
				go.SetParent( villageRoot );
				go.WorldPosition = propPos;
				go.WorldRotation = Rotation.FromYaw( (float)(rng.NextDouble() * 360 ) );
				go.WorldScale = propScale;

				var mr = go.AddComponent<Sandbox.ModelRenderer>();
				mr.Model = model;

				_props++;
			}
		}
	}
}
