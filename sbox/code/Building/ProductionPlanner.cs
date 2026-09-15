using System;
using System.Linq;
using Sandbox;
using Lute.Items;
using Lute.Crafting;

namespace Lute.Building
{
	/// <summary>
	/// Production planner — demand-driven crafting supply.
	///
	/// When a construction task needs a crafted material (Plank, Brick,
	/// Timber, etc.) that no stockpile currently has, this planner:
	///   1. Looks up the recipe that produces that material.
	///   2. Finds a workstation that can craft it (sawmill, brick bench).
	///   3. Finds the stockpile nearest to that workstation.
	///   4. If that stockpile lacks the recipe's raw inputs, creates haul
	///      jobs to bring the raw inputs there (from a source or another
	///      stockpile).
	///   5. The crafter NPC then fetches inputs, crafts, and deposits the
	///      output — making the finished good available for
	///      LogisticsBoard.SupplyTaskMaterials to haul to the build site.
	///
	/// This closes the demand-driven production loop:
	/// <code>
	/// task needs Plank → no stockpile has Plank → recipe: 1 Wood → 2 Planks
	///   → haul Wood to sawmill stockpile → crafter makes Planks
	///   → SupplyTaskMaterials hauls Planks to build site
	/// </code>
	///
	/// Per Move 7: "Resource pressure should generate production orders.
	/// Production should not simply choose the nearest source."
	/// </summary>
	public sealed class ProductionPlanner : Component
	{
		/// <summary> How often to scan for unmet crafted-material demand. </summary>
		[Property] public float ScanInterval { get; set; } = 5f;

		/// <summary>
		/// Maximum pending production-supply haul jobs before throttling.
		/// Prevents flooding logistics while crafters are still working.
		/// </summary>
		[Property] public int MaxPendingSupplyJobs { get; set; } = 30;

		/// <summary>
		/// Maximum new supply haul jobs to create per scan cycle.
		/// </summary>
		[Property] public int MaxJobsPerCycle { get; set; } = 5;

		/// <summary>
		/// Minimum raw-input stock to maintain at a workstation's stockpile.
		/// If the stockpile has fewer than this, create a supply job.
		/// </summary>
		[Property] public int MinInputStock { get; set; } = 20;

		float _timer;
		bool _started;

		/// <summary>
		/// Tracks (workstationId, itemType) pairs for which we already have
		/// a pending supply job, to avoid duplicate jobs.
		/// </summary>
		static readonly System.Collections.Generic.HashSet<string> _pendingSupply = new();

		protected override void OnStart()
		{
			Log.Info( "Lute: ProductionPlanner started — demand-driven crafting supply." );
		}

		protected override void OnUpdate()
		{
			if ( !_started )
			{
				_timer += Time.Delta;
				if ( _timer < 5f ) return;
				_started = true;
				_timer = 0f;
				Log.Info( "Lute: ProductionPlanner — starting demand scan." );
			}

			_timer += Time.Delta;
			if ( _timer >= ScanInterval )
			{
				_timer = 0f;
				ScanDemand();
			}
		}

		/// <summary>
		/// Scan construction tasks for unmet crafted-material demand and
		/// create supply orders to bring raw inputs to the appropriate
		/// workstation stockpile.
		/// </summary>
		void ScanDemand()
		{
			// Don't flood if too many jobs are pending.
			if ( LogisticsBoard.PendingCount > MaxPendingSupplyJobs )
				return;

			// Clean up stale pending-supply tracking for completed/failed jobs.
			CleanupPendingTracking();

			// Collect all unmet crafted-material requirements across
			// all active tasks.
			var demand = new System.Collections.Generic.Dictionary<ItemType, int>();
			foreach ( var task in ConstructionDirector.AllTasks() )
			{
				if ( task.MaterialRequirements == null ) continue;
				if ( task.Status == TaskStatus.Complete ) continue;
				if ( task.Status == TaskStatus.Cancelled || task.Status == TaskStatus.Failed ) continue;

				foreach ( var req in task.MaterialRequirements )
				{
					if ( req.Satisfied ) continue;
					if ( !ItemDefs.IsCraftedMaterial( req.Type ) ) continue;
					int needed = req.Amount - req.Delivered;
					if ( needed <= 0 ) continue;

					// Only create production orders if no stockpile has
					// enough of this material. If a stockpile already has
					// it, SupplyTaskMaterials will handle the haul.
					int totalAvailable = ResourceRegistry.AllStockpiles()
						.Sum( p => p.Count( req.Type ) );
					if ( totalAvailable >= needed ) continue;

					// There's unmet demand for a crafted material that
					// no stockpile can satisfy. Record it.
					if ( !demand.ContainsKey( req.Type ) )
						demand[req.Type] = 0;
					demand[req.Type] += needed;
				}
			}

			if ( demand.Count == 0 )
				return;

			int created = 0;
			foreach ( var kvp in demand )
			{
				if ( created >= MaxJobsPerCycle ) break;
				created += CreateProductionOrders( kvp.Key, kvp.Value );
			}

			if ( created > 0 )
				Log.Info( $"Lute: ProductionPlanner — created {created} production-supply haul jobs this cycle." );
		}

		/// <summary>
		/// For a crafted material (e.g. Plank), find the recipe, the
		/// workstation, and create haul jobs to supply its raw inputs.
		/// Returns the number of haul jobs created.
		/// </summary>
		int CreateProductionOrders( ItemType outputMaterial, int totalDemand )
		{
			try
			{
				return CreateProductionOrdersInner( outputMaterial, totalDemand );
			}
			catch ( System.Exception ex )
			{
				Log.Warning( $"Lute: ProductionPlanner — CreateProductionOrders({outputMaterial}) threw: {ex.GetType().Name}: {ex.Message}" );
				return 0;
			}
		}

		int CreateProductionOrdersInner( ItemType outputMaterial, int totalDemand )
		{
			// Find the recipe that produces this material.
			var recipeEntry = Recipes.All.FirstOrDefault( r => r.Value.OutputType == outputMaterial );
			if ( recipeEntry.Value.OutputType != outputMaterial )
			{
				Log.Warning( $"Lute: ProductionPlanner — no recipe produces {outputMaterial}." );
				return 0;
			}
			var recipe = recipeEntry.Value;

			// Find the bench type that can craft this recipe.
			BenchType benchType = BenchTypeForRecipe( recipeEntry.Key );
			if ( benchType == BenchType.BrickBench && recipeEntry.Key != "brick" )
			{
				// Can't determine bench type for this recipe.
				Log.Warning( $"Lute: ProductionPlanner — can't determine bench type for recipe '{recipeEntry.Key}'." );
				return 0;
			}

			// Find a workstation of that bench type.
			// All() prunes destroyed benches, but a bench can be destroyed
			// between All() and this loop, so guard every Bench access.
			var stations = WorkstationRegistry.All()
				.Where( s => s?.Bench != null && s.Bench.IsValid() && s.Type == benchType )
				.ToList();
			if ( stations.Count == 0 )
			{
				Log.Warning( $"Lute: ProductionPlanner — no {benchType} workstation registered for {outputMaterial}." );
				return 0;
			}

			int created = 0;
			foreach ( var station in stations )
			{
				if ( created >= MaxJobsPerCycle ) break;
				if ( station?.Bench == null ) continue;
				// Skip destroyed/invalid components — accessing
				// WorldPosition on a destroyed GameObject throws.
				if ( !station.Bench.IsValid() ) continue;

				Vector3 stationPos;
				string stationId;
				try
				{
					stationPos = station.Position;
					stationId = station.Id;
				}
				catch { continue; } // Bench destroyed mid-iteration.

				// Find the stockpile nearest to this workstation.
				var stationPile = ResourceRegistry.NearestStockpileWithSpace( stationPos );
				if ( stationPile == null )
				{
					Log.Warning( $"Lute: ProductionPlanner — no stockpile near {stationId} ({benchType})." );
					continue;
				}

				// For each raw input in the recipe, check if the
				// workstation's stockpile has enough. If not, create a
				// haul job to bring inputs there.
				foreach ( var input in recipe.Inputs )
				{
					int have = stationPile.Count( input.Key );
					if ( have >= MinInputStock ) continue;

					int toSupply = MinInputStock - have;

					// Check we don't already have a pending supply job
					// for this workstation + input type.
					string supplyKey = $"{stationId}:{input.Key}";
					if ( _pendingSupply.Contains( supplyKey ) )
						continue;

					// Find a source for the raw input.
					if ( !ItemDefs.IsRawMaterial( input.Key ) )
						continue; // Only raw materials come from sources.

					var src = ResourceRegistry.NearestSource( input.Key, stationPile.Position );
					var otherPile = ResourceRegistry.NearestStockpileWithResource( input.Key, stationPile.Position, toSupply );

					string fromId;
					Vector3 fromPos;
					if ( otherPile != null && otherPile.Count( input.Key ) >= toSupply )
					{
						fromId = $"stockpile:{otherPile.Id}";
						fromPos = otherPile.Position;
					}
					else if ( src != null )
					{
						fromId = $"source:{src.Id}";
						fromPos = src.Position;
					}
					else
					{
						// No source for this raw input — skip.
						continue;
					}

					var jobId = LogisticsBoard.CreateJob(
						input.Key, toSupply,
						fromId, fromPos,
						$"stockpile:{stationPile.Id}", stationPile.Position,
						forTaskId: "" );
					if ( jobId != null )
					{
						_pendingSupply.Add( supplyKey );
						created++;
						Log.Info( $"Lute: ProductionPlanner — supply order: haul {toSupply} {input.Key} → {stationPile.Id} (near {stationId} {benchType}) for {outputMaterial} production." );
					}
				}
			}

			return created;
		}

		/// <summary>
		/// Map a recipe name to the bench type that can craft it.
		/// </summary>
		static BenchType BenchTypeForRecipe( string recipeName )
		{
			return recipeName switch
			{
				"brick" => BenchType.BrickBench,
				"concrete" or "spade" or "pickaxe" or "shovel" or "trowel" or "storage_crate" => BenchType.Forge,
				"plank" or "timber" => BenchType.Sawmill,
				"ingot" => BenchType.Smelter,
				_ => BenchType.BrickBench,
			};
		}

		/// <summary>
		/// Remove pending-supply tracking entries whose jobs are no longer
		/// pending/assigned/in-progress (completed, failed, or not found).
		/// </summary>
		static void CleanupPendingTracking()
		{
			var stale = _pendingSupply.ToList();
			foreach ( var key in stale )
			{
				// The key is "stationId:itemType". We don't track the job
				// id here, so check if any pending job matches the
				// destination stockpile for this station + item.
				var parts = key.Split( ':' );
				if ( parts.Length != 2 ) { _pendingSupply.Remove( key ); continue; }
				string stationId = parts[0];
				if ( !Enum.TryParse<ItemType>( parts[1], out var itemType ) )
				{ _pendingSupply.Remove( key ); continue; }

				var station = WorkstationRegistry.Get( stationId );
				if ( station?.Bench == null || !station.Bench.IsValid() )
				{ _pendingSupply.Remove( key ); continue; }

				Vector3 stationPos;
				try { stationPos = station.Position; }
				catch { _pendingSupply.Remove( key ); continue; }

				var pile = ResourceRegistry.NearestStockpileWithSpace( stationPos );
				if ( pile == null ) { _pendingSupply.Remove( key ); continue; }

				bool hasPending = LogisticsBoard.AllJobs().Any( j =>
					j.ItemType == itemType
					&& j.ToId == $"stockpile:{pile.Id}"
					&& ( j.Status == LogisticsJobStatus.Pending
						 || j.Status == LogisticsJobStatus.Assigned
						 || j.Status == LogisticsJobStatus.InProgress ) );
				if ( !hasPending )
					_pendingSupply.Remove( key );
			}
		}
	}
}
