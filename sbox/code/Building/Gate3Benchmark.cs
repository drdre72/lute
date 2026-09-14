using System;
using System.Linq;
using Sandbox;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// Gate 3 benchmark — the closed-loop economy test.
	///
	/// Test 3.1: Pre-stocked stockpiles → hauler delivers → builder constructs
	/// Test 3.2: Raw materials → crafter processes → hauler delivers → builder constructs
	/// Test 3.3: Sources only → gather → haul → process → haul → build
	/// Test 3.4: Same as 3.3 with failure injected (deplete wood source mid-build)
	///
	/// This component sets up the benchmark scenario, enables
	/// EnforceMaterialGating, and periodically calls
	/// LogisticsBoard.SupplyTaskMaterials to create haul jobs for
	/// unsatisfied task requirements. It logs progress so the closed
	/// loop can be verified from logs.
	///
	/// Usage: Add this component to a GameObject in the scene, set the
	/// TestLevel property, and start play mode. The benchmark runs
	/// automatically.
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

		/// <summary> How often to create haul jobs for unsatisfied tasks (seconds). </summary>
		[Property] public float SupplyInterval { get; set; } = 2f;

		float _supplyTimer;
		float _logTimer;
		bool _initialized;
		bool _needInjected;
		bool _nightRunSaved;
		const float NightRunSaveTime = 47f * 60f; // 47 minutes
		int _houseCompleteCount;
		string _lastCompletedTask;

		protected override void OnStart()
		{
			Log.Info( $"Lute: Gate3Benchmark started — Level={Level}, Center={Center}" );
			// Static state persists across play sessions in S&Box. Reset
			// the need board so a fresh play session starts clean (otherwise
			// _lastEvaluation from a prior session makes Evaluate() return
			// early forever because Time.Now resets to 0).
			SettlementNeedBoard.Clear();
			_needInjected = false;
			Log.Info( $"Lute: Gate3Benchmark — cleared SettlementNeedBoard for fresh session." );
		}

		protected override void OnUpdate()
		{
			if ( !_initialized )
			{
				// Wait a few frames for ResourceBootstrap + NPCSpawner to finish.
				_supplyTimer += Time.Delta;
				if ( _supplyTimer > 3f )
				{
					_supplyTimer = 0f;
					InitializeBenchmark();
				}
				return;
			}

			// Periodically create haul jobs for unsatisfied tasks.
			_supplyTimer += Time.Delta;
			if ( _supplyTimer >= SupplyInterval )
			{
				_supplyTimer = 0f;
				SupplyUnsatisfiedTasks();
			}

			// Periodically log benchmark status.
			_logTimer += Time.Delta;
			if ( _logTimer >= 5f )
			{
				_logTimer = 0f;
				LogStatus();
			}

			// Evaluate settlement needs — this drives adaptive planning.
			// The SettlementNeedBoard generates StructureRequests from
			// needs, which the Surveyor resolves by selecting sites.
			SettlementNeedBoard.Evaluate( Time.Now );

			// Inject a test shelter need after 30 seconds (first tiny test
			// per the professor's proposal: can the settlement recognize
			// it needs a house, select a valid plot, and build it?).
			if ( !_needInjected && Time.Now > 30f )
			{
				_needInjected = true;
				// Count existing cottages to set the "existing" count.
				int existingCottages = ConstructionDirector.AllTasks()
					.Count( t => t.BuildTask?.TaskType == "cottage" );
				SettlementNeedBoard.RegisterNeed( new SettlementNeed
				{
					Type = SettlementNeedType.Shelter,
					StructureType = "cottage",
					Urgency = 0.8f,
					DesiredCount = existingCottages + 2,
					ExistingCount = existingCottages,
					Reason = "Test: inject shelter need for adaptive planning",
				} );
				Log.Info( $"Lute: Gate3Benchmark — injected test Shelter need (existing={existingCottages}, desired={existingCottages + 2})." );
			}

			// Night-run save: at ~47 minutes, snapshot village progress to
			// "night_run.json" for the daily proof-of-concept run. This lets
			// us leave the sim running overnight (120-min shadow timeout) and
			// capture a mid-run save without manual intervention. The save is
			// non-redundant (skips if nothing changed since last save).
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

			// Enable material gating — the whole point of Gate 3.
			ConstructionDirector.EnforceMaterialGating = true;
			Log.Info( $"Lute: Gate3Benchmark — EnforceMaterialGating = true" );

			// For Test 3.1: pre-stock the village stockpile (nearest to
			// the build site) with enough materials for multiple cottages.
			if ( Level == TestLevel.Test3_1_PreStocked )
			{
				var pile = ResourceRegistry.AllStockpiles()
					.FirstOrDefault( p => p.Id == "village_stockpile" )
					?? ResourceRegistry.AllStockpiles()
						.OrderBy( p => p.Position.Distance( Center ) )
						.FirstOrDefault();
				if ( pile != null )
				{
					// Enough for ~10 cottages (Plank x20, Timber x6, Brick x15 each).
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
					// Wood for planks/timber, Clay+Straw for bricks.
					pile.Deposit( ItemType.Wood, 200 );
					pile.Deposit( ItemType.Clay, 100 );
					pile.Deposit( ItemType.Straw, 50 );
					Log.Info( $"Lute: Gate3Benchmark — pre-stocked {pile.Id} with raw materials for crafting" );
				}
			}

			// For Test 3.3 and 3.4: sources only — no pre-stocking.
			// The gatherer will gather from sources, the hauler will
			// haul to the crafter, the crafter will process, etc.
			// (Requires gatherer NPCs + crafter NPCs in the scene.)

			Log.Info( $"Lute: Gate3Benchmark initialized — {Level}" );
			LogStatus();
		}

		void SupplyUnsatisfiedTasks()
		{
			// Don't create new jobs if there are already many pending —
			// the hauler can't keep up and we'd flood the board.
			int existingPending = LogisticsBoard.PendingCount;
			if ( existingPending > 50 )
				return;

			int totalCreated = 0;
			foreach ( var task in ConstructionDirector.AllTasks() )
			{
				if ( task.MaterialsSatisfied ) continue;
				if ( task.Status == TaskStatus.Complete ) continue;

				int created = LogisticsBoard.SupplyTaskMaterials( task, task.BuildTask?.Position ?? Center );
				if ( created > 0 )
				{
					totalCreated += created;
					Log.Info( $"Lute: Gate3Benchmark — created {created} haul jobs for task '{task.Id}' ({task.BuildTask?.Name})." );
				}

				// Stop if we've created enough this cycle.
				if ( totalCreated >= 10 )
					break;
			}

			if ( totalCreated > 0 )
				Log.Info( $"Lute: Gate3Benchmark — created {totalCreated} total haul jobs this cycle." );
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

			// Log material requirements for each non-completed task.
			foreach ( var task in tasks.Where( t => t.Status != TaskStatus.Complete ) )
			{
				if ( task.MaterialRequirements == null ) continue;
				var reqSummary = string.Join( ", ",
					task.MaterialRequirements.Select( r => $"{r.Type} {r.Delivered}/{r.Amount}{(r.Satisfied ? "✓" : "")}" ) );
				Log.Info( $"  Task '{task.Id}' ({task.BuildTask?.Name}) — {task.Status} — materials: {reqSummary}" );
			}

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
