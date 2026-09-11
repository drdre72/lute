using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Conversation mode for NPC-to-NPC text exchange via the NLP brain.
	/// This is intentionally latent — it exists as infrastructure but doesn't
	/// drive gameplay until a use case emerges (e.g. NPCs negotiating build
	/// sites, coordinating tasks, or generating quest dialogue).
	///
	/// Conversation flow:
	/// 1. NPC A posts a message to the SpatialBlackboard addressed to NPC B
	/// 2. NPC B's NPCConversation picks it up on the next tick
	/// 3. NPC B's NPCBrain processes it through the LLM
	/// 4. The LLM response is posted back to the blackboard for NPC A
	/// 5. Both sides can apply critiques to their own task queues
	///
	/// This mirrors the "Partner NPCs (Multi-Agent Sync)" critique source
	/// from the design notes — NPCs coordinate by exchanging text messages
	/// that the LLM translates into structured BuildingCritique payloads.
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
			if ( !ConversationEnabled || Brain == null )
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
					_ = HandleIncomingMessage( msg );
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
		/// Handle an incoming conversation message. Sends it through the
		/// NPCBrain's LLM to generate a response, then posts the reply.
		/// </summary>
		async Task HandleIncomingMessage( SpatialMessage msg )
		{
			_turnCount++;
			Log.Info( $"[NPCConversation:{NpcName}] Turn {_turnCount}/{MaxTurns} from {msg.From}: \"{msg.Content}\"" );

			if ( _turnCount > MaxTurns )
			{
				EndConversation( msg.From, "Max turns reached." );
				return;
			}

			// Process through the LLM brain — the LLM generates both a
			// conversational reply and optionally a BuildingCritique
			if ( Brain != null )
			{
				// The critique path: LLM may modify our task queue based on
				// what the other NPC said (e.g. "move your wall 200 units east")
				await Brain.SubmitCritique( msg.From, msg.Content );

				// Generate a conversational reply
				// For now, post a simple acknowledgment. The full LLM reply
				// path would use a separate prompt that asks for a natural
				// language response rather than a BuildingCritique.
				Reply( msg.From, $"Acknowledged: {msg.Content}" );
			}
		}

		/// <summary> Get conversation status for debugging. </summary>
		public string GetStatus()
		{
			return $"NPC={NpcName}, Enabled={ConversationEnabled}, Turns={_turnCount}/{MaxTurns}";
		}
	}
}
