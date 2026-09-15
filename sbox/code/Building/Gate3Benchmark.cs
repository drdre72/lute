using System;
using System.Linq;
using Sandbox;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// Gate 3 benchmark — the closed-loop economy test harness.
	///
	/// Test 3.1: Pre-stocked stockpiles → hauler delivers → builder constructs
	/// Test 3.2: Raw materials → crafter processes → hauler delivers → builder constructs
	/// Test 3.3: Sources only → gather → haul → process → haul → build
	/// Test 3.4: Same as 3.3 with failure injected (deplete wood source mid-build)
	///
	/// Per the professor's review, this component should ONLY:
	///   - configure the test scenario (pre-stock materials for 3.1/3.2)
	///   - inject test needs (shelter need after 30s)
	///   - inject test failures (deplete wood source for 3.4)
	///   - assert/report outcomes (LogStatus)
	///   - save night-run snapshots
	///
	/// It should NOT:
	///   - drive need evaluation (SettlementPlanner does this)
	///   - create haul jobs (LogisticsPlanner does this)
	///   - enable material gating (SettlementPlanner does this)
	///   - clear the need board (SettlementPlanner does this)
	///
	/// No production simulation should stop functioning if Gate3Benchmark
	/// is removed from the scene.
	/// </summary>
	public sealed class Gate3Benchmark : Component
	{
		/// <summary>
		/// Which test level to run.
		/// </summary>
		public enum TestLevel
		{
			/// <summary> Pre-stocked stockpiles → hauler → builder. </summary>
			Test3_1_PreStocked,
			/// <summary> Raw materials → crafter → hauler → builder. </summary>
			Test3_2_Crafter,
			/// <summary> Sources only → gather → haul → process → haul → build. </summary>
			Test3_3_FullLoop,
			/// <summary> Same as 3.3 with failure injected. </summary>
			Test3_4_Failure,
		}

		[Property] public TestLevel Level { get; set; } = TestLevel.Test3_3_FullLoop;

		/// <summary> Center of the benchmark area. </summary>
		[Property] public Vector3 Center { get; set; } = new Vector3( 5000, 5000, 0 );

		float _logTimer;
		bool _initialized;
		bool _needInjected;
		bool _nightRunSaved;
		const float NightRunSaveTime = 100f * 60f; // 100 minutes
		int _houseCompleteCount;
		string _lastCompletedTask;

		protected override void OnStart()
		{
			Log.Info( $"Lute: Gate3Benchmark started — Level={Level}, Center={Center}" );
			Log.Info( "Lute: Gate3Benchmark — TEST HARNESS ONLY. Production loops (need evaluation, logistics supply, material gating) are owned by SettlementPlanner and LogisticsPlanner." );
		}

		protected override void OnUpdate()
		{
			if ( !_initialized )
			{
				// Wait a few frames for ResourceBootstrap + NPCSpawner to finish.
				_logTimer += Time.Delta;
				if ( _logTimer > 5f )
				{
					_logTimer = 0f;
					InitializeBenchmark();
				}
				return;
			}

			// Periodically log benchmark status (test reporting only).
			_logTimer += Time.Delta;
			if ( _logTimer >= 5f )
			{
				_logTimer = 0f;
				LogStatus();
			}

			// Inject a test shelter need after 30 seconds (first tiny test
			// per the professor's proposal: can the settlement recognize
			// it needs a house, select a valid plot, and build it?).
			if ( !_needInjected && Time.Now > 30f )
			{
				_needInjected = true;
				// Count COMPLETED cottages only for the existing baseline.
				// PlannedCount/InProgressCount/FailedCount are derived by
				// SettlementNeedBoard.ReconcileFromDirector() from the
				// authoritative ConstructionDirector task catalog.
				int existingCottages = ConstructionDirector.AllTasks()
					.Count( t => t.BuildTask?.TaskType == "cottage" && t.Status == TaskStatus.Complete );
				SettlementNeedBoard.RegisterNeed( new SettlementNeed
				{
					Type = SettlementNeedType.Shelter,
					StructureType = "cottage",
					Urgency = 0.8f,
					DesiredCount = existingCottages + 2,
					ExistingCount = existingCottages,
					Reason = "Test: inject shelter need for adaptive planning",
				} );
				Log.Info( $"Lute: Gate3Benchmark — injected test Shelter need (existing={existingCottages}, desired={existingCottages + 2}). Planned/in-progress counts will be derived by ReconcileFromDirector." );
			}

			// Night-run save: at 100 minutes, snapshot village progress to
			// "night_run.json" for the daily proof-of-concept run.
			if ( !_nightRunSaved && Time.Now >= NightRunSaveTime )
			{
				_nightRunSaved = true;
				var tasks = ConstructionDirector.AllTasks()
					.Select( t => t.BuildTask )
					.Where( b => b != null )
					.ToList();
				bool saved = VillagePersistence.SaveProgress( "night_run.json",
					Center, tasks, Time.Now );
				int done = tasks.Count( t => t.Status == 2 );
				Log.Info( $"Lute: Gate3Benchmark — NIGHT RUN SAVE at {Time.Now / 60f:F1}min:" +
					$" {( saved ? "saved" : "no change (skipped)" )}," +
					$" {tasks.Count} tasks, {done} completed → night_run.json" );
			}
		}

		void InitializeBenchmark()
		{
			_initialized = true;

			// For Test 3.1: pre-stock the village stockpile with enough
			// materials for multiple cottages.
			if ( Level == TestLevel.Test3_1_PreStocked )
			{
				var pile = ResourceRegistry.AllStockpiles()
					.FirstOrDefault( p => p.Id == "village_stockpile" )
					?? ResourceRegistry.AllStockpiles()
						.OrderBy( p => p.Position.Distance( Center ) )
						.FirstOrDefault();
				if ( pile != null )
				{
					pile.Deposit( ItemType.Plank, 2000 );
					pile.Deposit( ItemType.Timber, 600 );
					pile.Deposit( ItemType.Brick, 1500 );
					Log.Info( $"Lute: Gate3Benchmark — pre-stocked {pile.Id} at {pile.Position} with Plank x2000, Timber x600, Brick x1500" );
				}
				else
				{
					Log.Warning( $"Lute: Gate3Benchmark — no stockpile found to pre-stock!" );
				}
			}

			// For Test 3.2: pre-stock raw materials for the crafter.
			if ( Level == TestLevel.Test3_2_Crafter )
			{
				var pile = ResourceRegistry.AllStockpiles()
					.FirstOrDefault( p => p.Id == "village_stockpile" )
					?? ResourceRegistry.AllStockpiles()
						.OrderBy( p => p.Position.Distance( Center ) )
						.FirstOrDefault();
				if ( pile != null )
				{
					pile.Deposit( ItemType.Wood, 200 );
					pile.Deposit( ItemType.Clay, 100 );
					pile.Deposit( ItemType.Straw, 50 );
					Log.Info( $"Lute: Gate3Benchmark — pre-stocked {pile.Id} with raw materials for crafting" );
				}
			}

			// For Test 3.3 and 3.4: sources only — no pre-stocking.
			// The gatherer will gather from sources, the hauler will
			// haul to the crafter, the crafter will process, etc.

			Log.Info( $"Lute: Gate3Benchmark initialized — {Level}" );
			LogStatus();
		}

		void LogStatus()
		{
			var tasks = ConstructionDirector.AllTasks();
			var jobs = LogisticsBoard.AllJobs();

			int pending = jobs.Count( j => j.Status == LogisticsJobStatus.Pending );
			int assigned = jobs.Count( j => j.Status == LogisticsJobStatus.Assigned );
			int inProgress = jobs.Count( j => j.Status == LogisticsJobStatus.InProgress );
			int completed = jobs.Count( j => j.Status == LogisticsJobStatus.Completed );
			int failed = jobs.Count( j => j.Status == LogisticsJobStatus.Failed );

			Log.Info( $"Lute: Gate3Benchmark status — tasks: {tasks.Count} total, {tasks.Count( t => t.MaterialsSatisfied )} materials-satisfied, {tasks.Count( t => t.Status == TaskStatus.Complete )} completed | jobs: {pending} pending, {assigned} assigned, {inProgress} in-progress, {completed} completed, {failed} failed" );

			// Check for newly completed tasks.
			foreach ( var task in tasks.Where( t => t.Status == TaskStatus.Complete && t.Id != _lastCompletedTask ) )
			{
				_lastCompletedTask = task.Id;
				_houseCompleteCount++;
				Log.Info( $"Lute: Gate3Benchmark — HOUSE COMPLETE! Task '{task.Id}' ({task.BuildTask?.Name}) finished. Total completed: {_houseCompleteCount}" );
			}
		}

		/// <summary>
		/// Inject a failure for Test 3.4: deplete the preferred wood source.
		/// Call this mid-build to test recovery.
		/// </summary>
		[ConCmd( "gate3_fail" )]
		public static void InjectFailure()
		{
			var woodSource = ResourceRegistry.AllSources()
				.FirstOrDefault( s => s.Type == ItemType.Wood );
			if ( woodSource != null )
			{
				woodSource.RemainingYield = 0;
				Log.Info( $"Lute: Gate3Benchmark — FAILURE INJECTED: depleted wood source '{woodSource.Id}'" );
			}
			else
			{
				Log.Warning( "Lute: Gate3Benchmark — no wood source found to deplete" );
			}
		}

		/// <summary>
		/// Dump full benchmark status to console.
		/// </summary>
		[ConCmd( "gate3_status" )]
		public static void DumpStatus()
		{
			Log.Info( $"Lute: Gate3Benchmark — EnforceMaterialGating={ConstructionDirector.EnforceMaterialGating}" );
			Log.Info( LogisticsBoard.Summary() );
			Log.Info( ResourceRegistry.Summary() );
		}

		/// <summary>
		/// Manually trigger a night-run save to "night_run.json". Use this
		/// to snapshot the current simulation state at any point during a
		/// long overnight run.
		/// </summary>
		[ConCmd( "gate3_night_run_save" )]
		public static void NightRunSave()
		{
			// Use the known Gate 3 benchmark center; the save file stores
			// the village center for resume.
			var center = new Vector3( -400f * 39.37f, -400f * 39.37f, 0f );
			var tasks = ConstructionDirector.AllTasks()
				.Select( t => t.BuildTask )
				.Where( b => b != null )
				.ToList();
			bool saved = VillagePersistence.SaveProgress( "night_run.json",
				center, tasks, Sandbox.Time.Now );
			int done = tasks.Count( t => t.Status == 2 );
			Log.Info( $"Lute: Gate3Benchmark — manual NIGHT RUN SAVE:" +
				$" {( saved ? "saved" : "no change (skipped)" )}," +
				$" {tasks.Count} tasks, {done} completed → night_run.json" );
		}
	}
}
