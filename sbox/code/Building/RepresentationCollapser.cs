using System;
using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using Sandbox;

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

		// Masonry module dimensions (must match VillageBuilder)
		const float M = 39.37f;
		const float BrickModuleX = 0.25f * M;
		const float BrickModuleY = 0.125f * M;
		const float BrickModuleZ = 0.0625f * M;
		const float WallSegmentLength = 2f * M;
		const float WallThickness = 0.5f * M;

		/// <summary>
		/// Bake a wall mesh from SpatialRegistry metadata and atomically
		/// swap it in place of the per-brick GameObjects. If any step
		/// fails, the original bricks remain as a fallback.
		///
		/// Only WallBrick placements are used for the mesh. CornerAssemblyBrick
		/// placements are excluded — they are shared junction objects with
		/// their own ownership path and may be rotated relative to the wall.
		/// Corner GameObjects are NOT destroyed by straight-wall collapse.
		/// </summary>
		void BakeAndSwap( DirectedTask task )
		{
			string structureType = task.BuildTask.TaskType ?? "";
			if ( structureType != "wall" )
				return;

			if ( _collapsed.Contains( task.Id ) )
				return;

			// ── 1. Collect authoritative placements from registry ──
			var allPlacements = SpatialRegistry.GetByAssembly( task.BuildTask.Name );
			int cornerCount = allPlacements.Count( p => p.SemanticType == StructuralType.CornerAssemblyBrick );
			// WallBrick only — exclude CornerAssemblyBrick
			var wallBrickPlacements = allPlacements
				.Where( p => p.SemanticType == StructuralType.WallBrick )
				.ToList();

			if ( wallBrickPlacements.Count == 0 )
			{
				Log.Warning( $"Lute: RepresentationCollapser — no WallBrick placements for '{task.BuildTask.Name}'. Falling back to box." );
				CollapseToBoxFallback( task );
				return;
			}

			// ── 2. Build the collapsed mesh via CollapsedWallMeshBuilder ──
			var wallPos = task.BuildTask.Position;
			float wallRotation = task.BuildTask.Rotation;
			var brickMaterial = Material.Load( "materials/medieval/brick_wall.vmat" );
			var coreMaterial = Material.Load( "materials/medieval/archway_stone.vmat" );

			var mesh = CollapsedWallMeshBuilder.Build(
				wallBrickPlacements, wallPos, wallRotation, brickMaterial, coreMaterial,
				out var envelope,
				out int frontGridY,
				out int backGridY,
				out int frontSkinFaces,
				out int backSkinFaces );

			if ( !mesh.VertexHandles.Any() )
			{
				Log.Warning( $"Lute: RepresentationCollapser — baked mesh has no vertices for '{task.BuildTask.Name}'. Falling back to box." );
				CollapseToBoxFallback( task );
				return;
			}

			Vector3 envSize = envelope.Size;
			Vector3 envCenter = envelope.Center;

			// ── 2a. Pre-bake diagnostic logging ──
			Log.Info( $"Lute: RepresentationCollapser — pre-bake '{task.BuildTask.Name}': WallBrick={wallBrickPlacements.Count}, CornerAssemblyBrick={cornerCount}, frontGridY={frontGridY}, backGridY={backGridY}, envelope mins=({envelope.Mins}), maxs=({envelope.Maxs}), center=({envCenter}), size=({envSize}), frontSkinFaces={frontSkinFaces}, backSkinFaces={backSkinFaces}" );

			// ── 2b. Envelope safeguard — refuse impossible dimensions ──
			float expectedLength = WallSegmentLength;
			float expectedThickness = WallThickness;
			float expectedHeight = task.BuildTask.WallHeight > 0f
				? task.BuildTask.WallHeight
				: 4f * M;

			if ( envSize.x > expectedLength + BrickModuleX ||
			     envSize.y > expectedThickness + BrickModuleX ||
			     envSize.z > expectedHeight + BrickModuleZ )
			{
				Log.Warning( $"Lute: Refusing collapsed wall bake: impossible envelope {envSize} for '{task.BuildTask.Name}' (expected ~{expectedLength},{expectedThickness},{expectedHeight}). Leaving bricks intact." );
				return;
			}

			// ── 3. Report face/vertex counts for verification ──
			int vertexCount = mesh.VertexHandles.Count();
			int faceCount = mesh.FaceHandles.Count();
			int brickCount = wallBrickPlacements.Count;

			// ── 4. Create the baked GameObject (before destroying bricks) ──
			// DEFECT 2 FIX: vertices are centered on local origin, so place
			// the GameObject at wallPos + rotated envelope.Center.
			GameObject bakedGo = null;
			try
			{
				bakedGo = Scene.CreateObject( false );
				bakedGo.Name = $"Village_{task.BuildTask.Name}_baked";
				bakedGo.WorldPosition = wallPos + Rotation.FromYaw( wallRotation ) * envCenter;
				bakedGo.WorldRotation = Rotation.FromYaw( wallRotation );
				bakedGo.WorldScale = Vector3.One;

				var meshComponent = bakedGo.AddComponent<MeshComponent>();
				meshComponent.Mesh = mesh;
				meshComponent.Collision = MeshComponent.CollisionType.None;

				// Box collider from envelope size, centered on local origin
				var collider = bakedGo.AddComponent<BoxCollider>();
				collider.Scale = envSize;

				bakedGo.Enabled = true;
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Lute: RepresentationCollapser — MeshComponent creation failed for '{task.BuildTask.Name}': {ex.Message}. Falling back to box." );
				bakedGo?.Destroy();
				CollapseToBoxFallback( task );
				return;
			}

			// ── 5. Atomic swap: destroy original WallBrick GameObjects only ──
			// Do NOT destroy CornerAssemblyBrick GameObjects — they are shared.
			int destroyed = DestroyWallBrickGameObjects( task.BuildTask.Name );
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
			Log.Info( $"Lute: RepresentationCollapser — baked wall '{task.BuildTask.Name}': {brickCount} WallBricks → {faceCount} faces / {vertexCount} verts (1 MeshComponent), destroyed {destroyed} brick GameObjects (corners preserved). Envelope={envSize}. Total: {CollapsedSegments} segments, {CollapsedBrickCount} bricks." );
		}

		/// <summary>
		/// Destroy only WallBrick GameObjects for the given task name.
		/// CornerAssemblyBrick GameObjects are preserved (shared junctions).
		/// </summary>
		int DestroyWallBrickGameObjects( string taskName )
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
				if ( go.Name.EndsWith( "_corner", StringComparison.Ordinal ) )
					continue;
				if ( go.Name.Contains( "_corner_", StringComparison.Ordinal ) )
					continue;
				go.Destroy();
				destroyed++;
			}
			return destroyed;
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
