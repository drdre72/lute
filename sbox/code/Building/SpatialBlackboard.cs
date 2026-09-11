using System;
using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// A spatial claim on a region of the world. NPCs register claims
	/// before building to avoid overlapping with each other's work.
	/// Claims have a radius and an expiry time — stale claims are
	/// automatically cleaned up.
	/// </summary>
	public struct SpatialClaim
	{
		/// <summary> Unique ID for this claim. </summary>
		public string Id;

		/// <summary> NPC that made the claim. </summary>
		public string Owner;

		/// <summary> Center of the claimed area (world position). </summary>
		public Vector3 Position;

		/// <summary> Radius of the claim in inches. </summary>
		public float Radius;

		/// <summary> What the NPC is doing here (e.g. "building", "walking", "gathering"). </summary>
		public string Activity;

		/// <summary> When the claim was made (game time seconds). </summary>
		public float Timestamp;

		/// <summary> How long the claim is valid (seconds). 0 = permanent. </summary>
		public float Expiry;
	}

	/// <summary>
	/// A spatial message posted to the blackboard. NPCs use these to
	/// share coordinate updates, warnings, and coordination signals
	/// without direct references to each other.
	///
	/// Messages have a monotonic <see cref="MessageId"/> so each NPC can
	/// track which messages it has already processed. This ensures
	/// exactly-once delivery — no duplicate processing.
	/// </summary>
	public struct SpatialMessage
	{
		/// <summary> Monotonic message ID (global sequence). Used for exactly-once consumption. </summary>
		public long MessageId;

		/// <summary> NPC that posted the message. </summary>
		public string From;

		/// <summary> Target NPC name, or empty for broadcast. </summary>
		public string To;

		/// <summary> Message type: "position", "warning", "request", "done", "help", "nlp_message", "nlp_reply". </summary>
		public string Type;

		/// <summary> Text content of the message. </summary>
		public string Content;

		/// <summary> Associated world position (if relevant). </summary>
		public Vector3? Position;

		/// <summary> When the message was posted (game time seconds). </summary>
		public float Timestamp;
	}

	/// <summary>
	/// Shared spatial blackboard for multi-NPC coordination. This is a
	/// singleton-style static store that all NPCs read from and write to.
	/// It provides:
	///
	/// - **Position tracking**: each NPC registers its current position
	///   every tick, so other NPCs know where everyone is.
	/// - **Spatial claims**: NPCs claim a radius around their build site
	///   before starting work. Other NPCs check for overlapping claims
	///   before building in the same area.
	/// - **Message passing**: NPCs post spatial messages (warnings,
	///   requests, done signals) that other NPCs can read.
	/// - **Stale data cleanup**: claims and messages expire after a
	///   configurable timeout.
	///
	/// This is the "shared world model" that lets multiple builders
	/// coordinate without direct references to each other — they just
	/// read and write to the blackboard.
	/// </summary>
	public static class SpatialBlackboard
	{
		private static readonly Dictionary<string, SpatialClaim> _claims = new();
		private static readonly Dictionary<string, Vector3> _positions = new();
		private static readonly List<SpatialMessage> _messages = new();
		private static float _lastCleanup;
		private const float CleanupInterval = 5.0f; // seconds
		private const float DefaultClaimExpiry = 300.0f; // 5 minutes
		private const float MessageExpiry = 60.0f; // 1 minute

		/// <summary> Monotonic message sequence counter. Never resets. </summary>
		private static long _messageSeq;

		/// <summary> Monotonic claim ID counter. Deterministic (no Guid). </summary>
		private static long _claimSeq;

		/// <summary> Current game time (updated by Update). </summary>
		public static float CurrentTime { get; private set; }

		/// <summary>
		/// Called every frame by any NPC. Updates the clock and cleans
		/// up stale claims and messages.
		/// </summary>
		public static void Update( float deltaTime )
		{
			CurrentTime += deltaTime;

			if ( CurrentTime - _lastCleanup > CleanupInterval )
			{
				CleanupStale();
				_lastCleanup = CurrentTime;
			}
		}

		// ── Position tracking ──

		/// <summary>
		/// Register an NPC's current position. Called every tick by
		/// each NPC so the blackboard always knows where everyone is.
		/// </summary>
		public static void UpdatePosition( string npcName, Vector3 position )
		{
			_positions[npcName] = position;
		}

		/// <summary> Get an NPC's last known position. </summary>
		public static Vector3? GetPosition( string npcName )
		{
			if ( _positions.TryGetValue( npcName, out var pos ) )
				return pos;
			return null;
		}

		/// <summary> Get all known NPC positions. </summary>
		public static Dictionary<string, Vector3> GetAllPositions()
		{
			return new Dictionary<string, Vector3>( _positions );
		}

		/// <summary> Get all NPC names currently tracked. </summary>
		public static List<string> GetActiveNPCs()
		{
			return _positions.Keys.ToList();
		}

		// ── Spatial claims ──

		/// <summary>
		/// Claim a circular area around a position. Returns true if the
		/// claim was successful, false if another NPC already has an
		/// overlapping claim.
		/// </summary>
		public static bool Claim( string npcName, Vector3 position, float radius, string activity = "building", float expiry = DefaultClaimExpiry )
		{
			// Check for overlapping claims from other NPCs
			foreach ( var kvp in _claims )
			{
				if ( kvp.Value.Owner == npcName )
					continue; // own claim — OK

				float dist = Vector3.DistanceBetween( position, kvp.Value.Position );
				if ( dist < radius + kvp.Value.Radius )
				{
					// Overlap detected
					return false;
				}
			}

			// No overlap — register the claim
			var claimId = $"{npcName}_{_claimSeq++}";
			_claims[claimId] = new SpatialClaim
			{
				Id = claimId,
				Owner = npcName,
				Position = position,
				Radius = radius,
				Activity = activity,
				Timestamp = CurrentTime,
				Expiry = expiry,
			};
			return true;
		}

		/// <summary> Release a specific claim by ID. </summary>
		public static void ReleaseClaim( string claimId )
		{
			_claims.Remove( claimId );
		}

		/// <summary> Release all claims owned by an NPC. </summary>
		public static void ReleaseAllClaims( string npcName )
		{
			var toRemove = _claims.Where( kvp => kvp.Value.Owner == npcName ).Select( kvp => kvp.Key ).ToList();
			foreach ( var id in toRemove )
				_claims.Remove( id );
		}

		/// <summary>
		/// Check if a position is clear of claims from other NPCs.
		/// Returns the blocking claim's owner, or null if clear.
		/// </summary>
		public static string CheckClear( Vector3 position, float radius, string excludeNpc = null )
		{
			foreach ( var kvp in _claims )
			{
				if ( excludeNpc != null && kvp.Value.Owner == excludeNpc )
					continue;

				float dist = Vector3.DistanceBetween( position, kvp.Value.Position );
				if ( dist < radius + kvp.Value.Radius )
					return kvp.Value.Owner;
			}
			return null;
		}

		/// <summary> Get all active claims. </summary>
		public static List<SpatialClaim> GetClaims()
		{
			return _claims.Values.ToList();
		}

		/// <summary> Get all claims owned by a specific NPC. </summary>
		public static List<SpatialClaim> GetClaimsByOwner( string npcName )
		{
			return _claims.Values.Where( c => c.Owner == npcName ).ToList();
		}

		// ── Message passing ──

		/// <summary>
		/// Post a spatial message to the blackboard. Other NPCs can
		/// read it via GetMessages or GetUnprocessedMessages.
		/// Each message gets a monotonic MessageId for exactly-once
		/// consumption.
		/// </summary>
		public static void PostMessage( string from, string to, string type, string content, Vector3? position = null )
		{
			_messages.Add( new SpatialMessage
			{
				MessageId = ++_messageSeq,
				From = from,
				To = to,
				Type = type,
				Content = content,
				Position = position,
				Timestamp = CurrentTime,
			} );

			// Cap message history
			if ( _messages.Count > 100 )
				_messages.RemoveAt( 0 );
		}

		/// <summary>
		/// Get messages addressed to a specific NPC (or broadcast) that
		/// have a MessageId greater than <paramref name="lastProcessedId"/>.
		/// This is the exactly-once consumption API — each NPC tracks
		/// the highest MessageId it has processed and passes it here.
		/// </summary>
		public static List<SpatialMessage> GetUnprocessedMessages( string npcName, long lastProcessedId )
		{
			return _messages
				.Where( m => m.MessageId > lastProcessedId &&
					   ( m.To == npcName || string.IsNullOrEmpty( m.To ) ) )
				.ToList();
		}

		/// <summary>
		/// Get messages addressed to a specific NPC (or broadcast).
		/// Returns messages newer than the given timestamp.
		/// </summary>
		public static List<SpatialMessage> GetMessages( string npcName, float sinceTimestamp = 0 )
		{
			return _messages
				.Where( m => m.Timestamp > sinceTimestamp &&
					   ( m.To == npcName || string.IsNullOrEmpty( m.To ) ) )
				.ToList();
		}

		/// <summary> Get all recent messages (for debugging). </summary>
		public static List<SpatialMessage> GetAllMessages( float sinceTimestamp = 0 )
		{
			return _messages.Where( m => m.Timestamp > sinceTimestamp ).ToList();
		}

		// ── Cleanup ──

		/// <summary>
		/// Remove expired claims and messages. Called automatically
		/// by Update on a timer.
		/// </summary>
		static void CleanupStale()
		{
			// Remove expired claims
			var expiredClaims = _claims
				.Where( kvp => kvp.Value.Expiry > 0 &&
					   CurrentTime - kvp.Value.Timestamp > kvp.Value.Expiry )
				.Select( kvp => kvp.Key )
				.ToList();

			foreach ( var id in expiredClaims )
				_claims.Remove( id );

			// Remove expired messages
			_messages.RemoveAll( m => CurrentTime - m.Timestamp > MessageExpiry );
		}

		/// <summary> Clear all data (for testing/reset). </summary>
		public static void Clear()
		{
			_claims.Clear();
			_positions.Clear();
			_messages.Clear();
			CurrentTime = 0;
			_lastCleanup = 0;
			_messageSeq = 0;
			_claimSeq = 0;
		}

		/// <summary> Get a summary of the blackboard state (for console/debug). </summary>
		public static string GetSummary()
		{
			int activeNPCs = _positions.Count;
			int activeClaims = _claims.Count;
			int recentMessages = _messages.Count;
			return $"NPCs: {activeNPCs}, Claims: {activeClaims}, Messages: {recentMessages}, Time: {CurrentTime:F1}s";
		}
	}
}
