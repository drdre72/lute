using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Deterministic goal→action selection for autonomous NPCs.
	///
	/// Each NPC has a set of goals and needs. Every tick, the evaluator
	/// picks the highest-priority action based on the NPC's current
	/// state. This is what makes NPCs stop standing around — they
	/// always have something to do.
	///
	/// Priority order (highest first):
	/// 1. Survival / immediate danger — flee, seek safety
	/// 2. Finish current committed task — complete what you started
	/// 3. Resolve blocked task — if your task is blocked, fix it
	/// 4. Acquire required resources — if you lack materials, get them
	/// 5. Help trusted colleague — if a trusted NPC needs help
	/// 6. Claim useful work — if idle, find a task
	/// 7. Social interaction — greet, share info (lowest priority)
	/// 8. Idle — wait, observe
	///
	/// The evaluator is fully deterministic: the same (goals, beliefs,
	/// world state) always produces the same action.
	/// </summary>
	public static class GoalActionEvaluator
	{
		/// <summary>
		/// Evaluate an NPC's goals and beliefs and produce the next
		/// action to take. Returns a <see cref="NpcAction"/> describing
		/// what the NPC should do this tick.
		/// </summary>
		public static NpcAction Evaluate( BeliefModel beliefs, NpcState state )
		{
			if ( beliefs == null || state == null )
				return NpcAction.Idle( "null input" );

			// 1. Survival / immediate danger
			if ( state.InDanger )
				return NpcAction.Act( "flee_to_safety", "in danger — fleeing" );

			// 2. Finish current committed task
			if ( !string.IsNullOrEmpty( state.CurrentTaskId ) && state.TaskProgress < 1.0f )
			{
				if ( state.TaskBlocked )
				{
					// 3. Resolve blocked task
					return ResolveBlockedTask( beliefs, state );
				}

				// Continue working on the task
				return NpcAction.Act( $"continue_task:{state.CurrentTaskId}",
					$"continuing task {state.CurrentTaskId} ({state.TaskProgress * 100:F0}% done)" );
			}

			// 4. Acquire required resources
			if ( state.MissingResources.Count > 0 )
			{
				var resource = state.MissingResources[0];
				// Can we ask a trusted NPC for it?
				var supplier = FindSupplier( beliefs, resource );
				if ( supplier != null )
				{
					return NpcAction.Speak(
						Intent.Simple( IntentType.Request, IntentTopic.Material, resource,
							beliefs.SelfName, supplier ),
						$"requesting {resource} from {supplier}" );
				}

				// No supplier — go gather it
				return NpcAction.Act( $"gather:{resource}",
					$"no supplier for {resource} — gathering" );
			}

			// 5. Help trusted colleague
			var helpTarget = FindHelpRequest( beliefs );
			if ( helpTarget != null )
			{
				return NpcAction.Speak(
					Intent.Simple( IntentType.Volunteer, IntentTopic.Task, helpTarget.TaskSubject,
						beliefs.SelfName, helpTarget.NpcName ),
					$"volunteering to help {helpTarget.NpcName} with {helpTarget.TaskSubject}" );
			}

			// 6. Claim useful work
			if ( string.IsNullOrEmpty( state.CurrentTaskId ) && beliefs.CurrentGoal == "build" )
			{
				// Look for an available site
				var site = FindAvailableSite( beliefs );
				if ( site != null )
				{
					return NpcAction.SpeakAndAct(
						Intent.Simple( IntentType.Claim, IntentTopic.Site, site,
							beliefs.SelfName, "" ), // broadcast
						$"claim_site:{site}",
						$"claiming available site {site}" );
				}

				// No sites — ask if anyone knows of one
				if ( state.TicksIdle > 10 && _npcs.Count > 1 )
				{
					var other = _npcs.Keys.FirstOrDefault( k => k != beliefs.SelfName );
					if ( other != null )
					{
						return NpcAction.Speak(
							Intent.Simple( IntentType.Ask, IntentTopic.Site, "any_site",
								beliefs.SelfName, other ),
							$"asking {other} about available sites" );
					}
				}
			}

			// 7. Social interaction (very low priority — only if idle for a while)
			if ( state.TicksIdle > 30 && _npcs.Count > 1 )
			{
				var other = _npcs.Keys.FirstOrDefault( k => k != beliefs.SelfName );
				if ( other != null && beliefs.GetTrust( other ) >= 0 )
				{
					// Only greet if we haven't recently
					var recent = beliefs.GetRecentWith( other, 3 );
					bool recentGreet = recent.Any( m => m.IntentType == IntentType.Greet );
					if ( !recentGreet )
					{
						return NpcAction.Speak(
							Intent.Simple( IntentType.Greet, IntentTopic.None, "",
								beliefs.SelfName, other ),
							$"greeting {other} after idle period" );
					}
				}
			}

			// 8. Idle
			return NpcAction.Idle( "no goals to pursue" );
		}

		/// <summary>
		/// Resolve a blocked task. Either ask for help or abandon it.
		/// </summary>
		static NpcAction ResolveBlockedTask( BeliefModel beliefs, NpcState state )
		{
			// If blocked by a resource shortage, request it
			if ( state.MissingResources.Count > 0 )
			{
				var resource = state.MissingResources[0];
				var supplier = FindSupplier( beliefs, resource );
				if ( supplier != null )
				{
					return NpcAction.Speak(
						Intent.Simple( IntentType.Request, IntentTopic.Material, resource,
							beliefs.SelfName, supplier ),
						$"blocked — requesting {resource} from {supplier}" );
				}
			}

			// If blocked by a site conflict, ask the claimer to release
			if ( !string.IsNullOrEmpty( state.BlockingNpc ) )
			{
				return NpcAction.Speak(
					Intent.Simple( IntentType.Request, IntentTopic.Site, state.BlockingSite,
						beliefs.SelfName, state.BlockingNpc ),
					$"blocked by {state.BlockingNpc} — asking for {state.BlockingSite}" );
			}

			// Can't resolve — abandon the task
			return NpcAction.Act( $"abandon_task:{state.CurrentTaskId}",
				"cannot resolve block — abandoning task" );
		}

		/// <summary>
		/// Find a trusted NPC who might have the given resource.
		/// </summary>
		static string FindSupplier( BeliefModel beliefs, string resource )
		{
			// Look for an NPC with a different role (e.g. carpenter has wood)
			foreach ( var kvp in beliefs.Npcs )
			{
				if ( kvp.Key == beliefs.SelfName )
					continue;
				if ( kvp.Value.Trust < 10 )
					continue;

				// Carpenters supply wood/timber, masons supply stone
				var role = kvp.Value.Role.ToLowerInvariant();
				if ( resource.Contains( "wood" ) || resource.Contains( "timber" ) || resource.Contains( "plank" ) )
				{
					if ( role.Contains( "carpenter" ) || role.Contains( "wood" ) )
						return kvp.Key;
				}
				if ( resource.Contains( "stone" ) || resource.Contains( "brick" ) )
				{
					if ( role.Contains( "mason" ) || role.Contains( "stone" ) )
						return kvp.Key;
				}

				// Generic: any trusted NPC might have spare materials
				if ( role != "builder" )
					return kvp.Key;
			}
			return null;
		}

		/// <summary>
		/// Find a trusted colleague who needs help (has a pending request
		/// or blocked task that we could assist with).
		/// </summary>
		static HelpRequest FindHelpRequest( BeliefModel beliefs )
		{
			// Check memory for recent requests from trusted NPCs
			foreach ( var mem in beliefs.Memory.AsEnumerable().Reverse() )
			{
				if ( mem.IntentType == IntentType.Request && mem.Timestamp > SpatialBlackboard.CurrentTime - 30f )
				{
					var trust = beliefs.GetTrust( mem.OtherNpc );
					if ( trust >= 10 )
					{
						return new HelpRequest
						{
							NpcName = mem.OtherNpc,
							TaskSubject = mem.Subject,
						};
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Find an available site in our beliefs.
		/// </summary>
		static string FindAvailableSite( BeliefModel beliefs )
		{
			foreach ( var kvp in beliefs.Resources )
			{
				if ( kvp.Value.IsAvailable && kvp.Key.Contains( "site" ) )
					return kvp.Key;
			}

			// No known available sites — return a default
			return null;
		}

		// Reference to registered NPCs (set by ConversationManager)
		static Dictionary<string, object> _npcs = new();

		/// <summary>
		/// Set the list of registered NPC names (for finding other NPCs
		/// to interact with). Called by ConversationManager.
		/// </summary>
		public static void SetNpcNames( List<string> names )
		{
			_npcs.Clear();
			foreach ( var name in names )
				_npcs[name] = null;
		}

		sealed class HelpRequest
		{
			public string NpcName { get; set; }
			public string TaskSubject { get; set; }
		}
	}

	/// <summary>
	/// Runtime state of an NPC — what it's currently doing, what it
	/// needs, what's blocking it. Updated by the NPC's controller each
	/// tick, read by the <see cref="GoalActionEvaluator"/>.
	/// </summary>
	public sealed class NpcState
	{
		/// <summary> Current task ID (or "" if idle). </summary>
		public string CurrentTaskId { get; set; } = "";

		/// <summary> Task progress (0.0 to 1.0). </summary>
		public float TaskProgress { get; set; }

		/// <summary> Is the current task blocked? </summary>
		public bool TaskBlocked { get; set; }

		/// <summary> NPC blocking our task (if any). </summary>
		public string BlockingNpc { get; set; } = "";

		/// <summary> Site that's blocked (if any). </summary>
		public string BlockingSite { get; set; } = "";

		/// <summary> Resources we need but don't have. </summary>
		public List<string> MissingResources { get; set; } = new();

		/// <summary> Are we in immediate danger? </summary>
		public bool InDanger { get; set; }

		/// <summary> How many ticks we've been idle. </summary>
		public int TicksIdle { get; set; }

		/// <summary> Current physical position. </summary>
		public Vector3 Position { get; set; }
	}

	/// <summary>
	/// An action an NPC should take. Produced by
	/// <see cref="GoalActionEvaluator"/>.
	/// </summary>
	public sealed class NpcAction
	{
		/// <summary> What kind of action. </summary>
		public NpcActionType Type { get; set; } = NpcActionType.Idle;

		/// <summary> If speaking, the intent to express. </summary>
		public Intent SpeakIntent { get; set; }

		/// <summary> If acting, the world action description. </summary>
		public string WorldAction { get; set; } = "";

		/// <summary> Why this action was chosen (for debugging). </summary>
		public string Reason { get; set; } = "";

		/// <summary> Short summary. </summary>
		public string Summary =>
			$"{Type}" +
			(SpeakIntent != null ? $" ({SpeakIntent.Type})" : "") +
			(!string.IsNullOrEmpty( WorldAction ) ? $" [{WorldAction}]" : "") +
			(!string.IsNullOrEmpty( Reason ) ? $" — {Reason}" : "");

		public static NpcAction Idle( string reason ) => new() { Type = NpcActionType.Idle, Reason = reason };
		public static NpcAction Act( string action, string reason ) => new() { Type = NpcActionType.Act, WorldAction = action, Reason = reason };
		public static NpcAction Speak( Intent intent, string reason ) => new() { Type = NpcActionType.Speak, SpeakIntent = intent, Reason = reason };
		public static NpcAction SpeakAndAct( Intent intent, string action, string reason ) => new() { Type = NpcActionType.SpeakAndAct, SpeakIntent = intent, WorldAction = action, Reason = reason };
	}

	/// <summary> What kind of action an NPC takes. </summary>
	public enum NpcActionType
	{
		Idle,
		Speak,
		Act,
		SpeakAndAct,
	}
}
