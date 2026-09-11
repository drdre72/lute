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
	/// The swap accounts for collision/bounds differences between the
	/// primitive (simple box/cylinder) and the actual model (detailed mesh
	/// with PhysicsMeshFromRender collision). Each replacement:
	///   1. Records the primitive's transform (pos, rot, scale)
	///   2. Loads the target castle_kit model
	///   3. Compares model bounds to primitive bounds
	///   4. Calculates position/scale adjustments
	///   5. Spawns the new model with adjusted transform
	///   6. Removes the old primitive
	///   7. Logs the adjustment for verification
	///
	/// This follows the original asset plan (Monument_Spec_Neutral_Market.md
	/// section 4): "Pass 2 — art/detail: once the blockout plays correctly,
	/// swap primitives for higher-fidelity or custom assets."
	/// </summary>
	public static class VillageArtPass
	{
		/// <summary>
		/// Maps village task types to castle_kit model sets.
		/// Each task type gets a primary model and optional variants.
		/// </summary>
		static readonly Dictionary<string, ArtModelMapping> TaskTypeMap = new()
		{
			["wall"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/wall.vmdl",
				Variants = new[]
				{
					"models/castle_kit/wall.vmdl",
					"models/castle_kit/wall-half.vmdl",
					"models/castle_kit/wall-narrow.vmdl",
					"models/castle_kit/wall-pillar.vmdl",
				},
				CornerModel = "models/castle_kit/wall-corner.vmdl",
			},
			["gate"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/gate.vmdl",
				Variants = new[]
				{
					"models/castle_kit/gate.vmdl",
					"models/castle_kit/metal-gate.vmdl",
				},
				TowerModel = "models/castle_kit/tower-square.vmdl",
			},
			["road"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/ground.vmdl",
				Variants = new[]
				{
					"models/castle_kit/ground.vmdl",
					"models/castle_kit/ground-hills.vmdl",
				},
			},
			["well"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/wall-corner.vmdl",
				Variants = new[]
				{
					"models/castle_kit/wall-corner.vmdl",
					"models/castle_kit/wall-narrow-corner.vmdl",
				},
			},
			["market_square"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/ground.vmdl",
				Variants = new[] { "models/castle_kit/ground.vmdl" },
			},
			["cottage"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/wall.vmdl",
				Variants = new[]
				{
					"models/castle_kit/wall.vmdl",
					"models/castle_kit/wall-half.vmdl",
					"models/castle_kit/wall-narrow.vmdl",
				},
				RoofModel = "models/castle_kit/tower-square-roof.vmdl",
				DoorModel = "models/castle_kit/door.vmdl",
			},
			["shop"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/wall.vmdl",
				Variants = new[]
				{
					"models/castle_kit/wall.vmdl",
					"models/castle_kit/wall-half.vmdl",
				},
				RoofModel = "models/castle_kit/tower-square-roof.vmdl",
				DoorModel = "models/castle_kit/door.vmdl",
			},
			["smithy"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/wall.vmdl",
				Variants = new[] { "models/castle_kit/wall.vmdl" },
				RoofModel = "models/castle_kit/tower-square-roof.vmdl",
				DoorModel = "models/castle_kit/door.vmdl",
			},
			["tavern"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/wall.vmdl",
				Variants = new[]
				{
					"models/castle_kit/wall.vmdl",
					"models/castle_kit/wall-half.vmdl",
				},
				RoofModel = "models/castle_kit/tower-square-roof.vmdl",
				DoorModel = "models/castle_kit/door.vmdl",
			},
			["chapel"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/tower-square.vmdl",
				Variants = new[]
				{
					"models/castle_kit/tower-square.vmdl",
					"models/castle_kit/tower-square-arch.vmdl",
				},
				RoofModel = "models/castle_kit/tower-square-roof.vmdl",
				DoorModel = "models/castle_kit/door.vmdl",
			},
			["storage"] = new ArtModelMapping
			{
				Primary = "models/castle_kit/wall-narrow.vmdl",
				Variants = new[]
				{
					"models/castle_kit/wall-narrow.vmdl",
					"models/castle_kit/wall-half.vmdl",
				},
				RoofModel = "models/castle_kit/tower-square-roof.vmdl",
			},
		};

		/// <summary>
		/// Maps piece types (from blueprints) to model selectors.
		/// </summary>
		static readonly Dictionary<string, string> PieceTypeMap = new()
		{
			["WALL"] = "wall",
			["DOOR"] = "door",
			["ROOF"] = "roof",
			["COLUMN"] = "column",
			["FLOOR"] = "floor",
		};

		/// <summary>
		/// Prop models for set-dressing (scattered after main swap).
		/// </summary>
		static readonly string[] PropModels =
		{
			"models/castle_kit/flag.vmdl",
			"models/castle_kit/flag-banner-long.vmdl",
			"models/castle_kit/flag-banner-short.vmdl",
			"models/castle_kit/tree-small.vmdl",
			"models/castle_kit/tree-large.vmdl",
			"models/castle_kit/rocks-small.vmdl",
			"models/castle_kit/rocks-large.vmdl",
		};

		static int _replaced;
		static int _adjusted;
		static int _failed;
		static int _props;

		/// <summary>
		/// Run the art pass on the village. Call after village is complete.
		/// Usage: village_artpass
		/// </summary>
		public static void RunArtPass()
		{
			_replaced = 0;
			_adjusted = 0;
			_failed = 0;
			_props = 0;

			Log.Info( "[VillageArtPass] Starting art pass..." );

			// Find the village root
			var villageRoot = FindVillageRoot();
			if ( villageRoot == null )
			{
				Log.Error( "[VillageArtPass] No MedievalVillage root found!" );
				return;
			}

			var children = villageRoot.Children.ToList();
			Log.Info( $"[VillageArtPass] Village root has {children.Count} child objects" );

			// Phase 1: Replace primitives with castle_kit models
			foreach ( var child in children )
			{
				ReplacePrimitive( child );
			}

			// Phase 2: Scatter props (flags, trees, rocks)
			ScatterProps( villageRoot, children );

			Log.Info( $"[VillageArtPass] Art pass complete:" );
			Log.Info( $"  Replaced: {_replaced} primitives with castle_kit models" );
			Log.Info( $"  Adjusted: {_adjusted} transforms for collision fit" );
			Log.Info( $"  Failed:   {_failed} replacements" );
			Log.Info( $"  Props:    {_props} set-dressing props scattered" );
		}

		static GameObject FindVillageRoot()
		{
			return Game.ActiveScene
				?.GetAllObjects( true )
				?.FirstOrDefault( o => o.Name == "MedievalVillage" );
		}

		static void ReplacePrimitive( GameObject obj )
		{
			if ( obj == null || !obj.Enabled )
				return;

			var mr = obj.GetComponent<Sandbox.ModelRenderer>();
			if ( mr == null )
			{
				// Check children recursively
				foreach ( var child in obj.Children.ToList() )
					ReplacePrimitive( child );
				return;
			}

			// Determine what kind of primitive this is based on name/parent
			var modelName = DetermineReplacementModel( obj );
			if ( modelName == null )
				return;

			// Record original transform
			var origPos = obj.WorldPosition;
			var origRot = obj.WorldRotation;
			var origScale = obj.WorldScale;

			// Load the target model
			var model = Sandbox.Model.Load( modelName );
			if ( model == null )
			{
				Log.Warning( $"[VillageArtPass] Failed to load '{modelName}' for '{obj.Name}'" );
				_failed++;
				return;
			}

			// Calculate adjustment based on bounds difference
			var modelBounds = model.Bounds;
			var modelSize = modelBounds.Size;
			var origSize = origScale * 100f; // approximate primitive size

			// Calculate scale adjustment to fit the model into the primitive's footprint
			var scaleAdj = 1f;
			if ( modelSize.x > 0 && modelSize.y > 0 && modelSize.z > 0 )
			{
				// Don't stretch — use uniform scale based on the largest axis
				var targetSize = MathF.Max( origSize.x, MathF.Max( origSize.y, origSize.z ) );
				var currentSize = MathF.Max( modelSize.x, MathF.Max( modelSize.y, modelSize.z ) );
				if ( currentSize > 0 )
					scaleAdj = targetSize / currentSize;
			}

			// Calculate position offset (center the model on the primitive's center)
			var posOffset = modelBounds.Center * scaleAdj;
			var newPos = origPos - posOffset;

			// Check if adjustment was needed
			var needsAdjust = MathF.Abs( scaleAdj - 1f ) > 0.01f ||
				posOffset.Length > 1f;

			// Spawn the new model
			var newGo = Game.ActiveScene.CreateObject( true );
			newGo.Name = $"ArtPass_{obj.Name}";
			newGo.SetParent( obj.Parent );
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
				Log.Info( $"[VillageArtPass] {obj.Name} → {modelName}" );
				Log.Info( $"  pos: {origPos} → {newPos} (offset={posOffset})" );
				Log.Info( $"  scale: {origScale} → {scaleAdj}" );
			}
		}

		static string DetermineReplacementModel( GameObject obj )
		{
			var name = obj.Name?.ToLowerInvariant() ?? "";

			// Check name patterns to determine type
			if ( name.Contains( "wall" ) || name.Contains( "wall_" ) )
			{
				if ( name.Contains( "corner" ) )
					return "models/castle_kit/wall-corner.vmdl";
				return "models/castle_kit/wall.vmdl";
			}
			if ( name.Contains( "tower" ) )
			{
				if ( name.Contains( "hex" ) )
					return "models/castle_kit/tower-hexagon-base.vmdl";
				return "models/castle_kit/tower-square.vmdl";
			}
			if ( name.Contains( "gate" ) )
				return "models/castle_kit/gate.vmdl";
			if ( name.Contains( "door" ) )
				return "models/castle_kit/door.vmdl";
			if ( name.Contains( "roof" ) )
				return "models/castle_kit/tower-square-roof.vmdl";
			if ( name.Contains( "column" ) || name.Contains( "pillar" ) )
				return "models/castle_kit/wall-pillar.vmdl";
			if ( name.Contains( "floor" ) || name.Contains( "road" ) )
				return "models/castle_kit/ground.vmdl";
			if ( name.Contains( "bridge" ) )
				return "models/castle_kit/bridge-straight.vmdl";
			if ( name.Contains( "stairs" ) )
				return "models/castle_kit/stairs-stone.vmdl";
			if ( name.Contains( "cottage" ) || name.Contains( "shop" ) ||
				 name.Contains( "smithy" ) || name.Contains( "tavern" ) ||
				 name.Contains( "storage" ) || name.Contains( "barn" ) )
				return "models/castle_kit/wall.vmdl";
			if ( name.Contains( "chapel" ) || name.Contains( "church" ) )
				return "models/castle_kit/tower-square.vmdl";
			if ( name.Contains( "well" ) )
				return "models/castle_kit/wall-corner.vmdl";
			if ( name.Contains( "market" ) )
				return "models/castle_kit/ground.vmdl";

			// If it has a ModelRenderer but we can't identify it, skip
			return null;
		}

		static void ScatterProps( GameObject villageRoot, List<GameObject> buildings )
		{
			if ( buildings.Count == 0 )
				return;

			var rng = new Random( 42 ); // deterministic seed
			var propCount = Math.Min( buildings.Count / 3, 20 );

			for ( int i = 0; i < propCount; i++ )
			{
				var targetBuilding = buildings[rng.Next( buildings.Count )];
				if ( targetBuilding == null ) continue;

				var propName = PropModels[rng.Next( PropModels.Length )];
				var model = Sandbox.Model.Load( propName );
				if ( model == null ) continue;

				var go = Game.ActiveScene.CreateObject( true );
				go.Name = $"Prop_{propName.Split( '/' ).Last().Replace( ".vmdl", "" )}_{i}";
				go.SetParent( villageRoot );

				// Position near the target building with a small offset
				var offset = new Vector3(
					(float)(rng.NextDouble() * 200 - 100),
					(float)(rng.NextDouble() * 200 - 100),
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

	/// <summary>
	/// Maps a village task type to its castle_kit model set.
	/// </summary>
	public class ArtModelMapping
	{
		public string Primary;
		public string[] Variants;
		public string CornerModel;
		public string TowerModel;
		public string RoofModel;
		public string DoorModel;
	}
}
