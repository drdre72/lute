using System;
using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Representation collapser — Move 9 of the architecture hardening
	/// roadmap.
	///
	/// While construction is visible, walls are built brick-by-brick
	/// (one GameObject per brick: ModelRenderer + per-piece metadata).
	/// This is essential for the "NPC places pieces" presentation, but
	/// once a structure is complete, the per-brick representation becomes
	/// a scale ceiling: hundreds of GameObjects per wall segment × many
	/// segments × many structures = brick-by-brick GameObject counts
	/// dominate the scene graph and physics broadphase.
	///
	/// <see cref="RepresentationCollapser"/> collapses a completed wall
	/// segment's N individual brick GameObjects into a single static
	/// GameObject with one <see cref="MeshComponent"/> (runtime-baked
	/// <see cref="PolygonMesh"/> preserving the running-bond brick
	/// pattern) and one <see cref="BoxCollider"/> sized to the wall
	/// segment's world bounds. The per-brick metadata
	/// (<c>PlacedBricks</c>, <c>PiecesPlaced</c>, <c>TotalPieces</c>) is
	/// preserved on the <see cref="VillageBuildTask"/> — only the
	/// runtime GameObject representation is collapsed.
	///
	/// The collapser subscribes to
	/// <see cref="ConstructionEventBus.TaskCompleted"/> events (same
	/// channel <see cref="CompletionEffectsManager"/> uses for Move 8
	/// effects) and is a production-owned component — the sim collapses
	/// completed structures on its own, without test-harness involvement.
	///
	/// Design note: This uses S&Box's runtime-capable
	/// <see cref="MeshComponent"/> + <see cref="PolygonMesh"/> path
	/// (not the editor-only <c>CreateModelFromPolygonMeshes</c> which
	/// calls <c>InitEngineTool</c>). The mesh is built deterministically
	/// from <see cref="SpatialRegistry"/> metadata — each brick's
	/// world position, size, and yaw are transformed into wall-local
	/// space and appended as a cuboid face set. This preserves the
	/// running-bond silhouette, mortar gaps (via brick body vs module
	/// dimensions), and course offsets without depending on the cloud
	/// brick model's compiled vertices.
	///
	/// Atomic swap: the baked mesh is created and validated before the
	/// original brick GameObjects are destroyed. If baking fails, the
	/// original bricks remain visible as a fallback.
	/// </summary>
	public sealed class RepresentationCollapser : Component
	{
		/// <summary>
		/// Total number of per-brick GameObjects destroyed by the
		/// collapser across all completed structures. Useful for
		/// observing the scale-ceiling relief over a long run.
		/// </summary>
		[Property] public int CollapsedBrickCount { get; set; }

		/// <summary>
		/// Total number of wall segments collapsed so far.
		/// </summary>
		[Property] public int CollapsedSegments { get; set; }

		/// <summary>
		/// Task IDs that have already been collapsed. Prevents
		/// re-collapse if a TaskCompleted event is delivered twice or
		/// after a save/resume cycle where the runtime objects were
		/// recreated by <c>ReconstructCompletedTasks</c>.
		/// </summary>
		readonly HashSet<string> _collapsed = new();

		/// <summary>
		/// Queue of tasks awaiting bake. Processed on OnUpdate to avoid
		/// doing heavy mesh work inside the event callback.
		/// </summary>
		readonly Queue<DirectedTask> _bakeQueue = new();

		static RepresentationCollapser _instance;

		protected override void OnStart()
		{
			_instance = this;
			ConstructionEventBus.Subscribe( OnConstructionEvent );
			Log.Info( "Lute: RepresentationCollapser started — will runtime-bake completed wall segments to static MeshComponent." );
		}

		protected override void OnDestroy()
		{
			ConstructionEventBus.Unsubscribe( OnConstructionEvent );
			if ( _instance == this ) _instance = null;
		}

		static void OnConstructionEvent( ConstructionEvent evt )
		{
			if ( evt.Type != ConstructionEventType.TaskCompleted )
				return;
			if ( _instance == null ) return;

			var task = ConstructionDirector.GetTask( evt.TaskId );
			if ( task?.BuildTask == null ) return;

			_instance._bakeQueue.Enqueue( task );
		}

		protected override void OnUpdate()
		{
			while ( _bakeQueue.Count > 0 )
			{
				var task = _bakeQueue.Dequeue();
				try
				{
					BakeAndSwap( task );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"Lute: RepresentationCollapser bake failed for task '{task.Id}': {ex.Message}" );
				}
			}
		}

		/// <summary>
		/// Bake a wall mesh from SpatialRegistry metadata and atomically
		/// swap it in place of the per-brick GameObjects. If any step
		/// fails, the original bricks remain as a fallback.
		/// </summary>
		void BakeAndSwap( DirectedTask task )
		{
			string structureType = task.BuildTask.TaskType ?? "";
			if ( structureType != "wall" )
				return;

			if ( _collapsed.Contains( task.Id ) )
				return;

			// ── 1. Collect authoritative brick placements from registry ──
			var placements = SpatialRegistry.GetByAssembly( task.BuildTask.Name )
				.Where( p => p.SemanticType == StructuralType.WallBrick
					|| p.SemanticType == StructuralType.CornerAssemblyBrick )
				.ToList();

			if ( placements.Count == 0 )
			{
				Log.Warning( $"Lute: RepresentationCollapser — no SpatialRegistry placements for '{task.BuildTask.Name}'. Falling back to box." );
				CollapseToBoxFallback( task );
				return;
			}

			// ── 2. Build the PolygonMesh in wall-local space ──
			var wallPos = task.BuildTask.Position;
			var wallRot = Rotation.FromYaw( task.BuildTask.Rotation );

			var mesh = new PolygonMesh();
			var brickMaterial = Material.Load( "materials/medieval/castle_wall.vmat" );

			int brickCount = 0;
			foreach ( var p in placements )
			{
				// Transform world position to wall-local space
				var localCenter = wallRot.Inverse * (p.Position - wallPos);
				// Local yaw relative to wall
				float localYaw = p.Yaw - task.BuildTask.Rotation;
				var localRot = Rotation.FromYaw( localYaw );

				AppendBrickCuboid( mesh, localCenter, p.Size, localRot );
				brickCount++;
			}

			if ( !mesh.VertexHandles.Any() )
			{
				Log.Warning( $"Lute: RepresentationCollapser — baked mesh has no vertices for '{task.BuildTask.Name}'. Falling back to box." );
				CollapseToBoxFallback( task );
				return;
			}

			// ── 3. Validate baked height matches task ──
			float wallH = task.BuildTask.WallHeight > 0f
				? task.BuildTask.WallHeight
				: 4f * 39.37f;
			float segLen = 2f * 39.37f;
			float wallDepth = 0.5f * 39.37f;

			// ── 4. Create the baked GameObject (before destroying bricks) ──
			GameObject bakedGo = null;
			try
			{
				bakedGo = Scene.CreateObject( false );
				bakedGo.Name = $"Village_{task.BuildTask.Name}_baked";
				bakedGo.WorldPosition = wallPos; // mesh vertices already include full Z range (0..wallH)
				bakedGo.WorldRotation = wallRot;
				bakedGo.WorldScale = Vector3.One; // mesh is authored at correct world dimensions

				var meshComponent = bakedGo.AddComponent<MeshComponent>();
				meshComponent.Mesh = mesh;
				meshComponent.Collision = MeshComponent.CollisionType.None; // separate box collider
				if ( brickMaterial is not null )
				{
					// Set material on all faces
					int faceCount = mesh.FaceHandles.Count();
					for ( int i = 0; i < faceCount; i++ )
					{
						meshComponent.SetMaterial( brickMaterial, i * 2 ); // 2 triangles per quad
					}
				}

				// Simple box collider matching wall dimensions
				var collider = bakedGo.AddComponent<BoxCollider>();
				collider.Scale = new Vector3( segLen, wallDepth, wallH );

				bakedGo.Enabled = true;
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Lute: RepresentationCollapser — MeshComponent creation failed for '{task.BuildTask.Name}': {ex.Message}. Falling back to box." );
				bakedGo?.Destroy();
				CollapseToBoxFallback( task );
				return;
			}

			// ── 5. Atomic swap: destroy original bricks + stale collider ──
			int destroyed = DestroyBrickGameObjects( task.BuildTask.Name );
			DestroyStaleCollider( task.BuildTask.Name );

			// Destroy old FinalizedMeshGo if present
			if ( task.BuildTask.FinalizedMeshGo is not null )
			{
				task.BuildTask.FinalizedMeshGo.Destroy();
				task.BuildTask.FinalizedMeshGo = null;
			}

			task.BuildTask.FinalizedMeshGo = bakedGo;

			_collapsed.Add( task.Id );
			CollapsedBrickCount += destroyed;
			CollapsedSegments++;
			Log.Info( $"Lute: RepresentationCollapser — baked wall '{task.BuildTask.Name}': {brickCount} bricks → 1 MeshComponent, destroyed {destroyed} GameObjects. Total: {CollapsedSegments} segments, {CollapsedBrickCount} bricks." );
		}

		/// <summary>
		/// Append a brick cuboid (6 faces) to the polygon mesh at the
		/// given local center with the given local size and rotation.
		/// The cuboid is axis-aligned in the brick's local frame, then
		/// rotated by localRot and translated to localCenter.
		/// </summary>
		static void AppendBrickCuboid( PolygonMesh mesh, Vector3 localCenter, Vector3 size, Rotation localRot )
		{
			float hx = size.x * 0.5f;
			float hy = size.y * 0.5f;
			float hz = size.z * 0.5f;

			// 8 corners in brick-local space (before rotation/translation)
			Vector3[] corners = new Vector3[8];
			corners[0] = new Vector3( -hx, -hy, -hz );
			corners[1] = new Vector3(  hx, -hy, -hz );
			corners[2] = new Vector3(  hx,  hy, -hz );
			corners[3] = new Vector3( -hx,  hy, -hz );
			corners[4] = new Vector3( -hx, -hy,  hz );
			corners[5] = new Vector3(  hx, -hy,  hz );
			corners[6] = new Vector3(  hx,  hy,  hz );
			corners[7] = new Vector3( -hx,  hy,  hz );

			// Transform to wall-local space
			for ( int i = 0; i < 8; i++ )
				corners[i] = localRot * corners[i] + localCenter;

			// 6 faces (quads), outward-facing
			// Bottom (z-)
			mesh.AddFace(
				mesh.AddVertex( corners[0] ),
				mesh.AddVertex( corners[1] ),
				mesh.AddVertex( corners[2] ),
				mesh.AddVertex( corners[3] ) );
			// Top (z+)
			mesh.AddFace(
				mesh.AddVertex( corners[4] ),
				mesh.AddVertex( corners[5] ),
				mesh.AddVertex( corners[6] ),
				mesh.AddVertex( corners[7] ) );
			// Front (y-)
			mesh.AddFace(
				mesh.AddVertex( corners[0] ),
				mesh.AddVertex( corners[1] ),
				mesh.AddVertex( corners[5] ),
				mesh.AddVertex( corners[4] ) );
			// Back (y+)
			mesh.AddFace(
				mesh.AddVertex( corners[2] ),
				mesh.AddVertex( corners[3] ),
				mesh.AddVertex( corners[7] ),
				mesh.AddVertex( corners[6] ) );
			// Left (x-)
			mesh.AddFace(
				mesh.AddVertex( corners[0] ),
				mesh.AddVertex( corners[3] ),
				mesh.AddVertex( corners[7] ),
				mesh.AddVertex( corners[4] ) );
			// Right (x+)
			mesh.AddFace(
				mesh.AddVertex( corners[1] ),
				mesh.AddVertex( corners[2] ),
				mesh.AddVertex( corners[6] ),
				mesh.AddVertex( corners[5] ) );
		}

		/// <summary>
		/// Destroy all per-brick GameObjects for the given task name.
		/// Returns the count destroyed.
		/// </summary>
		int DestroyBrickGameObjects( string taskName )
		{
			string brickPrefix = $"Village_{taskName}_";
			int destroyed = 0;
			foreach ( var go in Scene.GetAllObjects( false ) )
			{
				if ( go.Name == null ) continue;
				if ( !go.Name.StartsWith( brickPrefix, StringComparison.Ordinal ) )
					continue;
				if ( go.Components.Get<ModelRenderer>() == null )
					continue;
				if ( go.Name.EndsWith( "_collider", StringComparison.Ordinal ) )
					continue;
				if ( go.Name.EndsWith( "_baked", StringComparison.Ordinal ) )
					continue;
				go.Destroy();
				destroyed++;
			}
			return destroyed;
		}

		/// <summary>
		/// Destroy the stale segment collider (Village_{name}_collider).
		/// </summary>
		void DestroyStaleCollider( string taskName )
		{
			string colliderName = $"Village_{taskName}_collider";
			foreach ( var go in Scene.GetAllObjects( false ) )
			{
				if ( go.Name == colliderName )
				{
					go.Destroy();
					return;
				}
			}
		}

		/// <summary>
		/// Fallback: collapse to a stretched box.vmdl (the old behavior).
		/// Used when SpatialRegistry has no placements or mesh baking fails.
		/// </summary>
		void CollapseToBoxFallback( DirectedTask task )
		{
			if ( _collapsed.Contains( task.Id ) )
				return;
			_collapsed.Add( task.Id );

			float segLen = 2f * 39.37f;
			float wallDepth = 0.5f * 39.37f;
			float wallH = task.BuildTask.WallHeight > 0f
				? task.BuildTask.WallHeight
				: 4f * 39.37f;

			int destroyed = DestroyBrickGameObjects( task.BuildTask.Name );
			DestroyStaleCollider( task.BuildTask.Name );

			if ( task.BuildTask.FinalizedMeshGo is not null )
			{
				task.BuildTask.FinalizedMeshGo.Destroy();
				task.BuildTask.FinalizedMeshGo = null;
			}

			var collapsedGo = Scene.CreateObject( false );
			collapsedGo.Name = $"Village_{task.BuildTask.Name}_collapsed";
			collapsedGo.WorldPosition = task.BuildTask.Position + new Vector3( 0, 0, wallH * 0.5f );
			if ( task.BuildTask.Rotation != 0 )
				collapsedGo.WorldRotation = Rotation.FromYaw( task.BuildTask.Rotation );
			collapsedGo.WorldScale = new Vector3( segLen, wallDepth, wallH ) / 50f;

			var renderer = collapsedGo.AddComponent<ModelRenderer>();
			renderer.Model = Model.Load( "models/dev/box.vmdl" );
			var mat = Material.Load( "materials/medieval/castle_wall.vmat" );
			if ( mat is not null )
				renderer.MaterialOverride = mat;

			var collider = collapsedGo.AddComponent<BoxCollider>();
			collider.Scale = new Vector3( 50f, 50f, 50f );

			collapsedGo.Enabled = true;
			task.BuildTask.FinalizedMeshGo = collapsedGo;

			CollapsedBrickCount += destroyed;
			CollapsedSegments++;
			Log.Info( $"Lute: RepresentationCollapser — box-fallback for '{task.BuildTask.Name}': destroyed {destroyed} bricks. Total: {CollapsedSegments} segments." );
		}
	}
}
