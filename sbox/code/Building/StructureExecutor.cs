using System.Threading;
using System.Threading.Tasks;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Realizes one authorized <see cref="VillageBuildTask"/> / structure plan.
	/// This is the single-structure execution layer extracted from
	/// <see cref="VillageBuilder"/> (Move 10). It knows how to build a
	/// structure; it does not own village planning, task scheduling,
	/// multi-builder partitioning, or village persistence.
	///
	/// This first stage is a delegating facade: the executor is the
	/// authoritative entry point for structure realization, but the
	/// concrete build methods still live on VillageBuilder (accessed via
	/// the host reference) to avoid a large unstable rewrite. Future moves
	/// will physically relocate the build methods into this class, at
	/// which point the host reference is no longer needed for them.
	/// </summary>
	public sealed class StructureExecutor
	{
		private readonly VillageBuilder _host;

		public StructureExecutor( VillageBuilder host )
		{
			_host = host;
		}

		/// <summary>
		/// Realize one authorized plan. Dispatches to the per-type build
		/// method. This is the single entry point VillageBuilder calls;
		/// it no longer calls BuildWallSegment/BuildGate/etc. directly.
		/// </summary>
		public async Task Execute( VillageBuildTask task, CancellationToken token )
		{
			switch ( task.TaskType )
			{
				case "wall":
					await _host.BuildWallSegment( task, token );
					break;
				case "gate":
					await _host.BuildGate( task, token );
					break;
				case "road":
					await _host.BuildRoadSection( task, token );
					break;
				case "well":
					await _host.BuildWell( task, token );
					break;
				case "market_square":
					await _host.BuildMarketSquare( task, token );
					break;
				default:
					await _host.BuildBuilding( task, token );
					break;
			}
		}

		/// <summary>
		/// Finalize a wall segment's representation. Delegated from
		/// VillageBuilder so MCP/editor requests route through the
		/// executor (the structure-realization authority).
		/// </summary>
		public void FinalizeWall( VillageBuildTask task ) => _host.FinalizeWall( task );

		/// <summary>
		/// Deconstruct a finalized wall segment. Delegated from
		/// VillageBuilder so MCP/editor requests route through the
		/// executor.
		/// </summary>
		public void DeconstructWall( VillageBuildTask task ) => _host.DeconstructWall( task );
	}
}
