using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;

namespace Lute.Building
{
	using Lute.NLP;

	/// <summary>
	/// Conversation mode for NPC-to-NPC text exchange via the deterministic
	/// NLP pipeline. NPCs communicate through NlpParser → SocialRules →
	/// BlackboardProtocol, NOT through an LLM.
	///
	/// Conversation flow:
	/// 1. NPC A posts a message to the SpatialBlackboard addressed to NPC B
	/// 2. NPC B's NPCConversation picks it up on the next tick
	/// 3. NPC B's NlpParser parses the message into an Intent
	/// 4. SocialRules evaluates the intent and produces a ResponseDecision
	/// 5. If the decision is to speak, SpeechGenerator produces a reply
	/// 6. The reply is posted back to the blackboard for NPC A
	/// 7. BlackboardProtocol processes any CLAIM/RELEASE intents
	///
	/// The LLM-based NPCBrain is NOT used in the runtime conversation path.
	/// It remains as an optional dev tool for offline content generation.
	/// </summary>
	public sealed class NPCConversation : Component
	{
		/// <summary> This NPC's name (must match SpatialBlackboard position key). </summary>
		[Property] public string NpcName { get; set; } = "NPC";

		/// <summary> Reference to this NPC's brain (for LLM processing). </summary>
		[Property] public NPCBrain Brain { get; set; }

		/// <summary> Enable/disable conversation mode. Latent by default. </summary>
		[Property] public bool ConversationEnabled { get; set; } = false;

		/// <summary> How often to check for new messages (seconds). </summary>
		[Property] public float CheckInterval { get; set; } = 2.0f;

		/// <summary> Max conversation turns before auto-stopping. </summary>
		[Property] public int MaxTurns { get; set; } = 5;

		private float _checkTimer;
		private float _lastMessageTime;
		private int _turnCount;

		/// <summary>
		/// Start a conversation with another NPC. Posts an opening message
		/// to the SpatialBlackboard.
		/// </summary>
		public void StartConversation( string targetNpc, string openingText )
		{
			if ( !ConversationEnabled )
			{
				Log.Info( $"[NPCConversation:{NpcName}] Conversation mode is disabled." );
				return;
			}

			_turnCount = 0;
			_lastMessageTime = SpatialBlackboard.CurrentTime;

			SpatialBlackboard.PostMessage(
				from: NpcName,
				to: targetNpc,
				type: "conversation_start",
				content: openingText,
				position: GameObject.WorldPosition
			);

			Log.Info( $"[NPCConversation:{NpcName}] Started conversation with {targetNpc}: \"{openingText}\"" );
		}

		/// <summary>
		/// Post a reply in an ongoing conversation.
		/// </summary>
		public void Reply( string targetNpc, string text )
		{
			SpatialBlackboard.PostMessage(
				from: NpcName,
				to: targetNpc,
				type: "conversation_reply",
				content: text,
				position: GameObject.WorldPosition
			);

			Log.Info( $"[NPCConversation:{NpcName}] Replied to {targetNpc}: \"{text}\"" );
		}

		/// <summary>
		/// End the conversation and reset turn count.
		/// </summary>
		public void EndConversation( string targetNpc, string reason = "" )
		{
			SpatialBlackboard.PostMessage(
				from: NpcName,
				to: targetNpc,
				type: "conversation_end",
				content: reason
			);

			_turnCount = 0;
			Log.Info( $"[NPCConversation:{NpcName}] Ended conversation with {targetNpc}: {reason}" );
		}

		protected override void OnUpdate()
		{
			if ( !ConversationEnabled )
				return;

			_checkTimer += Time.Delta;
			if ( _checkTimer < CheckInterval )
				return;
			_checkTimer = 0;

			// Check for new messages addressed to this NPC
			var messages = SpatialBlackboard.GetMessages( NpcName, _lastMessageTime );

			foreach ( var msg in messages )
			{
				if ( msg.Type == "conversation_start" || msg.Type == "conversation_reply" )
				{
					_lastMessageTime = msg.Timestamp;
					HandleIncomingMessage( msg );
				}
				else if ( msg.Type == "conversation_end" )
				{
					_lastMessageTime = msg.Timestamp;
					_turnCount = 0;
					Log.Info( $"[NPCConversation:{NpcName}] {msg.From} ended conversation: {msg.Content}" );
				}
			}
		}

		/// <summary>
		/// Handle an incoming conversation message using the deterministic
		/// NLP pipeline (NlpParser → SocialRules → BlackboardProtocol).
		/// No LLM is used in this path.
		/// </summary>
		void HandleIncomingMessage( SpatialMessage msg )
		{
			_turnCount++;
			Log.Info( $"[NPCConversation:{NpcName}] Turn {_turnCount}/{MaxTurns} from {msg.From}: \"{msg.Content}\"" );

			if ( _turnCount > MaxTurns )
			{
				EndConversation( msg.From, "Max turns reached." );
				return;
			}

			// ── Deterministic NLP path (no LLM) ──
			// 1. Parse the incoming message into an Intent
			var incomingIntent = NlpParser.Parse( msg.Content, sender: msg.From, target: NpcName );
			Log.Info( $"[NPCConversation:{NpcName}] Parsed intent: {incomingIntent.Summary}" );

			// 2. Process blackboard operations (CLAIM/RELEASE)
			BlackboardProtocol.ProcessIntent( incomingIntent );

			// 3. Evaluate the intent through SocialRules to get a ResponseDecision
			var beliefs = new BeliefModel( NpcName );
			var decision = SocialRules.Evaluate( incomingIntent, beliefs );

			// 4. If the decision is to speak, generate and post a reply
			if ( decision.Action == DecisionAction.Speak && decision.ResponseIntent != null )
			{
				var replyText = SpeechGenerator.Generate( decision.ResponseIntent );
				Reply( msg.From, replyText );

				// Process the response intent's blackboard operations too
				BlackboardProtocol.ProcessIntent( decision.ResponseIntent );
			}
			else if ( decision.Action == DecisionAction.AskForClarification )
			{
				Reply( msg.From, "I didn't understand. Can you clarify?" );
			}
			else if ( decision.Action == DecisionAction.Act )
			{
				// The decision is to act rather than speak — the NPC will
				// take a physical action (move, build, etc.) through the
				// construction system, not through conversation.
				Log.Info( $"[NPCConversation:{NpcName}] Decision: Act ({decision.Reason})" );
			}
			// Ignore and Defer: no reply
		}

		/// <summary> Get conversation status for debugging. </summary>
		public string GetStatus()
		{
			return $"NPC={NpcName}, Enabled={ConversationEnabled}, Turns={_turnCount}/{MaxTurns}";
		}
	}
}
