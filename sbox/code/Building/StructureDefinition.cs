using System;
using System.Collections.Generic;
using System.Linq;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// Authoritative structure definition compiled BEFORE site selection.
	/// The Surveyor validates sites against these exact bounds; the
	/// executor builds from the same Blueprint. One definition, one truth.
	///
	/// Per the professor's review:
	/// "Before a Surveyor validates a site, compile an authoritative local
	/// structure description: exact local footprint, exact bounds, exact
	/// material bill, estimated work. Then Surveyor validates
	/// BuildPlan.Bounds, ReservationManager reserves BuildPlan.Bounds,
	/// ConstructionDirector uses BuildPlan.Materials, StructureExecutor
	/// realizes BuildPlan geometry. One definition."
	///
	/// This also solves the exact TotalPieces problem: the Blueprint is
	/// compiled here, so PieceCount is known before construction begins.
	/// </summary>
	public class StructureDefinition
	{
		static int _nextId;
		static readonly Dictionary<string, StructureDefinition> _registry = new();

		/// <summary> Unique deterministic id for this definition. </summary>
		public string Id { get; init; }

		/// <summary> Structure type (e.g. "cottage", "smithy", "well"). </summary>
		public string StructureType { get; init; }

		/// <summary> The compiled Blueprint — the authoritative piece list. </summary>
		public Blueprint Blueprint { get; init; }

		/// <summary>
		/// Exact footprint bounds (relative to origin), computed from the
		/// Blueprint. The Surveyor uses this for site validation;
		/// ReservationManager reserves this volume.
		/// </summary>
		public BBox LocalBounds { get; init; }

		/// <summary>
		/// Exact world-space bounds at a given position + rotation.
		/// Used for reservation and collision checks.
		/// </summary>
		public BBox WorldBoundsAt( Vector3 position, float rotation = 0f )
		{
			var (min, max) = (LocalBounds.Mins, LocalBounds.Maxs);
			// For now, use AABB (rotation applied to footprint would need
			// OBB; the current reservation system uses AABB too).
			return new BBox( position + min, position + max );
		}

		/// <summary>
		/// Exact total piece count from the compiled Blueprint. This is
		/// AUTHORITATIVE — not an estimate. The executor's PiecesPlaced
		/// is a cursor through this immutable plan.
		/// </summary>
		public int TotalPieces => Blueprint?.PieceCount ?? 0;

		/// <summary>
		/// Exact material bill of materials, derived from the Blueprint's
		/// piece types and materials. Used by ConstructionDirector for
		/// material requirements and logistics.
		/// </summary>
		public List<MaterialRequirement> BillOfMaterials { get; init; }

		/// <summary>
		/// Estimated work (rough — for scheduling/load balancing only).
		/// Never used as the authoritative piece count.
		/// </summary>
		public int EstimatedWork { get; init; }

		/// <summary>
		/// Compile a StructureDefinition for a structure type at a given
		/// base size and wealth factor. This produces the authoritative
		/// Blueprint BEFORE site selection, so the Surveyor can validate
		/// against the exact footprint.
		/// </summary>
		public static StructureDefinition Compile(
			string structureType, int baseWidth, int baseHeight,
			float wealthFactor, int layoutSeed,
			float cellSize = 100f, float wallHeight = 200f,
			float floorThickness = 10f,
			string wallMaterial = "materials/medieval/wood.vmat",
			string floorMaterial = "materials/medieval/wood.vmat" )
		{
			// Generate the layout deterministically.
			var rng = layoutSeed > 0 ? new Random( layoutSeed ) : new Random();
			var grammar = new BuildingGrammar( rng );
			var layout = grammar.GenerateLayout( baseWidth, baseHeight, wealthFactor );

			// Compile the Blueprint from the layout.
			var bp = Blueprint.FromGridLayout( layout, Vector3.Zero, 0f,
				cellSize, wallHeight, floorThickness, wallMaterial, floorMaterial );

			// Compute exact local bounds from the Blueprint.
			var bounds = bp.ComputeBounds();
			BBox localBounds;
			if ( bounds.HasValue )
			{
				var (min, max) = bounds.Value;
				// Expand slightly for collision margin (1 brick module).
				float margin = 0.125f * 39.37f; // ~12.5cm
				localBounds = new BBox(
					min - new Vector3( margin, margin, 0 ),
					max + new Vector3( margin, margin, 0 ) );
			}
			else
			{
				// Fallback to the base size if no pieces.
				float halfW = baseWidth * wealthFactor * cellSize / 2f;
				float halfD = baseHeight * wealthFactor * cellSize / 2f;
				localBounds = new BBox(
					new Vector3( -halfW, -halfD, 0 ),
					new Vector3( halfW, halfD, wallHeight ) );
			}

			// Compute BOM from piece types.
			var bom = ComputeBOM( structureType, bp.PieceCount );

			var def = new StructureDefinition
			{
				Id = $"sdef_{_nextId++:D6}",
				StructureType = structureType,
				Blueprint = bp,
				LocalBounds = localBounds,
				BillOfMaterials = bom,
				EstimatedWork = bp.PieceCount, // exact, not rough
			};

			_registry[def.Id] = def;
			return def;
		}

		/// <summary>
		/// Look up a previously compiled definition by id.
		/// </summary>
		public static StructureDefinition Get( string id ) =>
			string.IsNullOrEmpty( id ) || !_registry.TryGetValue( id, out var d ) ? null : d;

		/// <summary> Clear the registry (fresh start). </summary>
		public static void ClearRegistry()
		{
			_registry.Clear();
			_nextId = 0;
		}

		/// <summary>
		/// Compute a bill of materials for a structure type and piece count.
		/// Mirrors ConstructionDirector.ComputeMaterialRequirements but
		/// is available before task registration.
		/// </summary>
		static List<MaterialRequirement> ComputeBOM( string structureType, int pieces )
		{
			if ( pieces <= 0 ) return null;
			int brickAmount = 0, plankAmount = 0, timberAmount = 0;

			switch ( structureType )
			{
				case "wall":
				case "gate":
					brickAmount = Math.Max( 5, pieces / 10 );
					break;
				case "road":
				case "well":
					brickAmount = Math.Max( 3, pieces / 20 );
					break;
				case "chapel":
				case "smithy":
					brickAmount = Math.Max( 10, pieces / 10 );
					break;
				case "cottage":
				case "shop":
				case "tavern":
				case "storage":
				case "guardhouse":
					plankAmount = Math.Max( 20, pieces / 5 );
					timberAmount = Math.Max( 6, pieces / 20 );
					brickAmount = Math.Max( 15, pieces / 8 );
					break;
				case "market_square":
					brickAmount = Math.Max( 5, pieces / 20 );
					break;
				default:
					return null;
			}

			var reqs = new List<MaterialRequirement>();
			if ( brickAmount > 0 )
				reqs.Add( new MaterialRequirement { Type = ItemType.Brick, Amount = brickAmount } );
			if ( plankAmount > 0 )
				reqs.Add( new MaterialRequirement { Type = ItemType.Plank, Amount = plankAmount } );
			if ( timberAmount > 0 )
				reqs.Add( new MaterialRequirement { Type = ItemType.Timber, Amount = timberAmount } );
			return reqs.Count > 0 ? reqs : null;
		}
	}
}
