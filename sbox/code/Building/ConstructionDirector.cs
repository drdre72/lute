using System;
using System.Collections.Generic;
using System.Linq;
using Lute.NLP;

namespace Lute.Building
{
	public enum TaskStatus
	{
		Pending = 0,
		InProgress = 1,
		Complete = 2,
		Blocked = 3,
		Failed = 4,
		Cancelled = 5,
		/// <summary>
		/// Task has been claimed and reserved, but the builder NPC has not
		/// yet arrived at the site. The executor must wait for
		/// <see cref="ConstructionDirector.AuthorizeExecution"/> before
		/// placing geometry.
		/// </summary>
		PendingExecution = 6,
	}

	/// <summary>
	/// Authoritative director-side construction task. VillageBuildTask is the
	/// executor view; DirectedTask owns scheduling, dependencies and reservation.
	/// </summary>
	public class DirectedTask
	{
		public string Id { get; set; }
		public VillageBuildTask BuildTask { get; set; }
		public TaskStatus Status { get; set; } = TaskStatus.Pending;
		public int AssignedBuilder { get; set; } = -1;
		public int EstimatedPieces { get; set; }
		public List<string> DependsOn { get; set; } = new();
		public string BlueprintId { get; set; }
		public int BlueprintVersion { get; set; }
		public string ReservationId { get; set; }
		public int RetryCount { get; set; }
		public int MaxRetries { get; set; } = 2;
		public int PiecesPlaced { get; set; }
		public Dictionary<string, string> Preconditions { get; set; } = new();
		public BBox? ReservationBounds { get; set; }
		public List<string> Conflicts { get; set; } = new();

		public bool DependenciesSatisfied =>
			DependsOn.All( depId => ConstructionDirector.GetTask( depId )?.Status == TaskStatus.Complete );

		public bool PreconditionsSatisfied => ConstructionDirector.CheckPreconditions( Preconditions );
	}

	public class BuilderState
	{
		public int BuilderId { get; set; }
		public string NpcName { get; set; }
		public string CurrentTaskId { get; set; }
		public int PiecesBuilt { get; set; }
		public bool Active { get; set; }
	}

	/// <summary>
	/// Single authoritative scheduler for construction. NPCs and executors may
	/// request work, but only the director may transition task ownership and
	/// acquire/release task reservations.
	/// </summary>
	public static class ConstructionDirector
	{
		static readonly Dictionary<string, DirectedTask> _tasks = new();
		static readonly Dictionary<int, BuilderState> _builders = new();
		static readonly Dictionary<string, List<string>> _dependents = new();
		static int _nextTaskSeq;

		public static void Reset()
		{
			ReservationManager.Reset();
			SpatialBlackboard.Clear();
			_tasks.Clear();
			_builders.Clear();
			_dependents.Clear();
			_nextTaskSeq = 0;
		}

		/// <summary>
		/// Register or reactivate a builder without destroying its current state.
		/// VillageBuilder and VillageBuilderController can safely register the same id.
		/// </summary>
		public static void RegisterBuilder( int builderId, string npcName )
		{
			if ( _builders.TryGetValue( builderId, out var existing ) )
			{
				existing.NpcName = npcName;
				existing.Active = true;
				return;
			}

			_builders[builderId] = new BuilderState
			{
				BuilderId = builderId,
				NpcName = npcName,
				Active = true,
			};
		}

		/// <summary>
		/// Deactivate a builder and safely return any unfinished task to the queue.
		/// </summary>
		public static void DeactivateBuilder( int builderId )
		{
			if ( !_builders.TryGetValue( builderId, out var state ) )
				return;

			state.Active = false;
			if ( !string.IsNullOrEmpty( state.CurrentTaskId ) &&
				_tasks.TryGetValue( state.CurrentTaskId, out var task ) &&
				( task.Status == TaskStatus.InProgress || task.Status == TaskStatus.PendingExecution ) )
			{
				ReservationManager.Release( task );
				task.Status = TaskStatus.Pending;
				task.AssignedBuilder = -1;
				state.CurrentTaskId = null;
			}

			AssignTasks();
		}

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
				PiecesPlaced = buildTask?.PiecesPlaced ?? 0,
				ReservationBounds = buildTask != null ? ReservationManager.EstimateTaskBounds( buildTask ) : null,
				// Completed persistence entries stay complete. An old in-progress
				// entry is made pending because runtime reservations do not survive reload.
				Status = buildTask?.Status == 2 ? TaskStatus.Complete : TaskStatus.Pending,
			};
			_tasks[id] = dt;

			if ( dependsOn != null )
			{
				foreach ( var dep in dependsOn )
				{
					if ( !_dependents.TryGetValue( dep, out var list ) )
					{
						list = new();
						_dependents[dep] = list;
					}
					if ( !list.Contains( id ) )
						list.Add( id );
				}
			}

			ConstructionEventBus.Fire( ConstructionEventType.TaskCreated,
				taskId: id, parameters: new() { { "name", buildTask?.Name ?? "" } } );
			return id;
		}

		public static DirectedTask GetTask( string id ) =>
			id != null && _tasks.TryGetValue( id, out var t ) ? t : null;

		/// <summary> Resolve an id first, then a unique human-readable task name for compatibility. </summary>
		public static DirectedTask ResolveTask( string key )
		{
			if ( string.IsNullOrWhiteSpace( key ) )
				return null;
			if ( _tasks.TryGetValue( key, out var direct ) )
				return direct;

			var matches = _tasks.Values.Where( t => t.BuildTask?.Name == key ).Take( 2 ).ToList();
			return matches.Count == 1 ? matches[0] : null;
		}

		public static DirectedTask FindTaskForBuildTask( VillageBuildTask buildTask ) =>
			buildTask == null ? null : _tasks.Values.FirstOrDefault( t => ReferenceEquals( t.BuildTask, buildTask ) );

		public static List<DirectedTask> AllTasks() => _tasks.Values.ToList();
		public static List<BuilderState> AllBuilders() => _builders.Values.ToList();

		public static void AssignTasks()
		{
			foreach ( var t in _tasks.Values )
			{
				if ( t.Status == TaskStatus.Pending && !t.DependenciesSatisfied )
					t.Status = TaskStatus.Blocked;
				else if ( t.Status == TaskStatus.Blocked && t.DependenciesSatisfied )
					t.Status = TaskStatus.Pending;
			}

			var activeBuilders = _builders.Values.Where( b => b.Active ).ToList();
			if ( activeBuilders.Count == 0 )
				return;

			// Remove assignments to inactive builders so work can be rebalanced.
			var activeIds = activeBuilders.Select( b => b.BuilderId ).ToHashSet();
			foreach ( var t in _tasks.Values.Where( t =>
				t.Status == TaskStatus.Pending && t.AssignedBuilder >= 0 && !activeIds.Contains( t.AssignedBuilder ) ) )
				t.AssignedBuilder = -1;

			var load = activeBuilders.ToDictionary( b => b.BuilderId, b =>
				_tasks.Values.Where( t => t.AssignedBuilder == b.BuilderId &&
					t.Status != TaskStatus.Complete && t.Status != TaskStatus.Cancelled && t.Status != TaskStatus.Failed )
				.Sum( t => t.EstimatedPieces ) );
			// PendingExecution tasks are already claimed and walking � exclude from rebalancing.
			var pendingExec = _tasks.Values
				.Where( t => t.Status == TaskStatus.PendingExecution )
				.Select( t => t.Id )
				.ToHashSet();

			var assignable = _tasks.Values
				.Where( t => t.Status == TaskStatus.Pending && t.AssignedBuilder == -1 )
				.OrderByDescending( t => t.EstimatedPieces )
				.ThenBy( t => t.Id )
				.ToList();
			// PendingExecution tasks keep their assignment � don't reassign them.
			foreach ( var t in _tasks.Values.Where( t => t.Status == TaskStatus.PendingExecution ) )
				pendingExec.Add( t.Id );

			foreach ( var t in assignable )
			{
				int target = load.OrderBy( kvp => kvp.Value ).ThenBy( kvp => kvp.Key ).First().Key;
				t.AssignedBuilder = target;
				load[target] += t.EstimatedPieces;
				ConstructionEventBus.Fire( ConstructionEventType.TaskAssigned,
					taskId: t.Id,
					target: _builders.TryGetValue( target, out var b ) ? b.NpcName : target.ToString() );
			}
		}

		public static DirectedTask ClaimNextTask( int builderId, string npcName )
		{
			if ( !_builders.TryGetValue( builderId, out var state ) || !state.Active )
				return null;

			// A builder may own only one in-progress task at a time.
			if ( !string.IsNullOrEmpty( state.CurrentTaskId ) )
			{
				var current = GetTask( state.CurrentTaskId );
				if ( current?.Status == TaskStatus.InProgress || current?.Status == TaskStatus.PendingExecution )
					return current;
				state.CurrentTaskId = null;
			}

			var task = _tasks.Values
				.Where( t => t.AssignedBuilder == builderId &&
					t.Status == TaskStatus.Pending && t.DependenciesSatisfied && t.PreconditionsSatisfied )
				.OrderBy( t => t.Id )
				.FirstOrDefault();

			if ( task == null )
				task = StealTask( builderId );
			if ( task == null )
				return null;

			var reservation = ReservationManager.TryAcquire( task, npcName );
			if ( !reservation.Success )
			{
				Log.Info( $"Lute: ConstructionDirector could not reserve {task.Id} for {npcName}: {reservation.Reason}." );
				return null;
			}

			task.ReservationId = reservation.ReservationId;
			task.Status = TaskStatus.PendingExecution;
			task.AssignedBuilder = builderId;
			state.CurrentTaskId = task.Id;

			ConstructionEventBus.Fire( ConstructionEventType.TaskStarted,
				taskId: task.Id, actor: npcName );
			return task;
		}

		/// <summary>
		/// Called by the VillageBuilderController when its NPC has arrived at the
		/// build site. Transitions a PendingExecution task to InProgress so the
		/// executor (VillageBuilder) may begin placing geometry.
		/// </summary>
		public static bool AuthorizeExecution( string taskId, string npcName )
		{
			if ( !_tasks.TryGetValue( taskId, out var task ) )
				return false;
			if ( task.Status != TaskStatus.PendingExecution )
				return false;

			int builderId = FindBuilderByNpc( npcName );
			if ( task.AssignedBuilder != builderId )
				return false;

			task.Status = TaskStatus.InProgress;
			Log.Info( $"Lute: ConstructionDirector authorized execution of {taskId} for {npcName}." );
			return true;
		}

		/// <summary>
		/// Check whether a task is ready for the executor to start building.
		/// The executor should poll this after claiming a task and wait until
		/// it returns true before placing any geometry.
		/// </summary>
		public static bool IsExecutionAuthorized( string taskId )
		{
			if ( !_tasks.TryGetValue( taskId, out var task ) )
				return false;
			return task.Status == TaskStatus.InProgress;
		}

		static DirectedTask StealTask( int builderId )
		{
			var other = _builders.Values
				.Where( b => b.BuilderId != builderId && b.Active )
				.OrderByDescending( b => _tasks.Values.Count( t =>
					t.AssignedBuilder == b.BuilderId && t.Status == TaskStatus.Pending ) )
				.ThenBy( b => b.BuilderId )
				.FirstOrDefault();
			if ( other == null )
				return null;

			var victim = _tasks.Values
				.Where( t => t.AssignedBuilder == other.BuilderId &&
					t.Status == TaskStatus.Pending && t.DependenciesSatisfied && t.PreconditionsSatisfied )
				.OrderBy( t => t.EstimatedPieces )
				.ThenBy( t => t.Id )
				.FirstOrDefault();
			if ( victim == null )
				return null;

			victim.AssignedBuilder = builderId;
			return victim;
		}

		public static void ReportProgress( string taskId, int piecesPlaced )
		{
			if ( !_tasks.TryGetValue( taskId, out var t ) )
				return;
			t.PiecesPlaced = piecesPlaced;
			if ( _builders.TryGetValue( t.AssignedBuilder, out var b ) )
				b.PiecesBuilt = piecesPlaced;
		}

		public static void CompleteTask( string taskId )
		{
			if ( !_tasks.TryGetValue( taskId, out var t ) )
				return;
			if ( t.Status == TaskStatus.Complete )
				return;

			t.Status = TaskStatus.Complete;
			if ( t.BuildTask != null )
				t.PiecesPlaced = Math.Max( t.PiecesPlaced, t.BuildTask.PiecesPlaced );
			if ( t.AssignedBuilder >= 0 && _builders.TryGetValue( t.AssignedBuilder, out var b ) )
				b.CurrentTaskId = null;

			ReservationManager.Release( t );
			ConstructionEventBus.Fire( ConstructionEventType.TaskCompleted,
				taskId: t.Id,
				actor: _builders.TryGetValue( t.AssignedBuilder, out var cb ) ? cb.NpcName : null );

			// Completing a dependency can make blocked work runnable immediately.
			AssignTasks();
		}

		public static void FailTask( string taskId, string reason = null )
		{
			if ( !_tasks.TryGetValue( taskId, out var t ) )
				return;

			ReservationManager.Release( t );
			if ( t.AssignedBuilder >= 0 && _builders.TryGetValue( t.AssignedBuilder, out var b ) )
				b.CurrentTaskId = null;

			t.RetryCount++;
			if ( t.RetryCount <= t.MaxRetries )
			{
				t.Status = TaskStatus.Pending;
				Log.Info( $"Lute: ConstructionDirector task {taskId} failed ({reason}) — retry {t.RetryCount}/{t.MaxRetries}." );
				ConstructionEventBus.Fire( ConstructionEventType.TaskBlocked,
					taskId: t.Id,
					parameters: new() { { "reason", reason ?? "" }, { "retry", t.RetryCount.ToString() } } );
			}
			else
			{
				t.Status = TaskStatus.Failed;
				Log.Warning( $"Lute: ConstructionDirector task {taskId} failed permanently ({reason}) after {t.RetryCount} attempts." );
				ConstructionEventBus.Fire( ConstructionEventType.TaskFailed,
					taskId: t.Id, parameters: new() { { "reason", reason ?? "" } } );
			}
		}

		public static void CancelTask( string taskId )
		{
			if ( !_tasks.TryGetValue( taskId, out var t ) )
				return;
			ReservationManager.Release( t );
			t.Status = TaskStatus.Cancelled;
			if ( t.AssignedBuilder >= 0 && _builders.TryGetValue( t.AssignedBuilder, out var b ) )
				b.CurrentTaskId = null;
		}

		public static List<DirectedTask> TasksNeedingReconstruction() => _tasks.Values
			.Where( t => t.Status == TaskStatus.Complete )
			.OrderBy( t => t.Id )
			.ToList();

		public static string StatusSummary()
		{
			int pending = _tasks.Values.Count( t => t.Status == TaskStatus.Pending );
			int pendingExec = _tasks.Values.Count( t => t.Status == TaskStatus.PendingExecution );
			int inProg = _tasks.Values.Count( t => t.Status == TaskStatus.InProgress );
			int done = _tasks.Values.Count( t => t.Status == TaskStatus.Complete );
			int blocked = _tasks.Values.Count( t => t.Status == TaskStatus.Blocked );
			int failed = _tasks.Values.Count( t => t.Status == TaskStatus.Failed );
			int active = _builders.Values.Count( b => b.Active );

			var summary = $"Director: {done}/{_tasks.Count} done, {inProg} building, {pendingExec} walking, {pending} pending, {blocked} blocked, {failed} failed. Builders: {active}/{_builders.Count} active.";
			foreach ( var b in _builders.Values.OrderBy( b => b.BuilderId ) )
			{
				var t = b.CurrentTaskId != null ? GetTask( b.CurrentTaskId ) : null;
				summary += $"\n  [builder {b.BuilderId}] {(b.Active ? "active" : "inactive")} — {(t != null ? $"{t.Id} ({t.BuildTask?.Name}) {t.PiecesPlaced}/{t.EstimatedPieces}" : "idle")}";
			}
			return summary;
		}

		public static bool CheckPreconditions( Dictionary<string, string> preconditions )
		{
			if ( preconditions == null || preconditions.Count == 0 )
				return true;

			foreach ( var (key, value) in preconditions )
			{
				if ( key == "foundation.exists" && value == "true" )
				{
					bool hasFoundation = _tasks.Values.Any( t =>
						t.Status == TaskStatus.Complete && t.BuildTask?.TaskType == "foundation" );
					if ( !hasFoundation ) return false;
				}
				else if ( key == "area.available" )
				{
					var parts = value.Split( ',' );
					if ( parts.Length >= 6 &&
						float.TryParse( parts[0], out float x ) &&
						float.TryParse( parts[1], out float y ) &&
						float.TryParse( parts[2], out float z ) &&
						float.TryParse( parts[3], out float w ) &&
						float.TryParse( parts[4], out float h ) &&
						float.TryParse( parts[5], out float d ) )
					{
						var center = new Vector3( x, y, z );
						var bounds = new BBox(
							center - new Vector3( w * 0.5f, h * 0.5f, d * 0.5f ),
							center + new Vector3( w * 0.5f, h * 0.5f, d * 0.5f ) );
						if ( SpatialBlackboard.CheckClearBox( bounds ) != null ) return false;
					}
				}
				else if ( key.StartsWith( "materials." ) )
				{
					// Material inventory integration is intentionally a separate system.
				}
			}
			return true;
		}

		public static BlackboardResult ProcessRequest( BlackboardRequest request )
		{
			if ( request == null )
				return BlackboardResult.Fail( "null request" );

			return request.Operation switch
			{
				BlackboardOperation.Read => HandleRead( request ),
				BlackboardOperation.Claim => HandleClaim( request ),
				BlackboardOperation.Release => HandleRelease( request ),
				BlackboardOperation.Update => HandleUpdate( request ),
				BlackboardOperation.Request => HandleRequest( request ),
				BlackboardOperation.Offer => HandleOffer( request ),
				BlackboardOperation.Complete => HandleComplete( request ),
				BlackboardOperation.Fail => HandleFail( request ),
				_ => BlackboardResult.Fail( $"unknown operation: {request.Operation}" ),
			};
		}

		static BlackboardResult HandleRead( BlackboardRequest req )
		{
			var task = ResolveTask( req.Key );
			if ( task != null ) return BlackboardResult.Ok( task.Status.ToString() );
			if ( req.Key == "summary" ) return BlackboardResult.Ok( StatusSummary() );
			return BlackboardResult.Fail( $"key not found: {req.Key}" );
		}

		static BlackboardResult HandleClaim( BlackboardRequest req )
		{
			var task = ResolveTask( req.Key );
			if ( task == null ) return BlackboardResult.Fail( $"task not found or ambiguous: {req.Key}" );

			// Claim is idempotent for the builder that already owns the director task.
			if ( task.Status == TaskStatus.InProgress || task.Status == TaskStatus.PendingExecution )
			{
				var ownership = ReservationManager.ValidateOwnership( task, req.Actor );
				return ownership.Success
					? BlackboardResult.Ok( ownership.ReservationId )
					: BlackboardResult.Fail( ownership.Reason );
			}

			if ( task.Status != TaskStatus.Pending )
				return BlackboardResult.Fail( $"task {task.Id} is not pending (status={task.Status})" );
			if ( !task.DependenciesSatisfied )
				return BlackboardResult.Fail( $"task {task.Id} has unsatisfied dependencies" );
			if ( !task.PreconditionsSatisfied )
				return BlackboardResult.Fail( $"task {task.Id} has unsatisfied preconditions" );

			var reservation = ReservationManager.TryAcquire( task, req.Actor );
			if ( !reservation.Success )
				return BlackboardResult.Fail( $"{reservation.Reason}{(reservation.BlockedBy != null ? $" by {reservation.BlockedBy}" : "")}" );

			task.Status = TaskStatus.PendingExecution;
			task.ReservationId = reservation.ReservationId;
			int builderId = FindBuilderByNpc( req.Actor );
			if ( builderId >= 0 )
			{
				task.AssignedBuilder = builderId;
				_builders[builderId].CurrentTaskId = task.Id;
			}

			ConstructionEventBus.Fire( ConstructionEventType.TaskClaimed,
				taskId: task.Id, actor: req.Actor );
			return BlackboardResult.Ok( reservation.ReservationId );
		}

		static BlackboardResult HandleRelease( BlackboardRequest req )
		{
			var task = ResolveTask( req.Key );
			if ( task == null ) return BlackboardResult.Fail( $"task not found or ambiguous: {req.Key}" );

			ReservationManager.Release( task );
			task.Status = TaskStatus.Pending;
			if ( task.AssignedBuilder >= 0 && _builders.TryGetValue( task.AssignedBuilder, out var b ) )
				b.CurrentTaskId = null;
			ConstructionEventBus.Fire( ConstructionEventType.ReservationReleased,
				taskId: task.Id, actor: req.Actor );
			return BlackboardResult.Ok();
		}

		static BlackboardResult HandleUpdate( BlackboardRequest req )
		{
			var task = ResolveTask( req.Key );
			if ( task == null ) return BlackboardResult.Fail( $"task not found or ambiguous: {req.Key}" );
			if ( req.Value is int pieces )
			{
				ReportProgress( task.Id, pieces );
				return BlackboardResult.Ok( pieces );
			}
			return BlackboardResult.Ok();
		}

		static BlackboardResult HandleRequest( BlackboardRequest req )
		{
			int builderId = FindBuilderByNpc( req.Actor );
			if ( builderId < 0 ) return BlackboardResult.Fail( $"builder not registered: {req.Actor}" );
			var task = ClaimNextTask( builderId, req.Actor );
			return task == null ? BlackboardResult.Fail( "no available tasks" ) : BlackboardResult.Ok( task.Id );
		}

		static BlackboardResult HandleOffer( BlackboardRequest req )
		{
			ConstructionEventBus.Fire( ConstructionEventType.NpcOfferedHelp,
				taskId: req.Key, actor: req.Actor,
				parameters: req.Value != null ? new() { { "offer", req.Value.ToString() } } : null );
			return BlackboardResult.Ok();
		}

		static BlackboardResult HandleComplete( BlackboardRequest req )
		{
			var task = ResolveTask( req.Key );
			if ( task == null ) return BlackboardResult.Fail( $"task not found or ambiguous: {req.Key}" );
			CompleteTask( task.Id );
			return BlackboardResult.Ok();
		}

		static BlackboardResult HandleFail( BlackboardRequest req )
		{
			var task = ResolveTask( req.Key );
			if ( task == null ) return BlackboardResult.Fail( $"task not found or ambiguous: {req.Key}" );
			FailTask( task.Id, req.Value?.ToString() );
			return BlackboardResult.Ok();
		}

		static int FindBuilderByNpc( string npcName )
		{
			foreach ( var kvp in _builders )
				if ( kvp.Value.NpcName == npcName ) return kvp.Key;
			return -1;
		}

		static int EstimatePieces( VillageBuildTask task )
		{
			if ( task == null ) return 0;
			return task.TaskType switch
			{
				"wall" => 12,
				"gate" => 30,
				"road" => 8,
				"well" => 20,
				"market_square" => 25,
				_ => (int)( task.BaseWidth * task.WealthFactor ) * (int)( task.BaseHeight * task.WealthFactor ),
			};
		}
	}
}
