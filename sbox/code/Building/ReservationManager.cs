using System;
using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Deterministic result returned by the construction reservation layer.
	/// Callers get a stable reason instead of inferring failure from dialogue.
	/// </summary>
	public sealed class ReservationResult
	{
		public bool Success { get; private set; }
		public string ReservationId { get; private set; }
		public string BlockedBy { get; private set; }
		public string Reason { get; private set; }

		public static ReservationResult Granted( string reservationId ) => new()
		{
			Success = true,
			ReservationId = reservationId,
		};

		public static ReservationResult Rejected( string reason, string blockedBy = null ) => new()
		{
			Success = false,
			Reason = reason,
			BlockedBy = blockedBy,
		};
	}

	/// <summary>
	/// A committed piece of construction geometry known to Lute. This is not
	/// a replacement for engine collision; it is the deterministic occupancy
	/// ledger used to stop one construction task from placing into geometry
	/// already committed by another task.
	/// </summary>
	public sealed class ConstructionOccupancy
	{
		public string Id { get; set; }
		public string TaskId { get; set; }
		public string Source { get; set; }
		public BBox Bounds { get; set; }
	}

	/// <summary>
	/// Authoritative construction reservation and occupancy service.
	///
	/// SpatialBlackboard remains the shared spatial store, but construction
	/// code no longer guesses claim ids or creates duplicate claims. The
	/// director acquires exactly one task-tied reservation here; executors
	/// validate it immediately before placement and commit occupied AABBs
	/// after a successful spawn.
	/// </summary>
	public static class ReservationManager
	{
		const float M = 39.37f;
		const float DefaultCell = 100f;
		const float TouchTolerance = 0.5f;

		static readonly Dictionary<string, ConstructionOccupancy> _occupied = new();
		static long _occupancySeq;

		/// <summary>
		/// Acquire the one construction reservation for a directed task.
		/// Reservations are task-tied and permanent until explicit release.
		/// </summary>
		public static ReservationResult TryAcquire( DirectedTask task, string npcName )
		{
			if ( task == null )
				return ReservationResult.Rejected( "invalid task" );
			if ( string.IsNullOrWhiteSpace( npcName ) )
				return ReservationResult.Rejected( "invalid reservation owner" );
			if ( !task.DependenciesSatisfied )
				return ReservationResult.Rejected( "dependency incomplete" );
			if ( !task.PreconditionsSatisfied )
				return ReservationResult.Rejected( "precondition failed" );

			// Idempotent: if this task already owns a live reservation, reuse it.
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

			// Do our own check first. SpatialBlackboard intentionally lets one
			// NPC overlap its own claims; construction does not. A stale claim
			// from the same builder must not silently authorize another task.
			var blocker = FindBlockingClaim( bounds, task.Id );
			if ( blocker.HasValue )
			{
				ConstructionEventBus.Fire( ConstructionEventType.ReservationConflict,
					taskId: task.Id, actor: npcName,
					parameters: new() { { "blocked_by", blocker.Value.Owner ?? "unknown" } } );
				return ReservationResult.Rejected( "reservation conflict", blocker.Value.Owner );
			}

			if ( !SpatialBlackboard.ClaimBox( npcName, bounds, "construction", 0, task.Id ) )
			{
				blocker = FindBlockingClaim( bounds, task.Id );
				return ReservationResult.Rejected( "reservation conflict", blocker?.Owner );
			}

			if ( !TryGetTaskClaim( task.Id, out var created ) )
			{
				// Defensive rollback: a successful blackboard claim must be
				// discoverable by task id or it cannot be safely released later.
				foreach ( var claim in SpatialBlackboard.GetClaimsByOwner( npcName )
					.Where( c => c.TaskId == task.Id ).ToList() )
					SpatialBlackboard.ReleaseClaim( claim.Id );
				return ReservationResult.Rejected( "reservation claim id unavailable" );
			}

			task.ReservationId = created.Id;
			return ReservationResult.Granted( created.Id );
		}

		/// <summary>
		/// Confirm the director's reservation still exists and belongs to the
		/// expected NPC. Controllers use this when they physically arrive at
		/// the site; they do not create a second reservation.
		/// </summary>
		public static ReservationResult ValidateOwnership( DirectedTask task, string npcName )
		{
			if ( task == null )
				return ReservationResult.Rejected( "invalid task" );
			if ( !TryGetTaskClaim( task.Id, out var claim ) )
				return ReservationResult.Rejected( "reservation missing" );
			if ( claim.Owner != npcName )
				return ReservationResult.Rejected( "reservation owned by another builder", claim.Owner );
			if ( !string.IsNullOrEmpty( task.ReservationId ) && claim.Id != task.ReservationId )
				return ReservationResult.Rejected( "reservation identity mismatch", claim.Owner );

			task.ReservationId = claim.Id;
			return ReservationResult.Granted( claim.Id );
		}

		/// <summary>
		/// Release every blackboard claim associated with a task. Releasing by
		/// task id as well as stored claim id heals stale ids from older code.
		/// </summary>
		public static void Release( DirectedTask task )
		{
			if ( task == null )
				return;

			if ( !string.IsNullOrEmpty( task.ReservationId ) )
				SpatialBlackboard.ReleaseClaim( task.ReservationId );

			foreach ( var claim in SpatialBlackboard.GetClaims()
				.Where( c => c.TaskId == task.Id ).ToList() )
				SpatialBlackboard.ReleaseClaim( claim.Id );

			task.ReservationId = null;
		}

		/// <summary>
		/// Placement gate. A directed task must still own its reservation and
		/// may not volumetrically intersect geometry committed by another task.
		/// Touching faces/edges are allowed so walls, floors and roofs can join.
		/// </summary>
		public static bool CanPlace( string taskId, BBox bounds, out string reason )
		{
			reason = null;
			var task = ConstructionDirector.GetTask( taskId );
			if ( task == null )
			{
				reason = $"unknown directed task {taskId}";
				return false;
			}
			if ( task.Status != TaskStatus.InProgress )
			{
				reason = $"task {taskId} is not in progress ({task.Status})";
				return false;
			}
			if ( !TryGetTaskClaim( taskId, out var claim ) )
			{
				reason = $"task {taskId} has no active reservation";
				return false;
			}
			if ( claim.Id != task.ReservationId )
			{
				reason = $"task {taskId} reservation identity mismatch";
				return false;
			}

			foreach ( var occupied in _occupied.Values )
			{
				// Pieces in the same task may intentionally meet/interlock.
				if ( occupied.TaskId == taskId )
					continue;
				if ( AabbOverlapsVolume( bounds, occupied.Bounds, TouchTolerance ) )
				{
					reason = $"occupied by {occupied.Source ?? occupied.Id} (task {occupied.TaskId ?? "external"})";
					return false;
				}
			}

			return true;
		}

		/// <summary> Record geometry after it has successfully spawned. </summary>
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

		/// <summary>
		/// Seed known non-Lute/static geometry into the deterministic occupancy
		/// ledger. Inspection/import systems can call this without giving NPCs
		/// direct authority over the world.
		/// </summary>
		public static string RegisterOccupiedRegion( string source, BBox bounds )
		{
			return CommitPlacement( null, bounds, source );
		}

		public static List<ConstructionOccupancy> GetOccupiedRegions() => _occupied.Values.ToList();

		/// <summary>
		/// Clear construction reservations and occupancy state. Position and
		/// conversation data on SpatialBlackboard are deliberately preserved.
		/// </summary>
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
			if ( task == null )
				return new BBox( Vector3.Zero, Vector3.Zero );

			float width;
			float depth;
			float height;

			switch ( task.TaskType )
			{
				case "wall":
					width = 10f * M;
					depth = 3f * M;
					height = 6f * M;
					break;
				case "gate":
					width = 16f * M;
					depth = 4f * M;
					height = 15f * M;
					break;
				case "road":
					width = 10f * M;
					depth = 10f * M;
					height = 2f * M;
					break;
				case "well":
					width = depth = 8f * M;
					height = 10f * M;
					break;
				case "market_square":
					width = depth = 20f * M;
					height = 2f * M;
					break;
				default:
					width = Math.Max( DefaultCell, task.BaseWidth * task.WealthFactor * DefaultCell );
					depth = Math.Max( DefaultCell, task.BaseHeight * task.WealthFactor * DefaultCell );
					height = 12f * M;
					break;
			}

			// Convert the rotated footprint to a conservative world AABB.
			float angle = task.Rotation * (float)Math.PI / 180f;
			float c = Math.Abs( (float)Math.Cos( angle ) );
			float s = Math.Abs( (float)Math.Sin( angle ) );
			float aabbWidth = width * c + depth * s;
			float aabbDepth = width * s + depth * c;
			var half = new Vector3( aabbWidth * 0.5f, aabbDepth * 0.5f, height * 0.5f );
			var center = task.Position + new Vector3( 0, 0, height * 0.5f );
			return new BBox( center - half, center + half );
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
				if ( claim.TaskId == taskId )
					continue;

				if ( claim.Bounds.HasValue )
				{
					if ( AabbOverlapsVolume( bounds, claim.Bounds.Value, TouchTolerance ) )
						return claim;
					continue;
				}

				// Sphere/circle-style legacy claim versus task AABB.
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

	/// <summary>
	/// Raised by an executor when the final placement gate rejects a piece.
	/// The build loop catches this and reports deterministic task failure to
	/// the ConstructionDirector instead of silently advancing progress.
	/// </summary>
	public sealed class ConstructionPlacementBlockedException : Exception
	{
		public string BlockReason { get; }

		public ConstructionPlacementBlockedException( string reason )
			: base( reason )
		{
			BlockReason = reason;
		}
	}
}
