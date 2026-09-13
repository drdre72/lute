using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// A material requirement for a construction task. The task cannot
	/// start until this requirement is satisfied (materials delivered
	/// to the build site or a nearby stockpile).
	/// </summary>
	public sealed class MaterialRequirement
	{
		/// <summary> Resource type needed. </summary>
		public ResourceType Type { get; init; }

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

		/// <summary> What resource is being hauled. </summary>
		public ResourceType ResourceType { get; init; }

		/// <summary> How many units. </summary>
		public int Amount { get; init; }

		/// <summary> Source: "source:<id>" or "stockpile:<id>". </summary>
		public string FromId { get; init; }

		/// <summary> Source position (for navigation). </summary>
		public Vector3 FromPosition { get; init; }

		/// <summary> Destination: "task:<id>" or "stockpile:<id>". </summary>
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
			$"{Id} {ResourceType}x{Amount} {FromId} -> {ToId}" +
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
		public static string CreateJob( ResourceType type, int amount,
			string fromId, Vector3 fromPos,
			string toId, Vector3 toPos,
			string forTaskId = "" )
		{
			if ( amount <= 0 ) return null;

			var id = $"haul_{++_jobSeq}";
			var job = new LogisticsJob
			{
				Id = id,
				ResourceType = type,
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
		/// Claim the next pending haul job for a hauler. Returns null if
		/// no jobs are available.
		/// </summary>
		public static LogisticsJob ClaimNextJob( string haulerName )
		{
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
		public static void StartJob( string jobId )
		{
			var job = GetJob( jobId );
			if ( job != null && job.Status == LogisticsJobStatus.Assigned )
				job.Status = LogisticsJobStatus.InProgress;
		}

		/// <summary>
		/// Complete a job: withdraw from source, deposit at destination,
		/// update material requirement if delivering to a build site.
		/// </summary>
		public static (bool success, string detail) CompleteJob( string jobId )
		{
			var job = GetJob( jobId );
			if ( job == null ) return ( false, "job not found" );
			if ( job.Status != LogisticsJobStatus.InProgress && job.Status != LogisticsJobStatus.Assigned )
				return ( false, $"job is {job.Status}, not in progress" );

			// Withdraw from source
			int withdrawn = 0;
			if ( job.FromId.StartsWith( "source:" ) )
			{
				var src = ResourceRegistry.GetSource( job.FromId.Substring( 7 ) );
				if ( src == null ) return ( false, $"source not found: {job.FromId}" );
				withdrawn = src.Gather();
			}
			else if ( job.FromId.StartsWith( "stockpile:" ) )
			{
				var pile = ResourceRegistry.GetStockpile( job.FromId.Substring( 10 ) );
				if ( pile == null ) return ( false, $"stockpile not found: {job.FromId}" );
				withdrawn = pile.Withdraw( job.ResourceType, job.Amount );
			}

			if ( withdrawn <= 0 )
			{
				job.Status = LogisticsJobStatus.Failed;
				return ( false, $"nothing to withdraw from {job.FromId}" );
			}

			// Deposit at destination
			int deposited = 0;
			if ( job.ToId.StartsWith( "stockpile:" ) )
			{
				var pile = ResourceRegistry.GetStockpile( job.ToId.Substring( 10 ) );
				if ( pile == null ) return ( false, $"destination stockpile not found: {job.ToId}" );
				deposited = pile.Deposit( job.ResourceType, withdrawn );
			}
			else if ( job.ToId.StartsWith( "task:" ) )
			{
				// Deliver to a build site: update the task's material
				// requirement directly.
				var task = ConstructionDirector.GetTask( job.ToId.Substring( 5 ) );
				if ( task != null && task.MaterialRequirements != null )
				{
					foreach ( var req in task.MaterialRequirements )
					{
						if ( req.Type == job.ResourceType && !req.Satisfied )
						{
							req.Delivered += withdrawn;
							deposited = withdrawn;
							break;
						}
					}
				}
			}

			job.Status = LogisticsJobStatus.Completed;
			job.CompletedAt = SpatialBlackboard.CurrentTime;

			return ( deposited > 0 )
				? ( true, $"delivered {deposited} {job.ResourceType}" )
				: ( false, "deposit failed" );
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

				if ( ResourceClassification.IsRaw( req.Type ) )
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
					// Gather from source, deliver to nearest stockpile first
					// (raw materials need processing at a workstation before
					// they can be used for construction — but for now, allow
					// direct delivery for the bootstrap test)
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
