using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Runtime probe that tests whether S&Box can run N concurrent
	/// <see cref="Task.DelaySeconds"/> loops in parallel — the core
	/// question for multi-NPC village building. Each "worker" mimics
	/// a <see cref="VillageBuilder.BuildLoop"/>: it counts up pieces
	/// with a 6-second delay between each, exactly like the real
	/// builder. If all N workers advance at the same rate, the runtime
	/// supports parallel async loops. If they serialize, only one
	/// advances at a time and multi-building would need a different
	/// scheduling approach.
	///
	/// Triggered via console command so it doesn't require scene changes:
	/// <code>village_probe 4</code>  — spawn 4 workers, run 60s, report.
	/// <code>village_probe 8</code>  — spawn 8 workers.
	/// <code>village_probe_stop</code> — cancel all active probes.
	/// </summary>
	public static class MultiBuilderProbe
	{
		private static CancellationTokenSource _cts;
		private static int _activeWorkers;
		private static readonly Dictionary<int, int> _pieceCounts = new();

		[ConCmd( "village_probe" )]
		public static void RunProbe( int workerCount = 4 )
		{
			if ( workerCount < 1 ) workerCount = 1;
			if ( workerCount > 32 ) workerCount = 32;

			StopProbe();

			_cts = new CancellationTokenSource();
			_pieceCounts.Clear();
			_activeWorkers = workerCount;

			Log.Info( $"[probe] Starting {workerCount} workers — each places 1 piece every 6s for 60s. Watching for parallel advancement." );

			for ( int i = 0; i < workerCount; i++ )
			{
				_pieceCounts[i] = 0;
				_ = RunWorker( i, _cts.Token );
			}

			_ = RunReporter( _cts.Token );
		}

		[ConCmd( "village_probe_stop" )]
		public static void StopProbe()
		{
			if ( _cts is not null )
			{
				_cts.Cancel();
				_cts = null;
			}
			_activeWorkers = 0;
		}

		static async Task RunWorker( int id, CancellationToken token )
		{
			try
			{
				for ( int piece = 0; piece < 10; piece++ ) // 10 pieces * 6s = 60s
				{
					token.ThrowIfCancellationRequested();
					await GameTask.DelaySeconds( 6.0f );
					_pieceCounts[id] = piece + 1;
				}
				Log.Info( $"[probe] Worker {id} finished — 10 pieces placed." );
			}
			catch ( System.OperationCanceledException )
			{
				Log.Info( $"[probe] Worker {id} cancelled at {_pieceCounts[id]} pieces." );
			}
		}

		static async Task RunReporter( CancellationToken token )
		{
			try
			{
				for ( int tick = 0; tick < 12; tick++ ) // report every 5s for 60s
				{
					token.ThrowIfCancellationRequested();
					await GameTask.DelaySeconds( 5.0f );

					var parts = new List<string>();
					for ( int i = 0; i < _activeWorkers; i++ )
						parts.Add( $"W{i}={_pieceCounts.GetValueOrDefault( i, 0 )}" );

					Log.Info( $"[probe] t={tick * 5 + 5}s — {string.Join( ", ", parts )}" );
				}
				Log.Info( "[probe] Done. If all workers reached ~10 pieces, parallel async works. If some are stuck at 0-1, the runtime serializes delays." );
			}
			catch ( System.OperationCanceledException )
			{
				// stopped — fine
			}
		}
	}
}
