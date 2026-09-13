using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HalfEdgeMesh;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Long-running village builder. Manages a queue of
	/// <see cref="VillageBuildTask"/>s and constructs each one piece by
	/// piece, saving progress every <see cref="SaveInterval"/> seconds
	/// (non-redundant — skips save if no progress was made).
	///
	/// Designed for an 8-hour build by a single NPC at ~6s per piece.
	/// On load, resumes from the last save: completed tasks are skipped,
	/// the in-progress task continues from its saved piece count, and
	/// pending tasks follow.
	///
	/// The <see cref="VillageBuilderController"/> (NPC body + FSM) reads
	/// <see cref="CurrentTask"/> to know where to walk and what to do.
	/// </summary>
	public sealed class VillageBuilder : Component
	{
		const float M = 39.37f;
		const float BoxModelNativeSize = 50f;

		// ── Canonical Lute Masonry Unit v1 ──────────────────────────────
		// Body and module are deliberately different. The body is the
		// physical brick; the module is the placement grid (body + mortar).
		// Never use one variable for both. See BrickSpec below.
		//
		//   Body:   24.0 × 11.5 × 5.5  cm  (length × depth × height)
		//   Module: 25.0 × 12.5 × 6.25 cm
		//
		// Anchor: bottom-center. Native orientation: X=length, Y=depth, Z=up.
		// Mortar lives in the assembly (the module gap), not in the body dims.
		//
		// Reference wall: 2.0m × 2.0m × 0.5m
		//   = 8 modules long × 32 courses high × 4 wythes deep
		//   = 8×32×4 = 1024 brick slots (even course: 8 full; odd: 1 half + 7 full + 1 half)
		//   = exactly fills -1.0..+1.0 in X, 0..2.0 in Z, -0.25..+0.25 in Y.
		static readonly Vector3 BrickBodySize = new( 0.24f * M, 0.115f * M, 0.055f * M );
		static readonly Vector3 BrickModuleSize = new( 0.25f * M, 0.125f * M, 0.0625f * M );

		// Convenience accessors (module components — the placement grid)
		const float BrickModuleX = 0.25f * M;   // 25cm horizontal pitch
		const float BrickModuleY = 0.125f * M;  // 12.5cm depth pitch (one wythe)
		const float BrickModuleZ = 0.0625f * M; // 6.25cm course pitch

		/// <summary> Wall segment length in world units. 2m = small Rust-style buildable unit. </summary>
		const float WallSegmentLength = 2f * M;
		/// <summary> Wall thickness in world units. 0.5m = 4 wythes of 12.5cm. </summary>
		const float WallThickness = 0.5f * M;

		enum PieceAnchor
		{
			Center,
			Base,
			Top,
		}

		/// <summary> Seconds between placed pieces. 1s = ~73-minute pace. </summary>
		[Property] public float BuildInterval { get; set; } = 0.5f;

	/// <summary>
	/// Set this to a wall task name (e.g. "Wall_N_0") to request finalization
	/// of that segment from MCP/the editor. The builder will call
	/// FinalizeWall on the next OnUpdate. Cleared after processing.
	/// </summary>
	[Property] public string RequestFinalizeWall { get; set; } = "";

	/// <summary>
	/// Set this to a wall task name (e.g. "Wall_N_0") to request deconstruction
	/// of a finalized segment from MCP/the editor. The builder will call
	/// DeconstructWall on the next OnUpdate. Cleared after processing.
	/// </summary>
	[Property] public string RequestDeconstructWall { get; set; } = "";

		/// <summary> Seconds between save checks. </summary>
		[Property] public float SaveInterval { get; set; } = 60f; // 1 minute

		/// <summary> World center of the village. </summary>
		[Property] public Vector3 Center { get; set; } = new Vector3( 5000, 5000, 0 );

		/// <summary> Grid cell size (inches). 100in ~= 2.54m. </summary>
		[Property] public float CellSize { get; set; } = 100f;

		/// <summary> Wall height (inches). </summary>
		[Property] public float WallHeight { get; set; } = 80f;

		/// <summary> Floor thickness (inches). </summary>
		[Property] public float FloorThickness { get; set; } = 10f;

		/// <summary> Material for walls. </summary>
		[Property] public string WallMaterial { get; set; } = "materials/medieval/castle_wall.vmat";

		/// <summary> Material for floors/roads. </summary>
		[Property] public string FloorMaterial { get; set; } = "materials/medieval/plaza.vmat";

		/// <summary> Material for building walls. </summary>
		[Property] public string BuildingWallMaterial { get; set; } = "materials/medieval/wood.vmat";

		/// <summary> Material for building floors. </summary>
		[Property] public string BuildingFloorMaterial { get; set; } = "materials/medieval/wood.vmat";

		/// <summary> Material for gates (darker stone). </summary>
		[Property] public string GateMaterial { get; set; } = "materials/medieval/stone_tower.vmat";

		/// <summary> Seed for village layout (0 = random). </summary>
		[Property] public int VillageSeed { get; set; } = 0;

		/// <summary> If true, clear any existing save and start fresh. </summary>
		[Property] public bool FreshBuild { get; set; } = false;

		/// <summary>
		/// Multi-builder mode: which builder ID this instance is (0-based).
		/// When TotalBuilders is 1 (default), this is ignored and the builder
		/// processes all tasks (single-builder mode, the original architecture).
		/// When TotalBuilders is above 1, this builder only processes tasks
		/// whose index in Tasks satisfies idx mod TotalBuilders equals BuilderId.
		/// Each builder needs its own VillageBuilder component and NPC controller.
		/// </summary>
		[Property] public int BuilderId { get; set; } = 0;

		/// <summary>
		/// Multi-builder mode: total number of builders sharing the task list.
		/// Default 1 = single-builder mode (process all tasks). Set above 1
		/// to partition tasks across N builders for parallel construction.
		/// All builders sharing a village must agree on this value.
		/// </summary>
		[Property] public int TotalBuilders { get; set; } = 1;

		/// <summary> The task currently being built (or null if idle). </summary>
		public VillageBuildTask CurrentTask { get; private set; }

		/// <summary>
		/// Director-side id of the task currently being built, or null.
		/// Set when the builder claims a task from the
		/// <see cref="ConstructionDirector"/>. Mirrors <see cref="CurrentTask"/>.
		/// </summary>
		public string CurrentDirectedTaskId { get; internal set; }

		/// <summary>
		/// Index into <see cref="Tasks"/> of the current task, or -1 if none.
		/// Used by the controller for index-based (not name-based) task tracking.
		/// </summary>
		public int CurrentTaskIndex { get; private set; } = -1;

		/// <summary> All tasks in the village (sorted by priority). Shared across all builders. </summary>
		public List<VillageBuildTask> Tasks { get; private set; } = new();

		/// <summary> Static shared task list — only builder 0 generates it; others reference it. </summary>
		private static List<VillageBuildTask> _sharedTasks;
		private static GameObject _sharedVillageRoot;

		/// <summary> Total elapsed build time (seconds). </summary>
		public float ElapsedTime { get; private set; }

		/// <summary> True when all tasks are complete. </summary>
		public bool IsComplete { get; private set; }

		private CancellationTokenSource _cts;
		private float _saveTimer;
		private int _totalPiecesPlaced;
		private int _totalPiecesAll;
		private GameObject _villageRoot;
		private bool _reconstructMode; // when true, skip delays and place all pieces instantly

		protected override async void OnStart()
		{
			_cts = new CancellationTokenSource();
			ReservationManager.SetScene( Scene );

			// Force build speed — 0.5s per brick lay with LAY animation.
			BuildInterval = 0.5f;
			// Clamp to minimum 0.01s to prevent engine stalls from spawning
			// thousands of GameObjects per second when BuildInterval is set to 0.
			if ( BuildInterval < 0.01f )
				BuildInterval = 0.01f;
			if ( SaveInterval > 120f )
			{
				Log.Info( $"Lute: VillageBuilder overriding SaveInterval {SaveInterval}s → 60s (fast mode)" );
				SaveInterval = 60f;
			}

			// Create village root GameObject (only builder 0 creates it; others reuse it)
			if ( BuilderId == 0 )
			{
				// Clear static state for a fresh play session
				_sharedTasks = null;
				_sharedVillageRoot = null;

				_villageRoot = Scene.CreateObject( true );
				_villageRoot.Name = "MedievalVillage";
				_villageRoot.WorldPosition = Center;
				_sharedVillageRoot = _villageRoot;

				// Generate village layout (only builder 0 generates; others share)
				var rng = VillageSeed > 0 ? new Random( VillageSeed ) : new Random();
				var grammar = new VillageGrammar( rng );
				Tasks = grammar.GenerateLayout( Center );
				_sharedTasks = Tasks;
			}
			else
			{
				// Wait for builder 0 to generate the shared task list
				while ( _sharedTasks is null )
				{
					await Task.DelaySeconds( 0.1f );
				}
				Tasks = _sharedTasks;
				_villageRoot = _sharedVillageRoot;
			}

			// Count total pieces estimate
			_totalPiecesAll = EstimateTotalPieces( Tasks );

			// Load save if it exists and not a fresh build
			if ( !FreshBuild && VillagePersistence.HasSave() )
			{
				var save = VillagePersistence.LoadProgress();
				if ( save != null )
				{
					VillagePersistence.ApplyProgress( Tasks, save );
					ElapsedTime = save.ElapsedTime;
					int completed = Tasks.Count( t => t.Status == 2 );
					int inProgress = Tasks.Count( t => t.Status == 1 );
					Log.Info( $"Lute: VillageBuilder loaded save — {completed}/{Tasks.Count} tasks complete, {inProgress} in progress, elapsed={ElapsedTime/60:F1} min." );

					// Reconstruct geometry for completed tasks instantly (no delay).
					// Runtime-spawned objects don't survive scene reload, so we must
					// re-place all pieces for tasks already marked complete.
					if ( completed > 0 )
					{
						bool didReconstruct = ReconstructCompletedTasks();
						if ( didReconstruct )
							Log.Info( $"Lute: VillageBuilder reconstructed geometry for {completed} completed tasks." );
					}
				}
			}
			else if ( FreshBuild )
			{
				VillagePersistence.ClearSave();
				Log.Info( "Lute: VillageBuilder fresh build — cleared existing save." );
			}

			int pending = Tasks.Count( t => t.Status == 0 );
			int done = Tasks.Count( t => t.Status == 2 );
			Log.Info( $"Lute: VillageBuilder started — {Tasks.Count} tasks total, {done} complete, {pending} pending. Estimated ~{_totalPiecesAll} pieces at {BuildInterval}s/piece = {_totalPiecesAll * BuildInterval / 3600:F1} hours." );

			// In multi-builder mode, partition tasks across builders by
			// estimated piece count (balanced deal, biggest-first).
			if ( TotalBuilders > 1 )
				PartitionTasks();

			// NOTE: ConstructionDirector.Reset() is now called once at
			// LuteWorld.Build() entry, BEFORE the static occupancy scan.
			// Do NOT reset here — it would wipe the scanned occupancy.

			// Register this builder and its tasks with the
			// ConstructionDirector so scheduling, reservations, and
			// dependency tracking go through one authoritative system.
			// Only builder 0 registers the shared task list (all builders
			// share the same Tasks reference), to avoid duplicate
			// registration. Each builder registers itself.
			RegisterWithDirector();

			_ = BuildLoop( _cts.Token );
		}

		/// <summary>
		/// Register this builder (and, for builder 0, the shared task
		/// list) with the <see cref="ConstructionDirector"/>. After this,
		/// <see cref="BuildLoop"/> asks the Director for the next task
		/// instead of iterating the shared list directly.
		///
		/// The NPC name used for the Director and SpatialBlackboard
		/// claims matches the name the spawner assigns to the
		/// controller (e.g. "VillageBuilderNPC_0") so claims and
		/// position updates use the same identity.
		/// </summary>
		void RegisterWithDirector()
		{
			var npcName = DirectorNpcName();
			ConstructionDirector.RegisterBuilder( BuilderId, npcName );

			// Only builder 0 registers the shared task list (all builders
			// share the same Tasks reference). Other builders would just
			// re-register the same tasks under new ids.
			if ( BuilderId == 0 )
			{
				foreach ( var t in Tasks )
				{
					var taskId = ConstructionDirector.RegisterTask( t );
					var directed = ConstructionDirector.GetTask( taskId );
					if ( directed is not null )
						directed.EstimatedPieces = EstimateTaskPieces( t );
				}

				Log.Info( $"Lute: VillageBuilder[{BuilderId}/{TotalBuilders}] registered {Tasks.Count} tasks with ConstructionDirector." );
			}

			// Rebalance task assignments every time a builder registers,
			// so tasks are distributed across all active builders rather
			// than only the first one to register.
			ConstructionDirector.AssignTasks();
		}

		/// <summary>
		/// Derive the NPC name the same way the spawner does, so the
		/// Director and SpatialBlackboard use the same identity as the
		/// VillageBuilderController. The spawner uses
		/// "VillageBuilderNPC" (or "VillageBuilderNPC_{i}" in
		/// multi-builder mode) as the controller's NpcName.
		/// </summary>
		string DirectorNpcName()
		{
			// Match NPCSpawner.SpawnVillageBuilder naming:
			//   count > 1 -> "{name}_{i}"  where name = "VillageBuilderNPC"
			//   count = 1 -> "VillageBuilderNPC"
			return TotalBuilders > 1
				? $"VillageBuilderNPC_{BuilderId}"
				: "VillageBuilderNPC";
		}

		protected override void OnDestroy()
		{
			_cts?.Cancel();
		}

		protected override void OnUpdate()
		{
			// Check for MCP/editor-requested wall finalization
			if ( !string.IsNullOrEmpty( RequestFinalizeWall ) )
			{
				var taskName = RequestFinalizeWall;
				RequestFinalizeWall = ""; // Clear before processing to avoid loops
				var task = Tasks?.Find( t => t.Name == taskName );
				if ( task != null )
				{
					Log.Info( $"Lute: MCP requested FinalizeWall('{taskName}')." );
					FinalizeWall( task );
				}
				else
				{
					Log.Warning( $"Lute: MCP requested FinalizeWall('{taskName}') but task not found." );
				}
			}

		// Check for MCP/editor-requested wall deconstruction
		if ( !string.IsNullOrEmpty( RequestDeconstructWall ) )
		{
			var taskName = RequestDeconstructWall;
			RequestDeconstructWall = ""; // Clear before processing to avoid loops
			var task = Tasks?.Find( t => t.Name == taskName );
			if ( task != null )
			{
				Log.Info( $"Lute: MCP requested DeconstructWall('{taskName}')." );
				DeconstructWall( task );
			}
			else
			{
				Log.Warning( $"Lute: MCP requested DeconstructWall('{taskName}') but task not found." );
			}
		}
	}

		/// <summary> Cancel the build (e.g. NPC reassigned or destroyed). </summary>
		public void CancelBuild()
		{
			_cts?.Cancel();
		}

		int EstimateWallPieces()
		{
			int bricksPerEvenRow = (int)MathF.Round( WallSegmentLength / BrickModuleX );
			int bricksPerOddRow = bricksPerEvenRow + 1; // half + (N-1) full + half = N+1 pieces
			int numRows = (int)MathF.Round( WallHeight / BrickModuleZ );
			int numWythes = (int)MathF.Round( WallThickness / BrickModuleY );
			int evenRows = (numRows + 1) / 2;
			int oddRows = numRows / 2;
			return (bricksPerEvenRow * evenRows + bricksPerOddRow * oddRows) * numWythes;
		}

		int EstimateTaskPieces( VillageBuildTask task )
		{
			if ( task is null ) return 0;
			return task.TaskType switch
			{
				"wall" => EstimateWallPieces(),
				"gate" => 30,
				"road" => 8,
				"well" => 20,
				"market_square" => 25,
				_ => EstimateBuildingPieces( task ),
			};
		}

		int EstimateTotalPieces( List<VillageBuildTask> tasks )
		{
			int total = 0;
			foreach ( var t in tasks )
				total += EstimateTaskPieces( t );
			return total;
		}

		int EstimateBuildingPieces( VillageBuildTask task )
		{
			int w = (int)( task.BaseWidth * task.WealthFactor );
			int h = (int)( task.BaseHeight * task.WealthFactor );
			return w * h; // rough: perimeter + interior + floor
		}

		/// <summary>
		/// Partition tasks across builders for balanced load. Estimates
		/// piece counts, sorts tasks by size (descending), then deals them
		/// round-robin to builders like dealing cards. This spreads the
		/// biggest tasks (Chapel, Tavern) across different builders
		/// instead of stacking them on one. Only runs in multi-builder mode.
		/// </summary>
		void PartitionTasks()
		{
			// Estimate piece count for each task
			var estimates = new List<(int idx, int pieces)>();
			for ( int i = 0; i < Tasks.Count; i++ )
				estimates.Add( (i, EstimateTaskPieces( Tasks[i] )) );

			// Sort by piece count descending (biggest first)
			estimates.Sort( (a, b) => b.pieces.CompareTo( a.pieces ) );

			// Deal round-robin: biggest task to builder 0, next to 1, etc.
			for ( int i = 0; i < estimates.Count; i++ )
			{
				int builderId = i % TotalBuilders;
				Tasks[estimates[i].idx].BuilderAssignment = builderId;
			}

			// Log the partition for verification
			var perBuilder = new int[TotalBuilders];
			for ( int i = 0; i < estimates.Count; i++ )
			{
				int builderId = i % TotalBuilders;
				perBuilder[builderId] += estimates[i].pieces;
			}
			Log.Info( $"Lute: VillageBuilder[{BuilderId}/{TotalBuilders}] partitioned {Tasks.Count} tasks (balanced deal):" );
			for ( int b = 0; b < TotalBuilders; b++ )
				Log.Info( $"  builder {b}: ~{perBuilder[b]} pieces" );
		}

		async Task BuildLoop( CancellationToken token )
		{
			try
			{
				// Director-driven loop: ask the ConstructionDirector for
				// the next task assigned to this builder. Falls back to
				// the legacy index iteration if the Director has no
				// registered tasks (e.g. single-builder mode without
				// registration, or all tasks already claimed).
				bool usedDirector = false;

				while ( true )
				{
					token.ThrowIfCancellationRequested();

					DirectedTask directed = null;
					var npcName = DirectorNpcName();

					// Try the Director first (only if it has tasks).
					if ( ConstructionDirector.AllTasks().Count > 0 )
					{
						directed = ConstructionDirector.ClaimNextTask( BuilderId, npcName );
					}
					else if ( TotalBuilders > 1 && !usedDirector )
					{
						// Multi-builder mode: builder 0 registers the shared task
						// list. If we haven't seen tasks yet, wait briefly for
						// registration before falling back to legacy mode.
						await GameTask.DelaySeconds( 0.5f );
						continue;
					}

					if ( directed != null )
					{
						usedDirector = true;
						var task = directed.BuildTask;
						// Find the index in our shared list (for CurrentTaskIndex).
						int idx = Tasks.IndexOf( task );
						CurrentTask = task;
						CurrentTaskIndex = idx;
						CurrentDirectedTaskId = directed.Id;
						// Director set status to PendingExecution — wait for the NPC
						// controller to arrive at the site and call AuthorizeExecution
						// before we start placing geometry.
						Log.Info( $"Lute: VillageBuilder[{BuilderId}] (director) claimed '{task.Name}' ({task.TaskType}) at {task.Position} — waiting for NPC to arrive." );

						while ( !ConstructionDirector.IsExecutionAuthorized( directed.Id ) )
						{
							token.ThrowIfCancellationRequested();
							await GameTask.DelaySeconds( 0.2f );
						}

						Log.Info( $"Lute: VillageBuilder[{BuilderId}] (director) building '{task.Name}' ({task.TaskType}) at {task.Position} — NPC arrived." );

						try
						{
							await BuildTask( task, token );
						}
						catch ( ConstructionPlacementBlockedException ex )
						{
							Log.Warning( $"Lute: {directed.Id} blocked during placement: {ex.BlockReason}" );
							ConstructionDirector.FailTask( directed.Id, ex.BlockReason );
							CurrentDirectedTaskId = null;
							CurrentTask = null;
							CurrentTaskIndex = -1;
							continue;
						}

						task.Status = 2; // complete
						ConstructionDirector.CompleteTask( directed.Id );
						CurrentDirectedTaskId = null;
						CurrentTask = null;
						CurrentTaskIndex = -1;
						int done = Tasks.Count( t => t.Status == 2 );
						Log.Info( $"Lute: VillageBuilder[{BuilderId}] completed '{task.Name}' — {done}/{Tasks.Count} tasks done ({done * 100 / Tasks.Count}%)." );
						DoSave();
						continue;
					}

					// Director had nothing for us. If we never used the
					// director (no tasks registered), fall back to the
					// legacy index-based loop so single-builder mode and
					// any path that didn't register still works.
					if ( !usedDirector )
					{
						break; // fall through to legacy loop below
					}

					// We used the director and it has no more tasks for us.
					// Check if anything is still pending globally; if so,
					// wait briefly and retry (work-stealing may free up a
					// task). If nothing is pending, we're done.
					int pendingGlobal = ConstructionDirector.AllTasks()
						.Count( t => t.Status == TaskStatus.Pending || t.Status == TaskStatus.Blocked || t.Status == TaskStatus.PendingExecution );
					if ( pendingGlobal == 0 )
						break;

					// If the only remaining tasks are blocked by unsatisfied
					// prerequisites, record the dependency-wait reason so the
					// liveness system can explain the stall deterministically.
					var depBlocked = ConstructionDirector.DependencyBlockedTasks();
					if ( depBlocked.Count > 0 )
					{
						var first = depBlocked[0];
						BuilderLivenessRegistry.SetIdle( npcName,
							BuilderIdleReason.WaitingDependency,
							$"waiting on prerequisite {first.BlockedByDependency} for task {first.Id}",
							waitingOnTaskId: first.BlockedByDependency );
					}

					await GameTask.DelaySeconds( 1.0f );
				}

				// Legacy index-based loop (fallback when the Director was
				// not used). This preserves the original single-builder and
				// pre-Director multi-builder behavior exactly.
				if ( !usedDirector )
				{
					await LegacyBuildLoop( token );
				}

				CurrentTask = null;
				CurrentTaskIndex = -1;
				IsComplete = true;
				int globalDone = Tasks.Count( t => t.Status == 2 );
				if ( TotalBuilders > 1 )
					Log.Info( $"Lute: VillageBuilder[{BuilderId}/{TotalBuilders}] finished its slice. Village: {globalDone}/{Tasks.Count} tasks complete. Pieces: {_totalPiecesPlaced}. Time: {ElapsedTime/3600:F1} hours." );
				else
					Log.Info( $"Lute: VillageBuilder FINISHED — all {Tasks.Count} tasks complete. Total pieces: {_totalPiecesPlaced}. Time: {ElapsedTime/3600:F1} hours." );
				DoSave();
			}
			catch ( OperationCanceledException )
			{
				Log.Info( $"Lute: VillageBuilder cancelled — {Tasks.Count( t => t.Status == 2 )}/{Tasks.Count} tasks complete." );
				DoSave(); // save progress on cancel
			}
		}

		/// <summary>
		/// Legacy index-based build loop. Used as a fallback when the
		/// ConstructionDirector has no registered tasks. This is the
		/// original <see cref="BuildLoop"/> behavior, extracted so the
		/// new director-driven path can coexist.
		/// </summary>
		async Task LegacyBuildLoop( CancellationToken token )
		{
			for ( int idx = 0; idx < Tasks.Count; idx++ )
			{
				var task = Tasks[idx];
				token.ThrowIfCancellationRequested();

				// Multi-builder partitioning: in single-builder mode (TotalBuilders=1)
				// this builder processes every task. In multi-builder mode, each
				// task has a BuilderAssignment set by PartitionTasks (balanced deal
				// by estimated piece count). Skip tasks not assigned to us.
				if ( TotalBuilders > 1 && task.BuilderAssignment != BuilderId )
					continue;

				if ( task.Status == 2 )
					continue; // already complete — skip

				CurrentTask = task;
				CurrentTaskIndex = idx;
				task.Status = 1; // in progress

				Log.Info( $"Lute: VillageBuilder (legacy) starting task '{task.Name}' ({task.TaskType}) at {task.Position}." );

				await BuildTask( task, token );

				task.Status = 2; // complete
				int done = Tasks.Count( t => t.Status == 2 );
				Log.Info( $"Lute: VillageBuilder completed '{task.Name}' — {done}/{Tasks.Count} tasks done ({done * 100 / Tasks.Count}%)." );

				// Save after each task completion
				DoSave();
			}
		}

		/// <summary>
		/// Reconstruct geometry for all completed tasks instantly (no delay).
		/// Called after loading a save — runtime-spawned objects don't survive
		/// scene reload, so we must re-place all pieces for tasks marked complete.
		/// </summary>
		bool ReconstructCompletedTasks()
		{
			// In multi-builder mode, only builder 0 reconstructs geometry.
			// Other builders skip this to avoid re-placing the same pieces.
			if ( TotalBuilders > 1 && BuilderId != 0 )
				return false;

			_reconstructMode = true;
			var dummyToken = CancellationToken.None;
			foreach ( var task in Tasks )
			{
				if ( task.Status != 2 )
					continue;

				// Set CurrentTask so SpawnBox names pieces correctly
				CurrentTask = task;
				CurrentTaskIndex = -1; // not in the build loop

				// Temporarily set PiecesPlaced to 0 so the build methods re-place all pieces,
				// then restore it after.
				int savedPiecesPlaced = task.PiecesPlaced;
				task.PiecesPlaced = 0;
				_ = BuildTask( task, dummyToken );
				task.PiecesPlaced = savedPiecesPlaced;
			}
			CurrentTask = null;
			_reconstructMode = false;
			return true;
		}

		async Task BuildTask( VillageBuildTask task, CancellationToken token )
		{
			switch ( task.TaskType )
			{
				case "wall":
					await BuildWallSegment( task, token );
					break;
				case "gate":
					await BuildGate( task, token );
					break;
				case "road":
					await BuildRoadSection( task, token );
					break;
				case "well":
					await BuildWell( task, token );
					break;
				case "market_square":
					await BuildMarketSquare( task, token );
					break;
				default:
					await BuildBuilding( task, token );
					break;
			}
		}

		// ── Wall segment: individual bricks laid one at a time ──
		//
		// Canonical Lute Masonry Unit v1 geometry contract:
		//   Body:   24.0 × 11.5 × 5.5  cm  (length × depth × height)
		//   Module: 25.0 × 12.5 × 6.25 cm
		//   Wall:   2.0m × 0.5m × 2.0m  (length × depth × height)
		//     = 8 modules long × 4 wythes deep × 32 courses high
		//   Even course: 8 full bricks (centers at -0.875 .. +0.875, step 0.25)
		//   Odd course:  1 half + 7 full + 1 half (9 pieces, fills -1.0 .. +1.0)
		//   Brick centers start at -segLen/2 + moduleX/2 so the first brick's
		//   left face sits exactly on -segLen/2 (no overflow, no edge gap).
		async Task BuildWallSegment( VillageBuildTask task, CancellationToken token )
		{
			// Clamp BuildInterval to prevent engine stalls from spawning
			// thousands of GameObjects per second when set to 0 via MCP.
			if ( BuildInterval < 0.01f )
				BuildInterval = 0.01f;
			float segLen = WallSegmentLength;
			float wallH = WallHeight;
			float wallDepth = WallThickness;
			float brickLen = BrickBodySize.x;
			float brickDepth = BrickBodySize.y;
			float brickH = BrickBodySize.z;

			int modulesX = (int)MathF.Round( segLen / BrickModuleX );
			int numRows = (int)MathF.Round( wallH / BrickModuleZ );
			int numWythes = (int)MathF.Round( wallDepth / BrickModuleY );
			// Even course: modulesX full bricks. Odd course: 1 half + (modulesX-1) full + 1 half = modulesX+1 pieces.
			int evenRows = (numRows + 1) / 2;
			int oddRows = numRows / 2;
			int totalBricks = (modulesX * evenRows + (modulesX + 1) * oddRows) * numWythes;
			task.TotalPieces = totalBricks;

			float cos = (float)Math.Cos( task.Rotation * Math.PI / 180 );
			float sin = (float)Math.Sin( task.Rotation * Math.PI / 180 );

			Vector3 RotateLocal( Vector3 local )
			{
				return task.Position + new Vector3(
					local.x * cos - local.y * sin,
					local.x * sin + local.y * cos,
					local.z );
			}

			int brickIdx = 0;
			// Foundation snap: raycast down at the task center to find the
			// actual ground surface height. Row 0 bricks snap so their
			// bottom face sits exactly on the ground. All other rows use
			// the standard row * BrickModuleZ offset from the snapped base.
			float groundZ = SnapToGround( task.Position );

			// Corner butt helper: returns true if this brick should be
			// skipped (not spawned) because the other wall owns the corner
			// volume on this course. CornerButtCourses: 0=none, 1=even,
			// 2=odd. CornerButtSide: 0=none, 1=left, 2=right.
			bool ShouldButt( bool isLeftEdge, bool isRightEdge, int row )
			{
				if ( task.CornerButtSide == 0 )
					return false;
				if ( task.CornerButtSide == 1 && isLeftEdge ) return true;
				if ( task.CornerButtSide == 2 && isRightEdge ) return true;
				return false;
			}

			// On odd courses with left butt, the first full stretcher (col=1)
			// also overlaps the corner core and must be skipped. With the
			// shared assembly owning the 2x2 core on every course, this skip
			// applies to ALL courses, not just odd ones.
			bool ShouldButtLeftFull( int col, int row )
			{
				return false;
			}

		bool OwnsCornerAssembly( VillageBuildTask wall )
		{
			if ( wall.CornerButtSide == 0 ) return false;
			var nonOwner = FindPerpendicularWall( wall );
			if ( nonOwner == null ) return true; // no partner — own it
			return string.CompareOrdinal( wall.Name, nonOwner.Name ) <= 0;
		}

		VillageBuildTask FindPerpendicularWall( VillageBuildTask wall )
		{
			float bestDist = float.MaxValue;
			VillageBuildTask best = null;
			foreach ( var other in Tasks )
			{
				if ( other == wall || other.TaskType != "wall" ) continue;
				float rotDiff = MathF.Abs( MathF.Abs( other.Rotation - wall.Rotation ) - 90f );
				if ( rotDiff > 1f ) continue;
				float d = Vector3.DistanceBetween( wall.Position, other.Position );
				if ( d < bestDist ) { bestDist = d; best = other; }
			}
			return best;
		}

		// Build the shared corner assembly: 2 full bricks per course
		// covering the 2x2 cell core (0.25m x 0.25m). Orientation alternates
		// A/B by course parity. Uses CornerBondResolver.TryResolveCorner
		// for the geometry, so wall centerlines stay canonical.
		void BuildCornerAssembly( VillageBuildTask wall, int modulesX, int numRows, float groundZ )
		{
			var nonOwner = FindPerpendicularWall( wall );
			if ( nonOwner == null )
			{
				Log.Warning( $"Lute: BuildCornerAssembly('{wall.Name}') — no perpendicular wall found." );
				return;
			}

			// Wall A/B ordering for the resolver: A = the owner (this wall).
			var wallA = wall;
			var wallB = nonOwner;
			string cornerId = $"corner_{wallA.Name}_{wallB.Name}";

			for ( int course = 0; course < numRows; course++ )
			{
				var plan = CornerBondResolver.TryResolveCorner( wallA, wallB, course, cornerId );
				if ( plan == null )
				{
					Log.Warning( $"Lute: BuildCornerAssembly('{wall.Name}') course={course} — resolver returned null." );
					continue;
				}

				float z = groundZ + course * BrickModuleZ;
				if ( course == 0 )
				{
					Log.Info( $"Lute: [corner_diag] wall={wall.Name} junction={plan.Junction} inwardA={plan.InwardA} inwardB={plan.InwardB} placements={plan.Placements.Count}" );
					foreach ( var p in plan.Placements )
						Log.Info( $"Lute: [corner_diag]   center={p.WorldCenter} yaw={p.WorldYaw * 180f / MathF.PI:F1} size={p.WorldSize}" );
				}
				foreach ( var placement in plan.Placements )
				{
					var sp = StructuralPlacement.FromCornerBrick( placement, z + BrickModuleZ * 0.5f, wall.Name, _totalPiecesPlaced );
					SpawnPlacement( sp, WallMaterial, true, _villageRoot, PieceAnchor.Base );
					_totalPiecesPlaced++;
					wall.PlacedBricks.Add( placement.Slot );
				}
			}
			Log.Info( $"Lute: BuildCornerAssembly('{wall.Name}') emitted {numRows} courses x 2 bricks = {numRows * 2} corner bricks (cornerId={cornerId})." );
		}

			// Count how many bricks will be skipped for correct TotalPieces.
			int skipCount = 0;
			for ( int row = 0; row < numRows; row++ )
			{
				bool isOdd = (row % 2 == 1);
				if ( isOdd )
				{
					if ( ShouldButt( true, false, row ) ) skipCount += numWythes;
					if ( ShouldButt( false, true, row ) ) skipCount += numWythes * 2;
					// Left butt on odd courses also skips first full stretcher (col=1)
					if ( ShouldButtLeftFull( 1, row ) ) skipCount += numWythes;
				}
				else
				{
					if ( ShouldButt( true, false, row ) ) skipCount += numWythes;
					if ( ShouldButt( false, true, row ) ) skipCount += numWythes;
				}
			}
			totalBricks -= skipCount;
			task.TotalPieces = totalBricks;
			for ( int wythe = 0; wythe < numWythes; wythe++ )
			{
				float yCenter = -wallDepth * 0.5f + BrickModuleY * 0.5f + wythe * BrickModuleY;

				for ( int row = 0; row < numRows; row++ )
				{
					float z = groundZ + row * BrickModuleZ;
					bool isOdd = (row % 2 == 1);

					if ( isOdd )
					{
						// Odd course: half + (modulesX-1) full + half
						float halfLen = BrickModuleX * 0.5f;
						float lx = -segLen * 0.5f + halfLen * 0.5f;
						bool buttLeftHalf = ShouldButt( true, false, row );
						if ( !buttLeftHalf && brickIdx >= task.PiecesPlaced )
						{
							var pos = RotateLocal( new Vector3( lx, yCenter, z ) );
							SpawnBox( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ),
								WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
							task.PiecesPlaced = brickIdx + 1;
							_totalPiecesPlaced++;
							task.PlacedBricks.Add( BrickSlot.HalfStretcher( 0, wythe, row ) );
						}
						brickIdx++;
						ElapsedTime += BuildInterval;
						if ( !_reconstructMode ) await Task.DelaySeconds( BuildInterval );
						MaybeSave();

						// (modulesX - 1) full bricks — shifted by half a module
						// so vertical joints stagger (running bond). The left
						// half brick occupies the first half-module, so full
						// bricks start at col*moduleX from the left edge.
						for ( int col = 1; col < modulesX; col++ )
						{
							token.ThrowIfCancellationRequested();
							bool buttThisFull = ShouldButt( false, col == modulesX - 1, row ) || ShouldButtLeftFull( col, row );
							if ( buttThisFull )
							{
								brickIdx++;
								ElapsedTime += BuildInterval;
								if ( !_reconstructMode ) await Task.DelaySeconds( BuildInterval );
								MaybeSave();
								continue;
							}
							float x = -segLen * 0.5f + col * BrickModuleX;
							if ( brickIdx >= task.PiecesPlaced )
							{
								var pos = RotateLocal( new Vector3( x, yCenter, z ) );
								SpawnBox( pos, new Vector3( brickLen, brickDepth, brickH ),
									WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
								SpatialRegistry.Register( StructuralPlacement.ForWallBrick( pos, new Vector3( brickLen, brickDepth, brickH ), task.Rotation, task.Name, BrickSlot.Stretcher( col, wythe, row ), _totalPiecesPlaced ) );
								task.PiecesPlaced = brickIdx + 1;
								_totalPiecesPlaced++;
								task.PlacedBricks.Add( BrickSlot.Stretcher( col, wythe, row ) );
							}
							brickIdx++;
							ElapsedTime += BuildInterval;
							if ( !_reconstructMode ) await Task.DelaySeconds( BuildInterval );
							MaybeSave();
						}

						// Right half: center at +segLen/2 - moduleX*0.25
						{
							bool buttRightHalf = ShouldButt( false, true, row );
							float rx = segLen * 0.5f - halfLen * 0.5f;
							if ( !buttRightHalf && brickIdx >= task.PiecesPlaced )
							{
								var pos = RotateLocal( new Vector3( rx, yCenter, z ) );
								SpawnBox( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ),
									WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
								SpatialRegistry.Register( StructuralPlacement.ForWallBrick( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ), task.Rotation, task.Name, BrickSlot.HalfStretcher( modulesX, wythe, row ), _totalPiecesPlaced ) );
								task.PiecesPlaced = brickIdx + 1;
								_totalPiecesPlaced++;
								task.PlacedBricks.Add( BrickSlot.HalfStretcher( modulesX, wythe, row ) );
							}
							brickIdx++;
							ElapsedTime += BuildInterval;
							if ( !_reconstructMode ) await Task.DelaySeconds( BuildInterval );
							MaybeSave();
						}
					}
					else
					{
						// Even course: modulesX full bricks, centers at -segLen/2 + moduleX/2 + col*moduleX
						for ( int col = 0; col < modulesX; col++ )
						{
							token.ThrowIfCancellationRequested();
							bool isLeft = (col == 0);
							bool isRight = (col == modulesX - 1);
							if ( ShouldButt( isLeft, isRight, row ) )
							{
								brickIdx++;
								ElapsedTime += BuildInterval;
								if ( !_reconstructMode ) await Task.DelaySeconds( BuildInterval );
								MaybeSave();
								continue;
							}
							float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
							if ( brickIdx >= task.PiecesPlaced )
							{
								var pos = RotateLocal( new Vector3( x, yCenter, z ) );
								SpawnBox( pos, new Vector3( brickLen, brickDepth, brickH ),
									WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base,
									BrickForm.Full, BrickOrientation.Stretcher );
								SpatialRegistry.Register( StructuralPlacement.ForWallBrick( pos, new Vector3( brickLen, brickDepth, brickH ), task.Rotation, task.Name, BrickSlot.Stretcher( col, wythe, row ), _totalPiecesPlaced ) );
								task.PiecesPlaced = brickIdx + 1;
								_totalPiecesPlaced++;
								task.PlacedBricks.Add( BrickSlot.Stretcher( col, wythe, row ) );
							}
							brickIdx++;
							ElapsedTime += BuildInterval;
							if ( !_reconstructMode ) await Task.DelaySeconds( BuildInterval );
							MaybeSave();
						}
					}
				}
			}

			// Update wall state: check if structurally eligible for finalization.
			if ( task.PiecesPlaced >= totalBricks )
			{
				var query = EvaluateWallStructure( task, modulesX, numRows );
				if ( query.CanDirectorFinalize )
				{
					task.WallState = WallSegmentState.FinalizationEligible;
					Log.Info( $"Lute: Wall segment '{task.Name}' is FinalizationEligible (coverage={query.Coverage:F2}, courses={query.CoursesContinuous}, foundation={query.FoundationSupported})." );
				}
				else
				{
					task.WallState = WallSegmentState.BrickLaying;
					Log.Warning( $"Lute: Wall segment '{task.Name}' NOT eligible: coverage={query.Coverage:F2} foundation={query.FoundationSupported} courses={query.CoursesContinuous} noIllegalGap={query.NoIllegalGap} noPending={query.NoPendingStructuralPieces} placed={task.PiecesPlaced}/{totalBricks} placedBricks={task.PlacedBricks.Count}." );
				}
			}
			else
			{
				task.WallState = WallSegmentState.BrickLaying;
				Log.Warning( $"Lute: Wall segment '{task.Name}' pieces incomplete: placed={task.PiecesPlaced} total={totalBricks}." );
			}

			// Shared corner assembly: emit the 2-brick 2x2 cell core for
			// every course of this wall's corner. Only one wall of the pair
			// emits the assembly (deterministic ownership via name compare);
			// the other wall skips its corner column (ShouldButt) and relies
			// on the owner's assembly to fill the shared core.
			if ( task.CornerButtSide != 0 && OwnsCornerAssembly( task ) )
			{
				BuildCornerAssembly( task, modulesX, numRows, groundZ );
			}

			// Segment-level collider: one BoxCollider sized to the wall's
			// actual world dimensions (segLen × wallDepth × wallH). The
			// BoxCollider component uses Scale in local units where 50 = the
			// native box size, so WorldScale carries the real dimensions.
			{
				var segColliderGo = Scene.CreateObject( false );
				segColliderGo.Name = $"Village_{task.Name}_collider";
				segColliderGo.SetParent( _villageRoot );
				segColliderGo.WorldPosition = task.Position + new Vector3( 0, 0, wallH * 0.5f );
				if ( task.Rotation != 0 ) segColliderGo.WorldRotation = Rotation.FromYaw( task.Rotation );
				segColliderGo.WorldScale = new Vector3( segLen, wallDepth, wallH );
				var segCollider = segColliderGo.AddComponent<BoxCollider>();
				segCollider.Scale = new Vector3( BoxModelNativeSize, BoxModelNativeSize, BoxModelNativeSize );
				segColliderGo.Enabled = true;
			}
		}

	// Wall structural query: NOT brick count, topology-based
	//
	// modulesX is the even-course full-brick count. Odd courses have
	// modulesX+1 slots (col 0..modulesX): 1 half + (modulesX-1) full + 1 half.
	// Even courses have modulesX slots (col 0..modulesX-1): modulesX full.

	WallStructuralQuery EvaluateWallStructure( VillageBuildTask task, int modulesX, int numRows )
	{
		var q = new WallStructuralQuery();
		q.Coverage = task.TotalPieces > 0 ? (float)task.PlacedBricks.Count / task.TotalPieces : 0f;

		// Per-row slot count: odd rows have one extra (the right half).
		int SlotsForRow( int row ) => (row % 2 == 1) ? modulesX + 1 : modulesX;

		// SlotFilled: a slot at (col, wythe=0, row) is filled if it has a
		// Stretcher (full brick) OR a HalfStretcher (half brick at odd-row
		// edges). Odd rows: col 0 and col modulesX are HalfStretcher; cols
		// 1..modulesX-1 are Stretcher. Even rows: all cols are Stretcher.
		bool SlotFilled( int col, int row )
		{
			if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, row ) ) )
				return true;
			if ( task.PlacedBricks.Contains( BrickSlot.HalfStretcher( col, 0, row ) ) )
				return true;
			return false;
		}

		// Foundation: row 0 is even, has modulesX slots.
		int foundationCount = 0;
		for ( int col = 0; col < modulesX; col++ ) { if ( SlotFilled( col, 0 ) ) foundationCount++; }
		q.FoundationSupported = foundationCount == modulesX;

		q.CoursesContinuous = true;
		for ( int row = 0; row < numRows; row++ )
		{
			int slots = SlotsForRow( row );
			bool foundGap = false;
			for ( int col = 0; col < slots; col++ )
			{
				if ( !SlotFilled( col, row ) && !foundGap )
				{
					for ( int c2 = col + 1; c2 < slots; c2++ ) { if ( SlotFilled( c2, row ) ) { foundGap = true; break; } }
				}
			}
			if ( foundGap ) { q.CoursesContinuous = false; break; }
		}

		// Top course: check all slots for the top row.
		int topSlots = SlotsForRow( numRows - 1 );
		int topCount = 0;
		for ( int col = 0; col < topSlots; col++ ) { if ( SlotFilled( col, numRows - 1 ) ) topCount++; }
		q.TopCourseComplete = topCount == topSlots;

		// Corner bonding: every row must have its first and last slot filled.
		q.RequiredCornersBonded = true;
		for ( int row = 0; row < numRows; row++ )
		{
			int slots = SlotsForRow( row );
			if ( !SlotFilled( 0, row ) || !SlotFilled( slots - 1, row ) ) { q.RequiredCornersBonded = false; break; }
		}

		q.NoIllegalGap = q.CoursesContinuous;
		if ( q.NoIllegalGap )
		{
			for ( int row = 1; row < numRows; row++ )
			{
				bool rowHas = false, prevHas = false;
				int slots = SlotsForRow( row );
				int prevSlots = SlotsForRow( row - 1 );
				for ( int col = 0; col < slots; col++ ) { if ( SlotFilled( col, row ) ) rowHas = true; }
				for ( int col = 0; col < prevSlots; col++ ) { if ( SlotFilled( col, row - 1 ) ) prevHas = true; }
				if ( rowHas && !prevHas ) { q.NoIllegalGap = false; break; }
			}
		}
		q.NoPendingStructuralPieces = task.PiecesPlaced >= task.TotalPieces;
		return q;
	}

		public bool FinalizeWall( VillageBuildTask task )
	{
		if ( task.TaskType != "wall" ) return false;
		if ( task.WallState != WallSegmentState.FinalizationEligible ) { Log.Warning( $"Lute: FinalizeWall('{task.Name}') rejected - not eligible." ); return false; }

		// Visually lossless finalization: keep the individual brick_single_04
		// GameObjects as-is. They are already optimized cloud assets with
		// weathered 3D geometry, normal maps, and proper materials.
		// Finalization only adds a segment-level collider and updates state.
		// Do NOT collapse to a procedural mesh — that would downgrade the
		// visual from brick_single_04 to flat boxes with castle_wall.vmat.

		float segLen = WallSegmentLength;
		float wallH = WallHeight;
		float wallDepth = WallThickness;

		// Add a single segment-level BoxCollider for physics.
		var colliderGo = Scene.CreateObject( false );
		colliderGo.Name = $"Village_{task.Name}_collider";
		colliderGo.SetParent( _villageRoot );
		colliderGo.WorldPosition = task.Position + new Vector3( 0, 0, wallH * 0.5f );
		if ( task.Rotation != 0 ) colliderGo.WorldRotation = Rotation.FromYaw( task.Rotation );
		var collider = colliderGo.AddComponent<BoxCollider>();
		collider.Scale = new Vector3( segLen, wallDepth, wallH );
		colliderGo.Enabled = true;
		task.FinalizedMeshGo = colliderGo;

		// Do NOT destroy the individual brick GameObjects — they are the
		// visual representation and remain as-is after finalization.
		task.WallState = WallSegmentState.Finalized;
		Log.Info( $"Lute: FinalizeWall('{task.Name}') - visual lossless (kept brick_single_04 GameObjects, added segment collider). State=Finalized." );
		return true;
	}

		/// <summary>
		/// Add one brick box to the finalized wall mesh. Uses per-brick 0-1 UVs
		/// so each brick maps the full texture exactly once - matching the
		/// construction brick model-local UVs (brick.vmdl is a normalized
		/// 50-unit box with 0-1 UVs). This keeps the visual identical before
		/// and after finalization: same brick, same texture, same mortar edge.
		/// </summary>
		void AddBrickToMesh( List<Vertex> vertices, List<int> indices, float cx, float cy, float cz, float sx, float sy, float sz )
		{
			float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f; int baseIdx = vertices.Count;
			var p = new Vector3[8];
			p[0] = new Vector3( cx - hx, cy - hy, cz ); p[1] = new Vector3( cx + hx, cy - hy, cz );
			p[2] = new Vector3( cx + hx, cy + hy, cz ); p[3] = new Vector3( cx - hx, cy + hy, cz );
			p[4] = new Vector3( cx - hx, cy - hy, cz + sz ); p[5] = new Vector3( cx + hx, cy - hy, cz + sz );
			p[6] = new Vector3( cx + hx, cy + hy, cz + sz ); p[7] = new Vector3( cx - hx, cy + hy, cz + sz );
			// Per-brick 0-1 UVs: each face maps the full texture once, matching
			// the construction brick normalized model UVs (brick.vmdl).
			var uv0 = new Vector4( 0, 0, 0, 0 );
			var uv1 = new Vector4( 1, 0, 0, 0 );
			var uv2 = new Vector4( 1, 1, 0, 0 );
			var uv3 = new Vector4( 0, 1, 0, 0 );
			vertices.Add( new Vertex( p[3], uv0, Color32.White ) ); vertices.Add( new Vertex( p[2], uv1, Color32.White ) ); vertices.Add( new Vertex( p[6], uv2, Color32.White ) ); vertices.Add( new Vertex( p[7], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[1], uv0, Color32.White ) ); vertices.Add( new Vertex( p[0], uv1, Color32.White ) ); vertices.Add( new Vertex( p[4], uv2, Color32.White ) ); vertices.Add( new Vertex( p[5], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[0], uv0, Color32.White ) ); vertices.Add( new Vertex( p[3], uv1, Color32.White ) ); vertices.Add( new Vertex( p[7], uv2, Color32.White ) ); vertices.Add( new Vertex( p[4], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[2], uv0, Color32.White ) ); vertices.Add( new Vertex( p[1], uv1, Color32.White ) ); vertices.Add( new Vertex( p[5], uv2, Color32.White ) ); vertices.Add( new Vertex( p[6], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[7], uv0, Color32.White ) ); vertices.Add( new Vertex( p[6], uv1, Color32.White ) ); vertices.Add( new Vertex( p[5], uv2, Color32.White ) ); vertices.Add( new Vertex( p[4], uv3, Color32.White ) );
			vertices.Add( new Vertex( p[0], uv0, Color32.White ) ); vertices.Add( new Vertex( p[1], uv1, Color32.White ) ); vertices.Add( new Vertex( p[2], uv2, Color32.White ) ); vertices.Add( new Vertex( p[3], uv3, Color32.White ) );
			for ( int face = 0; face < 6; face++ ) { int i = baseIdx + face * 4; indices.Add( i ); indices.Add( i + 1 ); indices.Add( i + 2 ); indices.Add( i ); indices.Add( i + 2 ); indices.Add( i + 3 ); }
		}

		public bool DeconstructWall( VillageBuildTask task )
		{
			if ( task.TaskType != "wall" ) return false;
			if ( task.WallState != WallSegmentState.Finalized ) { Log.Warning( $"Lute: DeconstructWall('{task.Name}') rejected - not Finalized." ); return false; }
			float segLen = WallSegmentLength;
			float wallH = WallHeight;
			float wallDepth = WallThickness;
			float brickLen = BrickBodySize.x;
			float brickDepth = BrickBodySize.y;
			float brickH = BrickBodySize.z;
			int modulesX = (int)MathF.Round( segLen / BrickModuleX );
			int numRows = (int)MathF.Round( wallH / BrickModuleZ );
			int numWythes = (int)MathF.Round( wallDepth / BrickModuleY );
			if ( task.FinalizedMeshGo is not null ) { task.FinalizedMeshGo.Destroy(); task.FinalizedMeshGo = null; }
			int topRow = numRows - 1;
			bool isOdd = (topRow % 2 == 1);
			float halfLen = BrickModuleX * 0.5f;
			float cos = (float)Math.Cos( task.Rotation * Math.PI / 180 );
			float sin = (float)Math.Sin( task.Rotation * Math.PI / 180 );
			float yCenter = -wallDepth * 0.5f + BrickModuleY * 0.5f; // front wythe
			float z = topRow * BrickModuleZ;

			Vector3 RotateLocal( Vector3 local )
			{
				return task.Position + new Vector3(
					local.x * cos - local.y * sin,
					local.x * sin + local.y * cos,
					local.z );
			}

			if ( isOdd )
			{
				// Left half
				var pos = RotateLocal( new Vector3( -segLen * 0.5f + halfLen * 0.5f, yCenter, z ) );
				SpawnBox( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ), WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
				task.PlacedBricks.Remove( BrickSlot.HalfStretcher( 0, 0, topRow ) );
				for ( int col = 1; col < modulesX; col++ )
				{
					float x = -segLen * 0.5f + col * BrickModuleX;
					pos = RotateLocal( new Vector3( x, yCenter, z ) );
				SpawnBox( pos, new Vector3( brickLen, brickDepth, brickH ), WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
					task.PlacedBricks.Remove( BrickSlot.Stretcher( col, 0, topRow ) );
				}
				// Right half
				pos = RotateLocal( new Vector3( segLen * 0.5f - halfLen * 0.5f, yCenter, z ) );
				SpawnBox( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ), WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
				task.PlacedBricks.Remove( BrickSlot.HalfStretcher( modulesX, 0, topRow ) );
			}
			else
			{
				for ( int col = 0; col < modulesX; col++ )
				{
					float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
					var pos = RotateLocal( new Vector3( x, yCenter, z ) );
					SpawnBox( pos, new Vector3( brickLen, brickDepth, brickH ), WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
					task.PlacedBricks.Remove( BrickSlot.Stretcher( col, 0, topRow ) );
				}
			}
			task.WallState = WallSegmentState.Deconstructing;
			Log.Info( $"Lute: DeconstructWall('{task.Name}') - expanded top course. State=Deconstructing." );
			return true;
		}

		// ── Gate: two towers + lintel ──
		async Task BuildGate( VillageBuildTask task, CancellationToken token )
		{
			int pieces = 30;
			task.TotalPieces = pieces;

			float towerW = 4f * M;
			float towerH = 12f * M;
			float gateW = 8f * M;
			float lintelH = 3f * M;

			for ( int i = 0; i < pieces; i++ )
			{
				token.ThrowIfCancellationRequested();

				if ( i >= task.PiecesPlaced )
				{
					// Left tower (0-12), right tower (13-24), lintel (25-29)
					if ( i < 13 )
					{
						int layer = i;
						float z = layer * (towerH / 13f);
						var pos = task.Position + new Vector3( -gateW / 2f - towerW / 2f, 0, z + (towerH / 13f) / 2f );
						SpawnBox( pos, new Vector3( towerW, towerW, towerH / 13f ), GateMaterial, true, _villageRoot, 0f, PieceAnchor.Center );
					}
					else if ( i < 26 )
					{
						int layer = i - 13;
						float z = layer * (towerH / 13f);
						var pos = task.Position + new Vector3( gateW / 2f + towerW / 2f, 0, z + (towerH / 13f) / 2f );
						SpawnBox( pos, new Vector3( towerW, towerW, towerH / 13f ), GateMaterial, true, _villageRoot, 0f, PieceAnchor.Center );
					}
					else
					{
						// Lintel above gate
						var pos = task.Position + new Vector3( 0, 0, towerH + lintelH / 2f );
						SpawnBox( pos, new Vector3( gateW + towerW * 2, 3f * M, lintelH ), GateMaterial, true, _villageRoot, 0f, PieceAnchor.Center );
					}
					task.PiecesPlaced = i + 1;
					_totalPiecesPlaced++;
				}

				ElapsedTime += BuildInterval;
				if ( !_reconstructMode )
					await Task.DelaySeconds( BuildInterval );
				MaybeSave();
			}
		}

		// ── Road section: flat floor tiles ──
		async Task BuildRoadSection( VillageBuildTask task, CancellationToken token )
		{
			int pieces = 8;
			task.TotalPieces = pieces;
			float roadW = 10f * M;
			float segLen = 10f * M;
			float angle = task.Rotation * (float)Math.PI / 180f;
			float cos = (float)Math.Cos( angle );
			float sin = (float)Math.Sin( angle );

			for ( int i = 0; i < pieces; i++ )
			{
				token.ThrowIfCancellationRequested();

				if ( i >= task.PiecesPlaced )
				{
					float offset = (i - (pieces - 1) * 0.5f) * (segLen / pieces);
					// Road Rotation describes the local width axis. Advance tiles along
					// the perpendicular local Y axis so Rotation=0 is N/S and 90 is E/W.
					var pos = task.Position + new Vector3( -offset * sin, offset * cos, 0 );
					SpawnBox( pos, new Vector3( roadW, segLen / pieces, FloorThickness ), FloorMaterial, false, _villageRoot, task.Rotation, PieceAnchor.Top );
					task.PiecesPlaced = i + 1;
					_totalPiecesPlaced++;
				}

				ElapsedTime += BuildInterval;
				if ( !_reconstructMode )
					await Task.DelaySeconds( BuildInterval );
				MaybeSave();
			}
		}

		// ── Well: circular stone wall + water ──
		async Task BuildWell( VillageBuildTask task, CancellationToken token )
		{
			int pieces = 20;
			task.TotalPieces = pieces;
			float wellR = 2f * M;
			float wellH = 3f * M;

			for ( int i = 0; i < pieces; i++ )
			{
				token.ThrowIfCancellationRequested();

				if ( i >= task.PiecesPlaced )
				{
					if ( i < 16 )
					{
						// Stone wall around well (16 segments)
						float angle = i * (360f / 16f) * (float)Math.PI / 180f;
						var pos = task.Position + new Vector3( wellR * (float)Math.Cos( angle ), wellR * (float)Math.Sin( angle ), wellH / 2f );
						SpawnBox( pos, new Vector3( 1f * M, 0.5f * M, wellH ), WallMaterial, true, _villageRoot, 0f, PieceAnchor.Center );
					}
					else
					{
						// Wooden posts + roof (4 posts)
						int post = i - 16;
						float angle = post * 90f * (float)Math.PI / 180f;
						float postR = wellR + 1f * M;
						var pos = task.Position + new Vector3( postR * (float)Math.Cos( angle ), postR * (float)Math.Sin( angle ), 6f * M );
						SpawnBox( pos, new Vector3( 0.5f * M, 0.5f * M, 4f * M ), BuildingWallMaterial, true, _villageRoot, 0f, PieceAnchor.Center );
					}
					task.PiecesPlaced = i + 1;
					_totalPiecesPlaced++;
				}

				ElapsedTime += BuildInterval;
				if ( !_reconstructMode )
					await Task.DelaySeconds( BuildInterval );
				MaybeSave();
			}
		}

		// ── Market square: flat floor tiles ──
		async Task BuildMarketSquare( VillageBuildTask task, CancellationToken token )
		{
			int pieces = 25;
			task.TotalPieces = pieces;
			float sqW = 20f * M;
			float sqH = 20f * M;

			for ( int i = 0; i < pieces; i++ )
			{
				token.ThrowIfCancellationRequested();

				if ( i >= task.PiecesPlaced )
				{
					int col = i % 5;
					int row = i / 5;
					var pos = task.Position + new Vector3( (col - 2) * (sqW / 5), (row - 2) * (sqH / 5), 0 );
					SpawnBox( pos, new Vector3( sqW / 5, sqH / 5, FloorThickness ), FloorMaterial, false, _villageRoot, 0f, PieceAnchor.Top );
					task.PiecesPlaced = i + 1;
					_totalPiecesPlaced++;
				}

				ElapsedTime += BuildInterval;
				if ( !_reconstructMode )
					await Task.DelaySeconds( BuildInterval );
				MaybeSave();
			}
		}

		// ── Building: use StyleGrammar (if style set) or BuildingGrammar, place pieces ──
		async Task BuildBuilding( VillageBuildTask task, CancellationToken token )
		{
			var rng = task.LayoutSeed > 0 ? new Random( task.LayoutSeed ) : new Random();

			// Generate the blueprint: StyleGrammar if a style is assigned, plain BuildingGrammar otherwise
			Blueprint bp;
			if ( task.Style is not null )
			{
				var styleGrammar = new StyleGrammar( task.Style, rng );
				bp = styleGrammar.Generate( task.Position, task.Rotation,
					task.BaseWidth, task.BaseHeight, task.WealthFactor,
					CellSize, WallHeight, FloorThickness );
				Log.Info( $"Lute: VillageBuilder generating '{task.Name}' with style '{task.Style.Name}' — {bp.PieceCount} pieces." );
			}
			else
			{
				var grammar = new BuildingGrammar( rng );
				var layout = grammar.GenerateLayout( task.BaseWidth, task.BaseHeight, task.WealthFactor );
				bp = Blueprint.FromGridLayout( layout, task.Position, task.Rotation,
					CellSize, WallHeight, FloorThickness,
					BuildingWallMaterial, BuildingFloorMaterial );
			}

			task.TotalPieces = bp.Pieces.Count;

			// Record the blueprint id/version on the directed task (if
			// we're building via the ConstructionDirector) so the
			// registry and the director stay in sync.
			if ( CurrentDirectedTaskId != null )
			{
				var directed = ConstructionDirector.GetTask( CurrentDirectedTaskId );
				if ( directed != null )
				{
					directed.BlueprintId = bp.Id;
					directed.BlueprintVersion = bp.Version;
				}
			}

			// Validate the blueprint before placing pieces.
			// In reconstruct mode we skip validation (the blueprint was
			// already validated when first built).
			if ( !_reconstructMode )
				BlueprintValidator.ValidateAndLog( bp, task.Name );

			for ( int i = 0; i < bp.Pieces.Count; i++ )
			{
				token.ThrowIfCancellationRequested();

				if ( i >= task.PiecesPlaced )
				{
					var piece = bp.Pieces[i];
					var pos = task.Position + piece.Position;

					// Apply structure rotation around task position
					if ( task.Rotation != 0 )
					{
						var offset = pos - task.Position;
						var angle = task.Rotation * (float)Math.PI / 180f;
						var rotated = new Vector3(
							offset.x * (float)Math.Cos( angle ) - offset.y * (float)Math.Sin( angle ),
							offset.x * (float)Math.Sin( angle ) + offset.y * (float)Math.Cos( angle ),
							offset.z );
						pos = task.Position + rotated;
					}

					Vector3 size;
					string materialPath;
					bool collides;
					PieceAnchor anchor;

					switch ( piece.PieceType )
					{
						case "WALL":
							size = new Vector3( CellSize, CellSize, WallHeight );
							materialPath = piece.Material ?? BuildingWallMaterial;
							collides = true;
							anchor = PieceAnchor.Base;
							break;
						case "DOOR":
							size = new Vector3( CellSize, CellSize, FloorThickness * 2f );
							materialPath = piece.Material ?? BuildingWallMaterial;
							collides = true;
							anchor = PieceAnchor.Base;
							break;
						case "ROOF":
							size = new Vector3( CellSize, CellSize, FloorThickness );
							materialPath = piece.Material ?? BuildingFloorMaterial;
							collides = false;
							anchor = PieceAnchor.Top;
							break;
						case "COLUMN":
							size = piece.Size.Length > 0 ? piece.Size : new Vector3( CellSize * 0.3f, CellSize * 0.3f, WallHeight );
							materialPath = piece.Material ?? BuildingWallMaterial;
							collides = true;
							anchor = PieceAnchor.Base;
							break;
						case "FLOOR":
						default:
							size = new Vector3( CellSize, CellSize, FloorThickness );
							materialPath = piece.Material ?? BuildingFloorMaterial;
							collides = false;
							anchor = PieceAnchor.Top;
							break;
					}

					SpawnBox( pos, size, materialPath, collides, _villageRoot, 0f, anchor );
					task.PiecesPlaced = i + 1;
					_totalPiecesPlaced++;
				}

				ElapsedTime += BuildInterval;
				if ( !_reconstructMode )
					await Task.DelaySeconds( BuildInterval );
				MaybeSave();
			}
		}

		// ── Helper: snap foundation to ground ──
	// Raycasts downward from the task position to find the actual ground
	// surface height. Returns the z value where the bottom of row 0 should
	// sit so the foundation rests on the ground instead of floating.
	// Filters out market/plaza floors and wall pieces so only the real
	// ground (WorldGround or terrain) is used. If no ground is hit,
	// returns a small negative offset so the foundation overlaps the
	// ground surface slightly (eliminates visible gap from model offset).
	float SnapToGround( Vector3 taskPos )
	{
		// Raycast from well above the task position straight down.
		var start = taskPos + new Vector3( 0, 0, 10000 );
		var end = taskPos - new Vector3( 0, 0, 10000 );
		var tr = Scene.Trace.Ray( start, end ).Run();

		if ( tr.Hit && tr.GameObject is not null )
		{
			// Skip market/plaza floors and wall pieces — we want the
			// actual ground (WorldGround, terrain, or similar).
			var name = tr.GameObject.Name ?? "";
			if ( name.Contains( "Plaza" ) || name.Contains( "Market" ) || name.Contains( "Wall" ) || name.Contains( "Floor" ) || name.Contains( "Inner" ) )
			{
				Log.Info( $"Lute: SnapToGround({taskPos}) skipped '{name}' at z={tr.HitPosition.z:F3}, using ground offset." );
				// Fall through to ground offset below
			}
			else
			{
				// Subtract a small overlap so the brick bottom sinks
				// slightly into the ground, eliminating any visible gap
				// caused by model surface vs collider offset.
				float hitZ = tr.HitPosition.z - BrickModuleZ * 0.5f;
				Log.Info( $"Lute: SnapToGround({taskPos}) hit ground '{name}' at z={tr.HitPosition.z:F3}, snapped to z={hitZ:F3}." );
				return hitZ;
			}
		}

		// No ground hit (or skipped) — the WorldGround top surface is at
		// z=0. Sink the foundation by a tiny fraction of a brick module
		// to eliminate the rendering seam at z=0 (Z-fighting between the
		// brick bottom face and the ground top face).
		float groundZ = taskPos.z - BrickModuleZ * 0.5f;
		Log.Info( $"Lute: SnapToGround({taskPos}) no ground hit, using z={groundZ:F3} (seam offset from {taskPos.z:F3})." );
		return groundZ;
	}

	// ── Helper: spawn a box mesh piece ──
		void SpawnBox( Vector3 worldPos, Vector3 size, string materialPath, bool collides, GameObject parent, float yaw = 0f, PieceAnchor anchor = PieceAnchor.Center,
		BrickForm form = BrickForm.Full, BrickOrientation orientation = BrickOrientation.Stretcher, float? overrideLength = null )
		{
			// When overrideLength is set, the caller (e.g. corner assembly)
			// is authoritative — size is the exact render size, not the
			// form-based module size. This avoids the dual-truth mismatch
			// between placement.WorldSize and form-based scaling.
			var isWallBrick = CurrentTask is not null && CurrentTask.TaskType == "wall";
			var isExactSize = overrideLength.HasValue;
			var anchorHeight = isWallBrick ? (isExactSize ? size.z : BrickModuleZ) : size.z;

			switch ( anchor )
			{
				case PieceAnchor.Base:
					worldPos = worldPos.WithZ( worldPos.z + anchorHeight * 0.5f );
					break;
				case PieceAnchor.Top:
					worldPos = worldPos.WithZ( worldPos.z - anchorHeight * 0.5f );
					break;
				case PieceAnchor.Center:
					break;
			}

			// Per-piece occupancy is only checked for non-wall pieces.
			// Wall bricks rely on the task-level reservation (the whole
			// segment bounds are reserved by ConstructionDirector). This
			// avoids 550,000+ dictionary entries and O(n) CanPlace scans.
			var rot = yaw != 0f ? Rotation.FromYaw( yaw ) : Rotation.Identity;
			if ( !_reconstructMode && !isWallBrick && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
			{
				var half = size * 0.5f;
				var pieceBounds = new BBox( -half, half ).Rotate( rot ).Translate( worldPos );

				if ( !ReservationManager.CanPlace( CurrentDirectedTaskId, pieceBounds, out var reason ) )
				{
					// Self-repair: try bounded deterministic corrections before failing
					var repair = ConstructionSelfRepair.TryRecover( worldPos, yaw, size,
						( p, r ) =>
						{
							var h = size * 0.5f;
							var rr = r != 0f ? Rotation.FromYaw( r ) : Rotation.Identity;
							var b = new BBox( -h, h ).Rotate( rr ).Translate( p );
							return ReservationManager.CanPlace( CurrentDirectedTaskId, b, out _ );
						}, reason );
					if ( repair.Success )
					{
						Log.Info( $"Lute: SelfRepair recovered piece at {worldPos} -> {repair.CorrectedPosition} (attempts={repair.AttemptsUsed})" );
						worldPos = repair.CorrectedPosition;
						yaw = repair.CorrectedRotation;
						rot = yaw != 0f ? Rotation.FromYaw( yaw ) : Rotation.Identity;
					}
					else
					{
						Log.Warning( $"Lute: VillageBuilder piece placement blocked at {worldPos}: {reason} (self-repair failed: {repair.FailureType})" );
						throw new ConstructionPlacementBlockedException( $"{CurrentDirectedTaskId} placement blocked at {worldPos}: {reason} (self-repair: {repair.Reason})" );
					}
				}
			}

			var go = Scene.CreateObject( false );
			go.Name = $"Village_{CurrentTask?.Name ?? "piece"}_{_totalPiecesPlaced}";
			go.SetParent( parent );

			go.WorldPosition = worldPos;
			if ( yaw != 0f ) go.WorldRotation = rot;

			// models/dev/box.vmdl is 50x50x50 local units. Convert requested
			// world dimensions into transform scale so rendered bounds match
			// the same size used by occupancy and collision.
			// Wall bricks use a pre-textured brick model (materials/medieval/brick.vmdl)
			// so the stone texture is baked into the model — no MaterialOverride
			// needed (which doesn't render textures correctly on dev/box.vmdl).
			var renderer = go.AddComponent<ModelRenderer>();
			if ( isWallBrick )
			{
				renderer.Model = Cloud.Model( "facepunch.brick_single_04" );
			}
			else
			{
				renderer.Model = Model.Load( "models/dev/box.vmdl" );
			}
			// Scale: dev/box.vmdl is 50x50x50, but cloud brick models have
			// their own native bounds. Scale wall bricks to the MODULE size
			// (which includes mortar gap) so courses fill without blue gaps.
			// Form (Full/Half/Quarter) determines dimensions.
			// Orientation (Stretcher/Header) determines rotation only —
			// the render size is NOT swapped for headers; the +90° yaw
			// handles the visual rotation.
			if ( isWallBrick && renderer.Model is not null )
			{
				var modelSize = renderer.Model.Bounds.Size;
				if ( modelSize.x > 0 && modelSize.y > 0 && modelSize.z > 0 )
				{
					if ( isExactSize )
					{
						// Caller is authoritative — use the exact requested
						// size as the render size (e.g. corner assembly 2-module
						// bricks). This makes placement.WorldSize the single
						// source of truth for special assemblies.
						go.WorldScale = size / modelSize;
					}
					else
					{
						float length = form switch
						{
							BrickForm.Half    => BrickModuleX * 0.5f,
							BrickForm.Quarter => BrickModuleX * 0.25f,
							_                 => BrickModuleX
						};
						var renderSize = new Vector3( length, BrickModuleY, BrickModuleZ );
						go.WorldScale = renderSize / modelSize;
					}
				}
				else
					go.WorldScale = size / BoxModelNativeSize;
			}
			else
			{
				go.WorldScale = size / BoxModelNativeSize;
			}

			// Apply material override only for non-wall pieces (wall bricks
			// already have the texture baked into brick.vmdl).
			if ( !isWallBrick )
			{
				var material = Material.Load( materialPath );
				if ( material is null )
				{
					Log.Warning( $"Lute: SpawnBox material '{materialPath}' failed to load, using default." );
					material = Material.Load( "materials/medieval/castle_wall.vmat" );
				}
				if ( material is not null )
					renderer.MaterialOverride = material;
			}

			// Per-brick colliders are skipped for wall bricks — a single
			// segment-level collider is added when the wall segment completes
			// (see BuildWallSegment). This avoids 550,000+ physics bodies.
			// Non-wall pieces (gates, wells, buildings) keep per-piece colliders.
			if ( collides && !isWallBrick )
			{
				var collider = go.AddComponent<BoxCollider>();
				collider.Scale = new Vector3( BoxModelNativeSize, BoxModelNativeSize, BoxModelNativeSize );
			}

			go.Enabled = true;

			// Commit per-piece occupancy only for non-wall pieces.
			if ( !_reconstructMode && !isWallBrick && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
			{
				var half = size * 0.5f;
				var pieceBounds = new BBox( -half, half ).Rotate( rot ).Translate( worldPos );
				ReservationManager.CommitPlacement( CurrentDirectedTaskId, pieceBounds, go.Name );
			}
		}

		/// <summary>
		/// Spawn a structural placement using its authoritative Size as
		/// the exact render size. This is the preferred entry point for
		/// special assemblies (corner bricks, custom pieces) where the
		/// placement's Size is the single source of truth — no form-based
		/// scaling or overrideLength needed.
		/// </summary>
		void SpawnPlacement( StructuralPlacement placement, string materialPath, bool collides, GameObject parent, PieceAnchor anchor = PieceAnchor.Base )
		{
			var form = placement.GridSlot?.Form ?? BrickForm.Full;
			var orientation = placement.GridSlot?.Orientation ?? BrickOrientation.Stretcher;
			SpawnBox( placement.Position, placement.Size,
				materialPath, collides, parent, placement.Yaw, anchor,
				form, orientation, placement.Size.x ); // overrideLength = exact size
			// Register in the spatial registry for NPC queries
			SpatialRegistry.Register( placement );
		}

		/// <summary>
		/// Spawn a brick-pattern wall piece. Uses BrickMeshBuilder to create
		/// proper running-bond brick geometry instead of a flat box.
		/// </summary>
		void SpawnBrickBox( Vector3 worldPos, Vector3 size, string materialPath, bool collides, GameObject parent, float yaw = 0f )
		{
			// Walls: base at z=0 (lift center)
			worldPos = worldPos.WithZ( worldPos.z + size.z * 0.5f );

			var half = size * 0.5f;
			var rot = yaw != 0f ? Rotation.FromYaw( yaw ) : Rotation.Identity;
			var pieceBounds = new BBox( -half, half ).Rotate( rot ).Translate( worldPos );

			if ( !_reconstructMode && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
			{
				if ( !ReservationManager.CanPlace( CurrentDirectedTaskId, pieceBounds, out var reason ) )
				{
					// Self-repair: try bounded deterministic corrections before failing
					var repair = ConstructionSelfRepair.TryRecover( worldPos, yaw, size,
						( p, r ) =>
						{
							var h = size * 0.5f;
							var rr = r != 0f ? Rotation.FromYaw( r ) : Rotation.Identity;
							var b = new BBox( -h, h ).Rotate( rr ).Translate( p );
							return ReservationManager.CanPlace( CurrentDirectedTaskId, b, out _ );
						}, reason );
					if ( repair.Success )
					{
						Log.Info( $"Lute: SelfRepair recovered brick at {worldPos} -> {repair.CorrectedPosition} (attempts={repair.AttemptsUsed})" );
						worldPos = repair.CorrectedPosition;
						yaw = repair.CorrectedRotation;
						rot = yaw != 0f ? Rotation.FromYaw( yaw ) : Rotation.Identity;
					}
					else
					{
						Log.Warning( $"Lute: VillageBuilder brick placement blocked at {worldPos}: {reason} (self-repair failed: {repair.FailureType})" );
						throw new ConstructionPlacementBlockedException( $"{CurrentDirectedTaskId} placement blocked at {worldPos}: {reason} (self-repair: {repair.Reason})" );
					}
				}
			}

			var go = Scene.CreateObject( false );
			go.Name = $"Village_brick_{CurrentTask?.Name ?? "piece"}_{_totalPiecesPlaced}";
			go.SetParent( parent );
			go.WorldPosition = worldPos;
			if ( yaw != 0f ) go.WorldRotation = rot;

			var meshComp = go.AddComponent<MeshComponent>();
			meshComp.Collision = collides
				? MeshComponent.CollisionType.Mesh
				: MeshComponent.CollisionType.None;

			var material = Material.Load( materialPath );
			if ( material is null )
			{
				Log.Warning( $"Lute: Brick material '{materialPath}' failed to load, falling back to stone_wall" );
				material = Material.Load( "materials/medieval/castle_wall.vmat" );
			}
			var mesh = BrickMeshBuilder.BuildBrickWall( size, material );
			meshComp.Mesh = mesh;
			go.Enabled = true;

			if ( !_reconstructMode && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
				ReservationManager.CommitPlacement( CurrentDirectedTaskId, pieceBounds, go.Name );
		}

		// ── Save management ──

		void MaybeSave()
		{
			_saveTimer += BuildInterval;
			if ( _saveTimer >= SaveInterval )
			{
				_saveTimer = 0;
				DoSave();
			}
		}

		void DoSave()
		{
			// Only builder 0 saves — it owns the authoritative shared task list.
			// Other builders would save stale copies of the same shared list.
			if ( BuilderId != 0 )
				return;

			bool saved = VillagePersistence.SaveProgress( Center, Tasks, ElapsedTime );
			if ( saved )
			{
				int done = Tasks.Count( t => t.Status == 2 );
				Log.Info( $"Lute: VillageBuilder saved — {done}/{Tasks.Count} tasks complete, {_totalPiecesPlaced} pieces, elapsed={ElapsedTime/60:F1} min." );
			}
		}

		/// <summary>
		/// Set the build interval (seconds per piece) on all VillageBuilder
		/// instances. Use 0.01 for fast acceleration, 0.5 for normal pace.
		/// </summary>
		[ConCmd( "village_set_interval" )]
		public static void SetBuildIntervalCommand( float interval )
		{
			int count = 0;
			foreach ( var vb in Game.ActiveScene.GetAllComponents<VillageBuilder>() )
			{
				vb.BuildInterval = MathF.Max( interval, 0.01f );
				count++;
			}
			Log.Info( $"Lute: village_set_interval {interval} applied to {count} builder(s)." );
		}
	}
}
