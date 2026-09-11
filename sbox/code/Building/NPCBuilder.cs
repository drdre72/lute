using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HalfEdgeMesh;
using Lute.Building;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// NPC builder component that constructs a room-subdivided building
	/// segment-by-segment using the proven MeshComponent + PolygonMesh
	/// pattern from <see cref="LuteBuilderNpc"/> (render + collision in
	/// one component). A <see cref="CancellationTokenSource"/> tied to the
	/// component lifecycle stops a build in progress cleanly if the NPC is
	/// destroyed, reassigned, or the job system pulls them off the task.
	/// </summary>
	public sealed class NPCBuilder : Component
	{
		/// <summary> Seconds between placed segments. </summary>
		[Property] public float BuildInterval { get; set; } = 0.5f;

		/// <summary> Wealth multiplier — drives layout size and room count. </summary>
		[Property] public float WealthFactor { get; set; } = 1.0f;

		/// <summary> Base layout width (cells) before wealth scaling. </summary>
		[Property] public int BaseWidth { get; set; } = 4;

		/// <summary> Base layout height (cells) before wealth scaling. </summary>
		[Property] public int BaseHeight { get; set; } = 4;

		/// <summary> World size of one grid cell along X/Y (inches). 100in ~= 2.54m. </summary>
		[Property] public float CellSize { get; set; } = 100f;

		/// <summary> Wall height (inches). </summary>
		[Property] public float WallHeight { get; set; } = 200f;

		/// <summary> Floor thickness (inches). </summary>
		[Property] public float FloorThickness { get; set; } = 10f;

		/// <summary> Material for wall pieces. </summary>
		[Property] public string WallMaterial { get; set; } = "materials/dev/gray_75.vmat";

		/// <summary> Material for floor pieces. </summary>
		[Property] public string FloorMaterial { get; set; } = "materials/dev/gray_50.vmat";

		/// <summary> Optional seed for deterministic layouts (0 = random per build). </summary>
		[Property] public int LayoutSeed { get; set; } = 0;

		private Queue<KeyValuePair<Vector2Int, string>> _buildQueue = new();
		private CancellationTokenSource _cts;
		private readonly List<GameObject> _pieces = new();

		// Layout extents captured in OnStart so the footprint collider can
		// be sized from them after the build completes.
		private int _minX, _maxX, _minY, _maxY;

		/// <summary> Pieces placed so far (read-only view for diagnostics). </summary>
		public IReadOnlyList<GameObject> Pieces => _pieces;

		/// <summary>
		/// True once the build queue has drained and every piece has been
		/// placed. Drives the controller's FSM transition out of Building.
		/// </summary>
		public bool IsComplete { get; private set; }

		/// <summary>
		/// World position the NPC body should walk to before/while building —
		/// the geometric center of the structure's footprint, on the ground.
		/// Computed from the layout in OnStart so the controller can steer
		/// toward it before the first piece is placed.
		/// </summary>
		public Vector3 BuildSiteCenter { get; private set; }

		protected override void OnStart()
		{
			_cts = new CancellationTokenSource();

			var rng = LayoutSeed > 0 ? new Random( LayoutSeed ) : new Random();
			var grammar = new BuildingGrammar( rng );
			var layout = grammar.GenerateLayout( BaseWidth, BaseHeight, WealthFactor );

			// Compute the build-site center from the layout's cell extents so
			// the controller can steer toward the middle of the footprint
			// before the first piece exists.
			int minX = int.MaxValue; int maxX = int.MinValue;
			int minY = int.MaxValue; int maxY = int.MinValue;
			foreach ( var kvp in layout )
			{
				if ( kvp.Key.X < minX ) minX = kvp.Key.X;
				if ( kvp.Key.X > maxX ) maxX = kvp.Key.X;
				if ( kvp.Key.Y < minY ) minY = kvp.Key.Y;
				if ( kvp.Key.Y > maxY ) maxY = kvp.Key.Y;
			}
			_minX = minX; _maxX = maxX; _minY = minY; _maxY = maxY;
			var centerCell = new Vector2Int( (minX + maxX) / 2, (minY + maxY) / 2 );
			BuildSiteCenter = WorldPosition
				+ new Vector3( centerCell.X * CellSize, centerCell.Y * CellSize, 0f );

			foreach ( var kvp in layout )
				_buildQueue.Enqueue( kvp );

			Log.Info( $"Lute: NPCBuilder '{GameObject.Name}' queued {_buildQueue.Count} pieces (wealth={WealthFactor}, seed={LayoutSeed})." );

			_ = ConstructSequence( _cts.Token );
		}

		protected override void OnDestroy()
		{
			// Stops the async loop from touching a GameObject that no longer exists.
			_cts?.Cancel();
		}

		/// <summary>
		/// Call this from your job/AI system when an NPC is reassigned mid-build.
		/// Remaining queue entries are dropped — hand them to a replacement
		/// builder if you want work to resume elsewhere.
		/// </summary>
		public void CancelConstruction()
		{
			_cts?.Cancel();
		}

		private async Task ConstructSequence( CancellationToken token )
		{
			try
			{
				while ( _buildQueue.Count > 0 )
				{
					token.ThrowIfCancellationRequested();

					var step = _buildQueue.Dequeue();
					SpawnSegment( step.Key, step.Value );

					await Task.DelaySeconds( BuildInterval );
				}

				Log.Info( $"Lute: NPCBuilder '{GameObject.Name}' finished — {_pieces.Count} pieces placed." );
				IsComplete = true;
				SpawnFootprintCollider();
			}
			catch ( OperationCanceledException )
			{
				Log.Info( $"Lute: NPCBuilder '{GameObject.Name}' build cancelled with {_buildQueue.Count} pieces remaining." );
			}
		}

		/// <summary>
		/// Place a single piece (wall/floor/door) as a box mesh at the grid
		/// position. Doors are rendered as a short wall stub so the opening
		/// is visible; collision is mesh-exact so the NPC can walk through.
		/// </summary>
		void SpawnSegment( Vector2Int gridPos, string pieceType )
		{
			var go = Scene.CreateObject( false );
			go.Name = $"Structure_{pieceType}_{gridPos.X}_{gridPos.Y}";

			Vector3 localOffset = new Vector3( gridPos.X * CellSize, gridPos.Y * CellSize, 0f );
			go.WorldPosition = WorldPosition + localOffset;
			go.WorldRotation = Rotation.Identity;

			Vector3 size;
			string materialPath;

			switch ( pieceType )
			{
				case "WALL":
					size = new Vector3( CellSize, CellSize, WallHeight );
					materialPath = WallMaterial;
					break;
				case "DOOR":
					// Short stub wall — marks the doorway visually but is short
					// enough to step over. Mesh collision is exact so the NPC
					// can walk through the gap above the stub.
					size = new Vector3( CellSize, CellSize, FloorThickness * 2f );
					materialPath = WallMaterial;
					break;
				case "FLOOR":
				default:
					size = new Vector3( CellSize, CellSize, FloorThickness );
					materialPath = FloorMaterial;
					break;
			}

			BuildBoxMesh( go, size, materialPath, collides: pieceType != "FLOOR" );
			go.Enabled = true;

			_pieces.Add( go );
		}

		/// <summary>
		/// Build a box PolygonMesh on a MeshComponent. Mirrors the verified
		/// pattern from <see cref="LuteBuilderNpc.PlaceBlock"/>. When
		/// <paramref name="collides"/> is false the piece is render-only —
		/// used for floor tiles, which get a single shared footprint collider
		/// instead of one collider per tile.
		/// </summary>
		void BuildBoxMesh( GameObject go, Vector3 size, string materialPath, bool collides = true )
		{
			var meshComp = go.AddComponent<MeshComponent>();
			meshComp.Collision = collides
				? MeshComponent.CollisionType.Mesh
				: MeshComponent.CollisionType.None;

			var mesh = new PolygonMesh();
			var half = size * 0.5f;

			// Floor pieces sit with their top at z=0; walls/floor sit centered
			// on the cell. For walls we lift the box so its base is at z=0.
			if ( size.z > FloorThickness * 1.5f )
			{
				// Wall/door — base at z=0, so offset center up by half height.
				go.WorldPosition = go.WorldPosition.WithZ( go.WorldPosition.z + half.z );
			}
			else
			{
				// Floor — top at z=0, so offset center down by half thickness.
				go.WorldPosition = go.WorldPosition.WithZ( go.WorldPosition.z - half.z );
			}

			var v0 = mesh.AddVertex( new Vector3( -half.x, -half.y, -half.z ) );
			var v1 = mesh.AddVertex( new Vector3(  half.x, -half.y, -half.z ) );
			var v2 = mesh.AddVertex( new Vector3(  half.x,  half.y, -half.z ) );
			var v3 = mesh.AddVertex( new Vector3( -half.x,  half.y, -half.z ) );
			var v4 = mesh.AddVertex( new Vector3( -half.x, -half.y,  half.z ) );
			var v5 = mesh.AddVertex( new Vector3(  half.x, -half.y,  half.z ) );
			var v6 = mesh.AddVertex( new Vector3(  half.x,  half.y,  half.z ) );
			var v7 = mesh.AddVertex( new Vector3( -half.x,  half.y,  half.z ) );

			mesh.AddFace( v0, v3, v2, v1 ); // bottom (-Z)
			mesh.AddFace( v4, v5, v6, v7 ); // top (+Z)
			mesh.AddFace( v0, v1, v5, v4 ); // front (-Y)
			mesh.AddFace( v1, v2, v6, v5 ); // right (+X)
			mesh.AddFace( v2, v3, v7, v6 ); // back (+Y)
			mesh.AddFace( v3, v0, v4, v7 ); // left (-X)

			var material = Material.Load( materialPath );
			if ( material is not null )
			{
				var allFaces = new List<FaceHandle>();
				for ( int f = 0; f < 6; f++ )
					allFaces.Add( mesh.FaceHandleFromIndex( f ) );
				mesh.AssignMaterialToFaces( allFaces, material );
			}

			meshComp.Mesh = mesh;
		}

		/// <summary>
		/// Spawn a single BoxCollider spanning the structure's footprint so
		/// the NPC (and player) can stand on the floor without one collider
		/// per floor tile. Sized from the layout extents captured in OnStart.
		/// The box is centered on the footprint, top at z=0 (matching where
		/// floor tiles sit), and thin enough not to obstruct doorways.
		/// </summary>
		void SpawnFootprintCollider()
		{
			float spanX = (_maxX - _minX + 1) * CellSize;
			float spanY = (_maxY - _minY + 1) * CellSize;
			// Center of the footprint in world space.
			var centerCell = new Vector2Int( (_minX + _maxX) / 2, (_minY + _maxY) / 2 );
			var center = WorldPosition
				+ new Vector3( centerCell.X * CellSize, centerCell.Y * CellSize, 0f );
			// Top at z=0 (floor surface), thickness = FloorThickness so it
			// matches the floor tiles visually without sticking up.
			float z = -FloorThickness * 0.5f;

			var go = Scene.CreateObject( false );
			go.Name = $"{GameObject.Name}_FloorCollider";
			go.WorldPosition = center.WithZ( center.z + z );
			go.WorldRotation = Rotation.Identity;

			var box = go.AddComponent<BoxCollider>();
			box.Scale = new Vector3( spanX, spanY, FloorThickness );

			go.Enabled = true;
			_pieces.Add( go );

			Log.Info( $"Lute: NPCBuilder '{GameObject.Name}' footprint collider {spanX:F0}x{spanY:F0}x{FloorThickness:F0} at {go.WorldPosition}." );
		}

		/// <summary>
		/// Rebuild instantly from a save file (no queue, no delay — the
		/// structure just appears complete). Reuses <see cref="SpawnSegment"/>
		/// so the same material/collision path runs as the original build.
		/// </summary>
		public void RebuildFromSave( string filename )
		{
			_cts?.Cancel();
			_cts = new CancellationTokenSource();
			_buildQueue.Clear();

			WorldPosition = StructurePersistence.LoadOrigin( filename );
			var layout = StructurePersistence.LoadLayout( filename );

			foreach ( var kvp in layout )
				SpawnSegment( kvp.Key, kvp.Value );

			Log.Info( $"Lute: NPCBuilder '{GameObject.Name}' rebuilt from '{filename}' — {_pieces.Count} pieces." );
		}

		/// <summary>
		/// Save the current build to disk. Call after a build finishes.
		/// </summary>
		public void SaveCurrent( string filename )
		{
			// Reconstruct the layout from placed pieces by parsing their names.
			var layout = new Dictionary<Vector2Int, string>();
			foreach ( var piece in _pieces )
			{
				var parts = piece.Name.Split( '_' );
				if ( parts.Length != 4 )
					continue;
				if ( !int.TryParse( parts[2], out int x ) || !int.TryParse( parts[3], out int y ) )
					continue;
				layout[new Vector2Int( x, y )] = parts[1];
			}

			StructurePersistence.SaveStructure( filename, WorldPosition, layout );
			Log.Info( $"Lute: NPCBuilder '{GameObject.Name}' saved {layout.Count} pieces to '{filename}'." );
		}
	}
}
