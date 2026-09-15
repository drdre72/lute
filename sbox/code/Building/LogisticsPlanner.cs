using System;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Production logistics planner. Drives the closed-loop economy by
	/// periodically creating haul jobs for unsatisfied task material
	/// requirements. This is the production owner of the logistics
	/// supply loop — it replaces the SupplyUnsatisfiedTasks() call that
	/// was previously inside Gate3Benchmark.
	///
	/// Per the professor's review:
	/// "Construction demand creates logistics demand. Then remove
	/// Gate3Benchmark and prove the settlement continues functioning."
	///
	/// The logistics supply loop:
	/// <code>
	/// ConstructionDirector task has unmet material requirements
	///         ↓
	/// LogisticsPlanner calls LogisticsBoard.SupplyTaskMaterials()
	///         ↓
	/// LogisticsBoard creates haul jobs
	///         ↓
	/// Hauler NPCs claim and execute jobs
	///         ↓
	/// Materials delivered to build site
	///         ↓
	/// Task.MaterialsSatisfied becomes true
	///         ↓
	/// ConstructionDirector releases task for execution
	/// </code>
	/// </summary>
	public sealed class LogisticsPlanner : Component
	{
		/// <summary> How often to create haul jobs (seconds). </summary>
		[Property] public float SupplyInterval { get; set; } = 2f;

		/// <summary>
		/// Maximum pending haul jobs before throttling new job creation.
		/// Prevents flooding the board faster than haulers can process.
		/// </summary>
		[Property] public int MaxPendingJobs { get; set; } = 50;

		/// <summary>
		/// Maximum new jobs to create per supply cycle. Prevents a single
		/// cycle from creating hundreds of jobs.
		/// </summary>
		[Property] public int MaxJobsPerCycle { get; set; } = 10;

		/// <summary>
		/// How long (seconds) a task can remain material-unsatisfied with
		/// zero new haul jobs created before it is declared starved and
		/// failed. This is the production failure-recovery path for Gate
		/// 3.4: when a resource source is depleted mid-build, logistics
		/// can no longer supply the task, SupplyTaskMaterials returns 0,
		/// and after this timeout the task fails -- which reopens the
		/// originating settlement need via
		/// SettlementNeedBoard.OnTaskFailed so the settlement can replan.
		/// </summary>
		[Property] public float StarvationTimeout { get; set; } = 90f;

		/// <summary>
		/// Fallback center for tasks without a position (shouldn't happen
		/// in production, but defensive).
		/// </summary>
		[Property] public Vector3 FallbackCenter { get; set; } = new Vector3( -400f * 39.37f, -400f * 39.37f, 0f );

		float _timer;
		bool _started;

		/// <summary>
		/// Per-task starvation tracking. Records the simulation time at
		/// which a task first became material-unsatisfied with zero new
		/// haul jobs created. If it stays in that state past
		/// <see cref="StarvationTimeout"/>, the task is failed.
		/// </summary>
		readonly System.Collections.Generic.Dictionary<string, float> _starvationSince = new();

		protected override void OnStart()
		{
			Log.Info( "Lute: LogisticsPlanner started — production logistics supply loop." );
		}

		protected override void OnUpdate()
		{
			// Wait a few seconds for ResourceBootstrap + NPCSpawner to finish.
			if ( !_started )
			{
				_timer += Time.Delta;
				if ( _timer < 5f ) return;
				_started = true;
				_timer = 0f;
				Log.Info( "Lute: LogisticsPlanner — starting logistics supply loop." );
			}

			_timer += Time.Delta;
			if ( _timer >= SupplyInterval )
			{
				_timer = 0f;
				SupplyUnsatisfiedTasks();
			}
		}

		/// <summary>
		/// Create haul jobs for tasks with unmet material requirements.
		/// This is the production version of what was previously
		/// Gate3Benchmark.SupplyUnsatisfiedTasks().
		/// </summary>
		void SupplyUnsatisfiedTasks()
		{
			// Don't create new jobs if there are already many pending —
			// the haulers can't keep up and we'd flood the board.
			int existingPending = LogisticsBoard.PendingCount;
			if ( existingPending > MaxPendingJobs )
				return;

			int totalCreated = 0;
			var suppliedTasks = new System.Collections.Generic.HashSet<string>();
			foreach ( var task in ConstructionDirector.AllTasks() )
			{
				if ( task.MaterialsSatisfied ) continue;
				if ( task.Status == TaskStatus.Complete ) continue;
				if ( task.Status == TaskStatus.Cancelled || task.Status == TaskStatus.Failed ) continue;

				int created = LogisticsBoard.SupplyTaskMaterials( task,
					task.BuildTask?.Position ?? FallbackCenter );
				if ( created > 0 )
				{
					totalCreated += created;
					suppliedTasks.Add( task.Id );
					Log.Info( $"Lute: LogisticsPlanner — created {created} haul jobs for task '{task.Id}' ({task.BuildTask?.Name})." );
				}

				// Stop if we've created enough this cycle.
				if ( totalCreated >= MaxJobsPerCycle )
					break;
			}

			if ( totalCreated > 0 )
				Log.Info( $"Lute: LogisticsPlanner — created {totalCreated} total haul jobs this cycle." );

			// Starvation watchdog (Gate 3.4 failure recovery): any task
			// that remained material-unsatisfied AND produced zero new
			// haul jobs this cycle accumulates starvation time. If a
			// task stays starved past StarvationTimeout, fail it -- this
			// reopens the originating need so the settlement can replan
			// around the depleted resource.
			CheckStarvation( suppliedTasks );
		}

		/// <summary>
		/// Per-task starvation watchdog. A task is "starving" if it
		/// remained material-unsatisfied with zero new haul jobs this
		/// supply cycle. After <see cref="StarvationTimeout"/> seconds
		/// of continuous starvation, the task is failed via
		/// ConstructionDirector.FailTask, which reopens the originating
		/// settlement need via SettlementNeedBoard.OnTaskFailed.
		/// </summary>
		void CheckStarvation( System.Collections.Generic.HashSet<string> suppliedTasks )
		{
			var tasks = ConstructionDirector.AllTasks()
				.Where( t => !t.MaterialsSatisfied
					&& t.Status != TaskStatus.Complete
					&& t.Status != TaskStatus.Cancelled
					&& t.Status != TaskStatus.Failed )
				.ToList();

			var stillStarving = new System.Collections.Generic.HashSet<string>();
			foreach ( var task in tasks )
			{
				if ( suppliedTasks.Contains( task.Id ) )
				{
					// Got supply this cycle -- not starving, reset timer.
					_starvationSince.Remove( task.Id );
					continue;
				}

				if ( !_starvationSince.TryGetValue( task.Id, out float since ) )
				{
					since = Sandbox.Time.Now;
					_starvationSince[task.Id] = since;
					Log.Info( $"Lute: LogisticsPlanner — task '{task.Id}' ({task.BuildTask?.Name}) is starved (no new haul jobs). Starvation timer started." );
				}

				float elapsed = Sandbox.Time.Now - since;
				if ( elapsed >= StarvationTimeout )
				{
					Log.Warning( $"Lute: LogisticsPlanner — task '{task.Id}' ({task.BuildTask?.Name}) STARVED for {elapsed:F1}s (>={StarvationTimeout}s). Failing task to reopen need." );
					_starvationSince.Remove( task.Id );
					ConstructionDirector.FailTask( task.Id, "material starvation: no haul jobs created for " + elapsed.ToString( "F0" ) + "s (resource source likely depleted)" );
				}
				else
				{
					stillStarving.Add( task.Id );
				}
			}

			// Clean up tracking for tasks that are no longer in the
			// unsatisfied set (completed, failed, or became satisfied).
			var stale = _starvationSince.Keys.Where( k => !stillStarving.Contains( k ) ).ToList();
			foreach ( var id in stale ) _starvationSince.Remove( id );
		}
	}
}
