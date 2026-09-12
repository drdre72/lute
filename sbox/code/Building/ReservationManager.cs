using System;
using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	public sealed class ReservationResult
	{
		public bool Success { get; private set; }
		public string ReservationId { get; private set; }
		public string BlockedBy { get; private set; }
		public string Reason { get; private set; }

		public static ReservationResult Granted( string id ) => new()
		{
			Success = true,
			ReservationId = id,
		};

		public static ReservationResult Rejected( string reason, string blockedBy = null ) => new()
		{
			Success = false,
			Reason = reason,
			BlockedBy = blockedBy,
		};
	}

	public sealed class ConstructionOccupancy
	{
		public string Id { get; set; }
		public string TaskId { get; set; }
		public string Source { get; set; }
		public BBox Bounds { get; set; }
	}

	/// <summary>
	/// Owns task-tied spatial reservations and the deterministic construction
	/// occupancy ledger. SpatialBlackboard remains the shared spatial store;
	/// this class adds construction semantics and stable claim ownership.
	/// </summary>
	public static class ReservationManager
	{
		const float M = 39.37f;
		const float DefaultCell = 100f;
		const float TouchTolerance = 0.5f;

		// Tracks the task ID of the most recent occupancy blocker, so
		// ConstructionDirector.FailTask can defer (not retry) when the
		// blocker is still an active task.
		static string _lastBlockerTaskId;
		public static string GetLastBlockerTaskId() => _lastBlockerTaskId;
		// Join tolerance for structural connections (wall corners, road
		// intersections, road-gate, road-market). These are intentional
		// overlaps where structural pieces meet. Much larger than
		// TouchTolerance so small join-zone overlaps don't block.
		const float JoinTolerance = 1.5f * M; // ~1.5m

		/// <summary>
		/// Returns true if two task types are allowed to structurally
		/// join/overlap at their boundaries. This covers wall corners,
		/// road intersections, road-gate connections, and road-market
		/// square connections.
		/// </summary>
		static bool IsJoinCompatible( string typeA, string typeB )
		{
			if ( string.IsNullOrEmpty( typeA ) || string.IsNullOrEmpty( typeB ) )
				return false;

			// Same-type structural joins: wall-wall corners, road-road intersections
			if ( typeA == typeB )
				return typeA == "wall" || typeA == "road";

			// Cross-type structural joins
			var pair = (typeA, typeB);
			return pair == ("road", "gate") || pair == ("gate", "road") ||
				pair == ("road", "market_square") || pair == ("market_square", "road") ||
				pair == ("wall", "gate") || pair == ("gate", "wall");
		}

		static readonly Dictionary<string, ConstructionOccupancy> _occupied = new();
		static long _occupancySeq;

		public static ReservationResult TryAcquire( DirectedTask task, string npcName )
		{
			if ( task == null ) return ReservationResult.Rejected( "invalid task" );
			if ( string.IsNullOrWhiteSpace( npcName ) ) return ReservationResult.Rejected( "invalid reservation owner" );
			if ( !task.DependenciesSatisfied ) return ReservationResult.Rejected( "dependency incomplete" );
			if ( !task.PreconditionsSatisfied ) return ReservationResult.Rejected( "precondition failed" );

			if ( TryGetTaskClaim( task.Id, out var existing ) )
			{
				if ( existing.Owner != npcName )
					return ReservationResult.Rejected( "task already reserved", existing.Owner );
				task.ReservationId = existing.Id;
				task.ReservationBounds ??= existing.Bounds;
				return ReservationResult.Granted( existing.Id );
			}

			var bounds = task.ReservationBounds ?? EstimateTaskBounds( task.BuildTask );
			task.ReservationBounds = bounds;

			var occupied = FindBlockingOccupancy( bounds, task.Id, task.BuildTask?.TaskType );
			if ( occupied != null )
			{
				var ixMins = new Vector3( Math.Max( bounds.Mins.x, occupied.Bounds.Mins.x ), Math.Max( bounds.Mins.y, occupied.Bounds.Mins.y ), Math.Max( bounds.Mins.z, occupied.Bounds.Mins.z ) );
				var ixMaxs = new Vector3( Math.Min( bounds.Maxs.x, occupied.Bounds.Maxs.x ), Math.Min( bounds.Maxs.y, occupied.Bounds.Maxs.y ), Math.Min( bounds.Maxs.z, occupied.Bounds.Maxs.z ) );
				var ixSize = ixMaxs - ixMins;
				Log.Info( $"Lute: ReservationConflict request={task.Id} name={task.BuildTask?.Name ?? "?"} type={task.BuildTask?.TaskType ?? "?"} bounds={bounds.Mins}:{bounds.Maxs} blocked_by={occupied.Source ?? occupied.Id} blocker_task={occupied.TaskId ?? "external"} kind=occupied intersection=({ixSize.x:F1},{ixSize.y:F1},{ixSize.z:F1})" );
				_lastBlockerTaskId = occupied.TaskId;
				ConstructionEventBus.Fire( ConstructionEventType.ReservationConflict,
					taskId: task.Id, actor: npcName,
					parameters: new()
					{
						{ "blocked_by", occupied.Source ?? occupied.Id },
						{ "blocker_task", occupied.TaskId ?? "external" },
						{ "reason", "occupied" },
						{ "intersection", $"({ixSize.x:F1},{ixSize.y:F1},{ixSize.z:F1})" },
					} );
				return ReservationResult.Rejected( $"occupied by {occupied.Source ?? occupied.Id}", occupied.Source );
			}

			var blocker = FindBlockingClaim( bounds, task.Id );
			if ( blocker.HasValue )
			{
				Log.Info( $"Lute: ReservationConflict request={task.Id} name={task.BuildTask?.Name ?? "?"} type={task.BuildTask?.TaskType ?? "?"} bounds={bounds.Mins}:{bounds.Maxs} blocked_by={blocker.Value.Owner ?? "unknown"} kind=reservation" );
				ConstructionEventBus.Fire( ConstructionEventType.ReservationConflict,
					taskId: task.Id, actor: npcName,
					parameters: new()
					{
						{ "blocked_by", blocker.Value.Owner ?? "unknown" },
						{ "reason", "reservation" },
					} );
				return ReservationResult.Rejected( "reservation conflict", blocker.Value.Owner );
			}

			if ( !SpatialBlackboard.ClaimBox( npcName, bounds, "construction", 0, task.Id ) )
			{
				blocker = FindBlockingClaim( bounds, task.Id );
				return ReservationResult.Rejected( "reservation conflict", blocker?.Owner );
			}

			if ( !TryGetTaskClaim( task.Id, out var created ) )
			{
				foreach ( var claim in SpatialBlackboard.GetClaimsByOwner( npcName )
					.Where( c => c.TaskId == task.Id ).ToList() )
					SpatialBlackboard.ReleaseClaim( claim.Id );
				return ReservationResult.Rejected( "reservation claim id unavailable" );
			}

			task.ReservationId = created.Id;
			return ReservationResult.Granted( created.Id );
		}

		public static ReservationResult ValidateOwnership( DirectedTask task, string npcName )
		{
			if ( task == null ) return ReservationResult.Rejected( "invalid task" );
			if ( !TryGetTaskClaim( task.Id, out var claim ) ) return ReservationResult.Rejected( "reservation missing" );
			if ( claim.Owner != npcName ) return ReservationResult.Rejected( "reservation owned by another builder", claim.Owner );
			if ( !string.IsNullOrEmpty( task.ReservationId ) && claim.Id != task.ReservationId )
				return ReservationResult.Rejected( "reservation identity mismatch", claim.Owner );

			task.ReservationId = claim.Id;
			return ReservationResult.Granted( claim.Id );
		}

		/// <summary>
		/// Release all claims associated with a task. Completed work is first
		/// committed to occupancy, so releasing a reservation does not make the
		/// finished structure's region appear free to later tasks.
		/// </summary>
		public static void Release( DirectedTask task )
		{
			if ( task == null ) return;

			// Compatibility fallback: if the task is complete and has no
			// piece-level occupancy (e.g. legacy executors that don't commit
			// individual pieces), commit the coarse task bounds. Skip if
			// piece-level occupancy already exists for this task.
			if ( task.Status == TaskStatus.Complete && task.ReservationBounds.HasValue &&
				!_occupied.Values.Any( o => o.TaskId == task.Id ) )
			{
				CommitPlacement( task.Id, task.ReservationBounds.Value, $"task:{task.Id}" );
			}

			if ( !string.IsNullOrEmpty( task.ReservationId ) )
				SpatialBlackboard.ReleaseClaim( task.ReservationId );

			foreach ( var claim in SpatialBlackboard.GetClaims()
				.Where( c => c.TaskId == task.Id ).ToList() )
				SpatialBlackboard.ReleaseClaim( claim.Id );

			task.ReservationId = null;
		}

		public static bool CanPlace( string taskId, BBox bounds, out string reason )
		{
			reason = null;
			var task = ConstructionDirector.GetTask( taskId );
			if ( task == null ) { reason = $"unknown directed task {taskId}"; return false; }
			if ( task.Status != TaskStatus.InProgress ) { reason = $"task {taskId} is not in progress ({task.Status})"; return false; }
			if ( !TryGetTaskClaim( taskId, out var claim ) ) { reason = $"task {taskId} has no active reservation"; return false; }
			if ( claim.Id != task.ReservationId ) { reason = $"task {taskId} reservation identity mismatch"; return false; }

			var requestTask = ConstructionDirector.GetTask( taskId );
			var blocked = FindBlockingOccupancy( bounds, taskId, requestTask?.BuildTask?.TaskType );
			if ( blocked != null )
			{
				var ixMins = new Vector3( Math.Max( bounds.Mins.x, blocked.Bounds.Mins.x ), Math.Max( bounds.Mins.y, blocked.Bounds.Mins.y ), Math.Max( bounds.Mins.z, blocked.Bounds.Mins.z ) );
				var ixMaxs = new Vector3( Math.Min( bounds.Maxs.x, blocked.Bounds.Maxs.x ), Math.Min( bounds.Maxs.y, blocked.Bounds.Maxs.y ), Math.Min( bounds.Maxs.z, blocked.Bounds.Maxs.z ) );
				var ixSize = ixMaxs - ixMins;
				reason = $"occupied by {blocked.Source ?? blocked.Id} (task {blocked.TaskId ?? "external"})";
				Log.Info( $"Lute: PlacementConflict task={taskId} bounds={bounds.Mins}:{bounds.Maxs} blocked_by={blocked.Source ?? blocked.Id} blocker_task={blocked.TaskId ?? "external"} intersection=({ixSize.x:F1},{ixSize.y:F1},{ixSize.z:F1})" );
				_lastBlockerTaskId = blocked.TaskId;
				return false;
			}
			return true;
		}

		public static string CommitPlacement( string taskId, BBox bounds, string source = null )
		{
			var id = $"occupied_{_occupancySeq++}";
			_occupied[id] = new ConstructionOccupancy
			{
				Id = id,
				TaskId = taskId,
				Bounds = bounds,
				Source = source ?? id,
			};
			return id;
		}

		public static string RegisterOccupiedRegion( string source, BBox bounds ) =>
			CommitPlacement( null, bounds, source );

		public static List<ConstructionOccupancy> GetOccupiedRegions() => _occupied.Values.ToList();

		public static void Reset()
		{
			foreach ( var claim in SpatialBlackboard.GetClaims()
				.Where( c => !string.IsNullOrEmpty( c.TaskId ) ).ToList() )
				SpatialBlackboard.ReleaseClaim( claim.Id );
			_occupied.Clear();
			_occupancySeq = 0;
		}

		public static BBox EstimateTaskBounds( VillageBuildTask task )
		{
			if ( task == null ) return new BBox( Vector3.Zero, Vector3.Zero );

			float width, depth, height;
			switch ( task.TaskType )
			{
				case "wall": width = 10f * M; depth = 3f * M; height = 6f * M; break;
				case "gate": width = 16f * M; depth = 4f * M; height = 15f * M; break;
				case "road": width = 10f * M; depth = 10f * M; height = 2f * M; break;
				case "well": width = depth = 8f * M; height = 10f * M; break;
				case "market_square": width = depth = 20f * M; height = 2f * M; break;
				default:
					width = Math.Max( DefaultCell, task.BaseWidth * task.WealthFactor * DefaultCell );
					depth = Math.Max( DefaultCell, task.BaseHeight * task.WealthFactor * DefaultCell );
					height = 12f * M;
					break;
			}

			float angle = task.Rotation * (float)Math.PI / 180f;
			float c = Math.Abs( (float)Math.Cos( angle ) );
			float s = Math.Abs( (float)Math.Sin( angle ) );
			float aabbWidth = width * c + depth * s;
			float aabbDepth = width * s + depth * c;
			var half = new Vector3( aabbWidth * 0.5f, aabbDepth * 0.5f, height * 0.5f );
			var center = task.Position + new Vector3( 0, 0, height * 0.5f );
			return new BBox( center - half, center + half );
		}

		static ConstructionOccupancy FindBlockingOccupancy( BBox bounds, string taskId, string requestTaskType = null )
		{
			foreach ( var o in _occupied.Values )
			{
				if ( o.TaskId == taskId ) continue;

				// Determine if this is a structural join (corner/intersection)
				// between compatible task types. If so, allow small overlaps
				// using a per-axis join tolerance rather than a uniform shrink.
				bool isJoin = false;
				if ( requestTaskType != null )
				{
					var blockerTask = o.TaskId != null ? ConstructionDirector.GetTask( o.TaskId ) : null;
					var blockerType = blockerTask?.BuildTask?.TaskType;
					isJoin = IsJoinCompatible( requestTaskType, blockerType );
				}

				if ( isJoin )
				{
					// For structural joins, allow the overlap if the intersection
					// is small in at least one horizontal axis (the joining axis).
					// This handles wall corners (thin overlap in thickness direction)
					// and road intersections (thin overlap along travel direction).
					if ( AabbJoinOverlap( bounds, o.Bounds ) )
						continue; // allowed join — skip this blocker
				}

				if ( AabbOverlapsVolume( bounds, o.Bounds, TouchTolerance ) )
					return o;
			}
			return null;
		}

		/// <summary>
		/// Check if two AABBs overlap with a small enough intersection to be
		/// a structural join (corner/intersection). The overlap is allowed if
			/// at least one horizontal axis intersection is below JoinTolerance.
		/// Z (height) overlap is always allowed for joins.
		/// </summary>
		static bool AabbJoinOverlap( BBox a, BBox b )
		{
			// Must overlap at all (with touch tolerance)
			if ( !AabbOverlapsVolume( a, b, TouchTolerance ) )
				return false;

			// Compute intersection size on each axis
			float ixX = Math.Min( a.Maxs.x, b.Maxs.x ) - Math.Max( a.Mins.x, b.Mins.x );
			float ixY = Math.Min( a.Maxs.y, b.Maxs.y ) - Math.Max( a.Mins.y, b.Mins.y );

			// Allow if at least one horizontal axis intersection is within
			// join tolerance (the pieces are just touching at a corner/junction)
			return ixX <= JoinTolerance || ixY <= JoinTolerance;
		}

		static bool TryGetTaskClaim( string taskId, out SpatialClaim claim )
		{
			foreach ( var candidate in SpatialBlackboard.GetClaims() )
			{
				if ( candidate.TaskId == taskId )
				{
					claim = candidate;
					return true;
				}
			}
			claim = default;
			return false;
		}

		static SpatialClaim? FindBlockingClaim( BBox bounds, string taskId )
		{
			foreach ( var claim in SpatialBlackboard.GetClaims() )
			{
				if ( claim.TaskId == taskId ) continue;

				if ( claim.Bounds.HasValue )
				{
					if ( AabbOverlapsVolume( bounds, claim.Bounds.Value, TouchTolerance ) ) return claim;
					continue;
				}

				var r = claim.Radius;
				if ( claim.Position.x + r > bounds.Mins.x + TouchTolerance &&
					claim.Position.x - r < bounds.Maxs.x - TouchTolerance &&
					claim.Position.y + r > bounds.Mins.y + TouchTolerance &&
					claim.Position.y - r < bounds.Maxs.y - TouchTolerance &&
					claim.Position.z + r > bounds.Mins.z + TouchTolerance &&
					claim.Position.z - r < bounds.Maxs.z - TouchTolerance )
					return claim;
			}
			return null;
		}

		static bool AabbOverlapsVolume( BBox a, BBox b, float tolerance )
		{
			return a.Mins.x < b.Maxs.x - tolerance && a.Maxs.x > b.Mins.x + tolerance &&
				a.Mins.y < b.Maxs.y - tolerance && a.Maxs.y > b.Mins.y + tolerance &&
				a.Mins.z < b.Maxs.z - tolerance && a.Maxs.z > b.Mins.z + tolerance;
		}
	}

	public sealed class ConstructionPlacementBlockedException : Exception
	{
		public string BlockReason { get; }
		public ConstructionPlacementBlockedException( string reason ) : base( reason ) => BlockReason = reason;
	}
}
