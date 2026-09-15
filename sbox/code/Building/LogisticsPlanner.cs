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
		/// Fallback center for tasks without a position (shouldn't happen
		/// in production, but defensive).
		/// </summary>
		[Property] public Vector3 FallbackCenter { get; set; } = new Vector3( -400f * 39.37f, -400f * 39.37f, 0f );

		float _timer;
		bool _started;

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
					Log.Info( $"Lute: LogisticsPlanner — created {created} haul jobs for task '{task.Id}' ({task.BuildTask?.Name})." );
				}

				// Stop if we've created enough this cycle.
				if ( totalCreated >= MaxJobsPerCycle )
					break;
			}

			if ( totalCreated > 0 )
				Log.Info( $"Lute: LogisticsPlanner — created {totalCreated} total haul jobs this cycle." );
		}
	}
}
