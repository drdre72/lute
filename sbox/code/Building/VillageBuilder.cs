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
		public string CurrentDirectedTaskId { get; private set; }

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

			// Force build speed — 0.5s per brick lay with LAY animation.
			BuildInterval = 0.5f;
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
			for ( int wythe = 0; wythe < numWythes; wythe++ )
			{
				float yCenter = -wallDepth * 0.5f + BrickModuleY * 0.5f + wythe * BrickModuleY;

				for ( int row = 0; row < numRows; row++ )
				{
					float z = row * BrickModuleZ;
					bool isOdd = (row % 2 == 1);

					if ( isOdd )
					{
						// Odd course: half + (modulesX-1) full + half
						float halfLen = BrickModuleX * 0.5f;
						float lx = -segLen * 0.5f + halfLen * 0.5f;
						if ( brickIdx >= task.PiecesPlaced )
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
							float x = -segLen * 0.5f + col * BrickModuleX;
							if ( brickIdx >= task.PiecesPlaced )
							{
								var pos = RotateLocal( new Vector3( x, yCenter, z ) );
								SpawnBox( pos, new Vector3( brickLen, brickDepth, brickH ),
									WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
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
							float rx = segLen * 0.5f - halfLen * 0.5f;
							if ( brickIdx >= task.PiecesPlaced )
							{
								var pos = RotateLocal( new Vector3( rx, yCenter, z ) );
								SpawnBox( pos, new Vector3( brickLen * 0.5f, brickDepth, brickH ),
									WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
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
							float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
							if ( brickIdx >= task.PiecesPlaced )
							{
								var pos = RotateLocal( new Vector3( x, yCenter, z ) );
								SpawnBox( pos, new Vector3( brickLen, brickDepth, brickH ),
									WallMaterial, true, _villageRoot, task.Rotation, PieceAnchor.Base );
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
				}
			}
			else
			{
				task.WallState = WallSegmentState.BrickLaying;
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

		// Foundation: row 0 is even, has modulesX slots.
		int foundationCount = 0;
		for ( int col = 0; col < modulesX; col++ ) { if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, 0 ) ) ) foundationCount++; }
		q.FoundationSupported = foundationCount == modulesX;

		q.CoursesContinuous = true;
		for ( int row = 0; row < numRows; row++ )
		{
			int slots = SlotsForRow( row );
			bool foundGap = false;
			for ( int col = 0; col < slots; col++ )
			{
				if ( !task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, row ) ) && !foundGap )
				{
					for ( int c2 = col + 1; c2 < slots; c2++ ) { if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( c2, 0, row ) ) ) { foundGap = true; break; } }
				}
			}
			if ( foundGap ) { q.CoursesContinuous = false; break; }
		}

		// Top course: check all slots for the top row.
		int topSlots = SlotsForRow( numRows - 1 );
		int topCount = 0;
		for ( int col = 0; col < topSlots; col++ ) { if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, numRows - 1 ) ) ) topCount++; }
		q.TopCourseComplete = topCount == topSlots;

		// Corner bonding: every row must have its first and last slot filled.
		q.RequiredCornersBonded = true;
		for ( int row = 0; row < numRows; row++ )
		{
			int slots = SlotsForRow( row );
			if ( !task.PlacedBricks.Contains( BrickSlot.Stretcher( 0, 0, row ) ) && !task.PlacedBricks.Contains( BrickSlot.HalfStretcher( 0, 0, row ) ) || !task.PlacedBricks.Contains( BrickSlot.Stretcher( slots - 1, 0, row ) ) && !task.PlacedBricks.Contains( BrickSlot.HalfStretcher( slots - 1, 0, row ) ) ) { q.RequiredCornersBonded = false; break; }
		}

		q.NoIllegalGap = q.CoursesContinuous;
		if ( q.NoIllegalGap )
		{
			for ( int row = 1; row < numRows; row++ )
			{
				bool rowHas = false, prevHas = false;
				int slots = SlotsForRow( row );
				int prevSlots = SlotsForRow( row - 1 );
				for ( int col = 0; col < slots; col++ ) { if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, row ) ) ) rowHas = true; }
				for ( int col = 0; col < prevSlots; col++ ) { if ( task.PlacedBricks.Contains( BrickSlot.Stretcher( col, 0, row - 1 ) ) ) prevHas = true; }
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
			float segLen = WallSegmentLength;
			float wallH = WallHeight;
			float wallDepth = WallThickness;
			float brickLen = BrickBodySize.x;
			float brickDepth = BrickBodySize.y;
			float brickH = BrickBodySize.z;
			int modulesX = (int)MathF.Round( segLen / BrickModuleX );
			int numRows = (int)MathF.Round( wallH / BrickModuleZ );
			int numWythes = (int)MathF.Round( wallDepth / BrickModuleY );

			var vertices = new List<Vertex>(); var indices = new List<int>();
			for ( int wythe = 0; wythe < numWythes; wythe++ )
			{
				float yCenter = -wallDepth * 0.5f + BrickModuleY * 0.5f + wythe * BrickModuleY;
				for ( int row = 0; row < numRows; row++ )
				{
					float z = row * BrickModuleZ;
					bool isOdd = (row % 2 == 1);
					float halfLen = BrickModuleX * 0.5f;
					if ( isOdd )
					{
						// Left half
						AddBrickToMesh( vertices, indices, -segLen * 0.5f + halfLen * 0.5f, yCenter, z, brickLen * 0.5f, brickDepth, brickH );
						// (modulesX - 1) full - shifted by half module for running bond
						for ( int col = 1; col < modulesX; col++ )
						{
							float x = -segLen * 0.5f + col * BrickModuleX;
							AddBrickToMesh( vertices, indices, x, yCenter, z, brickLen, brickDepth, brickH );
						}
						// Right half
						AddBrickToMesh( vertices, indices, segLen * 0.5f - halfLen * 0.5f, yCenter, z, brickLen * 0.5f, brickDepth, brickH );
					}
					else
					{
						for ( int col = 0; col < modulesX; col++ )
						{
							float x = -segLen * 0.5f + BrickModuleX * 0.5f + col * BrickModuleX;
							AddBrickToMesh( vertices, indices, x, yCenter, z, brickLen, brickDepth, brickH );
						}
					}
				}
			}

			var mesh = new Mesh(); mesh.Material = Material.Load( WallMaterial );
#pragma warning disable CS0618
			mesh.CreateVertexBuffer( vertices.Count, Vertex.Layout, vertices );
#pragma warning restore CS0618
			mesh.CreateIndexBuffer( indices.Count, indices );
			mesh.Bounds = new BBox( new Vector3( -segLen * 0.5f, -wallDepth * 0.5f, 0 ), new Vector3( segLen * 0.5f, wallDepth * 0.5f, wallH ) );
			var model = Model.Builder.AddMesh( mesh ).Create();
			var wallGo = Scene.CreateObject( false ); wallGo.Name = $"Village_{task.Name}_finalized"; wallGo.SetParent( _villageRoot );
			// Mesh is already built in world-sized local coordinates (bounds span
			// -segLen/2..+segLen/2, -wallDepth/2..+wallDepth/2, 0..wallH). Position
			// the GameObject at the task origin (bottom-center) and leave scale
			// at 1 - do NOT multiply the already-world-sized mesh by segLen again.
			wallGo.WorldPosition = task.Position;
			if ( task.Rotation != 0 ) wallGo.WorldRotation = Rotation.FromYaw( task.Rotation );
			wallGo.WorldScale = Vector3.One;
			var renderer = wallGo.AddComponent<ModelRenderer>(); renderer.Model = model;
			var collider = wallGo.AddComponent<BoxCollider>();
			collider.Scale = new Vector3( BoxModelNativeSize, BoxModelNativeSize, BoxModelNativeSize );
			wallGo.Enabled = true; task.FinalizedMeshGo = wallGo;
			int destroyed = 0;
			foreach ( var child in _villageRoot.Children )
			{
				if ( child.Name != null && child.Name.StartsWith( $"Village_{task.Name}_" ) && !child.Name.EndsWith( "_collider" ) && !child.Name.EndsWith( "_finalized" ) ) { child.Destroy(); destroyed++; }
			}
			foreach ( var child in _villageRoot.Children ) { if ( child.Name == $"Village_{task.Name}_collider" ) { child.Destroy(); break; } }
			task.WallState = WallSegmentState.Finalized;
			Log.Info( $"Lute: FinalizeWall('{task.Name}') - collapsed {destroyed} bricks to 1 mesh. State=Finalized." );
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

		// ── Helper: spawn a box mesh piece ──
		void SpawnBox( Vector3 worldPos, Vector3 size, string materialPath, bool collides, GameObject parent, float yaw = 0f, PieceAnchor anchor = PieceAnchor.Center )
		{
			switch ( anchor )
			{
				case PieceAnchor.Base:
					worldPos = worldPos.WithZ( worldPos.z + size.z * 0.5f );
					break;
				case PieceAnchor.Top:
					worldPos = worldPos.WithZ( worldPos.z - size.z * 0.5f );
					break;
				case PieceAnchor.Center:
					break;
			}

			// Per-piece occupancy is only checked for non-wall pieces.
			// Wall bricks rely on the task-level reservation (the whole
			// segment bounds are reserved by ConstructionDirector). This
			// avoids 550,000+ dictionary entries and O(n) CanPlace scans.
			var isWallBrick = CurrentTask is not null && CurrentTask.TaskType == "wall";
			var rot = yaw != 0f ? Rotation.FromYaw( yaw ) : Rotation.Identity;
			if ( !_reconstructMode && !isWallBrick && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
			{
				var half = size * 0.5f;
				var pieceBounds = new BBox( -half, half ).Rotate( rot ).Translate( worldPos );

				if ( !ReservationManager.CanPlace( CurrentDirectedTaskId, pieceBounds, out var reason ) )
				{
					Log.Warning( $"Lute: VillageBuilder piece placement blocked at {worldPos}: {reason}" );
					throw new ConstructionPlacementBlockedException( $"{CurrentDirectedTaskId} placement blocked at {worldPos}: {reason}" );
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
				renderer.Model = Model.Load( "models/medieval/brick.vmdl" );
			}
			else
			{
				renderer.Model = Model.Load( "models/dev/box.vmdl" );
			}
			go.WorldScale = size / BoxModelNativeSize;

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
					Log.Warning( $"Lute: VillageBuilder brick placement blocked at {worldPos}: {reason}" );
					throw new ConstructionPlacementBlockedException( $"{CurrentDirectedTaskId} placement blocked at {worldPos}: {reason}" );
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
	}
}
