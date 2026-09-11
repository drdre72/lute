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
			if ( Npcs.Count > 0 )
			{
				sb += $"\n  NPCs ({Npcs.Count}):";
				foreach ( var kvp in Npcs.Take( 10 ) )
					sb += $"\n    {kvp.Key}: trust={kvp.Value.Trust} role={kvp.Value.Role}";
			}
			if ( Resources.Count > 0 )
			{
				sb += $"\n  Resources ({Resources.Count}):";
				foreach ( var kvp in Resources.Take( 10 ) )
					sb += $"\n    {kvp.Key}: available={kvp.Value.IsAvailable} claimedBy={kvp.Value.ClaimedBy}";
			}
			sb += $"\n  Memory: {Memory.Count}/{MaxMemory} entries";
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
}
