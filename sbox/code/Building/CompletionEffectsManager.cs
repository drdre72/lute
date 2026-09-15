using System;
using System.Linq;
using Sandbox;
using Lute.Crafting;

namespace Lute.Building
{
	/// <summary>
	/// Completion effects manager — applies gameplay effects when a
	/// structure finishes building.
	///
	/// When <see cref="ConstructionDirector.CompleteTask"/> fires a
	/// <see cref="ConstructionEventType.TaskCompleted"/> event, this
	/// component applies the appropriate completion effect based on the
	/// structure type:
	///
	/// <code>
	/// sawmill   → spawn CraftingBench(Sawmill) → +plank/timber capacity
	/// forge     → spawn CraftingBench(Forge)   → +smithing capacity
	/// brick_bench → spawn CraftingBench(BrickBench) → +brick capacity
	/// smelter   → spawn CraftingBench(Smelter) → +ingot capacity
	/// cottage   → +housing capacity
	/// house     → +housing capacity
	/// </code>
	///
	/// This is the Move 8 mechanism: completed structures change the
	/// settlement's production and housing capacity, creating a positive
	/// feedback loop — more structures → more capacity → more growth.
	///
	/// The manager subscribes to the <see cref="ConstructionEventBus"/>
	/// on start and applies effects for every completed task. It is a
	/// production component (not test-harness) so the sim is self-driving.
	/// </summary>
	public sealed class CompletionEffectsManager : Component
	{
		/// <summary>
		/// Total housing capacity provided by completed residential
		/// structures. Incremented when cottages/houses complete.
		/// </summary>
		[Property] public int HousingCapacity { get; set; }

		/// <summary>
		/// Number of production workstations registered by completed
		/// structures (not counting the initial world-gen ones).
		/// </summary>
		[Property] public int RegisteredWorkstations { get; set; }

		static CompletionEffectsManager _instance;

		protected override void OnStart()
		{
			_instance = this;
			ConstructionEventBus.Subscribe( OnConstructionEvent );
			Log.Info( "Lute: CompletionEffectsManager started — applying structure completion effects." );
		}

		protected override void OnDestroy()
		{
			ConstructionEventBus.Unsubscribe( OnConstructionEvent );
			if ( _instance == this ) _instance = null;
		}

		static void OnConstructionEvent( ConstructionEvent evt )
		{
			if ( evt.Type != ConstructionEventType.TaskCompleted )
				return;
			if ( _instance == null ) return;

			var task = ConstructionDirector.GetTask( evt.TaskId );
			if ( task?.BuildTask == null ) return;

			_instance.ApplyCompletionEffect( task );
		}

		/// <summary>
		/// Apply the completion effect for a finished structure based on
		/// its TaskType. Each effect is idempotent (safe to call multiple
		/// times — the workstation registry deduplicates, housing is
		/// just a counter).
		/// </summary>
		void ApplyCompletionEffect( DirectedTask task )
		{
			string structureType = task.BuildTask.TaskType ?? "";
			Vector3 pos = task.BuildTask.Position;

			switch ( structureType )
			{
				case "sawmill":
					SpawnWorkstation( BenchType.Sawmill, pos, structureType );
					break;
				case "forge":
					SpawnWorkstation( BenchType.Forge, pos, structureType );
					break;
				case "brick_bench":
				case "brickbench":
				case "mason":
					SpawnWorkstation( BenchType.BrickBench, pos, structureType );
					break;
				case "smelter":
					SpawnWorkstation( BenchType.Smelter, pos, structureType );
					break;
				case "cottage":
					HousingCapacity += 4;
					Log.Info( $"Lute: CompletionEffects — cottage completed at {pos} → +4 housing (total: {HousingCapacity})." );
					break;
				case "house":
					HousingCapacity += 6;
					Log.Info( $"Lute: CompletionEffects — house completed at {pos} → +6 housing (total: {HousingCapacity})." );
					break;
				case "wall":
				case "gate":
				case "tower":
					// Defensive structures — no production/housing effect
					// yet, but future: wall → +defense, tower → +vision.
					break;
				default:
					// Unknown structure type — no effect.
					break;
			}
		}

		/// <summary>
		/// Spawn a CraftingBench component at the structure's position and
		/// re-discover workstations so the new bench is registered. This
		/// increases production capacity for the relevant recipe chain.
		/// </summary>
		void SpawnWorkstation( BenchType benchType, Vector3 pos, string structureType )
		{
			try
			{
				var go = Scene.CreateObject( true );
				go.Name = $"{structureType}_bench_{RegisteredWorkstations}";
				go.WorldPosition = pos;
				var bench = go.AddComponent<CraftingBench>();
				bench.Bench = benchType;
				RegisteredWorkstations++;
				WorkstationRegistry.DiscoverAll();
				Log.Info( $"Lute: CompletionEffects — {structureType} completed at {pos} → spawned {benchType} workstation #{RegisteredWorkstations} (total registered: {WorkstationRegistry.All().Count})." );
			}
			catch ( System.Exception ex )
			{
				Log.Warning( $"Lute: CompletionEffects — failed to spawn {benchType} at {pos}: {ex.Message}" );
			}
		}
	}
}
