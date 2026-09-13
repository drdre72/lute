using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.NLP;

namespace Lute.Building
{
	/// <summary>
	/// Deterministic bridge from concrete world state to NPC communication.
	///
	/// Observes <see cref="ConstructionDirector"/>, <see cref="SpatialRegistry"/>,
	/// and <see cref="BuilderLivenessRegistry"/> and emits structured
	/// <see cref="Intent"/> objects through the existing NLP pipeline
	/// (<see cref="SpeechGenerator"/> → <see cref="ConversationManager"/>).
	///
	/// This is what lets NPCs say things like:
	///   "South wall section 4 is blocked by StorageRack_17."
	///   "I reserved the western foundation cells."
	///   "Gate support is incomplete; waiting for Builder_2."
	///
	/// No LLM. No randomness. Every fact is derived from authoritative runtime
	/// state. The same world state always produces the same message.
	/// </summary>
	public static class WorldFactProvider
	{
		/// <summary>
		/// Minimum seconds between two facts of the same category from the
		/// same NPC. Prevents blackboard spam when many tasks fail at once.
		/// </summary>
		const float MinIntervalPerCategory = 5f;

		/// <summary>
		/// Maximum number of facts emitted per <see cref="Tick"/> call.
		/// Bounds the work done per frame so a sudden burst of failures
		/// doesn't flood the blackboard.
		/// </summary>
		const int MaxFactsPerTick = 3;

		// ── Per-NPC, per-category throttle state ──
		static readonly Dictionary<(string npc, string category), float> _lastEmit = new();
		static float _lastTickTime;

		// ── Already-announced task transitions (avoid re-announcing) ──
		static readonly HashSet<string> _announcedBlocked = new();
		static readonly HashSet<string> _announcedDepBlocked = new();
		static readonly HashSet<string> _announcedComplete = new();
		static readonly HashSet<string> _announcedFailed = new();
		static readonly HashSet<string> _announcedStall = new();

		/// <summary>
		/// Observe world state and emit any notable facts. Call this once
		/// per frame from a central update point (e.g. LuteGame.OnUpdate).
		/// </summary>
		public static void Tick()
		{
			float now = Time.Now;
			_lastTickTime = now;

			int emitted = 0;

			// 1. Task completions → ReportCompletion
			foreach ( var t in ConstructionDirector.AllTasks()
				.Where( t => t.Status == TaskStatus.Complete && !_announcedComplete.Contains( t.Id ) )
				.OrderBy( t => t.Id ) )
			{
				if ( emitted >= MaxFactsPerTick ) return;
				if ( !ThrottleOk( t.AssignedBuilder, "complete", now ) ) continue;

				_announcedComplete.Add( t.Id );
				EmitTaskCompletion( t, now );
				emitted++;
			}

			// 2. Dependency-blocked tasks → ReportProblem (waiting on prerequisite)
			foreach ( var t in ConstructionDirector.DependencyBlockedTasks()
				.Where( t => !_announcedDepBlocked.Contains( t.Id ) )
				.OrderBy( t => t.Id ) )
			{
				if ( emitted >= MaxFactsPerTick ) return;
				if ( !ThrottleOk( t.AssignedBuilder, "dep_block", now ) ) continue;

				_announcedDepBlocked.Add( t.Id );
				EmitDependencyWait( t, now );
				emitted++;
			}

			// 3. Reservation-blocked tasks → ReportProblem (construction conflict)
			foreach ( var t in ConstructionDirector.AllTasks()
				.Where( t => t.Status == TaskStatus.Blocked &&
					!string.IsNullOrEmpty( t.BlockedByTaskId ) &&
					!_announcedBlocked.Contains( t.Id ) )
				.OrderBy( t => t.Id ) )
			{
				if ( emitted >= MaxFactsPerTick ) return;
				if ( !ThrottleOk( t.AssignedBuilder, "res_block", now ) ) continue;

				_announcedBlocked.Add( t.Id );
				EmitReservationConflict( t, now );
				emitted++;
			}

			// 4. Permanently failed tasks → Warn
			foreach ( var t in ConstructionDirector.AllTasks()
				.Where( t => t.Status == TaskStatus.Failed && !_announcedFailed.Contains( t.Id ) )
				.OrderBy( t => t.Id ) )
			{
				if ( emitted >= MaxFactsPerTick ) return;
				if ( !ThrottleOk( t.AssignedBuilder, "failed", now ) ) continue;

				_announcedFailed.Add( t.Id );
				EmitTaskFailure( t, now );
				emitted++;
			}

			// 5. Stalled builders → Warn (idle beyond threshold)
			foreach ( var s in BuilderLivenessRegistry.GetStalledBuilders()
				.Where( s => !_announcedStall.Contains( s.NpcId ) ) )
			{
				if ( emitted >= MaxFactsPerTick ) return;
				if ( !ThrottleOk( BuilderIdFor( s.NpcId ), "stall", now ) ) continue;

				_announcedStall.Add( s.NpcId );
				EmitBuilderStall( s, now );
				emitted++;
			}
		}

		/// <summary>
		/// Reset all announced-state tracking (e.g. on scene reload or
		/// ConstructionDirector.Reset). Call this so a fresh build run
		/// doesn't skip facts that were announced in a prior run.
		/// </summary>
		public static void Reset()
		{
			_lastEmit.Clear();
			_announcedBlocked.Clear();
			_announcedDepBlocked.Clear();
			_announcedComplete.Clear();
			_announcedFailed.Clear();
			_announcedStall.Clear();
		}

		// ── Fact emitters ──

		static void EmitTaskCompletion( DirectedTask t, float now )
		{
			var npcName = NpcNameFor( t.AssignedBuilder );
			var subject = t.BuildTask?.Name ?? t.Id;
			var intent = new Intent
			{
				Type = IntentType.ReportCompletion,
				Topic = IntentTopic.Task,
				Subject = subject,
				Sender = npcName,
				Target = "",
				Parameters = new()
				{
					{ "task_id", t.Id },
					{ "pieces", t.PiecesPlaced.ToString() },
				},
			};
			Deliver( intent, npcName, now, "complete" );
		}

		static void EmitDependencyWait( DirectedTask t, float now )
		{
			var npcName = NpcNameFor( t.AssignedBuilder );
			var subject = t.BuildTask?.Name ?? t.Id;
			var blocker = ConstructionDirector.GetTask( t.BlockedByDependency );
			var blockerName = blocker?.BuildTask?.Name ?? t.BlockedByDependency ?? "unknown";
			var intent = new Intent
			{
				Type = IntentType.ReportProblem,
				Topic = IntentTopic.Task,
				Subject = subject,
				Sender = npcName,
				Target = "",
				Parameters = new()
				{
					{ "task_id", t.Id },
					{ "waiting_on", t.BlockedByDependency ?? "" },
					{ "waiting_on_name", blockerName },
					{ "reason", "dependency" },
				},
			};
			Deliver( intent, npcName, now, "dep_block" );
		}

		static void EmitReservationConflict( DirectedTask t, float now )
		{
			var npcName = NpcNameFor( t.AssignedBuilder );
			var subject = t.BuildTask?.Name ?? t.Id;
			var blocker = ConstructionDirector.GetTask( t.BlockedByTaskId );
			var blockerName = blocker?.BuildTask?.Name ?? t.BlockedByTaskId ?? "unknown";
			var intent = new Intent
			{
				Type = IntentType.ReportProblem,
				Topic = IntentTopic.Site,
				Subject = subject,
				Sender = npcName,
				Target = "",
				Parameters = new()
				{
					{ "task_id", t.Id },
					{ "blocker", t.BlockedByTaskId ?? "" },
					{ "blocker_name", blockerName },
					{ "reason", "reservation" },
				},
			};
			Deliver( intent, npcName, now, "res_block" );
		}

		static void EmitTaskFailure( DirectedTask t, float now )
		{
			var npcName = NpcNameFor( t.AssignedBuilder );
			var subject = t.BuildTask?.Name ?? t.Id;
			var intent = new Intent
			{
				Type = IntentType.Warn,
				Topic = IntentTopic.Task,
				Subject = subject,
				Sender = npcName,
				Target = "",
				Parameters = new()
				{
					{ "task_id", t.Id },
					{ "retries", t.RetryCount.ToString() },
					{ "reason", "permanent_failure" },
				},
			};
			Deliver( intent, npcName, now, "failed" );
		}

		static void EmitBuilderStall( BuilderLivenessState s, float now )
		{
			var intent = new Intent
			{
				Type = IntentType.Warn,
				Topic = IntentTopic.Task,
				Subject = s.NpcId,
				Sender = s.NpcId,
				Target = "",
				Parameters = new()
				{
					{ "npc", s.NpcId },
					{ "reason", s.IdleReason.ToString() },
					{ "duration", s.IdleDuration.ToString( "F1" ) },
					{ "waiting_on", s.WaitingOnTaskId ?? "" },
				},
			};
			Deliver( intent, s.NpcId, now, "stall" );
		}

		// ── Delivery ──

		static void Deliver( Intent intent, string npcName, float now, string category )
		{
			var text = SpeechGenerator.Generate( intent );
			if ( string.IsNullOrWhiteSpace( text ) )
				return;

			// Broadcast to all registered NPCs — any builder that cares
			// can pick this up via ConversationManager.ProcessIncoming.
			ConversationManager.Broadcast( npcName, text );
			Log.Info( $"Lute: WorldFactProvider [{category}] {npcName}: \"{text}\" — {intent.Summary}" );
		}

		// ── Throttle ──

		static bool ThrottleOk( int builderId, string category, float now )
		{
			var key = (builderId.ToString(), category);
			if ( _lastEmit.TryGetValue( key, out float last ) && now - last < MinIntervalPerCategory )
				return false;
			_lastEmit[key] = now;
			return true;
		}

		// ── Helpers ──

		static string NpcNameFor( int builderId )
		{
			var b = ConstructionDirector.AllBuilders().FirstOrDefault( x => x.BuilderId == builderId );
			return b?.NpcName ?? $"Builder_{builderId}";
		}

		static int BuilderIdFor( string npcName )
		{
			foreach ( var b in ConstructionDirector.AllBuilders() )
				if ( b.NpcName == npcName ) return b.BuilderId;
			return -1;
		}

		/// <summary>
		/// Diagnostic: how many facts have been announced so far.
		/// </summary>
		public static string Status()
		{
			return $"WorldFactProvider: complete={_announcedComplete.Count} " +
				$"depBlocked={_announcedDepBlocked.Count} " +
				$"resBlocked={_announcedBlocked.Count} " +
				$"failed={_announcedFailed.Count} " +
				$"stalled={_announcedStall.Count} " +
				$"throttles={_lastEmit.Count}";
		}

		[ConCmd( "world_facts" )]
		static void WorldFactsCmd()
		{
			Log.Info( $"Lute: {Status()}" );
		}
	}
}
