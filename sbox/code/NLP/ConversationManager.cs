using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Central conversation manager. Each NPC that wants to participate
	/// in NLP-driven conversation registers here with its name and
	/// <see cref="BeliefModel"/>. The manager routes messages between
	/// NPCs, parses incoming text into intents, evaluates them through
	/// social rules, generates response text, and posts it back.
	///
	/// This replaces the LLM-based <see cref="Lute.Building.NPCConversation"/>
	/// with a fully deterministic pipeline:
	///
	///   text in → NlpParser.Parse → SocialRules.Evaluate → SpeechGenerator.Generate → text out
	///
	/// All via the <see cref="Lute.Building.SpatialBlackboard"/> message
	/// system — no LLM, no cloud, no randomness.
	/// </summary>
	public static class ConversationManager
	{
		/// <summary> Registered NPCs keyed by name. </summary>
		static readonly Dictionary<string, NpcEntry> _npcs = new();

		/// <summary> Max conversation turns before auto-close. </summary>
		const int MaxTurns = 8;

		/// <summary>
		/// Register an NPC for NLP conversation. The NPC must have a
		/// <see cref="BeliefModel"/> — the manager will update it as
		/// messages arrive.
		/// </summary>
		public static void Register( string npcName, BeliefModel beliefs, string role = "builder" )
		{
			_npcs[npcName] = new NpcEntry
			{
				Name = npcName,
				Beliefs = beliefs,
				Role = role,
			};
			beliefs.Role = role;
			Log.Info( $"[NLP] Registered NPC '{npcName}' as {role}." );
		}

		/// <summary> Unregister an NPC. </summary>
		public static void Unregister( string npcName )
		{
			_npcs.Remove( npcName );
		}

		/// <summary> Get all registered NPC names. </summary>
		public static List<string> GetNpcNames() => _npcs.Keys.ToList();

		/// <summary> Get an NPC's belief model (or null). </summary>
		public static BeliefModel GetBeliefs( string npcName )
		{
			return _npcs.TryGetValue( npcName, out var e ) ? e.Beliefs : null;
		}

		/// <summary>
		/// Send a text message from one NPC to another. This is the main
		/// entry point for NPC-to-NPC communication. The message is:
		/// 1. Posted to the SpatialBlackboard (for spatial awareness)
		/// 2. Parsed into an Intent by NlpParser
		/// 3. Evaluated by SocialRules against the receiver's beliefs
		/// 4. If a response intent is produced, converted to text by
		///    SpeechGenerator and sent back
		/// </summary>
		/// <returns> The response text, or null if no response. </returns>
		public static string Send( string from, string to, string text )
		{
			if ( string.IsNullOrWhiteSpace( text ) )
				return null;

			// Post to blackboard for spatial awareness
			Lute.Building.SpatialBlackboard.PostMessage(
				from: from,
				to: to,
				type: "nlp_message",
				content: text
			);

			// Parse the incoming text
			var intent = NlpParser.Parse( text, sender: from, target: to );

			Log.Info( $"[NLP] {from} → {to}: \"{text}\" → {intent.Summary}" );

			// Find the receiver
			if ( !_npcs.TryGetValue( to, out var receiver ) )
			{
				Log.Info( $"[NLP] Receiver '{to}' not registered — message dropped." );
				return null;
			}

			// Track conversation turns
			receiver.TurnCount++;
			if ( receiver.TurnCount > MaxTurns )
			{
				Log.Info( $"[NLP] {to} max turns reached — ending conversation with {from}." );
				receiver.TurnCount = 0;
				return null;
			}

			// Evaluate through social rules
			var responseIntent = SocialRules.Evaluate( intent, receiver.Beliefs );

			if ( responseIntent == null )
			{
				Log.Info( $"[NLP] {to} stayed silent (no response to {intent.Type})." );
				return null;
			}

			// Generate response text
			var responseText = SpeechGenerator.Generate( responseIntent );

			Log.Info( $"[NLP] {to} → {from}: \"{responseText}\" ← {responseIntent.Summary}" );

			// Post response to blackboard
			Lute.Building.SpatialBlackboard.PostMessage(
				from: to,
				to: from,
				type: "nlp_reply",
				content: responseText
			);

			return responseText;
		}

		/// <summary>
		/// Broadcast a message to all registered NPCs. Each receiver
		/// processes it independently through their own beliefs.
		/// </summary>
		public static void Broadcast( string from, string text )
		{
			foreach ( var name in _npcs.Keys )
			{
				if ( name != from )
					Send( from, name, text );
			}
		}

		/// <summary>
		/// Process pending blackboard messages for a specific NPC.
		/// Called by the NPC's update loop. This picks up any messages
		/// that were posted to the blackboard since the last check.
		/// </summary>
		public static void ProcessIncoming( string npcName, float sinceTimestamp )
		{
			if ( !_npcs.TryGetValue( npcName, out var entry ) )
				return;

			var messages = Lute.Building.SpatialBlackboard.GetMessages( npcName, sinceTimestamp );

			foreach ( var msg in messages )
			{
				if ( msg.Type == "nlp_message" || msg.Type == "conversation_start" )
				{
					// Parse and respond
					var intent = NlpParser.Parse( msg.Content, sender: msg.From, target: npcName );
					var response = SocialRules.Evaluate( intent, entry.Beliefs );

					if ( response != null )
					{
						var responseText = SpeechGenerator.Generate( response );
						Log.Info( $"[NLP] {npcName} → {msg.From}: \"{responseText}\" ← {response.Summary}" );

						Lute.Building.SpatialBlackboard.PostMessage(
							from: npcName,
							to: msg.From,
							type: "nlp_reply",
							content: responseText
						);
					}
				}
			}
		}

		/// <summary> Get a debug summary of all registered NPCs. </summary>
		public static string Summary()
		{
			var sb = $"[ConversationManager] {_npcs.Count} NPC(s) registered:";
			foreach ( var e in _npcs.Values )
			{
				sb += $"\n  {e.Name} ({e.Role}) — turns={e.TurnCount}, beliefs={e.Beliefs.Npcs.Count} npc(s), {e.Beliefs.Resources.Count} resource(s)";
			}
			return sb;
		}

		sealed class NpcEntry
		{
			public string Name { get; set; }
			public BeliefModel Beliefs { get; set; }
			public string Role { get; set; }
			public int TurnCount { get; set; }
		}
	}
}
