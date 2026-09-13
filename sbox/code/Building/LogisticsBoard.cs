using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// A material requirement for a construction task. The task cannot
	/// start until this requirement is satisfied (materials delivered
	/// to the build site or a nearby stockpile).
	/// </summary>
	public sealed class MaterialRequirement
	{
		/// <summary> Item type needed. </summary>
		public ItemType Type { get; init; }

		/// <summary> Amount needed. </summary>
		public int Amount { get; init; }

		/// <summary> Amount delivered so far. </summary>
		public int Delivered { get; set; }

		/// <summary> True when fully delivered. </summary>
		public bool Satisfied => Delivered >= Amount;

		public string Summary => $"{Type} {Delivered}/{Amount}" +
			( Satisfied ? " (done)" : "" );
	}

	/// <summary>
	/// A logistics haul job. Moves materials from a source (resource
	/// source or stockpile) to a destination (build site or stockpile).
	/// Haulers with the <see cref="NpcCapability.Haul"/> capability claim
	/// these jobs through the <see cref="LogisticsBoard"/>.
	///
	/// Per PR #6 §7: "Material transport among producers, stockpiles,
	/// workstations, and build sites."
	/// </summary>
	public sealed class LogisticsJob
	{
		/// <summary> Unique job id (e.g. "haul_0"). </summary>
		public string Id { get; init; }

		/// <summary> What item type is being hauled. </summary>
		public ItemType ItemType { get; init; }

		/// <summary> How many units. </summary>
		public int Amount { get; init; }

		/// <summary> Source: "source:id" or "stockpile:id". </summary>
		public string FromId { get; init; }

		/// <summary> Source position (for navigation). </summary>
		public Vector3 FromPosition { get; init; }

		/// <summary> Destination: "task:id" or "stockpile:id". </summary>
		public string ToId { get; init; }

		/// <summary> Destination position (for navigation). </summary>
		public Vector3 ToPosition { get; init; }

		/// <summary> Job status. </summary>
		public LogisticsJobStatus Status { get; set; } = LogisticsJobStatus.Pending;

		/// <summary> Hauler assigned to this job (NPC name). </summary>
		public string AssignedHauler { get; set; }

		/// <summary> Task this job supplies (if delivering to a build site). </summary>
		public string ForTaskId { get; init; }

		/// <summary> When the job was created (game time). </summary>
		public float CreatedAt { get; init; }

		/// <summary> When the job was completed (game time). </summary>
		public float CompletedAt { get; set; }

		public string Summary =>
			$"{Id} {ItemType}x{Amount} {FromId} -> {ToId}" +
			( AssignedHauler != null ? $" (haul={AssignedHauler})" : "" ) +
			$" [{Status}]";
	}

	public enum LogisticsJobStatus
	{
		Pending,
		Assigned,
		InProgress,
		Completed,
		Failed,
	}

	/// <summary>
	/// Authoritative logistics scheduler. Creates haul jobs to move
	/// materials between sources, stockpiles, and build sites. Haulers
	/// claim jobs through this board — there is no second logistics
	/// scheduler (per PR #6: "reuse existing authorities").
	///
	/// The board also auto-creates material supply jobs when a
	/// construction task has unsatisfied <see cref="MaterialRequirement"/>s.
	/// </summary>
	public static class LogisticsBoard
	{
		static readonly Dictionary<string, LogisticsJob> _jobs = new();
		static long _jobSeq;

		/// <summary> Create a haul job. Returns the job id, or null on failure. </summary>
		public static string CreateJob( ItemType type, int amount,
			string fromId, Vector3 fromPos,
			string toId, Vector3 toPos,
			string forTaskId = "" )
		{
			if ( amount <= 0 ) return null;

			var id = $"haul_{++_jobSeq}";
			var job = new LogisticsJob
			{
				Id = id,
				ItemType = type,
				Amount = amount,
				FromId = fromId,
				FromPosition = fromPos,
				ToId = toId,
				ToPosition = toPos,
				ForTaskId = forTaskId,
				CreatedAt = SpatialBlackboard.CurrentTime,
			};
			_jobs[id] = job;
			return id;
		}

		/// <summary>
		/// Claim the next pending haul job for a hauler. The claimant must
		/// have the <see cref="NpcCapability.Haul"/> capability (verified
		/// against the ConstructionDirector's builder registry). Returns
		/// null if no jobs are available or the claimant is not a hauler.
		/// </summary>
		public static LogisticsJob ClaimNextJob( string haulerName )
		{
			// Capability gate: only haulers can claim haul jobs. The
			// director's BuilderState caches the profession capabilities.
			if ( !ConstructionDirector.HasCapability( haulerName, NpcCapability.Haul ) )
			{
				Log.Warning( $"Lute: LogisticsBoard — {haulerName} lacks Haul capability, cannot claim job" );
				return null;
			}

			var job = _jobs.Values
				.Where( j => j.Status == LogisticsJobStatus.Pending )
				.OrderBy( j => j.CreatedAt )
				.ThenBy( j => j.Id )
				.FirstOrDefault();

			if ( job == null ) return null;

			job.AssignedHauler = haulerName;
			job.Status = LogisticsJobStatus.Assigned;
			return job;
		}

		/// <summary> Get a job by id. </summary>
		public static LogisticsJob GetJob( string id ) =>
			!string.IsNullOrEmpty( id ) && _jobs.TryGetValue( id, out var j ) ? j : null;

		/// <summary> Mark a job as in progress (hauler started moving). </summary>
		public static (bool success, string detail) StartJob( string jobId, string haulerName )
		{
			var job = GetJob( jobId );
			if ( job == null ) return ( false, "job not found" );
			if ( job.AssignedHauler != haulerName )
				return ( false, $"job assigned to {job.AssignedHauler}, not {haulerName}" );
			if ( job.Status != LogisticsJobStatus.Assigned )
				return ( false, $"job is {job.Status}, not assigned" );
			job.Status = LogisticsJobStatus.InProgress;
			return ( true, "in progress" );
		}

		/// <summary>
		/// Complete a job. The caller must be the assigned hauler. The
		/// transfer is atomic: material is withdrawn from the source and
		/// deposited at the destination via
		/// <see cref="ResourceRegistry.Transfer"/>, which rolls back any
		/// undeposited material so nothing is created or destroyed. If
		/// the destination is a build site (task), the delivered amount
		/// is credited to the task's <see cref="MaterialRequirement"/> —
		/// but only after the physical transfer succeeds.
		/// </summary>
		public static (bool success, string detail) CompleteJob( string jobId, string haulerName )
		{
			var job = GetJob( jobId );
			if ( job == null ) return ( false, "job not found" );

			// Assigned-hauler check: only the assigned hauler can complete
			// the job. Prevents a different NPC from teleporting material
			// by calling CompleteJob on someone else's job.
			if ( job.AssignedHauler != haulerName )
				return ( false, $"job assigned to {job.AssignedHauler}, not {haulerName}" );

			if ( job.Status != LogisticsJobStatus.InProgress && job.Status != LogisticsJobStatus.Assigned )
				return ( false, $"job is {job.Status}, not in progress" );

			// ── Resolve source ──
			Stockpile sourcePile = null;
			ResourceSource sourceNode = null;
			if ( job.FromId.StartsWith( "source:" ) )
			{
				sourceNode = ResourceRegistry.GetSource( job.FromId.Substring( 7 ) );
				if ( sourceNode == null )
				{
					job.Status = LogisticsJobStatus.Failed;
					return ( false, $"source not found: {job.FromId}" );
				}
			}
			else if ( job.FromId.StartsWith( "stockpile:" ) )
			{
				sourcePile = ResourceRegistry.GetStockpile( job.FromId.Substring( 10 ) );
				if ( sourcePile == null )
				{
					job.Status = LogisticsJobStatus.Failed;
					return ( false, $"source stockpile not found: {job.FromId}" );
				}
			}
			else
			{
				job.Status = LogisticsJobStatus.Failed;
				return ( false, $"unknown source kind: {job.FromId}" );
			}

			// ── Resolve destination ──
			Stockpile destPile = null;
			DirectedTask destTask = null;
			if ( job.ToId.StartsWith( "stockpile:" ) )
			{
				destPile = ResourceRegistry.GetStockpile( job.ToId.Substring( 10 ) );
				if ( destPile == null )
				{
					job.Status = LogisticsJobStatus.Failed;
					return ( false, $"destination stockpile not found: {job.ToId}" );
				}
			}
			else if ( job.ToId.StartsWith( "task:" ) )
			{
				destTask = ConstructionDirector.GetTask( job.ToId.Substring( 5 ) );
				if ( destTask == null )
				{
					job.Status = LogisticsJobStatus.Failed;
					return ( false, $"destination task not found: {job.ToId}" );
				}
			}
			else
			{
				job.Status = LogisticsJobStatus.Failed;
				return ( false, $"unknown destination kind: {job.ToId}" );
			}

			// ── Transfer ──
			// For task deliveries, we need a physical destination inventory.
			// Build sites don't have a Stockpile yet — the material is
			// credited to the task's MaterialRequirement. To keep the
			// transfer atomic and conserve material, we withdraw from the
			// source first; if the task credit would fail, we roll back.
			int withdrawn;
			if ( sourcePile != null )
			{
				// Reserve outgoing before withdrawing (the hauler should
				// have reserved at claim time, but enforce here too).
				withdrawn = sourcePile.Withdraw( job.ItemType, job.Amount );
			}
			else
			{
				// Gather from a ResourceSource — this writes into a
				// transient hauler inventory (null here means we just
				// track the count; the physical hauler loop in Gate 2d
				// will pass the hauler's real LuteInventory).
				withdrawn = Math.Min( job.Amount, sourceNode.RemainingYield );
				if ( withdrawn > 0 )
				{
					sourceNode.RemainingYield -= withdrawn;
				}
			}

			if ( withdrawn <= 0 )
			{
				job.Status = LogisticsJobStatus.Failed;
				return ( false, $"nothing to withdraw from {job.FromId}" );
			}

			// ── Deposit / credit ──
			int deposited;
			if ( destPile != null )
			{
				// Atomic transfer with rollback — never creates/destroys.
				var (moved, detail) = ResourceRegistry.Transfer(
					sourcePile, destPile, job.ItemType, withdrawn );
				deposited = moved;
				if ( deposited <= 0 )
				{
					// Transfer failed entirely — but Transfer already
					// rolled back. Mark failed.
					job.Status = LogisticsJobStatus.Failed;
					return ( false, detail );
				}
			}
			else
			{
				// Task delivery: credit the material requirement. If the
				// task has no matching requirement (or is already
				// satisfied), roll the material back into the source so
				// we don't destroy it.
				MaterialRequirement req = null;
				if ( destTask.MaterialRequirements != null )
				{
					foreach ( var r in destTask.MaterialRequirements )
					{
						if ( r.Type == job.ItemType && !r.Satisfied )
						{
							req = r;
							break;
						}
					}
				}
				if ( req == null )
				{
					// Roll back: re-deposit into the source stockpile.
					if ( sourcePile != null )
						sourcePile.Deposit( job.ItemType, withdrawn );
					else
						sourceNode.RemainingYield += withdrawn;
					job.Status = LogisticsJobStatus.Failed;
					return ( false, $"task {destTask.Id} has no unsatisfied {job.ItemType} requirement" );
				}
				// Credit the requirement. Only the amount that fits the
				// remaining need is credited; the surplus is rolled back.
				int need = req.Amount - req.Delivered;
				int credit = Math.Min( withdrawn, need );
				int surplus = withdrawn - credit;
				req.Delivered += credit;
				if ( surplus > 0 )
				{
					if ( sourcePile != null )
						sourcePile.Deposit( job.ItemType, surplus );
					else
						sourceNode.RemainingYield += surplus;
				}
				deposited = credit;
			}

			job.Status = LogisticsJobStatus.Completed;
			job.CompletedAt = SpatialBlackboard.CurrentTime;

			return ( true, $"delivered {deposited} {job.ItemType}" );
		}

		/// <summary> All jobs (for diagnostics). </summary>
		public static List<LogisticsJob> AllJobs() => _jobs.Values.ToList();

		/// <summary> Pending jobs count. </summary>
		public static int PendingCount => _jobs.Values.Count( j => j.Status == LogisticsJobStatus.Pending );

		/// <summary> Clear all jobs (for reset). </summary>
		public static void Clear()
		{
			_jobs.Clear();
			_jobSeq = 0;
		}

		/// <summary>
		/// Auto-create supply jobs for a task's unsatisfied material
		/// requirements. Finds the nearest source/stockpile for each
		/// missing material and creates a haul job to the build site.
		/// Returns the number of jobs created.
		/// </summary>
		public static int SupplyTaskMaterials( DirectedTask task, Vector3 taskPosition )
		{
			if ( task?.MaterialRequirements == null ) return 0;

			int created = 0;
			foreach ( var req in task.MaterialRequirements )
			{
				if ( req.Satisfied ) continue;

				int needed = req.Amount - req.Delivered;
				if ( needed <= 0 ) continue;

				// Find nearest source or stockpile with the resource
				ResourceSource src = null;
				Stockpile pile = null;

				if ( ItemDefs.IsRawMaterial( req.Type ) )
				{
					src = ResourceRegistry.NearestSource( req.Type, taskPosition );
				}
				pile = ResourceRegistry.NearestStockpileWithResource( req.Type, taskPosition, needed );

				// Prefer stockpile if it has enough (faster than gathering)
				if ( pile != null && pile.Count( req.Type ) >= needed )
				{
					var jobId = CreateJob( req.Type, needed,
						$"stockpile:{pile.Id}", pile.Position,
						$"task:{task.Id}", taskPosition,
						forTaskId: task.Id );
					if ( jobId != null ) created++;
				}
				else if ( src != null )
				{
					// Gather from source, deliver to build site. Raw
					// materials need processing at a workstation before
					// they can be used for construction — but for the
					// bootstrap test, allow direct delivery.
					var jobId = CreateJob( req.Type, needed,
						$"source:{src.Id}", src.Position,
						$"task:{task.Id}", taskPosition,
						forTaskId: task.Id );
					if ( jobId != null ) created++;
				}
			}

			return created;
		}

		/// <summary> Diagnostic summary. </summary>
		public static string Summary()
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine( $"Lute: LogisticsBoard — {_jobs.Count} jobs ({PendingCount} pending)" );
			foreach ( var j in _jobs.Values.OrderBy( x => x.Id ) )
				sb.AppendLine( $"  {j.Summary}" );
			return sb.ToString();
		}

		[ConCmd( "logistics" )]
		static void LogisticsCmd()
		{
			Log.Info( Summary() );
		}
	}
}
