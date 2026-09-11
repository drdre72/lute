using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Art pass system for the medieval village. Runs after the village
	/// finishes building with primitives, and replaces each primitive piece
	/// with an appropriate Kenney Castle Kit model.
	///
	/// The village builder creates pieces as GameObjects with MeshComponent
	/// (PolygonMesh boxes) named "Village_{taskName}_{pieceIndex}".
	///
	/// This pass:
	///   1. Scans the MedievalVillage hierarchy for MeshComponent objects
	///   2. Parses the task type from the object name
	///   3. Selects an appropriate castle_kit model
	///   4. Records the primitive's transform (pos, rot, scale)
	///   5. Loads the target model and compares bounds
	///   6. Calculates position/scale adjustments for collision fit
	///   7. Spawns the new model with ModelRenderer + adjusted transform
	///   8. Removes the old primitive
	///   9. Logs adjustments for verification
	/// </summary>
	public static class VillageArtPass
	{
		static int _replaced;
		static int _adjusted;
		static int _failed;
		static int _props;
		static int _skipped;

		/// <summary>
		/// Maps task type keywords to castle_kit model paths.
		/// </summary>
		static string GetModelForTaskType( string taskType )
		{
			// Determine model based on task type keyword
			if ( taskType.Contains( "wall" ) )
				return "models/castle_kit/wall.vmdl";
			if ( taskType.Contains( "gate" ) )
				return "models/castle_kit/gate.vmdl";
			if ( taskType.Contains( "tower" ) )
				return "models/castle_kit/tower-square.vmdl";
			if ( taskType.Contains( "road" ) || taskType.Contains( "market" ) )
				return "models/castle_kit/ground.vmdl";
			if ( taskType.Contains( "well" ) )
				return "models/castle_kit/wall-corner.vmdl";
			if ( taskType.Contains( "bridge" ) )
				return "models/castle_kit/bridge-straight.vmdl";
			if ( taskType.Contains( "stairs" ) )
				return "models/castle_kit/stairs-stone.vmdl";
			if ( taskType.Contains( "chapel" ) || taskType.Contains( "church" ) )
				return "models/castle_kit/tower-square.vmdl";
			if ( taskType.Contains( "cottage" ) || taskType.Contains( "shop" ) ||
				 taskType.Contains( "smithy" ) || taskType.Contains( "tavern" ) ||
				 taskType.Contains( "storage" ) || taskType.Contains( "barn" ) )
				return "models/castle_kit/wall.vmdl";

			return null;
		}

		/// <summary>
		/// Prop models for set-dressing.
		/// </summary>
		static readonly string[] PropModels =
		{
			"models/castle_kit/flag.vmdl",
			"models/castle_kit/flag-banner-long.vmdl",
			"models/castle_kit/tree-small.vmdl",
			"models/castle_kit/tree-large.vmdl",
			"models/castle_kit/rocks-small.vmdl",
			"models/castle_kit/rocks-large.vmdl",
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

			// Collect all children with MeshComponent
			var allChildren = villageRoot.Children.ToList();
			var meshObjects = new List<GameObject>();

			foreach ( var child in allChildren )
			{
				CollectMeshObjects( child, meshObjects );
			}

			Log.Info( $"[VillageArtPass] Found {meshObjects.Count} mesh objects in village" );

			if ( meshObjects.Count == 0 )
			{
				Log.Warning( "[VillageArtPass] No mesh objects found! Checking direct children..." );
				foreach ( var child in allChildren.Take( 5 ) )
					Log.Info( $"  Child: '{child.Name}' has MeshComponent: {child.GetComponent<Sandbox.MeshComponent>() != null}" );
				return;
			}

			// Group by task type for organized replacement
			var byType = new Dictionary<string, List<GameObject>>();
			foreach ( var obj in meshObjects )
			{
				var taskType = ParseTaskType( obj.Name );
				if ( !byType.ContainsKey( taskType ) )
					byType[taskType] = new List<GameObject>();
				byType[taskType].Add( obj );
			}

			Log.Info( "[VillageArtPass] Objects by type:" );
			foreach ( var kvp in byType.OrderBy( k => k.Key ) )
				Log.Info( $"  {kvp.Key}: {kvp.Value.Count} pieces" );

			// Replace each primitive with a castle_kit model
			foreach ( var obj in meshObjects.ToList() )
			{
				ReplacePrimitive( obj );
			}

			// Scatter props
			ScatterProps( villageRoot, meshObjects );

			Log.Info( "[VillageArtPass] Art pass complete:" );
			Log.Info( $"  Replaced: {_replaced} primitives with castle_kit models" );
			Log.Info( $"  Adjusted: {_adjusted} transforms for collision fit" );
			Log.Info( $"  Skipped:  {_skipped} (no matching model)" );
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

			// Recurse into children
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
		/// Parse the task type from a Village piece name.
		/// Names are like "Village_cottage_7_3436" or "Village_Wall_N_10_5".
		/// Returns the task type keyword (lowercase).
		/// </summary>
		static string ParseTaskType( string name )
		{
			if ( string.IsNullOrEmpty( name ) )
				return "unknown";

			// Remove "Village_" prefix if present
			var cleaned = name.StartsWith( "Village_" ) ? name.Substring( 8 ) : name;

			// Take the first segment before the first digit
			var parts = cleaned.Split( '_' );
			var typePart = "";
			foreach ( var p in parts )
			{
				if ( p.Length > 0 && char.IsDigit( p[0] ) )
					break;
				typePart += (typePart.Length > 0 ? "_" : "") + p;
			}

			return typePart.ToLowerInvariant();
		}

		static void ReplacePrimitive( GameObject obj )
		{
			if ( obj == null || !obj.IsValid )
				return;

			var taskType = ParseTaskType( obj.Name );
			var modelPath = GetModelForTaskType( taskType );
			if ( modelPath == null )
			{
				_skipped++;
				return;
			}

			// Record original transform
			var origPos = obj.WorldPosition;
			var origRot = obj.WorldRotation;
			var origScale = obj.WorldScale;
			var parent = obj.Parent;

			// Load the target model
			var model = Sandbox.Model.Load( modelPath );
			if ( model == null )
			{
				Log.Warning( $"[VillageArtPass] Failed to load '{modelPath}' for '{obj.Name}'" );
				_failed++;
				return;
			}

			// Get model bounds for adjustment calculation
			var modelBounds = model.Bounds;
			var modelSize = modelBounds.Size;
			var modelCenter = modelBounds.Center;

			// Get the original mesh size from the object's scale
			// (PolygonMesh doesn't expose Bounds directly, but the
			//  SpawnBox function creates boxes with known sizes and
			//  the scale reflects the piece size)
			var origMeshSize = origScale * 100f; // approximate

			// Calculate uniform scale to fit model into primitive's footprint
			var scaleAdj = 1f;
			if ( modelSize.x > 0 && modelSize.y > 0 && modelSize.z > 0 )
			{
				// Scale based on the largest axis to maintain proportions
				var targetSize = MathF.Max( origMeshSize.x, MathF.Max( origMeshSize.y, origMeshSize.z ) );
				var currentSize = MathF.Max( modelSize.x, MathF.Max( modelSize.y, modelSize.z ) );
				if ( currentSize > 0 )
					scaleAdj = targetSize / currentSize;
			}

			// Calculate position offset to center the model
			var posOffset = modelCenter * scaleAdj;
			var newPos = origPos - posOffset;

			// Check if adjustment was needed
			var needsAdjust = MathF.Abs( scaleAdj - 1f ) > 0.01f || posOffset.Length > 1f;

			// Spawn the new model
			var newGo = Game.ActiveScene.CreateObject( true );
			newGo.Name = $"ArtPass_{obj.Name}";
			if ( parent != null )
				newGo.SetParent( parent );
			newGo.WorldPosition = newPos;
			newGo.WorldRotation = origRot;
			newGo.WorldScale = scaleAdj;

			var newMr = newGo.AddComponent<Sandbox.ModelRenderer>();
			newMr.Model = model;

			// Remove the old primitive
			obj.Destroy();

			_replaced++;
			if ( needsAdjust )
			{
				_adjusted++;
				if ( _adjusted <= 20 ) // log first 20 adjustments
				{
					Log.Info( $"[VillageArtPass] {obj.Name} -> {modelPath}" );
					Log.Info( $"  pos: {origPos} -> {newPos} (offset={posOffset})" );
					Log.Info( $"  scale: {origScale} -> {scaleAdj}" );
				}
			}
		}

		static void ScatterProps( GameObject villageRoot, List<GameObject> buildings )
		{
			if ( buildings.Count == 0 )
				return;

			var rng = new Random( 42 ); // deterministic seed
			var propCount = Math.Min( buildings.Count / 5, 30 );

			for ( int i = 0; i < propCount; i++ )
			{
				var targetBuilding = buildings[rng.Next( buildings.Count )];
				if ( targetBuilding == null || !targetBuilding.IsValid ) continue;

				var propName = PropModels[rng.Next( PropModels.Length )];
				var model = Sandbox.Model.Load( propName );
				if ( model == null ) continue;

				var go = Game.ActiveScene.CreateObject( true );
				go.Name = $"Prop_{propName.Split( '/' ).Last().Replace( ".vmdl", "" )}_{i}";
				go.SetParent( villageRoot );

				// Position near the target building with a small offset
				var offset = new Vector3(
					(float)(rng.NextDouble() * 300 - 150),
					(float)(rng.NextDouble() * 300 - 150),
					0 );
				go.WorldPosition = targetBuilding.WorldPosition + offset;
				go.WorldRotation = Rotation.FromYaw( (float)(rng.NextDouble() * 360 ) );
				go.WorldScale = 1f;

				var mr = go.AddComponent<Sandbox.ModelRenderer>();
				mr.Model = model;

				_props++;
			}
		}
	}
}
