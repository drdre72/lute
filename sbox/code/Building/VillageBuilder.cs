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

		/// <summary> Seconds between placed pieces. 1s = ~73-minute pace. </summary>
		[Property] public float BuildInterval { get; set; } = 0.5f;

		/// <summary> Seconds between save checks. </summary>
		[Property] public float SaveInterval { get; set; } = 60f; // 1 minute

		/// <summary> World center of the village. </summary>
		[Property] public Vector3 Center { get; set; } = new Vector3( 5000, 5000, 0 );

		/// <summary> Grid cell size (inches). 100in ~= 2.54m. </summary>
		[Property] public float CellSize { get; set; } = 100f;

		/// <summary> Wall height (inches). </summary>
		[Property] public float WallHeight { get; set; } = 200f;

		/// <summary> Floor thickness (inches). </summary>
		[Property] public float FloorThickness { get; set; } = 10f;

		/// <summary> Material for walls. </summary>
		[Property] public string WallMaterial { get; set; } = "materials/medieval/brick_wall.vmat";

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

		/// <summary> All tasks in the village (sorted by priority). </summary>
		public List<VillageBuildTask> Tasks { get; private set; } = new();

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

		protected override void OnStart()
		{
			_cts = new CancellationTokenSource();

			// Force build speed — 0.5s per brick lay with LAY animation.
			BuildInterval = 0.5f;
			if ( SaveInterval > 120f )
			{
				Log.Info( $"Lute: VillageBuilder overriding SaveInterval {SaveInterval}s → 60s (fast mode)" );
				SaveInterval = 60f;
			}

			// Create village root GameObject
			_villageRoot = Scene.CreateObject( true );
			_villageRoot.Name = "MedievalVillage";
			_villageRoot.WorldPosition = Center;

			// Generate village layout
			var rng = VillageSeed > 0 ? new Random( VillageSeed ) : new Random();
			var grammar = new VillageGrammar( rng );
			Tasks = grammar.GenerateLayout( Center );

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
					ConstructionDirector.RegisterTask( t );

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

		/// <summary> Cancel the build (e.g. NPC reassigned or destroyed). </summary>
		public void CancelBuild()
		{
			_cts?.Cancel();
		}

		int EstimateTotalPieces( List<VillageBuildTask> tasks )
		{
			int total = 0;
			foreach ( var t in tasks )
			{
				total += t.TaskType switch
				{
					"wall" => 12,          // 10m wall segment: ~12 pieces
					"gate" => 30,           // gate with towers: ~30 pieces
					"road" => 8,            // road section: ~8 pieces
					"well" => 20,           // well: ~20 pieces
					"market_square" => 25,  // market square: ~25 pieces
					_ => EstimateBuildingPieces( t ), // buildings
				};
			}
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
			{
				int pieces = Tasks[i].TaskType switch
				{
					"wall" => 12,
					"gate" => 30,
					"road" => 8,
					"well" => 20,
					"market_square" => 25,
					_ => EstimateBuildingPieces( Tasks[i] ),
				};
				estimates.Add( (i, pieces) );
			}

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
			foreach ( var e in estimates )
				perBuilder[e.idx % TotalBuilders] += e.pieces;
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
						// Director set status to PendingExecution � wait for the NPC
						// controller to arrive at the site and call AuthorizeExecution
						// before we start placing geometry.
						Log.Info( $"Lute: VillageBuilder[{BuilderId}] (director) claimed '{task.Name}' ({task.TaskType}) at {task.Position} � waiting for NPC to arrive." );

						while ( !ConstructionDirector.IsExecutionAuthorized( directed.Id ) )
						{
							token.ThrowIfCancellationRequested();
							await GameTask.DelaySeconds( 0.2f );
						}

						Log.Info( $"Lute: VillageBuilder[{BuilderId}] (director) building '{task.Name}' ({task.TaskType}) at {task.Position} � NPC arrived." );

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

		// ── Wall segment: a row of wall pieces ──
		async Task BuildWallSegment( VillageBuildTask task, CancellationToken token )
		{
			float segLen = 10f * M;
			int pieces = 12; // 10m wall, ~0.83m per piece
			task.TotalPieces = pieces;

			for ( int i = 0; i < pieces; i++ )
			{
				token.ThrowIfCancellationRequested();

				if ( i >= task.PiecesPlaced )
				{
					float offset = (i - pieces / 2f) * (segLen / pieces);
					var pos = task.Position + new Vector3( offset * (float)Math.Cos( task.Rotation * Math.PI / 180 ),
														   offset * (float)Math.Sin( task.Rotation * Math.PI / 180 ),
														   0 );
					SpawnBox( pos, new Vector3( segLen / pieces, 3f * M, WallHeight ), WallMaterial, true, _villageRoot, task.Rotation );
					task.PiecesPlaced = i + 1;
					_totalPiecesPlaced++;
				}

				ElapsedTime += BuildInterval;
				if ( !_reconstructMode )
					await Task.DelaySeconds( BuildInterval );
				MaybeSave();
			}
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
						SpawnBox( pos, new Vector3( towerW, towerW, towerH / 13f ), GateMaterial, true, _villageRoot );
					}
					else if ( i < 26 )
					{
						int layer = i - 13;
						float z = layer * (towerH / 13f);
						var pos = task.Position + new Vector3( gateW / 2f + towerW / 2f, 0, z + (towerH / 13f) / 2f );
						SpawnBox( pos, new Vector3( towerW, towerW, towerH / 13f ), GateMaterial, true, _villageRoot );
					}
					else
					{
						// Lintel above gate
						var pos = task.Position + new Vector3( 0, 0, towerH + lintelH / 2f );
						SpawnBox( pos, new Vector3( gateW + towerW * 2, 3f * M, lintelH ), GateMaterial, true, _villageRoot );
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

			for ( int i = 0; i < pieces; i++ )
			{
				token.ThrowIfCancellationRequested();

				if ( i >= task.PiecesPlaced )
				{
					float offset = (i - pieces / 2f) * (segLen / pieces);
					var pos = task.Position + new Vector3( offset * (float)Math.Cos( task.Rotation * Math.PI / 180 ),
														   offset * (float)Math.Sin( task.Rotation * Math.PI / 180 ),
														   0 );
					SpawnBox( pos, new Vector3( roadW, segLen / pieces, FloorThickness ), FloorMaterial, false, _villageRoot, task.Rotation );
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
						SpawnBox( pos, new Vector3( 1f * M, 0.5f * M, wellH ), WallMaterial, true, _villageRoot );
					}
					else
					{
						// Wooden posts + roof (4 posts)
						int post = i - 16;
						float angle = post * 90f * (float)Math.PI / 180f;
						float postR = wellR + 1f * M;
						var pos = task.Position + new Vector3( postR * (float)Math.Cos( angle ), postR * (float)Math.Sin( angle ), 6f * M );
						SpawnBox( pos, new Vector3( 0.5f * M, 0.5f * M, 4f * M ), BuildingWallMaterial, true, _villageRoot );
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
					SpawnBox( pos, new Vector3( sqW / 5, sqH / 5, FloorThickness ), FloorMaterial, false, _villageRoot );
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

					switch ( piece.PieceType )
					{
						case "WALL":
							size = new Vector3( CellSize, CellSize, WallHeight );
							materialPath = piece.Material ?? BuildingWallMaterial;
							collides = true;
							break;
						case "DOOR":
							size = new Vector3( CellSize, CellSize, FloorThickness * 2f );
							materialPath = piece.Material ?? BuildingWallMaterial;
							collides = true;
							break;
						case "ROOF":
							size = new Vector3( CellSize, CellSize, FloorThickness );
							materialPath = piece.Material ?? BuildingFloorMaterial;
							collides = false;
							break;
						case "COLUMN":
							size = piece.Size.Length > 0 ? piece.Size : new Vector3( CellSize * 0.3f, CellSize * 0.3f, WallHeight );
							materialPath = piece.Material ?? BuildingWallMaterial;
							collides = true;
							break;
						case "FLOOR":
						default:
							size = new Vector3( CellSize, CellSize, FloorThickness );
							materialPath = piece.Material ?? BuildingFloorMaterial;
							collides = false;
							break;
					}

					SpawnBox( pos, size, materialPath, collides, _villageRoot );
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
		void SpawnBox( Vector3 worldPos, Vector3 size, string materialPath, bool collides, GameObject parent, float yaw = 0f )
		{
			// Walls: base at z=0 (lift center). Floors: top at z=0 (lower center).
			if ( size.z > FloorThickness * 1.5f )
				worldPos = worldPos.WithZ( worldPos.z + size.z * 0.5f );
			else
				worldPos = worldPos.WithZ( worldPos.z - size.z * 0.5f );

			// Piece-level occupancy gate: check that this exact piece's bounds
			// don't conflict with existing occupancy (static geometry or other
			// tasks' pieces). Only for live directed construction - skip during
			// reconstruction mode (re-placing saved pieces).
			var half = size * 0.5f;
			var rot = yaw != 0f ? Rotation.FromYaw( yaw ) : Rotation.Identity;
			var pieceBounds = new BBox( -half, half ).Rotate( rot ).Translate( worldPos );

			if ( !_reconstructMode && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
			{
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

			// Use ModelRenderer with box.vmdl + MaterialOverride — this is the
			// proven approach (market walls use it) that handles UVs correctly.
			var renderer = go.AddComponent<ModelRenderer>();
			renderer.Model = Model.Load( "models/dev/box.vmdl" );
			go.WorldScale = size;  // box.vmdl is 1x1x1, scale to desired size

			var material = Material.Load( materialPath );
			if ( material is null )
			{
				Log.Warning( $"Lute: SpawnBox material '{materialPath}' failed to load, using default." );
				material = Material.Load( "materials/medieval/stone_wall.vmat" );
			}
			if ( material is not null )
			{
				renderer.MaterialOverride = material;
			}

			// Add collision if needed
			if ( collides )
			{
				var collider = go.AddComponent<BoxCollider>();
				collider.Scale = size;
			}

			go.Enabled = true;

			// Commit exact piece bounds to the occupancy ledger so future
			// pieces/tasks can't overlap this one.
			if ( !_reconstructMode && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
			{
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
			material = Material.Load( "materials/medieval/stone_wall.vmat" );
		}
		var mesh = BrickMeshBuilder.BuildBrickWall( size, material );
		meshComp.Mesh = mesh;
		go.Enabled = true;

		if ( !_reconstructMode && !string.IsNullOrEmpty( CurrentDirectedTaskId ) )
		{
			ReservationManager.CommitPlacement( CurrentDirectedTaskId, pieceBounds, go.Name );
		}
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
			bool saved = VillagePersistence.SaveProgress( Center, Tasks, ElapsedTime );
			if ( saved )
			{
				int done = Tasks.Count( t => t.Status == 2 );
				Log.Info( $"Lute: VillageBuilder saved — {done}/{Tasks.Count} tasks complete, {_totalPiecesPlaced} pieces, elapsed={ElapsedTime/60:F1} min." );
			}
		}
	}
}
