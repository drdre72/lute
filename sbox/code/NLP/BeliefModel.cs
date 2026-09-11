using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Per-NPC belief model. Each NPC maintains beliefs about:
	/// - Other NPCs (trust level, role, last known position)
	/// - World state (sites, resources, weather)
	/// - Its own goals and needs
	/// - Conversation memory (recent interactions)
	///
	/// The belief model is the NPC's "memory" — it's updated by
	/// observations and incoming intents, and read by the social rules
	/// engine to decide how to respond.
	///
	/// All beliefs are deterministic — no fuzzy logic, no probability
	/// clouds. Trust is a simple integer that goes up when an NPC helps
	/// and down when an NPC betrays or fails to deliver.
	/// </summary>
	public sealed class BeliefModel
	{
		/// <summary> The name of the NPC that owns these beliefs. </summary>
		public string SelfName { get; }

		/// <summary> This NPC's role (builder, merchant, guard, etc.). </summary>
		public string Role { get; set; } = "builder";

		/// <summary> This NPC's current goal (free-form, matched by social rules). </summary>
		public string CurrentGoal { get; set; } = "build";

		/// <summary>
		/// Beliefs about other NPCs, keyed by NPC name.
		/// </summary>
		public Dictionary<string, NpcBelief> Npcs { get; } = new();

		/// <summary>
		/// Beliefs about world resources/sites, keyed by subject name.
		/// </summary>
		public Dictionary<string, ResourceBelief> Resources { get; } = new();

		/// <summary>
		/// Recent conversation memory — last N interactions.
		/// </summary>
		public List<MemoryEntry> Memory { get; } = new();

		/// <summary> Max memory entries to retain. </summary>
		const int MaxMemory = 50;

		// ── Conversation context (for entity/pronoun resolution) ──

		/// <summary> The most recently referenced entity (for "it", "that" resolution). </summary>
		public string LastReferencedEntity { get; set; }

		/// <summary> The most recently referenced location (for "there", "here" resolution). </summary>
		public string LastReferencedLocation { get; set; }

		/// <summary> The NPC's current task (for entity keyword resolution). </summary>
		public string CurrentTask { get; set; }

		/// <summary> Recently referenced entities (for pronoun back-reference). </summary>
		public List<string> RecentEntities { get; } = new();
		const int MaxRecentEntities = 10;

		/// <summary> Add an entity to the recent entities list. </summary>
		public void AddRecentEntity( string entity )
		{
			if ( string.IsNullOrEmpty( entity ) )
				return;
			RecentEntities.Remove( entity );
			RecentEntities.Insert( 0, entity );
			if ( RecentEntities.Count > MaxRecentEntities )
				RecentEntities.RemoveAt( RecentEntities.Count - 1 );
		}

		// ── Self-beliefs (Phase 5) ──

		/// <summary> This NPC's self-beliefs (energy, morale, skill). </summary>
		public SelfBelief Self { get; set; } = new();

		// ── Task history (Phase 5) ──

		/// <summary> History of tasks this NPC has worked on. </summary>
		public List<TaskHistoryEntry> TaskHistory { get; } = new();
		const int MaxTaskHistory = 100;

		/// <summary> Record a task completion or failure in history. </summary>
		public void RecordTaskHistory( string taskId, string taskName, bool completed, string reason = "" )
		{
			TaskHistory.Add( new TaskHistoryEntry
			{
				TaskId = taskId,
				TaskName = taskName ?? "",
				Completed = completed,
				Reason = reason,
				Timestamp = SpatialBlackboard.CurrentTime,
			} );
			if ( TaskHistory.Count > MaxTaskHistory )
				TaskHistory.RemoveAt( 0 );
		}

		/// <summary> Count of completed tasks. </summary>
		public int TasksCompleted => TaskHistory.Count( t => t.Completed );

		/// <summary> Count of failed tasks. </summary>
		public int TasksFailed => TaskHistory.Count( t => !t.Completed );

		/// <summary> Success rate (0-1). </summary>
		public float SuccessRate => TaskHistory.Count == 0
			? 1f
			: (float)TasksCompleted / TaskHistory.Count;

		// ── Reputation (Phase 5) ──
		// Reputation is separate from trust. Trust is interpersonal (how
		// much I trust YOU). Reputation is communal (what others think of
		// you based on your actions). NPCs can observe another NPC's
		// task completions/failures and update their reputation belief.

		/// <summary> Reputation beliefs about other NPCs, keyed by name. </summary>
		public Dictionary<string, ReputationBelief> Reputation { get; } = new();

		/// <summary> Get or create a reputation belief about an NPC. </summary>
		public ReputationBelief GetOrCreateReputation( string npcName )
		{
			if ( !Reputation.TryGetValue( npcName, out var rep ) )
			{
				rep = new ReputationBelief { NpcName = npcName };
				Reputation[npcName] = rep;
			}
			return rep;
		}

		/// <summary> Adjust another NPC's reputation. Clamped to [-100, 100]. </summary>
		public void AdjustReputation( string npcName, int delta )
		{
			var r = GetOrCreateReputation( npcName );
			r.Score = Math.Clamp( r.Score + delta, -100, 100 );
		}

		/// <summary> Get another NPC's reputation score (0 = neutral/unknown). </summary>
		public int GetReputation( string npcName )
		{
			return Reputation.TryGetValue( npcName, out var r ) ? r.Score : 0;
		}

		// ── Short-term conversational memory (Phase 5) ──

		/// <summary> Current conversation state (null if not in a conversation). </summary>
		public ConversationState CurrentConversation { get; set; }

		public BeliefModel( string selfName )
		{
			SelfName = selfName;
		}

		// ── NPC beliefs ──

		/// <summary>
		/// Get or create a belief about another NPC.
		/// </summary>
		public NpcBelief GetOrCreateNpc( string name )
		{
			if ( !Npcs.TryGetValue( name, out var belief ) )
			{
				belief = new NpcBelief { Name = name };
				Npcs[name] = belief;
			}
			return belief;
		}

		/// <summary> Adjust trust toward another NPC. Clamped to [-100, 100]. </summary>
		public void AdjustTrust( string npcName, int delta )
		{
			var b = GetOrCreateNpc( npcName );
			b.Trust = Math.Clamp( b.Trust + delta, -100, 100 );
		}

		/// <summary> Get trust level toward another NPC (0 for unknown NPCs). </summary>
		public int GetTrust( string npcName )
		{
			return Npcs.TryGetValue( npcName, out var b ) ? b.Trust : 0;
		}

		/// <summary> Record that another NPC has a specific role. </summary>
		public void SetNpcRole( string npcName, string role )
		{
			var b = GetOrCreateNpc( npcName );
			b.Role = role;
		}

		// ── Resource beliefs ──

		/// <summary> Get or create a belief about a resource/site. </summary>
		public ResourceBelief GetOrCreateResource( string subject )
		{
			if ( !Resources.TryGetValue( subject, out var belief ) )
			{
				belief = new ResourceBelief { Subject = subject };
				Resources[subject] = belief;
			}
			return belief;
		}

		/// <summary> Record that a site is claimed by someone. </summary>
		public void SetSiteClaim( string site, string claimedBy )
		{
			var r = GetOrCreateResource( site );
			r.ClaimedBy = claimedBy;
			r.IsAvailable = string.IsNullOrEmpty( claimedBy );
		}

		/// <summary> Check if a site is believed to be available. </summary>
		public bool IsSiteAvailable( string site )
		{
			if ( !Resources.TryGetValue( site, out var r ) )
				return true; // unknown = assume available
			return r.IsAvailable;
		}

		// ── Memory ──

		/// <summary> Record an interaction in memory. </summary>
		public void RecordMemory( MemoryEntry entry )
		{
			Memory.Add( entry );
			if ( Memory.Count > MaxMemory )
				Memory.RemoveAt( 0 );
		}

		/// <summary> Record a simple interaction. </summary>
		public void RecordMemory( string otherNpc, Intent intent, bool wasInitiator )
		{
			RecordMemory( new MemoryEntry
			{
				OtherNpc = otherNpc,
				IntentType = intent.Type,
				Topic = intent.Topic,
				Subject = intent.Subject,
				Timestamp = SpatialBlackboard.CurrentTime,
				WasInitiator = wasInitiator,
			} );
		}

		/// <summary> Get recent interactions with a specific NPC. </summary>
		public IEnumerable<MemoryEntry> GetRecentWith( string npcName, int count = 5 )
		{
			return Memory.Where( m => m.OtherNpc == npcName )
				.TakeLast( count );
		}

		/// <summary> Get a debug summary of this NPC's beliefs. </summary>
		public string Summary()
		{
			var sb = $"Beliefs[{SelfName}] role={Role} goal={CurrentGoal}";
			sb += $"\n  Self: energy={Self.Energy:F0} morale={Self.Morale:F0} skill={Self.Skill:F0} tasks={Self.TasksCompleted}done/{Self.TasksFailed}fail";
			if ( Npcs.Count > 0 )
			{
				sb += $"\n  NPCs ({Npcs.Count}):";
				foreach ( var kvp in Npcs.Take( 10 ) )
					sb += $"\n    {kvp.Key}: trust={kvp.Value.Trust} role={kvp.Value.Role}";
			}
			if ( Reputation.Count > 0 )
			{
				sb += $"\n  Reputation ({Reputation.Count}):";
				foreach ( var kvp in Reputation.Take( 10 ) )
					sb += $"\n    {kvp.Key}: score={kvp.Value.Score} done={kvp.Value.TasksCompletedObserved} fail={kvp.Value.TasksFailedObserved}";
			}
			if ( Resources.Count > 0 )
			{
				sb += $"\n  Resources ({Resources.Count}):";
				foreach ( var kvp in Resources.Take( 10 ) )
					sb += $"\n    {kvp.Key}: available={kvp.Value.IsAvailable} claimedBy={kvp.Value.ClaimedBy}";
			}
			sb += $"\n  Memory: {Memory.Count}/{MaxMemory} entries";
			sb += $"\n  TaskHistory: {TaskHistory.Count}/{MaxTaskHistory} ({TasksCompleted} done, {TasksFailed} fail, {SuccessRate:P0} success)";
			if ( CurrentConversation != null && CurrentConversation.IsActive )
				sb += $"\n  Conversation: with={CurrentConversation.OtherNpc} turns={CurrentConversation.TurnCount}";
			return sb;
		}
	}

	/// <summary> Belief about a specific other NPC. </summary>
	public sealed class NpcBelief
	{
		/// <summary> NPC name. </summary>
		public string Name { get; set; } = "";

		/// <summary> Trust level: -100 (enemy) to 100 (ally). 0 = neutral/unknown. </summary>
		public int Trust { get; set; }

		/// <summary> Believed role of this NPC. </summary>
		public string Role { get; set; } = "";

		/// <summary> Last known position (optional). </summary>
		public Vector3? LastKnownPosition { get; set; }

		/// <summary> Last interaction timestamp. </summary>
		public float LastInteraction { get; set; }
	}

	/// <summary> Belief about a world resource or site. </summary>
	public sealed class ResourceBelief
	{
		/// <summary> Subject name (e.g. "north_site", "stone"). </summary>
		public string Subject { get; set; } = "";

		/// <summary> Who currently claims this resource ("" = unclaimed). </summary>
		public string ClaimedBy { get; set; } = "";

		/// <summary> True if believed to be available. </summary>
		public bool IsAvailable { get; set; } = true;

		/// <summary> Estimated quantity (for materials). </summary>
		public int EstimatedQuantity { get; set; }
	}

	/// <summary> A memory entry — record of a past interaction. </summary>
	public sealed class MemoryEntry
	{
		/// <summary> The other NPC involved. </summary>
		public string OtherNpc { get; set; } = "";

		/// <summary> What type of intent was exchanged. </summary>
		public IntentType IntentType { get; set; }

		/// <summary> Topic of the exchange. </summary>
		public IntentTopic Topic { get; set; }

		/// <summary> Subject of the exchange. </summary>
		public string Subject { get; set; } = "";

		/// <summary> When this happened. </summary>
		public float Timestamp { get; set; }

		/// <summary> True if this NPC initiated the exchange. </summary>
		public bool WasInitiator { get; set; }
	}

	/// <summary>
	/// Self-beliefs — an NPC's beliefs about itself. These affect
	/// decision-making (e.g. low energy → request rest, high skill →
	/// volunteer for harder tasks) but NOT intelligence.
	/// </summary>
	public sealed class SelfBelief
	{
		/// <summary> Energy level (0-100). Low energy → slower building. </summary>
		public float Energy { get; set; } = 100f;

		/// <summary> Morale (0-100). Low morale → less likely to volunteer. </summary>
		public float Morale { get; set; } = 80f;

		/// <summary> Skill level (0-100). Higher skill → faster building. </summary>
		public float Skill { get; set; } = 50f;

		/// <summary> Tasks completed (lifetime counter). </summary>
		public int TasksCompleted { get; set; }

		/// <summary> Tasks failed (lifetime counter). </summary>
		public int TasksFailed { get; set; }

		/// <summary> Reduce energy by the given amount (clamped to 0). </summary>
		public void DrainEnergy( float amount )
		{
			Energy = Math.Max( 0, Energy - amount );
		}

		/// <summary> Restore energy by the given amount (clamped to 100). </summary>
		public void RestoreEnergy( float amount )
		{
			Energy = Math.Min( 100, Energy + amount );
		}

		/// <summary> Adjust morale (clamped to 0-100). </summary>
		public void AdjustMorale( float delta )
		{
			Morale = Math.Clamp( Morale + delta, 0, 100 );
		}
	}

	/// <summary>
	/// A task history entry — record of a task this NPC worked on.
	/// </summary>
	public sealed class TaskHistoryEntry
	{
		/// <summary> The task ID. </summary>
		public string TaskId { get; set; } = "";

		/// <summary> The task name (for readability). </summary>
		public string TaskName { get; set; } = "";

		/// <summary> True if completed successfully, false if failed. </summary>
		public bool Completed { get; set; }

		/// <summary> Failure reason (if applicable). </summary>
		public string Reason { get; set; } = "";

		/// <summary> When this happened (game time). </summary>
		public float Timestamp { get; set; }
	}

	/// <summary>
	/// Reputation belief about another NPC. Reputation is communal —
	/// it's based on observed actions (task completions, failures,
	/// help offered) rather than direct interpersonal interactions.
	///
	/// Trust is "how much I trust you based on our interactions."
	/// Reputation is "what I think of you based on your behavior."
	/// </summary>
	public sealed class ReputationBelief
	{
		/// <summary> NPC name. </summary>
		public string NpcName { get; set; } = "";

		/// <summary> Reputation score: -100 (notorious) to 100 (renowned). 0 = unknown. </summary>
		public int Score { get; set; }

		/// <summary> Number of tasks observed completing. </summary>
		public int TasksCompletedObserved { get; set; }

		/// <summary> Number of tasks observed failing. </summary>
		public int TasksFailedObserved { get; set; }

		/// <summary> Number of times observed offering help. </summary>
		public int HelpOfferedObserved { get; set; }

		/// <summary> Number of times observed requesting help. </summary>
		public int HelpRequestedObserved { get; set; }
	}

	/// <summary>
	/// Short-term conversational state — tracks the current conversation
	/// an NPC is in, including the other participant, turn count, and
	/// referenced entities. This is separate from long-term memory.
	/// </summary>
	public sealed class ConversationState
	{
		/// <summary> The other NPC in this conversation. </summary>
		public string OtherNpc { get; set; }

		/// <summary> How many turns have elapsed. </summary>
		public int TurnCount { get; set; }

		/// <summary> When the conversation started (game time). </summary>
		public float StartTime { get; set; }

		/// <summary> Last turn time (game time). </summary>
		public float LastTurnTime { get; set; }

		/// <summary> Entities referenced in this conversation. </summary>
		public List<string> ReferencedEntities { get; } = new();

		/// <summary> Is this conversation still active? </summary>
		public bool IsActive => !string.IsNullOrEmpty( OtherNpc );

		/// <summary> Start a conversation with another NPC. </summary>
		public void Start( string otherNpc, float currentTime )
		{
			OtherNpc = otherNpc;
			TurnCount = 0;
			StartTime = currentTime;
			LastTurnTime = currentTime;
			ReferencedEntities.Clear();
		}

		/// <summary> Record a turn in this conversation. </summary>
		public void RecordTurn( float currentTime )
		{
			TurnCount++;
			LastTurnTime = currentTime;
		}

		/// <summary> End the conversation. </summary>
		public void End()
		{
			OtherNpc = null;
			TurnCount = 0;
			ReferencedEntities.Clear();
		}
	}
}
