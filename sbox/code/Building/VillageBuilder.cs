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

		/// <summary> Seconds between placed pieces. ~6s = 8-hour pace. </summary>
		[Property] public float BuildInterval { get; set; } = 6.0f;

		/// <summary> Seconds between save checks. </summary>
		[Property] public float SaveInterval { get; set; } = 600f; // 10 minutes

		/// <summary> World center of the village. </summary>
		[Property] public Vector3 Center { get; set; } = new Vector3( 5000, 5000, 0 );

		/// <summary> Grid cell size (inches). 100in ~= 2.54m. </summary>
		[Property] public float CellSize { get; set; } = 100f;

		/// <summary> Wall height (inches). </summary>
		[Property] public float WallHeight { get; set; } = 200f;

		/// <summary> Floor thickness (inches). </summary>
		[Property] public float FloorThickness { get; set; } = 10f;

		/// <summary> Material for walls. </summary>
		[Property] public string WallMaterial { get; set; } = "materials/medieval/stone_wall.vmat";

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

		/// <summary> The task currently being built (or null if idle). </summary>
		public VillageBuildTask CurrentTask { get; private set; }

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
						ReconstructCompletedTasks();
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

			_ = BuildLoop( _cts.Token );
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

		async Task BuildLoop( CancellationToken token )
		{
			try
			{
				for ( int idx = 0; idx < Tasks.Count; idx++ )
				{
					var task = Tasks[idx];
					token.ThrowIfCancellationRequested();

					if ( task.Status == 2 )
						continue; // already complete — skip

					CurrentTask = task;
					CurrentTaskIndex = idx;
					task.Status = 1; // in progress

					Log.Info( $"Lute: VillageBuilder starting task '{task.Name}' ({task.TaskType}) at {task.Position}." );

					await BuildTask( task, token );

					task.Status = 2; // complete
					int done = Tasks.Count( t => t.Status == 2 );
					Log.Info( $"Lute: VillageBuilder completed '{task.Name}' — {done}/{Tasks.Count} tasks done ({done * 100 / Tasks.Count}%)." );

					// Save after each task completion
					DoSave();
				}

				CurrentTask = null;
				CurrentTaskIndex = -1;
				IsComplete = true;
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
		/// Reconstruct geometry for all completed tasks instantly (no delay).
		/// Called after loading a save — runtime-spawned objects don't survive
		/// scene reload, so we must re-place all pieces for tasks marked complete.
		/// </summary>
		void ReconstructCompletedTasks()
		{
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
					SpawnBox( pos, new Vector3( segLen / pieces, 3f * M, WallHeight ), WallMaterial, true, _villageRoot );
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
					SpawnBox( pos, new Vector3( roadW / pieces, segLen / pieces, FloorThickness ), FloorMaterial, false, _villageRoot );
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
		void SpawnBox( Vector3 worldPos, Vector3 size, string materialPath, bool collides, GameObject parent )
		{
			var go = Scene.CreateObject( false );
			go.Name = $"Village_{CurrentTask?.Name ?? "piece"}_{_totalPiecesPlaced}";
			go.SetParent( parent );

			// Walls: base at z=0 (lift center). Floors: top at z=0 (lower center).
			if ( size.z > FloorThickness * 1.5f )
				worldPos = worldPos.WithZ( worldPos.z + size.z * 0.5f );
			else
				worldPos = worldPos.WithZ( worldPos.z - size.z * 0.5f );

			go.WorldPosition = worldPos;

			var meshComp = go.AddComponent<MeshComponent>();
			meshComp.Collision = collides
				? MeshComponent.CollisionType.Mesh
				: MeshComponent.CollisionType.None;

			var mesh = new PolygonMesh();
			var half = size * 0.5f;

			var v0 = mesh.AddVertex( new Vector3( -half.x, -half.y, -half.z ) );
			var v1 = mesh.AddVertex( new Vector3(  half.x, -half.y, -half.z ) );
			var v2 = mesh.AddVertex( new Vector3(  half.x,  half.y, -half.z ) );
			var v3 = mesh.AddVertex( new Vector3( -half.x,  half.y, -half.z ) );
			var v4 = mesh.AddVertex( new Vector3( -half.x, -half.y,  half.z ) );
			var v5 = mesh.AddVertex( new Vector3(  half.x, -half.y,  half.z ) );
			var v6 = mesh.AddVertex( new Vector3(  half.x,  half.y,  half.z ) );
			var v7 = mesh.AddVertex( new Vector3( -half.x,  half.y,  half.z ) );

			mesh.AddFace( v0, v3, v2, v1 );
			mesh.AddFace( v4, v5, v6, v7 );
			mesh.AddFace( v0, v1, v5, v4 );
			mesh.AddFace( v1, v2, v6, v5 );
			mesh.AddFace( v2, v3, v7, v6 );
			mesh.AddFace( v3, v0, v4, v7 );

			var material = Material.Load( materialPath );
			if ( material is not null )
			{
				var allFaces = new List<FaceHandle>();
				for ( int f = 0; f < 6; f++ )
					allFaces.Add( mesh.FaceHandleFromIndex( f ) );
				mesh.AssignMaterialToFaces( allFaces, material );
			}

			meshComp.Mesh = mesh;
			go.Enabled = true;
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
