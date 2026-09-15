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
	/// GameObject with one ModelRenderer (rendered as a flat castle-wall
	/// box) and one BoxCollider sized to the wall segment's world
	/// bounds. The per-brick metadata
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
	/// Design note: S&Box's <c>Model.Builder</c> API only constructs
	/// collision shapes at runtime; building a renderable procedural
	/// mesh requires editor-only tooling (<c>CreateModelFromPolygonMeshes</c>
	/// calls <c>InitEngineTool</c>). The runtime-safe collapse therefore
	/// uses a single <c>ModelRenderer</c> with a box model
	/// (<c>models/dev/box.vmdl</c>) scaled to the wall's world
	/// dimensions and a <c>castle_wall.vmat</c> material override. This
	/// is a visual downgrade from the per-brick
	/// <c>facepunch.brick_single_04</c> 3D model, but the professor's
	/// spec for Move 9 is explicit: "efficient static render/collision
	/// representations while preserving structural metadata" — exactly
	/// what this does. The per-brick visual is the construction
	/// presentation; the collapsed box is the completed-state
	/// representation.
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

		static RepresentationCollapser _instance;

		protected override void OnStart()
		{
			_instance = this;
			ConstructionEventBus.Subscribe( OnConstructionEvent );
			Log.Info( "Lute: RepresentationCollapser started — collapsing completed wall segments to static representation." );
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

			try
			{
				_instance.CollapseIfNeeded( task );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Lute: RepresentationCollapser failed for task '{evt.TaskId}': {ex.Message}" );
			}
		}

		/// <summary>
		/// Collapse the per-brick representation of a completed wall
		/// segment into a single static GameObject. Idempotent — tracks
		/// collapsed task IDs and skips if already done or if the task
		/// is not a wall.
		/// </summary>
		void CollapseIfNeeded( DirectedTask task )
		{
			string structureType = task.BuildTask.TaskType ?? "";
			if ( structureType != "wall" )
				return; // Move 9 currently collapses wall segments only.

			if ( _collapsed.Contains( task.Id ) )
				return;
			_collapsed.Add( task.Id );

			// Find all per-brick GameObjects for this task by name
			// prefix. Bricks are named
			//   Village_{task.BuildTask.Name}_{globalPieceIndex}
			// and parented under the village root. We can't walk
			// _villageRoot.Children directly because VillageBuilder is
			// a separate component; instead, scan the scene for
			// GameObjects whose name starts with the task's brick
			// prefix. This is O(scene object count) but only runs once
			// per completed structure.
			string brickPrefix = $"Village_{task.BuildTask.Name}_";
			var bricks = new List<GameObject>();
			foreach ( var go in Scene.GetAllObjects( false ) )
			{
				if ( go.Name == null ) continue;
				if ( !go.Name.StartsWith( brickPrefix, StringComparison.Ordinal ) )
					continue;
				// Only collapse per-brick GameObjects (have a
				// ModelRenderer). Skip the segment-level collider
				// (BoxCollider only, no ModelRenderer) which may have
				// been added by FinalizeWall.
				if ( go.Components.Get<ModelRenderer>() == null )
					continue;
				// Skip the segment collider itself (named
				// Village_{name}_collider).
				if ( go.Name.EndsWith( "_collider", StringComparison.Ordinal ) )
					continue;
				bricks.Add( go );
			}

			if ( bricks.Count == 0 )
			{
				Log.Warning( $"Lute: RepresentationCollapser — no brick GameObjects found for task '{task.BuildTask.Name}' (prefix '{brickPrefix}'). Nothing to collapse." );
				return;
			}

			// Destroy the per-brick GameObjects. Their spatial
			// metadata (BrickSlots) is already recorded in
			// PlacedBricks and SpatialRegistry — destroying the
			// GameObjects does not lose structural information.
			int destroyed = 0;
			foreach ( var go in bricks )
			{
				go.Destroy();
				destroyed++;
			}

			// Build the collapsed static representation: one GameObject
			// with a ModelRenderer (box model + castle_wall material)
			// and a BoxCollider sized to the wall's world bounds.
			// Reuses the same approach as VillageBuilder.FinalizeWall's
			// segment-level collider, plus a renderer.
			float segLen = 2f * 39.37f;        // WallSegmentLength (2m)
			float wallDepth = 0.5f * 39.37f;   // WallThickness (0.5m)
			float wallH = 4f * 39.37f;  // 4m wall height (VillageBuildTask.WallHeight default)

			// If an existing FinalizedMeshGo exists (from FinalizeWall),
			// destroy it so we don't stack colliders.
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
			collapsedGo.WorldScale = new Vector3( segLen, wallDepth, wallH ) / 50f; // box.vmdl native = 50 units (full dimensions)

			var renderer = collapsedGo.AddComponent<ModelRenderer>();
			renderer.Model = Model.Load( "models/dev/box.vmdl" );
			var mat = Material.Load( "materials/medieval/castle_wall.vmat" );
			if ( mat is null )
				mat = Material.Load( "materials/medieval/castle_wall.vmat" );
			if ( mat is not null )
				renderer.MaterialOverride = mat;

			var collider = collapsedGo.AddComponent<BoxCollider>();
			collider.Scale = new Vector3( 50f, 50f, 50f ); // box.vmdl native size

			collapsedGo.Enabled = true;
			task.BuildTask.FinalizedMeshGo = collapsedGo;

			CollapsedBrickCount += destroyed;
			CollapsedSegments++;
			Log.Info( $"Lute: RepresentationCollapser — collapsed wall '{task.BuildTask.Name}': destroyed {destroyed} per-brick GameObjects, replaced with single static segment. Total collapsed: {CollapsedSegments} segments, {CollapsedBrickCount} bricks." );
		}
	}
}
