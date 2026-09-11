using System;
using System.Collections.Generic;
using System.Linq;

namespace Lute.Building
{
	/// <summary>
	/// Construction event types. These form the event vocabulary that
	/// the NPC communication system subscribes to — conversation becomes
	/// a consequence of simulation events, not the source of truth.
	///
	/// Per professor Phase 3:
	///   TaskCreated, TaskAssigned, TaskClaimed, TaskStarted, TaskBlocked,
	///   TaskCompleted, TaskFailed,
	///   ReservationCreated, ReservationReleased, ReservationConflict,
	///   ResourceRequested, ResourceOffered, ResourceTransferred,
	///   NpcRequestedHelp, NpcOfferedHelp, NpcAcceptedHelp, NpcDeclinedHelp
	/// </summary>
	public enum ConstructionEventType
	{
		// Task lifecycle
		TaskCreated,
		TaskAssigned,
		TaskClaimed,
		TaskStarted,
		TaskBlocked,
		TaskCompleted,
		TaskFailed,
		TaskCancelled,

		// Reservations
		ReservationCreated,
		ReservationReleased,
		ReservationConflict,

		// Resources
		ResourceRequested,
		ResourceOffered,
		ResourceTransferred,

		// NPC coordination
		NpcRequestedHelp,
		NpcOfferedHelp,
		NpcAcceptedHelp,
		NpcDeclinedHelp,
	}

	/// <summary>
	/// A construction event. Events are the notification mechanism
	/// between the ConstructionDirector and the NPC communication system.
	/// NPCs subscribe to events and can generate conversation (intents)
	/// in response to them.
	/// </summary>
	public sealed class ConstructionEvent
	{
		public ConstructionEventType Type { get; init; }

		/// <summary> The task id (if relevant). </summary>
		public string TaskId { get; init; }

		/// <summary> The NPC that triggered the event. </summary>
		public string Actor { get; init; }

		/// <summary> The target NPC (if relevant). </summary>
		public string Target { get; init; }

		/// <summary> Event-specific parameters. </summary>
		public Dictionary<string, string> Parameters { get; init; } = new();

		/// <summary> When the event was fired (game time). </summary>
		public float Timestamp { get; init; }

		/// <summary> Monotonic event sequence. </summary>
		public long EventId { get; init; }

		/// <summary> Short debug summary. </summary>
		public string Summary =>
			$"[{EventId}] {Type} task={TaskId} actor={Actor}" +
			( Target != null ? $" target={Target}" : "" ) +
			( Parameters.Count > 0
				? $" [{string.Join( ",", Parameters.Select( kvp => $"{kvp.Key}={kvp.Value}" ) )}]"
				: "" );
	}

	/// <summary>
	/// Event handler delegate for construction events.
	/// </summary>
	public delegate void ConstructionEventHandler( ConstructionEvent evt );

	/// <summary>
	/// Central event bus for construction events. The
	/// ConstructionDirector fires events here, and subscribers (NPC
	/// communication system, debug loggers, etc.) receive them.
	///
	/// This is the mechanism that makes conversation a consequence of
	/// simulation:
	///
	///   Simulation problem
	///         ↓
	///   ConstructionEvent fired
	///         ↓
	///   NPC communication system receives event
	///         ↓
	///   Communication intent generated
	///         ↓
	///   Conversation
	///         ↓
	///   Decision
	///         ↓
	///   Simulation change
	///
	/// The event bus is a simple pub/sub — subscribers register handlers
	/// and receive all events. Events are also kept in a ring buffer for
	/// late subscribers to catch up.
	/// </summary>
	public static class ConstructionEventBus
	{
		static readonly List<ConstructionEventHandler> _handlers = new();
		static readonly List<ConstructionEvent> _history = new();
		static long _eventSeq;
		const int MaxHistory = 200;

		/// <summary> Current game time (set by Update). </summary>
		public static float CurrentTime { get; private set; }

		/// <summary> Update the clock. Called by the scene each tick. </summary>
		public static void Update( float deltaTime )
		{
			CurrentTime += deltaTime;
		}

		/// <summary>
		/// Subscribe to construction events. The handler will be called
		/// for every event fired after subscription.
		/// </summary>
		public static void Subscribe( ConstructionEventHandler handler )
		{
			if ( handler != null )
				_handlers.Add( handler );
		}

		/// <summary> Unsubscribe from events. </summary>
		public static void Unsubscribe( ConstructionEventHandler handler )
		{
			_handlers.Remove( handler );
		}

		/// <summary>
		/// Fire a construction event. All subscribers are notified
		/// immediately. The event is also stored in history for late
		/// subscribers.
		/// </summary>
		public static void Fire( ConstructionEventType type,
			string taskId = null, string actor = null, string target = null,
			Dictionary<string, string> parameters = null )
		{
			var evt = new ConstructionEvent
			{
				Type = type,
				TaskId = taskId,
				Actor = actor,
				Target = target,
				Parameters = parameters ?? new(),
				Timestamp = CurrentTime,
				EventId = ++_eventSeq,
			};

			_history.Add( evt );
			if ( _history.Count > MaxHistory )
				_history.RemoveAt( 0 );

			// Notify all subscribers
			foreach ( var handler in _handlers.ToList() )
			{
				try
				{
					handler( evt );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"Lute: ConstructionEventBus handler threw: {ex.Message}" );
				}
			}
		}

		/// <summary> Get recent events (for debugging/late subscribers). </summary>
		public static List<ConstructionEvent> GetHistory( int count = 50 )
		{
			return _history.TakeLast( count ).ToList();
		}

		/// <summary> Get events since a given event id. </summary>
		public static List<ConstructionEvent> GetEventsSince( long lastEventId )
		{
			return _history.Where( e => e.EventId > lastEventId ).ToList();
		}

		/// <summary> Clear all state (test/reset). </summary>
		public static void Clear()
		{
			_handlers.Clear();
			_history.Clear();
			_eventSeq = 0;
			CurrentTime = 0;
		}

		/// <summary> Debug summary. </summary>
		public static string GetSummary()
		{
			return $"EventBus: {_history.Count} events, {_handlers.Count} subscribers, seq={_eventSeq}";
		}
	}
}
