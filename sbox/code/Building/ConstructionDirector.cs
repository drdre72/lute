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
		public int MaxRetries { get; set; } = 10;
		public string BlockedByTaskId { get; set; }
		/// <summary>
		/// First unsatisfied prerequisite task id when this task is blocked by
		/// the dependency graph (distinct from <see cref="BlockedByTaskId"/>,
		/// which records reservation/runtime conflicts). Null when the task
		/// is not dependency-blocked.
		/// </summary>
		public string BlockedByDependency { get; set; }
		public int PiecesPlaced { get; set; }
		public Dictionary<string, string> Preconditions { get; set; } = new();
		public BBox? ReservationBounds { get; set; }
		public List<string> Conflicts { get; set; } = new();

		/// <summary>
		/// Material requirements for this task. When set, the task
		/// cannot start (claim proceeds but execution is gated) until
		/// all requirements are satisfied via <see cref="LogisticsBoard"/>
		/// deliveries. Null = no material requirements (backward
		/// compatibility with existing tasks).
		/// </summary>
		public List<MaterialRequirement> MaterialRequirements { get; set; }

		/// <summary>
		/// True when all material requirements are satisfied (or there
		/// are none). Distinct from <see cref="DependenciesSatisfied"/>
		/// (task prerequisites) and <see cref="PreconditionsSatisfied"/>
		/// (spatial preconditions).
		/// </summary>
		public bool MaterialsSatisfied =>
			MaterialRequirements == null ||
			MaterialRequirements.All( r => r.Satisfied );

		/// <summary>
		/// 8-digit spatial grid key (XXXXYYYY) for contiguous wall assignment.
		/// Computed from the build task position. Used to order tasks so
		/// builders work on adjacent wall segments instead of jumping around.
		/// </summary>
		public string Grid8 { get; set; }

		public bool DependenciesSatisfied =>
			DependsOn.All( depId => ConstructionDirector.GetTask( depId )?.Status == TaskStatus.Complete );

		public bool PreconditionsSatisfied => ConstructionDirector.CheckPreconditions( Preconditions );

		/// <summary>
		/// Compute an 8-digit spatial grid key from a world position.
		/// Format: XXXXYYYY where each is 4 digits of position in decimeters
		/// (10 units = 1 dm). This gives a deterministic spatial sort key
		/// so wall segments near each other sort together.
		/// </summary>
		public static string ComputeGrid8( Vector3 pos )
		{
			int x = (int)( pos.x / 10f );
			int y = (int)( pos.y / 10f );
			// Clamp to 4 digits (0..9999), wrap negatives
			x = ( ( x % 10000 ) + 10000 ) % 10000;
			y = ( ( y % 10000 ) + 10000 ) % 10000;
			return $"{x:D4}{y:D4}";
		}
	}

	/// <summary>
	/// Per-builder runtime state.
	/// </summary>
	public class BuilderState
	{
		public int BuilderId { get; set; }
		public string NpcName { get; set; }
		public string CurrentTaskId { get; set; }
		public int PiecesBuilt { get; set; }
		public string LastCompletedTaskId { get; set; }
		public bool Active { get; set; }

		/// <summary>
		/// Profession id (e.g. "mason", "carpenter"). Defaults to "builder"
		/// (GeneralConstruction) for backward compatibility with existing
		/// VillageBuilder NPCs that don't have a profession profile.
		/// </summary>
		public string ProfessionId { get; set; } = "builder";

		/// <summary>
		/// Capabilities this builder possesses, derived from
		/// <see cref="ProfessionId"/> via <see cref="CapabilityRegistry"/>.
		/// Cached on registration for fast eligibility checks.
		/// </summary>
		public HashSet<NpcCapability> Capabilities { get; set; } = new() { NpcCapability.GeneralConstruction };
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

		/// <summary>
		/// When true, <see cref="ClaimNextTask"/> will only claim tasks
		/// whose <see cref="DirectedTask.MaterialsSatisfied"/> is true.
		/// Default false — material requirements are computed and
		/// tracked but do not gate construction until hauler NPCs are
		/// active and the logistics loop is proven. Flip to true when
		/// the autonomous-town benchmark runs with haulers.
		/// </summary>
		public static bool EnforceMaterialGating { get; set; } = false;

		public static void Reset()
		{
			ReservationManager.Reset();
			SpatialBlackboard.Clear();
			_tasks.Clear();
			_builders.Clear();
			_dependents.Clear();
			_nextTaskSeq = 0;
			WorldFactProvider.Reset();
			LogisticsBoard.Clear();
		}

		/// <summary>
		/// Register or reactivate a builder without destroying its current state.
		/// VillageBuilder and VillageBuilderController can safely register the same id.
		/// </summary>
		public static void RegisterBuilder( int builderId, string npcName, string professionId = null )
		{
			if ( _builders.TryGetValue( builderId, out var existing ) )
			{
				existing.NpcName = npcName;
				existing.Active = true;
				if ( !string.IsNullOrEmpty( professionId ) )
				{
					existing.ProfessionId = professionId;
					existing.Capabilities = CapabilityRegistry.CapabilitiesForProfession( professionId );
				}
				return;
			}

			var prof = !string.IsNullOrEmpty( professionId ) ? professionId : "builder";
			_builders[builderId] = new BuilderState
			{
				BuilderId = builderId,
				NpcName = npcName,
				Active = true,
				ProfessionId = prof,
				Capabilities = CapabilityRegistry.CapabilitiesForProfession( prof ),
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

		/// <summary>
		/// Compute default material requirements for a task based on its
		/// TaskType and estimated piece count. Masonry tasks (wall, gate,
		/// road, well, chapel) need Brick; wooden tasks (cottage, shop,
		/// tavern, storage) need Plank + Timber. Returns null for unknown
		/// types or zero-piece tasks (backward compatibility — no
		/// material gating).
		/// </summary>
		static List<MaterialRequirement> ComputeMaterialRequirements( DirectedTask dt )
		{
			if ( dt?.BuildTask == null || dt.EstimatedPieces <= 0 )
				return null;

			var taskType = dt.BuildTask.TaskType;
			var pieces = dt.EstimatedPieces;

			// Scale: roughly 1 material unit per 50 pieces (tunable)
			int brickAmount = 0, plankAmount = 0, timberAmount = 0;

			switch ( taskType )
			{
				case "wall":
				case "gate":
				case "road":
				case "well":
				case "chapel":
				case "smithy":
					brickAmount = Math.Max( 1, pieces / 50 );
					break;
				case "cottage":
				case "shop":
				case "tavern":
				case "storage":
				case "guardhouse":
					plankAmount = Math.Max( 1, pieces / 60 );
					timberAmount = Math.Max( 1, pieces / 200 );
					break;
				case "market_square":
					// Market is mostly ground work — minimal materials
					brickAmount = Math.Max( 1, pieces / 100 );
					break;
				default:
					return null; // unknown type = no requirements
			}

			var reqs = new List<MaterialRequirement>();
			if ( brickAmount > 0 )
				reqs.Add( new MaterialRequirement { Type = ResourceType.Brick, Amount = brickAmount } );
			if ( plankAmount > 0 )
				reqs.Add( new MaterialRequirement { Type = ResourceType.Plank, Amount = plankAmount } );
			if ( timberAmount > 0 )
				reqs.Add( new MaterialRequirement { Type = ResourceType.Timber, Amount = timberAmount } );

			return reqs.Count > 0 ? reqs : null;
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
				Grid8 = buildTask != null ? DirectedTask.ComputeGrid8( buildTask.Position ) : "",
				// Completed persistence entries stay complete. An old in-progress
				// entry is made pending because runtime reservations do not survive reload.
				Status = buildTask?.Status == 2 ? TaskStatus.Complete : TaskStatus.Pending,
			};

			// Assign default material requirements based on task type.
			// This connects the construction pipeline to the resource
			// loop — tasks need materials, LogisticsBoard creates haul
			// jobs to supply them. Null = no requirements (backward
			// compatibility for tasks without a known type).
			dt.MaterialRequirements = ComputeMaterialRequirements( dt );

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

			// Cycle detection: if adding this task's dependencies creates a
			// cycle in the prerequisite graph, reject the registration
			// deterministically rather than allowing a silent deadlock.
			if ( dependsOn != null && dependsOn.Count > 0 && DetectCycleFrom( id, out string cyclePath ) )
			{
				_tasks.Remove( id );
				foreach ( var dep in dependsOn )
				{
					if ( _dependents.TryGetValue( dep, out var list ) )
						list.Remove( id );
				}
				_nextTaskSeq--;
				Log.Warning( $"Lute: ConstructionDirector rejected cyclic task '{buildTask?.Name}' — cycle: {cyclePath}" );
				return null;
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
				{
					t.Status = TaskStatus.Blocked;
					t.BlockedByDependency = GetBlockingDependency( t.Id );
				}
				else if ( t.Status == TaskStatus.Blocked && t.DependenciesSatisfied )
				{
					// If blocked by another task, only unblock when the blocker
					// is complete or failed (no longer active).
					if ( !string.IsNullOrEmpty( t.BlockedByTaskId ) &&
						_tasks.TryGetValue( t.BlockedByTaskId, out var blocker ) )
					{
						if ( blocker.Status == TaskStatus.Complete || blocker.Status == TaskStatus.Failed || blocker.Status == TaskStatus.Cancelled )
						{
							t.Status = TaskStatus.Pending;
							t.BlockedByTaskId = null;
							t.BlockedByDependency = null;
						}
					}
					else
					{
						t.Status = TaskStatus.Pending;
						t.BlockedByTaskId = null;
						t.BlockedByDependency = null;
					}
				}
			}

			var activeBuilders = _builders.Values.Where( b => b.Active ).ToList();
			if ( activeBuilders.Count == 0 )
				return;

			// Remove assignments to inactive builders so work can be rebalanced.
			var activeIds = activeBuilders.Select( b => b.BuilderId ).ToHashSet();
			foreach ( var t in _tasks.Values.Where( t =>
				t.Status == TaskStatus.Pending && t.AssignedBuilder >= 0 && !activeIds.Contains( t.AssignedBuilder ) ) )
				t.AssignedBuilder = -1;

			// PendingExecution and InProgress tasks are already claimed -
			// exclude them from rebalancing.
			var locked = _tasks.Values
				.Where( t => t.Status == TaskStatus.PendingExecution || t.Status == TaskStatus.InProgress )
				.Select( t => t.Id )
				.ToHashSet();

			var load = activeBuilders.ToDictionary( b => b.BuilderId, b =>
				_tasks.Values.Where( t => t.AssignedBuilder == b.BuilderId &&
					t.Status != TaskStatus.Complete && t.Status != TaskStatus.Cancelled && t.Status != TaskStatus.Failed )
				.Sum( t => t.EstimatedPieces ) );

			// Rebalance: if any builder has more than its fair share of
			// pending tasks, release the excess so they can be reassigned
			// to less-loaded builders. This handles the case where all
			// tasks were initially assigned to builder 0 before other
			// builders registered.
			int totalPendingPieces = _tasks.Values
				.Where( t => t.Status == TaskStatus.Pending && !locked.Contains( t.Id ) )
				.Sum( t => t.EstimatedPieces );
			int avgPerBuilder = activeBuilders.Count > 0
				? totalPendingPieces / activeBuilders.Count : 0;

			foreach ( var b in activeBuilders )
			{
				var pending = _tasks.Values
					.Where( t => t.AssignedBuilder == b.BuilderId &&
						t.Status == TaskStatus.Pending && !locked.Contains( t.Id ) )
					.ToList();

				// Group pending tasks by wall line so we release entire wall
				// lines at a time, keeping wall-line groups intact.
				var rebalanceWallGroups = pending
					.Where( t => t.BuildTask != null && t.BuildTask.Name.StartsWith( "Wall_" ) )
					.GroupBy( t => ExtractWallLine( t.BuildTask.Name ) ?? "Wall_?" )
					.OrderBy( g => g.Key )
					.ToList();

				var nonWall = pending
					.Where( t => t.BuildTask == null || !t.BuildTask.Name.StartsWith( "Wall_" ) )
					.OrderBy( t => t.Grid8 ?? "" )
					.ThenBy( t => t.Id )
					.ToList();

				int kept = load[b.BuilderId];
				// Release entire wall line groups first (biggest groups first
				// to minimize the number of groups released).
				foreach ( var group in rebalanceWallGroups.OrderByDescending( g => g.Sum( t => t.EstimatedPieces ) ) )
				{
					if ( kept <= avgPerBuilder ) break;
					int groupPieces = group.Sum( t => t.EstimatedPieces );
					if ( kept - groupPieces >= 0 || kept > avgPerBuilder )
				{
						foreach ( var t in group )
						{
							t.AssignedBuilder = -1;
						}
						kept -= groupPieces;
				}
				}
				// Release non-wall tasks individually.
				foreach ( var t in nonWall )
				{
					if ( kept <= avgPerBuilder ) break;
					t.AssignedBuilder = -1;
					kept -= t.EstimatedPieces;
				}
			}

			// Recalculate load after rebalancing.
			load = activeBuilders.ToDictionary( b => b.BuilderId, b =>
				_tasks.Values.Where( t => t.AssignedBuilder == b.BuilderId &&
					t.Status != TaskStatus.Complete && t.Status != TaskStatus.Cancelled && t.Status != TaskStatus.Failed )
				.Sum( t => t.EstimatedPieces ) );

			var assignable = _tasks.Values
				.Where( t => t.Status == TaskStatus.Pending && t.AssignedBuilder == -1 && !locked.Contains( t.Id ) )
				.ToList();

			// Group assignable wall tasks by wall line (Wall_N, Wall_E, Wall_S, Wall_W)
			// so each builder gets entire wall lines instead of scattered segments.
			// Non-wall tasks are assigned individually by load.
			var wallGroups = assignable
				.Where( t => t.BuildTask != null && t.BuildTask.Name.StartsWith( "Wall_" ) )
				.GroupBy( t => ExtractWallLine( t.BuildTask.Name ) ?? "Wall_?" )
				.OrderBy( g => g.Key )
				.ToList();

			var nonWallTasks = assignable
				.Where( t => t.BuildTask == null || !t.BuildTask.Name.StartsWith( "Wall_" ) )
				.OrderBy( t => t.Grid8 ?? "" )
				.ThenBy( t => t.Id )
				.ToList();

			// Assign entire wall line groups to the least-loaded builder.
			foreach ( var group in wallGroups )
			{
				int groupPieces = group.Sum( t => t.EstimatedPieces );
				int target = load.OrderBy( kvp => kvp.Value ).ThenBy( kvp => kvp.Key ).First().Key;
				foreach ( var t in group )
				{
					t.AssignedBuilder = target;
					ConstructionEventBus.Fire( ConstructionEventType.TaskAssigned,
						taskId: t.Id,
						target: _builders.TryGetValue( target, out var b ) ? b.NpcName : target.ToString() );
				}
				load[target] += groupPieces;
			}

			// Assign non-wall tasks by load balancing.
			foreach ( var t in nonWallTasks )
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

			// Capability filter: a builder can only claim tasks whose
			// TaskType is within its capability set. This is the
			// authoritative eligibility check (per PR #6 §3) — not role
			// strings. GeneralConstruction (the default "builder"
			// profession) can do anything in the current village task
			// list, so existing NPCs remain backward-compatible.
			var builderCaps = state.Capabilities ?? new HashSet<NpcCapability> { NpcCapability.GeneralConstruction };
			bool CanPerform( DirectedTask t ) =>
				t.BuildTask == null ||
				CapabilityRegistry.CanPerformTask( builderCaps, t.BuildTask.TaskType );

			// Material gating: when EnforceMaterialGating is true, only
			// claim tasks whose material requirements are satisfied.
			// Default false — existing builds continue without haulers.
			bool MaterialsOk( DirectedTask t ) =>
				!EnforceMaterialGating || t.MaterialsSatisfied;

			// Build a candidate list: tasks assigned to this builder first,
			// then stealable tasks from other builders, then unassigned tasks.
			// We try each candidate until one reserves successfully, so a
			// spatial reservation conflict on one task doesn't block the
			// builder from working on a different task.
			// Wall-line affinity: prefer tasks on the same wall line as the
			// builder's last completed task so the builder finishes one wall
			// line before moving to the next. This ensures both walls at a
			// corner get built, enabling corner topology validation.
			string lastWallLine = null;
			Vector3? lastPos = null;
			if ( _builders.TryGetValue( builderId, out var bs ) && !string.IsNullOrEmpty( bs.LastCompletedTaskId ) )
			{
				var lastTask = GetTask( bs.LastCompletedTaskId );
				if ( lastTask?.BuildTask != null )
				{
					lastPos = lastTask.BuildTask.Position;
					lastWallLine = ExtractWallLine( lastTask.BuildTask.Name );
				}
			}

			var candidates = _tasks.Values
				.Where( t => t.AssignedBuilder == builderId &&
					t.Status == TaskStatus.Pending && t.DependenciesSatisfied && t.PreconditionsSatisfied &&
					CanPerform( t ) && MaterialsOk( t ) )
				.OrderBy( t =>
				{
					// Same wall line as last task = 0, different = 1
					string thisLine = t.BuildTask != null ? ExtractWallLine( t.BuildTask.Name ) : null;
					return ( lastWallLine != null && thisLine == lastWallLine ) ? 0 : 1;
				} )
				.ThenBy( t =>
				{
					// Within the same wall line, order by segment index ascending
					// so the builder starts from index 0 and works to the end.
					string thisLine = t.BuildTask != null ? ExtractWallLine( t.BuildTask.Name ) : null;
					if ( lastWallLine != null && thisLine == lastWallLine )
					{
						int segIdx = ExtractWallSegmentIndex( t.BuildTask?.Name ?? "" );
						return segIdx >= 0 ? segIdx : 99999;
					}
					return 99999;
				} )
				.ThenBy( t => t.Grid8 ?? "" )
				.ThenBy( t => t.Id )
				.ToList();

			// Add stealable tasks from other builders, preferring same wall line.
			foreach ( var t in _tasks.Values
				.Where( t => t.AssignedBuilder != builderId && t.AssignedBuilder >= 0 &&
					t.Status == TaskStatus.Pending && t.DependenciesSatisfied && t.PreconditionsSatisfied &&
					CanPerform( t ) && MaterialsOk( t ) )
				.OrderBy( t =>
				{
					string thisLine = t.BuildTask != null ? ExtractWallLine( t.BuildTask.Name ) : null;
					return ( lastWallLine != null && thisLine == lastWallLine ) ? 0 : 1;
				} )
				.ThenBy( t => t.EstimatedPieces ).ThenBy( t => t.Id ) )
			{
				if ( !candidates.Contains( t ) )
					candidates.Add( t );
			}

			// Add unassigned tasks, preferring same wall line.
			foreach ( var t in _tasks.Values
				.Where( t => t.AssignedBuilder == -1 &&
					t.Status == TaskStatus.Pending && t.DependenciesSatisfied && t.PreconditionsSatisfied &&
					CanPerform( t ) && MaterialsOk( t ) )
				.OrderBy( t =>
				{
					string thisLine = t.BuildTask != null ? ExtractWallLine( t.BuildTask.Name ) : null;
					return ( lastWallLine != null && thisLine == lastWallLine ) ? 0 : 1;
				} )
				.ThenBy( t => t.Id ) )
			{
				if ( !candidates.Contains( t ) )
					candidates.Add( t );
			}

			foreach ( var task in candidates )
			{
				// Ghost-placement validation: check if the structure can be placed
				// without colliding with existing geometry before reserving.
				if ( task.BuildTask != null )
				{
					var validation = ReservationManager.ValidatePlacement(
						task.BuildTask.Position, task.BuildTask.Rotation,
						task.BuildTask.TaskType, excludeTaskId: task.Id );
					if ( !validation.IsValid )
					{
						Log.Info( $"Lute: ConstructionDirector placement validation failed for {task.Id} ({task.BuildTask?.Name}): {validation.Reason} blocked_by={validation.BlockingEntity} intersection={validation.IntersectionVolume:F1}" );
						if ( validation.NearestValidPosition.HasValue )
						{
							Log.Info( $"  Nearest valid position: {validation.NearestValidPosition.Value}" );
						}
						continue;
					}
				}

				var reservation = ReservationManager.TryAcquire( task, npcName );
				if ( !reservation.Success )
				{
					Log.Info( $"Lute: ConstructionDirector could not reserve {task.Id} for {npcName}: {reservation.Reason}." );
					continue;
				}

				task.ReservationId = reservation.ReservationId;
				task.Status = TaskStatus.PendingExecution;
				task.AssignedBuilder = builderId;
				state.CurrentTaskId = task.Id;

				ConstructionEventBus.Fire( ConstructionEventType.TaskStarted,
					taskId: task.Id, actor: npcName );
				return task;
			}

			return null;
		}

		/// <summary>
		/// Extract the wall line prefix from a task name (e.g. "Wall_E_105" -> "Wall_E").
		/// Returns null for non-wall tasks.
		/// </summary>
		static string ExtractWallLine( string name )
		{
			if ( string.IsNullOrEmpty( name ) ) return null;
			if ( !name.StartsWith( "Wall_" ) ) return null;
			// Format: Wall_N_0, Wall_E_105, Wall_S_8, Wall_W_139
			var parts = name.Split( '_' );
			if ( parts.Length >= 2 )
				return $"{parts[0]}_{parts[1]}";
			return name;
		}

		/// <summary>
		/// Extract the segment index from a wall task name (e.g. "Wall_E_105" -> 105).
		/// Returns -1 for non-wall tasks.
		/// </summary>
		static int ExtractWallSegmentIndex( string name )
		{
			if ( string.IsNullOrEmpty( name ) ) return -1;
			if ( !name.StartsWith( "Wall_" ) ) return -1;
			var parts = name.Split( '_' );
			if ( parts.Length >= 3 && int.TryParse( parts[2], out int idx ) )
				return idx;
			return -1;
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

			// Revalidate that the builder still holds a valid reservation
			// before switching to InProgress. This enforces the full invariant:
			// no construction without an active directed task AND a valid reservation.
			var ownership = ReservationManager.ValidateOwnership( task, npcName );
			if ( !ownership.Success )
			{
				Log.Warning( $"Lute: AuthorizeExecution rejected for {taskId} / {npcName}: {ownership.Reason}." );
				return false;
			}

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

			// Only steal tasks the stealing builder can actually perform.
			var builderCaps = _builders.TryGetValue( builderId, out var bs )
				? bs.Capabilities ?? new HashSet<NpcCapability> { NpcCapability.GeneralConstruction }
				: new HashSet<NpcCapability> { NpcCapability.GeneralConstruction };

			var victim = _tasks.Values
				.Where( t => t.AssignedBuilder == other.BuilderId &&
					t.Status == TaskStatus.Pending && t.DependenciesSatisfied && t.PreconditionsSatisfied &&
					( t.BuildTask == null || CapabilityRegistry.CanPerformTask( builderCaps, t.BuildTask.TaskType ) ) )
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
			{
				b.CurrentTaskId = null;
				b.LastCompletedTaskId = t.Id;
			}

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

			// Deterministic replanning: if the blocker is another task that
			// is still active (Pending, PendingExecution, InProgress, or
			// Blocked), defer this task instead of consuming a retry. The
			// task will be reactivated when the blocker completes (via
			// AssignTasks) or when ClaimNextTask tries it again.
			string blockerId = ReservationManager.GetLastBlockerTaskId();
			if ( !string.IsNullOrEmpty( blockerId ) && _tasks.TryGetValue( blockerId, out var blocker ) )
			{
				var bs = blocker.Status;
				if ( bs == TaskStatus.Pending || bs == TaskStatus.PendingExecution ||
					 bs == TaskStatus.InProgress || bs == TaskStatus.Blocked )
				{
					t.Status = TaskStatus.Blocked;
					t.BlockedByTaskId = blockerId;
					Log.Info( $"Lute: ConstructionDirector task {taskId} deferred — blocked by active task {blockerId} ({bs}). Will retry when blocker completes." );
					ConstructionEventBus.Fire( ConstructionEventType.TaskBlocked,
						taskId: t.Id,
						parameters: new() { { "reason", reason ?? "" }, { "blocker", blockerId } } );
					return;
				}
			}

			// Not blocked by an active task — consume a retry.
			t.RetryCount++;
			if ( t.RetryCount <= t.MaxRetries )
			{
				t.Status = TaskStatus.Pending;
				t.BlockedByTaskId = null;
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

		/// <summary>
		/// DFS cycle detection starting from <paramref name="taskId"/>. Returns
		/// true and a human-readable cycle path if a cycle exists.
		/// </summary>
		static bool DetectCycleFrom( string taskId, out string cyclePath )
		{
			cyclePath = null;
			var onStack = new HashSet<string>();
			var visited = new HashSet<string>();
			var path = new List<string>();
			string foundCycle = null;

			bool Dfs( string current )
			{
				if ( onStack.Contains( current ) )
				{
					int start = path.IndexOf( current );
					foundCycle = string.Join( " -> ", path.GetRange( start, path.Count - start ) ) + " -> " + current;
					return true;
				}
				if ( visited.Contains( current ) )
					return false;
				visited.Add( current );
				onStack.Add( current );
				path.Add( current );
				if ( _tasks.TryGetValue( current, out var t ) && t.DependsOn != null )
				{
					foreach ( var dep in t.DependsOn )
						if ( Dfs( dep ) ) return true;
				}
				path.RemoveAt( path.Count - 1 );
				onStack.Remove( current );
				return false;
			}

			bool result = Dfs( taskId );
			cyclePath = foundCycle;
			return result;
		}

		/// <summary>
		/// Returns the first unsatisfied prerequisite task id for a task, or
		/// null if all prerequisites are complete.
		/// </summary>
		public static string GetBlockingDependency( string taskId )
		{
			if ( !_tasks.TryGetValue( taskId, out var t ) || t.DependsOn == null )
				return null;
			foreach ( var dep in t.DependsOn )
			{
				if ( _tasks.TryGetValue( dep, out var d ) && d.Status != TaskStatus.Complete )
					return dep;
			}
			return null;
		}

		/// <summary>
		/// All tasks currently blocked by an unsatisfied prerequisite.
		/// </summary>
		public static List<DirectedTask> DependencyBlockedTasks() =>
			_tasks.Values.Where( t => t.Status == TaskStatus.Blocked &&
				!string.IsNullOrEmpty( t.BlockedByDependency ) ).ToList();

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
			if ( task == null )
				return BlackboardResult.Fail( $"task not found or ambiguous: {req.Key}" );

			int builderId = FindBuilderByNpc( req.Actor );
			if ( builderId < 0 )
				return BlackboardResult.Fail( $"builder not registered: {req.Actor}" );

			if ( task.AssignedBuilder != builderId )
				return BlackboardResult.Fail( $"{req.Actor} does not own {task.Id}" );

			if ( !_builders.TryGetValue( builderId, out var state ) ||
				state.CurrentTaskId != task.Id )
				return BlackboardResult.Fail( $"{task.Id} is not {req.Actor}'s current task" );

			if ( task.Status != TaskStatus.InProgress )
				return BlackboardResult.Fail( $"{task.Id} is not in progress" );

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

		/// <summary>
		/// Diagnostic dump of the dependency graph: every task with an
		/// unsatisfied prerequisite, the blocker, and the dependent chain.
		/// </summary>
		public static string DependencyGraphSummary()
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine( "Lute: ConstructionDirector — dependency graph" );

			int total = _tasks.Count;
			int complete = _tasks.Values.Count( t => t.Status == TaskStatus.Complete );
			int depBlocked = _tasks.Values.Count( t => t.Status == TaskStatus.Blocked && !string.IsNullOrEmpty( t.BlockedByDependency ) );
			int resBlocked = _tasks.Values.Count( t => t.Status == TaskStatus.Blocked && !string.IsNullOrEmpty( t.BlockedByTaskId ) );
			int runnable = _tasks.Values.Count( t => t.Status == TaskStatus.Pending || t.Status == TaskStatus.PendingExecution || t.Status == TaskStatus.InProgress );

			sb.AppendLine( $"  total={total} complete={complete} runnable={runnable} depBlocked={depBlocked} resBlocked={resBlocked}" );

			foreach ( var t in _tasks.Values
				.Where( t => t.Status == TaskStatus.Blocked && !string.IsNullOrEmpty( t.BlockedByDependency ) )
				.OrderBy( t => t.Id ) )
			{
				var dep = GetTask( t.BlockedByDependency );
				sb.AppendLine( $"  [blocked] {t.Id} ({t.BuildTask?.Name}) <- dep {t.BlockedByDependency} ({dep?.BuildTask?.Name}, status={dep?.Status})" );
			}

			return sb.ToString();
		}

		[ConCmd( "dag_status" )]
		static void DagStatusCmd()
		{
			Log.Info( DependencyGraphSummary() );
		}
	}
}
