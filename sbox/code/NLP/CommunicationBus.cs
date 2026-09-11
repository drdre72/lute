using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Dedicated communication bus for NPC-to-NPC messaging. Separate
	/// from <see cref="Lute.Building.SpatialBlackboard"/> which now only
	/// handles spatial concerns (positions, claims, reservations).
	///
	/// This separation (per professor feedback) keeps semantics clean:
	///
	///   WorldBlackboard (SpatialBlackboard)
	///     ├── positions
	///     ├── resources
	///     ├── claims
	///     └── spatial occupancy
	///
	///   CommunicationBus (this class)
	///     ├── messages
	///     ├── delivery
	///     ├── sequence IDs
	///     └── acknowledgements
	///
	/// Messages have monotonic MessageIds for exactly-once consumption.
	/// Each NPC tracks LastProcessedMessageId and receives only newer
	/// messages. No replay, no duplicates.
	///
	/// The bus is fully deterministic — no randomness, no cloud, no LLM.
	/// </summary>
	public static class CommunicationBus
	{
		static readonly List<ComMessage> _messages = new();
		static long _messageSeq;

		/// <summary> Max messages to retain (ring buffer). </summary>
		const int MaxMessages = 500;

		/// <summary>
		/// Post a message to the communication bus. Returns the assigned
		/// MessageId (monotonic sequence).
		/// </summary>
		public static long Post( string from, string to, string type, string content )
		{
			if ( string.IsNullOrWhiteSpace( content ) )
				return -1;

			var id = ++_messageSeq;
			_messages.Add( new ComMessage
			{
				MessageId = id,
				From = from,
				To = to,
				Type = type,
				Content = content,
				Timestamp = SpatialBlackboard.CurrentTime,
			} );

			// Ring buffer — drop oldest if over capacity
			while ( _messages.Count > MaxMessages )
				_messages.RemoveAt( 0 );

			return id;
		}

		/// <summary>
		/// Broadcast a message to all NPCs (To = "" means broadcast).
		/// </summary>
		public static long Broadcast( string from, string type, string content )
		{
			return Post( from, "", type, content );
		}

		/// <summary>
		/// Get unprocessed messages for a specific NPC. Returns messages
		/// with MessageId > lastProcessedId that are either addressed to
		/// this NPC or broadcast.
		///
		/// This is the exactly-once consumption API.
		/// </summary>
		public static List<ComMessage> GetUnprocessed( string npcName, long lastProcessedId )
		{
			return _messages
				.Where( m => m.MessageId > lastProcessedId &&
					   ( m.To == npcName || string.IsNullOrEmpty( m.To ) ) )
				.ToList();
		}

		/// <summary> Get all messages (for debugging). </summary>
		public static List<ComMessage> GetAll()
		{
			return _messages.ToList();
		}

		/// <summary> Get the latest message ID (for debugging). </summary>
		public static long LatestMessageId => _messageSeq;

		/// <summary> Total messages posted. </summary>
		public static int MessageCount => _messages.Count;

		/// <summary> Clear all messages (for testing/reset). </summary>
		public static void Clear()
		{
			_messages.Clear();
			_messageSeq = 0;
		}

		/// <summary> Debug summary. </summary>
		public static string Summary()
		{
			return $"[CommunicationBus] {_messages.Count} messages, latest ID={_messageSeq}";
		}
	}

	/// <summary>
	/// A message on the <see cref="CommunicationBus"/>. Has a monotonic
	/// MessageId for exactly-once consumption.
	/// </summary>
	public struct ComMessage
	{
		/// <summary> Monotonic message ID (global sequence). </summary>
		public long MessageId;

		/// <summary> NPC that posted the message. </summary>
		public string From;

		/// <summary> Target NPC name, or empty for broadcast. </summary>
		public string To;

		/// <summary> Message type: "nlp_message", "nlp_reply", "conversation_start". </summary>
		public string Type;

		/// <summary> Text content of the message. </summary>
		public string Content;

		/// <summary> When the message was posted (game time seconds). </summary>
		public float Timestamp;
	}
}
