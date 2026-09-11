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
	/// <see cref="BeliefModel"/>.
	///
	/// Architecture (fixed per professor feedback):
	///
	///   Send() = "I have placed a message into the communication system."
	///            It posts to the blackboard and returns. It does NOT
	///            synchronously evaluate the recipient.
	///
	///   ProcessIncoming() = the sole consumer. Each NPC's update loop
	///            calls this. It reads unprocessed messages from the
	///            blackboard (using monotonic MessageId for exactly-once
	///            delivery), parses them, evaluates through social rules,
	///            generates response text, and posts the reply back to
	///            the blackboard for the other NPC to consume on its
	///            next tick.
	///
	/// This makes NPCs independent agents — each processes messages on
	/// its own tick, at its own pace. No synchronous evaluation, no
	/// double-processing.
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
				State = new NpcState(),
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

		/// <summary> Get an NPC's runtime state (or null). </summary>
		public static NpcState GetState( string npcName )
		{
			return _npcs.TryGetValue( npcName, out var e ) ? e.State : null;
		}

		/// <summary>
		/// Evaluate an NPC's goals and produce the next action. Called
		/// by the NPC's update loop when it's not processing messages.
		/// </summary>
		public static NpcAction EvaluateGoals( string npcName )
		{
			if ( !_npcs.TryGetValue( npcName, out var entry ) )
				return NpcAction.Idle( "NPC not registered" );

			// Make sure the evaluator knows about all NPCs
			GoalActionEvaluator.SetNpcNames( _npcs.Keys.ToList() );

			return GoalActionEvaluator.Evaluate( entry.Beliefs, entry.State );
		}

		/// <summary>
		/// Send a text message from one NPC to another. This ONLY posts
		/// the message to the CommunicationBus — it does NOT evaluate the
		/// recipient synchronously. The recipient will process it on
		/// its own tick via <see cref="ProcessIncoming"/>.
		///
		/// Send() means: "I have placed a message into the
		/// communication system." Not: "I have simulated the entire
		/// conversation already."
		/// </summary>
		public static void Send( string from, string to, string text )
		{
			if ( string.IsNullOrWhiteSpace( text ) )
				return;

			CommunicationBus.Post( from, to, "nlp_message", text );
			Log.Info( $"[NLP] {from} → {to}: posted \"{text}\"" );
		}

		/// <summary>
		/// Broadcast a message to all registered NPCs. Only posts to
		/// the CommunicationBus — each NPC processes it independently on its
		/// own tick.
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
		/// Process pending CommunicationBus messages for a specific NPC.
		/// Called by the NPC's update loop. This is the SOLE consumer —
		/// messages are parsed, evaluated, and responded to here, not in
		/// Send().
		///
		/// Uses monotonic MessageId for exactly-once consumption: each
		/// NPC tracks the highest MessageId it has processed and only
		/// receives messages with higher IDs.
		/// </summary>
		public static void ProcessIncoming( string npcName )
		{
			if ( !_npcs.TryGetValue( npcName, out var entry ) )
				return;

			var messages = CommunicationBus.GetUnprocessed(
				npcName, entry.LastProcessedMessageId );

			foreach ( var msg in messages )
			{
				// Track the highest processed message ID
				if ( msg.MessageId > entry.LastProcessedMessageId )
					entry.LastProcessedMessageId = msg.MessageId;

				// Only process NLP messages
				if ( msg.Type != "nlp_message" && msg.Type != "nlp_reply" &&
					 msg.Type != "conversation_start" )
					continue;

				// Track conversation turns
				entry.TurnCount++;
				if ( entry.TurnCount > MaxTurns )
				{
					Log.Info( $"[NLP] {npcName} max turns reached — ending conversation with {msg.From}." );
					entry.TurnCount = 0;
					continue;
				}

				// Parse the incoming text
				var intent = NlpParser.Parse( msg.Content, sender: msg.From, target: npcName );
				Log.Info( $"[NLP] {npcName} received from {msg.From}: \"{msg.Content}\" → {intent.Summary}" );

				// Evaluate through social rules — returns a decision
				var decision = SocialRules.Evaluate( intent, entry.Beliefs );

				Log.Info( $"[NLP] {npcName} decision: {decision.Summary}" );

				// Handle the decision
				switch ( decision.Action )
				{
					case DecisionAction.Ignore:
						// Stay silent
						break;

					case DecisionAction.Speak:
						// Generate response text and post to CommunicationBus
						if ( decision.ResponseIntent != null )
						{
							var responseText = SpeechGenerator.Generate( decision.ResponseIntent );
							Log.Info( $"[NLP] {npcName} → {msg.From}: \"{responseText}\"" );
							CommunicationBus.Post( npcName, msg.From, "nlp_reply", responseText );
						}
						break;

					case DecisionAction.Act:
						// Take a physical action (the GoalAction system handles this)
						Log.Info( $"[NLP] {npcName} acting: {decision.WorldAction}" );
						// World actions are handled by the NPC's controller, not here.
						// The decision is logged for the controller to pick up.
						break;

					case DecisionAction.SpeakAndAct:
						// Both speak and act
						if ( decision.ResponseIntent != null )
						{
							var responseText = SpeechGenerator.Generate( decision.ResponseIntent );
							Log.Info( $"[NLP] {npcName} → {msg.From}: \"{responseText}\" + action: {decision.WorldAction}" );
							CommunicationBus.Post( npcName, msg.From, "nlp_reply", responseText );
						}
						Log.Info( $"[NLP] {npcName} acting: {decision.WorldAction}" );
						break;

					case DecisionAction.Defer:
						Log.Info( $"[NLP] {npcName} deferring: {decision.Reason}" );
						break;

					case DecisionAction.AskForClarification:
						if ( decision.ResponseIntent != null )
						{
							var responseText = SpeechGenerator.Generate( decision.ResponseIntent );
							Log.Info( $"[NLP] {npcName} → {msg.From}: \"{responseText}\" (clarification)" );
							CommunicationBus.Post( npcName, msg.From, "nlp_reply", responseText );
						}
						break;
				}

				// Also process through BlackboardProtocol for CLAIM/RELEASE intents
				BlackboardProtocol.ProcessIntent( intent );
			}
		}

		/// <summary> Get a debug summary of all registered NPCs. </summary>
		public static string Summary()
		{
			var sb = $"[ConversationManager] {_npcs.Count} NPC(s) registered:";
			foreach ( var e in _npcs.Values )
			{
				sb += $"\n  {e.Name} ({e.Role}) — turns={e.TurnCount}, lastMsg={e.LastProcessedMessageId}, beliefs={e.Beliefs.Npcs.Count} npc(s), {e.Beliefs.Resources.Count} resource(s)";
			}
			return sb;
		}

		sealed class NpcEntry
		{
			public string Name { get; set; }
			public BeliefModel Beliefs { get; set; }
			public NpcState State { get; set; }
			public string Role { get; set; }
			public int TurnCount { get; set; }
			/// <summary> Highest MessageId this NPC has processed (exactly-once consumption). </summary>
			public long LastProcessedMessageId { get; set; }
		}
	}
}
