using System;
using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Applies <see cref="BuildingCritique"/> payloads to a village task queue.
	/// This is the bridge between LLM qualitative reasoning and deterministic
	/// geometry execution. It modifies PENDING tasks only — never touches
	/// in-progress or complete tasks, respecting the incremental build model
	/// and save/resume system.
	///
	/// As of Phase 2, also supports direct <see cref="Blueprint"/> modification
	/// via <see cref="ModifyBlueprint"/>: applies a critique to a blueprint's
	/// pieces, bumps the version, recomputes the hash, and registers the new
	/// version with <see cref="BlueprintRegistry"/>. This enables the
	/// critique -> v(n+1) -> validate -> execute -> rollback-on-failure loop.
	/// </summary>
	public static class BlueprintModifier
	{
		/// <summary>
		/// Apply a critique directly to a Blueprint's pieces, producing a
		/// new version. The original blueprint is not mutated; a versioned
		/// copy is created, modified, validated, and registered. Returns
		/// the new version, or null if the critique was a no-op.
		///
		/// This is the piece-level path used when an LLM critique targets
		/// a specific blueprint (not a village task). The task-level path
		/// (<see cref="ApplyCritique"/>) is for village-build-time
		/// modifications before the blueprint is generated.
		/// </summary>
		public static Blueprint ModifyBlueprint( Blueprint original, BuildingCritique critique )
		{
			if ( original == null || critique == null )
				return null;

			// Create a versioned copy
			var modified = new Blueprint
			{
				Id = original.Id,
				Version = original.Version + 1,
				Name = original.Name,
				Origin = critique.PositionOffset ?? original.Origin,
				Rotation = original.Rotation,
				CellSize = original.CellSize,
				WallHeight = original.WallHeight,
				FloorThickness = original.FloorThickness,
				WallMaterial = !string.IsNullOrEmpty( critique.MaterialOverride ) ? critique.MaterialOverride : original.WallMaterial,
				FloorMaterial = !string.IsNullOrEmpty( critique.MaterialOverride ) ? critique.MaterialOverride : original.FloorMaterial,
				RoofMaterial = original.RoofMaterial,
				ColumnMaterial = original.ColumnMaterial,
				Constraints = original.Constraints,
				Anchors = original.Anchors,
				Dependencies = original.Dependencies,
				Provenance = new BlueprintProvenance
				{
					Producer = "BlueprintModifier.ModifyBlueprint",
					Preset = original.Provenance?.Preset ?? original.Name,
					Seed = original.Provenance?.Seed ?? 0,
					Parameters = new Dictionary<string, string>( original.Provenance?.Parameters ?? new() )
					{
						["critiqueSource"] = critique.CriticSource,
						["critiqueReason"] = critique.Reason,
						["parentVersion"] = original.Version.ToString(),
					},
				},
				Metadata = new Dictionary<string, string>( original.Metadata ),
				Pieces = new List<BlueprintPiece>( original.Pieces ),
			};

			// Apply material override to all pieces if specified
			if ( !string.IsNullOrEmpty( critique.MaterialOverride ) )
			{
				for ( int i = 0; i < modified.Pieces.Count; i++ )
				{
					var p = modified.Pieces[i];
					p.Material = critique.MaterialOverride;
				}
			}

			// Apply position offset to all pieces if specified
			if ( critique.PositionOffset.HasValue )
			{
				var offset = critique.PositionOffset.Value;
				for ( int i = 0; i < modified.Pieces.Count; i++ )
				{
					var p = modified.Pieces[i];
					p.Position = p.Position + offset;
				}
			}

			modified.ComputeHash();
			BlueprintRegistry.Register( modified );
			Log.Info( $"Lute: BlueprintModifier.ModifyBlueprint — {modified.Id} v{modified.Version} ({modified.PieceCount} pieces, hash={modified.Hash?[..8]})." );
			return modified;
		}

		/// <summary>
		/// Compute the diff between two blueprint versions and log it.
		/// Useful for understanding what an LLM critique actually changed.
		/// </summary>
		public static BlueprintDiff DiffVersions( Blueprint from, Blueprint to )
		{
			var diff = BlueprintDiffer.Diff( from, to );
			Log.Info( $"Lute: BlueprintDiff {diff.Summary}." );
			if ( diff.Added.Count > 0 )
				Log.Info( $"  Added pieces: [{string.Join( ",", diff.Added )}]" );
			if ( diff.Removed.Count > 0 )
				Log.Info( $"  Removed pieces: [{string.Join( ",", diff.Removed )}]" );
			if ( diff.Modified.Count > 0 )
				Log.Info( $"  Modified pieces: [{string.Join( ",", diff.Modified )}]" );
			return diff;
		}
		/// <summary>
		/// Apply a critique to the village task list. Only pending tasks
		/// (Status == 0) are modified. In-progress and complete tasks
		/// are left untouched.
		/// </summary>
		public static void ApplyCritique( List<VillageBuildTask> tasks, BuildingCritique critique )
		{
			if ( tasks == null || critique == null )
				return;

			Log.Info( $"Lute: BlueprintModifier applying critique from {critique.CriticSource}: {critique.Reason}" );

			// 1. Modify the target task (or next pending)
			var target = FindTargetTask( tasks, critique.TargetTaskIndex );
			if ( target != null )
			{
				ModifyTask( target, critique );
			}
			else
			{
				Log.Info( "Lute: BlueprintModifier — no pending task to modify." );
			}

			// 2. Add new modules (creates new tasks)
			foreach ( var module in critique.AddModules )
			{
				AddModule( tasks, target, module );
			}

			// 3. Remove modules (cancels pending tasks by type)
			foreach ( var module in critique.RemoveModules )
			{
				RemoveModule( tasks, module );
			}
		}

		/// <summary>
		/// Find the task to modify. If TargetTaskIndex is -1, find the next
		/// pending task. Otherwise, use the specified index (must be pending).
		/// </summary>
		static VillageBuildTask FindTargetTask( List<VillageBuildTask> tasks, int targetIndex )
		{
			if ( targetIndex >= 0 && targetIndex < tasks.Count )
			{
				var task = tasks[targetIndex];
				if ( task.Status == 0 )
					return task;

				Log.Info( $"Lute: BlueprintModifier — task #{targetIndex} is not pending (status={task.Status}). Skipping." );
				return null;
			}

			// Find next pending task
			return tasks.FirstOrDefault( t => t.Status == 0 );
		}

		/// <summary>
		/// Modify a pending task based on critique fields.
		/// </summary>
		static void ModifyTask( VillageBuildTask task, BuildingCritique critique )
		{
			// Apply wealth modifier
			if ( critique.WealthModifier != 1.0f && critique.WealthModifier > 0 )
			{
				task.WealthFactor *= critique.WealthModifier;
				Log.Info( $"Lute: BlueprintModifier — '{task.Name}' wealth adjusted to {task.WealthFactor:F2}" );
			}

			// Apply style change
			if ( !string.IsNullOrEmpty( critique.StyleTag ) )
			{
				var newStyle = ResolveStyle( critique.StyleTag );
				if ( newStyle != null )
				{
					task.Style = newStyle;
					Log.Info( $"Lute: BlueprintModifier — '{task.Name}' style changed to '{newStyle.Name}'" );
				}
			}

			// Apply position offset
			if ( critique.PositionOffset.HasValue )
			{
				task.Position += critique.PositionOffset.Value;
				Log.Info( $"Lute: BlueprintModifier — '{task.Name}' position shifted by {critique.PositionOffset.Value}" );
			}

			// Apply material override
			if ( !string.IsNullOrEmpty( critique.MaterialOverride ) )
			{
				Log.Info( $"Lute: BlueprintModifier — '{task.Name}' material override: {critique.MaterialOverride}" );
				// Material override is applied at build time via the Style's material fields
				if ( task.Style != null )
				{
					task.Style.WallMaterial = critique.MaterialOverride;
					task.Style.FloorMaterial = critique.MaterialOverride;
				}
			}
		}

		/// <summary>
		/// Add a new module as a new pending task near the target.
		/// </summary>
		static void AddModule( List<VillageBuildTask> tasks, VillageBuildTask nearTask, string moduleType )
		{
			if ( nearTask == null )
			{
				Log.Info( $"Lute: BlueprintModifier — cannot add module '{moduleType}' without a reference task." );
				return;
			}

			// Offset the new module slightly from the reference task
			var offset = new Vector3( 500f, 500f, 0 ); // ~12.7m offset
			var newTask = new VillageBuildTask
			{
				Position = nearTask.Position + offset,
				Rotation = nearTask.Rotation,
				TaskType = moduleType.ToLowerInvariant(),
				Name = $"{moduleType}_added_{tasks.Count}",
				Priority = nearTask.Priority + 1,
				WealthFactor = nearTask.WealthFactor,
				BaseWidth = 4,
				BaseHeight = 4,
				LayoutSeed = new Random().Next(),
				Style = nearTask.Style ?? ArchitecturalStyle.Vernacular,
			};

			tasks.Add( newTask );
			Log.Info( $"Lute: BlueprintModifier — added module '{moduleType}' at {newTask.Position} (task #{tasks.Count - 1})" );
		}

		/// <summary>
		/// Remove (cancel) pending tasks matching the module type.
		/// Only pending tasks are cancelled — in-progress and complete are left alone.
		/// </summary>
		static void RemoveModule( List<VillageBuildTask> tasks, string moduleType )
		{
			var lower = moduleType.ToLowerInvariant();
			int removed = 0;

			for ( int i = tasks.Count - 1; i >= 0; i-- )
			{
				var task = tasks[i];
				if ( task.Status == 0 && task.TaskType == lower )
				{
					tasks.RemoveAt( i );
					removed++;
				}
			}

			if ( removed > 0 )
				Log.Info( $"Lute: BlueprintModifier — removed {removed} pending '{moduleType}' tasks." );
			else
				Log.Info( $"Lute: BlueprintModifier — no pending '{moduleType}' tasks to remove." );
		}

		/// <summary>
		/// Resolve a style tag string to an ArchitecturalStyle preset.
		/// </summary>
		static ArchitecturalStyle ResolveStyle( string tag )
		{
			return tag.ToLowerInvariant() switch
			{
				"romanesque" => ArchitecturalStyle.Romanesque,
				"gothic" => ArchitecturalStyle.Gothic,
				"classical" => ArchitecturalStyle.Classical,
				"vernacular" => ArchitecturalStyle.Vernacular,
				"fortress" or "fortified" => ArchitecturalStyle.Fortress,
				_ => null,
			};
		}
	}
}
