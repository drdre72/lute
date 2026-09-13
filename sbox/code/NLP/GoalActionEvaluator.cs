using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Building;
using Lute.Items;

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
		///
		/// Per professor's Gate 0 hardening notes: this previously
		/// selected suppliers using role-name strings ("carpenter" →
		/// wood) plus trust. That was unreliable and bypassed the
		/// authoritative resource system. The new logic:
		/// <list type="number">
		/// <item>Parse the resource string into an ItemType.</item>
		/// <item>Query <see cref="ResourceRegistry"/> for a real
		/// stockpile with stock — if one exists, no NPC needs to be
		/// asked; the hauler loop (Gate 2d) will move it.</item>
		/// <item>If no stockpile has it, find an NPC with the relevant
		/// gather capability (via <see cref="ConstructionDirector.HasCapability"/>),
		/// not role-name string matching.</item>
		/// <item>Trust is still considered as a tiebreaker among
		/// capable NPCs, but in cooperative mode (construction phase)
		/// it does not gate selection.</item>
		/// </list>
		/// </summary>
		static string FindSupplier( BeliefModel beliefs, string resource )
		{
			// 1. Parse the resource string into an ItemType. The resource
			// string comes from NpcState.MissingResources, which today is
			// a free-form string. Try a case-insensitive enum parse; if it
			// fails, fall back to keyword matching.
			ItemType type = ResolveItemType( resource );

			// 2. Check the authoritative ResourceRegistry for real stock.
			// If a stockpile has the material, the hauler loop will move
			// it — no NPC needs to be asked. Return null so the caller
			// falls through to "go gather it" or waits for a haul job.
			if ( type != ItemType.Clay ) // Clay is the "none" sentinel
			{
				var pile = ResourceRegistry.NearestStockpileWithResource(
					type, Vector3.Zero, needed: 1, forNpc: beliefs.SelfName );
				if ( pile != null )
				{
					// Stock exists — don't ask an NPC; the logistics
					// board should create a haul job. Return null to
					// signal "no NPC supplier needed."
					return null;
				}
			}

			// 3. No stock — find an NPC with the relevant gather
			// capability. This replaces the old role-string matching.
			NpcCapability neededCap = CapabilityForResource( type, resource );
			foreach ( var kvp in beliefs.Npcs )
			{
				if ( kvp.Key == beliefs.SelfName ) continue;

				// In cooperative mode, trust is a tiebreaker, not a gate.
				// Keep a small floor so a deeply distrusted NPC (-50) is
				// still skipped even in cooperative mode.
				if ( kvp.Value.Trust < -50 ) continue;

				// Check capability via the authoritative director registry.
				if ( neededCap != NpcCapability.None &&
					ConstructionDirector.HasCapability( kvp.Key, neededCap ) )
				{
					return kvp.Key;
				}
			}

			// 4. Fallback: any trusted-enough NPC (cooperative mode
			// allows trust >= -50). Prefer ones with a non-default role.
			foreach ( var kvp in beliefs.Npcs )
			{
				if ( kvp.Key == beliefs.SelfName ) continue;
				if ( kvp.Value.Trust < -50 ) continue;
				if ( !string.IsNullOrEmpty( kvp.Value.Role ) &&
					kvp.Value.Role.ToLowerInvariant() != "builder" )
				{
					return kvp.Key;
				}
			}
			return null;
		}

		/// <summary>
		/// Resolve a free-form resource string into an ItemType. Returns
		/// ItemType.Clay (the "none"/default sentinel) if unrecognized.
		/// </summary>
		static ItemType ResolveItemType( string resource )
		{
			if ( string.IsNullOrEmpty( resource ) ) return ItemType.Clay;
			// Direct enum parse (e.g. "Brick", "Plank", "Wood")
			if ( Enum.TryParse<ItemType>( resource, true, out var t ) )
				return t;
			// Keyword fallback for common aliases
			var lower = resource.ToLowerInvariant();
			if ( lower.Contains( "wood" ) || lower.Contains( "timber" ) || lower.Contains( "plank" ) )
				return ItemType.Wood;
			if ( lower.Contains( "stone" ) || lower.Contains( "brick" ) )
				return ItemType.Stone;
			if ( lower.Contains( "ore" ) || lower.Contains( "iron" ) || lower.Contains( "ingot" ) )
				return ItemType.Ore;
			return ItemType.Clay;
		}

		/// <summary>
		/// Map an ItemType (or resource keyword) to the capability
		/// needed to gather/produce it. Returns NpcCapability.None if
		/// no specific capability applies (e.g. processed materials that
		/// come from workstations, not gatherers).
		/// </summary>
		static NpcCapability CapabilityForResource( ItemType type, string resource )
		{
			// Raw materials → gather capability
			if ( type == ItemType.Wood ) return NpcCapability.GatherWood;
			if ( type == ItemType.Stone ) return NpcCapability.GatherStone;
			if ( type == ItemType.Ore ) return NpcCapability.GatherOre;
			if ( type == ItemType.Clay || type == ItemType.Straw ) return NpcCapability.GatherClay;

			// Processed materials come from workstations, not gatherers.
			// The hauler should pull these from a stockpile, not ask an
			// NPC to produce them on demand. Return None so the caller
			// falls back to the stockpile query / haul job path.
			var lower = (resource ?? "").ToLowerInvariant();
			if ( lower.Contains( "wood" ) ) return NpcCapability.GatherWood;
			if ( lower.Contains( "stone" ) || lower.Contains( "brick" ) ) return NpcCapability.GatherStone;
			if ( lower.Contains( "ore" ) || lower.Contains( "iron" ) || lower.Contains( "ingot" ) ) return NpcCapability.GatherOre;
			return NpcCapability.None;
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
