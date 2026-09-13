using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Machine-readable reason an NPC builder is idle.
	/// Every idle NPC has exactly one of these reasons, so the
	/// ConstructionDirector can make informed reassignment decisions
	/// and explain why an NPC cannot proceed.
	/// </summary>
	public enum BuilderIdleReason
	{
		/// <summary> No idle reason assigned yet (initial state). </summary>
		None,
		/// <summary> No tasks available in the task list. </summary>
		IdleNoTask,
		/// <summary> Waiting for a dependency task to complete first. </summary>
		WaitingDependency,
		/// <summary> Waiting for a spatial reservation to be released. </summary>
		WaitingReservation,
		/// <summary> Path to the target site is blocked. </summary>
		PathBlocked,
		/// <summary> Required construction materials unavailable. </summary>
		MaterialUnavailable,
		/// <summary> Placement conflict with existing structure (self-repair failed). </summary>
		ConstructionConflict,
		/// <summary> Yielding to another NPC that has priority on this task. </summary>
		YieldingToNpc,
		/// <summary> Village is complete — no more work. </summary>
		VillageComplete,
	}

	/// <summary>
	/// Tracks an NPC builder's idle state with a machine-readable reason
	/// and a duration. The ConstructionDirector queries this to detect
	/// stalled builders and make reassignment decisions.
	///
	/// If an NPC sits in one state beyond a threshold, the director should
	/// either reassign it or explain exactly why it cannot proceed.
	/// </summary>
	public sealed class BuilderLivenessState
	{
		/// <summary> The NPC's identity (e.g. "VillageBuilderNPC_0"). </summary>
		public string NpcId { get; init; }

		/// <summary> Current idle reason (None if not idle). </summary>
		public BuilderIdleReason IdleReason { get; private set; } = BuilderIdleReason.None;

		/// <summary> How long the NPC has been in the current idle state (seconds). </summary>
		public float IdleDuration { get; private set; }

		/// <summary> The task ID the NPC is waiting on (if applicable). </summary>
		public string WaitingOnTaskId { get; private set; }

		/// <summary> Human-readable explanation for diagnostics. </summary>
		public string Explanation { get; private set; }

		/// <summary> Time threshold (seconds) before a stalled NPC should be reassigned. </summary>
		public const float StallThreshold = 30f;

		/// <summary> True if the NPC has been idle beyond the stall threshold. </summary>
		public bool IsStalled => IdleReason != BuilderIdleReason.None
			&& IdleReason != BuilderIdleReason.VillageComplete
			&& IdleDuration >= StallThreshold;

		public BuilderLivenessState( string npcId )
		{
			NpcId = npcId;
		}

		/// <summary>
		/// Set the idle reason. Resets the duration if the reason changes.
		/// </summary>
		public void SetIdle( BuilderIdleReason reason, string explanation = null, string waitingOnTaskId = null )
		{
			if ( IdleReason != reason )
			{
				IdleReason = reason;
				IdleDuration = 0f;
			}
			WaitingOnTaskId = waitingOnTaskId;
			Explanation = explanation ?? reason.ToString();
		}

		/// <summary>
		/// Clear the idle state (NPC is now active).
		/// </summary>
		public void ClearIdle()
		{
			IdleReason = BuilderIdleReason.None;
			IdleDuration = 0f;
			WaitingOnTaskId = null;
			Explanation = null;
		}

		/// <summary>
		/// Advance the idle timer. Called every frame while idle.
		/// </summary>
		public void Tick( float deltaTime )
		{
			if ( IdleReason != BuilderIdleReason.None )
				IdleDuration += deltaTime;
		}

		public override string ToString()
		{
			if ( IdleReason == BuilderIdleReason.None )
				return $"{NpcId}: active";

			var waiting = WaitingOnTaskId != null ? $" (waiting on {WaitingOnTaskId})" : "";
			var stalled = IsStalled ? " [STALLED]" : "";
			return $"{NpcId}: {IdleReason}{waiting} for {IdleDuration:F1}s{stalled} — {Explanation}";
		}
	}

	/// <summary>
	/// Central registry for all NPC builder liveness states.
	/// The ConstructionDirector queries this to detect stalled builders
	/// and make reassignment decisions.
	/// </summary>
	public static class BuilderLivenessRegistry
	{
		static readonly Dictionary<string, BuilderLivenessState> _states = new();

		/// <summary>
		/// Register a builder NPC. Called once at startup.
		/// </summary>
		public static void Register( string npcId )
		{
			if ( !_states.ContainsKey( npcId ) )
				_states[npcId] = new BuilderLivenessState( npcId );
		}

		/// <summary>
		/// Get the liveness state for a builder NPC.
		/// Returns null if not registered.
		/// </summary>
		public static BuilderLivenessState Get( string npcId )
		{
			return _states.TryGetValue( npcId, out var state ) ? state : null;
		}

		/// <summary>
		/// Set the idle reason for a builder NPC.
		/// </summary>
		public static void SetIdle( string npcId, BuilderIdleReason reason, string explanation = null, string waitingOnTaskId = null )
		{
			if ( !_states.TryGetValue( npcId, out var state ) )
			{
				state = new BuilderLivenessState( npcId );
				_states[npcId] = state;
			}
			state.SetIdle( reason, explanation, waitingOnTaskId );
		}

		/// <summary>
		/// Clear the idle state for a builder NPC (it's now active).
		/// </summary>
		public static void ClearIdle( string npcId )
		{
			if ( _states.TryGetValue( npcId, out var state ) )
				state.ClearIdle();
		}

		/// <summary>
		/// Advance all liveness timers. Called once per frame from a
		/// central update point.
		/// </summary>
		public static void TickAll( float deltaTime )
		{
			foreach ( var state in _states.Values )
				state.Tick( deltaTime );
		}

		/// <summary>
		/// Get all stalled builders (idle beyond threshold).
		/// </summary>
		public static List<BuilderLivenessState> GetStalledBuilders()
		{
			var result = new List<BuilderLivenessState>();
			foreach ( var state in _states.Values )
				if ( state.IsStalled ) result.Add( state );
			return result;
		}

		/// <summary>
		/// Get a summary of all builder states for diagnostics.
		/// </summary>
		public static string GetSummary()
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine( $"Builder Liveness — {_states.Count} builders:" );
			foreach ( var state in _states.Values )
				sb.AppendLine( $"  {state}" );
			return sb.ToString();
		}

		/// <summary>
		/// Reset all liveness states (e.g. on scene reload).
		/// </summary>
		public static void Reset()
		{
			_states.Clear();
		}

		/// <summary>
		/// Console command: dump all builder liveness states.
		/// </summary>
		[ConCmd( "builder_liveness" )]
		public static void LivenessCommand()
		{
			Log.Info( $"Lute: {GetSummary()}" );
			var stalled = GetStalledBuilders();
			if ( stalled.Count > 0 )
			{
				Log.Warning( $"Lute: {stalled.Count} stalled builders detected:" );
				foreach ( var s in stalled )
					Log.Warning( $"  {s}" );
			}
		}
	}
}
