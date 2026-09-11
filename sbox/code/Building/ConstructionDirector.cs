using System;
using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Status of a directed construction task. Mirrors VillageBuildTask.Status
	/// (0=pending,1=in_progress,2=complete) but adds Blocked (waiting on a
	/// dependency) and Failed (validation/execution failure).
	/// </summary>
	public enum TaskStatus
	{
		Pending = 0,
		InProgress = 1,
		Complete = 2,
		Blocked = 3,
		Failed = 4,
		Cancelled = 5,
	}

	/// <summary>
	/// A directed task: a unit of construction work the
	/// <see cref="ConstructionDirector"/> schedules. Wraps a
	/// <see cref="VillageBuildTask"/> with director-level metadata:
	/// assigned builder, dependency edges, blueprint id/version, retry
	/// count, and reservation id. This is the authoritative task record;
	/// <see cref="VillageBuildTask"/> becomes the executor-side view.
	/// </summary>
	public class DirectedTask
	{
		/// <summary> Stable director-side id (independent of list index). </summary>
		public string Id { get; set; }

		/// <summary> The underlying village build task (executor view). </summary>
		public VillageBuildTask BuildTask { get; set; }

		/// <summary> Current director status. </summary>
		public TaskStatus Status { get; set; } = TaskStatus.Pending;

		/// <summary> Builder assigned to this task (-1 = unassigned). </summary>
		public int AssignedBuilder { get; set; } = -1;

		/// <summary> Estimated piece count (for load balancing). </summary>
		public int EstimatedPieces { get; set; }

		/// <summary> Ids of tasks that must be Complete before this one starts. </summary>
		public List<string> DependsOn { get; set; } = new();

		/// <summary> Blueprint id this task produces (if any). </summary>
		public string BlueprintId { get; set; }

		/// <summary> Blueprint version last executed for this task. </summary>
		public int BlueprintVersion { get; set; }

		/// <summary> SpatialBlackboard claim id held by this task, if any. </summary>
		public string ReservationId { get; set; }

		/// <summary> Number of times this task has been retried after failure. </summary>
		public int RetryCount { get; set; }

		/// <summary> Max retries before the director gives up. </summary>
		public int MaxRetries { get; set; } = 2;

		/// <summary> Pieces placed so far (progress). </summary>
		public int PiecesPlaced { get; set; }

		/// <summary> True if all dependencies are Complete. </summary>
		public bool DependenciesSatisfied =>
			DependsOn.All( depId =>
				ConstructionDirector.GetTask( depId )?.Status == TaskStatus.Complete );
	}

	/// <summary>
	/// Per-builder runtime state tracked by the director.
	/// </summary>
	public class BuilderState
	{
		public int BuilderId { get; set; }
		public string NpcName { get; set; }
		public string CurrentTaskId { get; set; }
		public int PiecesBuilt { get; set; }
		public bool Active { get; set; }
	}

	/// <summary>
	/// Centralized construction scheduler. The professor's Phase 3
	/// recommendation: one authoritative system for task scheduling,
	/// builder assignment, spatial reservations, dependency tracking,
	/// progress, and recovery — instead of having those concerns spread
	/// across <see cref="VillageBuilder"/> (partitioning + build loop),
	/// <see cref="SpatialBlackboard"/> (claims), and
	/// <see cref="VillagePersistence"/> (progress).
	///
	/// Design:
	/// - Static singleton-style (matches SpatialBlackboard's pattern) so
	///   any system can query it without DI.
	/// - Backward compatible: existing VillageBuilder code keeps working.
	///   New code should go through the Director so scheduling,
	///   reservations, and dependencies are consistent.
	/// - The Director owns the task list; builders ask it for the next
	///   ready task instead of iterating a shared list themselves.
	/// - Work-stealing: when a builder finishes its assigned tasks, it
	///   can steal the oldest pending task from any builder with a
	///   heavier remaining load.
	/// </summary>
	public static class ConstructionDirector
	{
		// id -> task
		static readonly Dictionary<string, DirectedTask> _tasks = new();
		// builder id -> state
		static readonly Dictionary<int, BuilderState> _builders = new();
		// task id -> task ids that depend on it (reverse edges)
		static readonly Dictionary<string, List<string>> _dependents = new();
		static int _nextTaskSeq;

		// ── Registration ──

		/// <summary> Clear all director state (test/reset). </summary>
		public static void Reset()
		{
			_tasks.Clear();
			_builders.Clear();
			_dependents.Clear();
			_nextTaskSeq = 0;
		}

		/// <summary>
		/// Register a builder with the director. Must be called before
		/// that builder can be assigned tasks.
		/// </summary>
		public static void RegisterBuilder( int builderId, string npcName )
		{
			_builders[builderId] = new BuilderState
			{
				BuilderId = builderId,
				NpcName = npcName,
				Active = true,
			};
		}

		/// <summary> Mark a builder inactive (e.g. NPC destroyed). </summary>
		public static void DeactivateBuilder( int builderId )
		{
			if ( _builders.TryGetValue( builderId, out var st ) )
				st.Active = false;
		}

		/// <summary>
		/// Register a task with the director. Returns the assigned task id.
		/// Estimates piece count if not set. Does NOT assign a builder yet
		/// — call <see cref="AssignTasks"/> after all tasks are registered.
		/// </summary>
		public static string RegisterTask( VillageBuildTask buildTask,
			List<string> dependsOn = null, string blueprintId = null )
		{
			var id = $"task_{_nextTaskSeq++}";
			var dt = new DirectedTask
			{
				Id = id,
				BuildTask = buildTask,
				EstimatedPieces = EstimatePieces( buildTask ),
				BlueprintId = blueprintId,
				DependsOn = dependsOn ?? new(),
			};
			_tasks[id] = dt;

			// Build reverse dependency edges
			if ( dependsOn != null )
			{
				foreach ( var dep in dependsOn )
				{
					if ( !_dependents.TryGetValue( dep, out var list ) )
					{
						list = new();
						_dependents[dep] = list;
					}
					list.Add( id );
				}
			}

			return id;
		}

		/// <summary> Get a task by id. </summary>
		public static DirectedTask GetTask( string id )
		{
			return _tasks.TryGetValue( id, out var t ) ? t : null;
		}

		/// <summary> All registered tasks. </summary>
		public static List<DirectedTask> AllTasks() => _tasks.Values.ToList();

		/// <summary> All registered builders. </summary>
		public static List<BuilderState> AllBuilders() => _builders.Values.ToList();

		// ── Scheduling / assignment ──

		/// <summary>
		/// Assign all pending tasks to builders using balanced deal
		/// (biggest-first round-robin), respecting dependencies. Tasks
		/// with unsatisfied dependencies are marked Blocked and excluded
		/// from this pass; call AssignTasks again after their deps
		/// complete.
		/// </summary>
		public static void AssignTasks()
		{
			// Mark blocked tasks
			foreach ( var t in _tasks.Values )
			{
				if ( t.Status == TaskStatus.Pending && !t.DependenciesSatisfied )
					t.Status = TaskStatus.Blocked;
				else if ( t.Status == TaskStatus.Blocked && t.DependenciesSatisfied )
					t.Status = TaskStatus.Pending;
			}

			int builderCount = _builders.Count;
			if ( builderCount == 0 )
				return;

			// Collect assignable pending tasks, biggest first
			var assignable = _tasks.Values
				.Where( t => t.Status == TaskStatus.Pending && t.AssignedBuilder == -1 )
				.OrderByDescending( t => t.EstimatedPieces )
				.ToList();

			// Balanced deal
			var load = _builders.ToDictionary( b => b.Key, b => 0 );
			foreach ( var b in _builders.Values )
				load[b.BuilderId] = _tasks.Values
					.Where( t => t.AssignedBuilder == b.BuilderId &&
						 t.Status != TaskStatus.Complete )
					.Sum( t => t.EstimatedPieces );

			foreach ( var t in assignable )
			{
				// Pick the builder with the smallest current load
				int target = load.OrderBy( kvp => kvp.Value ).First().Key;
				t.AssignedBuilder = target;
				load[target] += t.EstimatedPieces;
			}
		}

		/// <summary>
		/// Get the next task a builder should work on. Returns null if
		/// none ready. The task is marked InProgress and a spatial
		/// reservation is attempted. If the reservation fails (another
		/// builder has the site), the task stays Pending and the builder
		/// should try again next tick.
		/// </summary>
		public static DirectedTask ClaimNextTask( int builderId, string npcName )
		{
			if ( !_builders.TryGetValue( builderId, out var state ) || !state.Active )
				return null;

			// Find an assigned, pending, dependency-satisfied task
			var task = _tasks.Values.FirstOrDefault( t =>
				t.AssignedBuilder == builderId &&
				t.Status == TaskStatus.Pending &&
				t.DependenciesSatisfied );

			// Work-stealing: if none assigned, steal the oldest pending
			// task from the most-loaded builder.
			if ( task == null )
				task = StealTask( builderId );

			if ( task == null )
				return null;

			// Attempt spatial reservation
			var bt = task.BuildTask;
			float radius = Math.Max( bt.BaseWidth, bt.BaseHeight ) * 100f + 200f;
			if ( !SpatialBlackboard.Claim( npcName, bt.Position, radius, "construction", 0 ) )
			{
				// Site blocked — leave pending, try again later
				return null;
			}

			task.ReservationId = $"{npcName}_{task.Id}";
			task.Status = TaskStatus.InProgress;
			state.CurrentTaskId = task.Id;
			return task;
		}

		static DirectedTask StealTask( int builderId )
		{
			// Find the most-loaded active builder other than us
			var other = _builders.Values
				.Where( b => b.BuilderId != builderId && b.Active )
				.OrderByDescending( b => _tasks.Values
					.Count( t => t.AssignedBuilder == b.BuilderId &&
						 t.Status == TaskStatus.Pending ) )
				.FirstOrDefault();
			if ( other == null )
				return null;

			// Steal its oldest pending, dependency-satisfied task
			var victim = _tasks.Values
				.Where( t => t.AssignedBuilder == other.BuilderId &&
					 t.Status == TaskStatus.Pending &&
					 t.DependenciesSatisfied )
				.OrderBy( t => t.EstimatedPieces ) // steal smallest first
				.FirstOrDefault();
			if ( victim == null )
				return null;

			victim.AssignedBuilder = builderId;
			return victim;
		}

		/// <summary>
		/// Report progress on a task (pieces placed). Called by the
		/// executor as it builds.
		/// </summary>
		public static void ReportProgress( string taskId, int piecesPlaced )
		{
			if ( _tasks.TryGetValue( taskId, out var t ) )
			{
				t.PiecesPlaced = piecesPlaced;
				if ( _builders.TryGetValue( t.AssignedBuilder, out var b ) )
					b.PiecesBuilt = piecesPlaced;
			}
		}

		/// <summary>
		/// Mark a task complete. Releases the spatial reservation and
		/// notifies dependents (their Blocked status will clear on the
		/// next <see cref="AssignTasks"/> pass).
		/// </summary>
		public static void CompleteTask( string taskId )
		{
			if ( !_tasks.TryGetValue( taskId, out var t ) )
				return;

			t.Status = TaskStatus.Complete;
			if ( t.AssignedBuilder >= 0 && _builders.TryGetValue( t.AssignedBuilder, out var b ) )
				b.CurrentTaskId = null;

			// Release reservation
			if ( !string.IsNullOrEmpty( t.ReservationId ) )
			{
				SpatialBlackboard.ReleaseClaim( t.ReservationId );
				t.ReservationId = null;
			}
		}

		/// <summary>
		/// Mark a task failed. Increments retry count; if under
		/// <see cref="DirectedTask.MaxRetries"/>, resets to Pending for
		/// retry; otherwise stays Failed. Releases the reservation.
		/// </summary>
		public static void FailTask( string taskId, string reason = null )
		{
			if ( !_tasks.TryGetValue( taskId, out var t ) )
				return;

			t.RetryCount++;
			if ( !string.IsNullOrEmpty( t.ReservationId ) )
			{
				SpatialBlackboard.ReleaseClaim( t.ReservationId );
				t.ReservationId = null;
			}

			if ( t.RetryCount <= t.MaxRetries )
			{
				t.Status = TaskStatus.Pending;
				Log.Info( $"Lute: ConstructionDirector task {taskId} failed ({reason}) — retry {t.RetryCount}/{t.MaxRetries}." );
			}
			else
			{
				t.Status = TaskStatus.Failed;
				Log.Warning( $"Lute: ConstructionDirector task {taskId} failed permanently ({reason}) after {t.RetryCount} retries." );
			}
		}

		/// <summary> Cancel a task (e.g. NPC reassigned). </summary>
		public static void CancelTask( string taskId )
		{
			if ( !_tasks.TryGetValue( taskId, out var t ) )
				return;
			t.Status = TaskStatus.Cancelled;
			if ( !string.IsNullOrEmpty( t.ReservationId ) )
			{
				SpatialBlackboard.ReleaseClaim( t.ReservationId );
				t.ReservationId = null;
			}
		}

		// ── Recovery ──

		/// <summary>
		/// Returns tasks whose geometry must be reconstructed on reload
		/// (Complete tasks whose pieces were runtime-spawned and didn't
		/// survive the scene reload). Only one builder should actually
		/// perform reconstruction; the others should call this only to
		/// know which tasks are covered.
		/// </summary>
		public static List<DirectedTask> TasksNeedingReconstruction()
		{
			return _tasks.Values
				.Where( t => t.Status == TaskStatus.Complete )
				.OrderBy( t => t.Id )
				.ToList();
		}

		// ── Status ──

		/// <summary> Console-friendly status summary. </summary>
		public static string StatusSummary()
		{
			int pending = _tasks.Values.Count( t => t.Status == TaskStatus.Pending );
			int inProg = _tasks.Values.Count( t => t.Status == TaskStatus.InProgress );
			int done = _tasks.Values.Count( t => t.Status == TaskStatus.Complete );
			int blocked = _tasks.Values.Count( t => t.Status == TaskStatus.Blocked );
			int failed = _tasks.Values.Count( t => t.Status == TaskStatus.Failed );
			int builders = _builders.Count;
			int active = _builders.Values.Count( b => b.Active );

			var sb = $"Director: {done}/{_tasks.Count} done, {inProg} in progress, {pending} pending, {blocked} blocked, {failed} failed. Builders: {active}/{builders} active.";
			foreach ( var b in _builders.Values.OrderBy( b => b.BuilderId ) )
			{
				var t = b.CurrentTaskId != null ? GetTask( b.CurrentTaskId ) : null;
				sb += $"\n  [builder {b.BuilderId}] {(b.Active ? "active" : "inactive")} — {(t != null ? $"{t.Id} ({t.BuildTask?.Name}) {t.PiecesPlaced}/{t.EstimatedPieces}" : "idle")}";
			}
			return sb;
		}

		// ── Helpers ──

		static int EstimatePieces( VillageBuildTask task )
		{
			if ( task == null )
				return 0;
			return task.TaskType switch
			{
				"wall" => 12,
				"gate" => 30,
				"road" => 8,
				"well" => 20,
				"market_square" => 25,
				_ => (int)( task.BaseWidth * task.WealthFactor ) *
					 (int)( task.BaseHeight * task.WealthFactor ),
			};
		}
	}
}
