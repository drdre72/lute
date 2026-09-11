using System;

namespace Lute.NLP
{
	/// <summary>
	/// The simulation mode controls which NPC behaviors are available.
	///
	/// Construction mode (default):
	///   - Cooperation
	///   - Task negotiation
	///   - Resource sharing
	///   - Blackboard coordination
	///   - Truthful reporting
	///   - Help requests
	///   - Conflict resolution
	///   - Memory
	///   - Reputation
	///   - NO lying, manipulation, concealment, sabotage, or false claims
	///
	/// Gameplay mode (future):
	///   - Deception
	///   - Persuasion
	///   - Manipulation
	///   - Secret information
	///   - Faction politics
	///   - Betrayal
	///   - Espionage
	///
	/// The same communication protocol survives across both modes.
	/// </summary>
	public enum NpcSimulationMode
	{
		/// <summary>
		/// Construction phase — NPCs are cooperative, truthful agents.
		/// Deception is disabled. This is the default.
		/// </summary>
		Construction,

		/// <summary>
		/// Gameplay phase — social simulation with deception, factions,
		/// and politics enabled. NOT YET IMPLEMENTED.
		/// </summary>
		Gameplay,
	}

	/// <summary>
	/// Global simulation context. Controls which NPC behaviors are
	/// available. In Construction mode, deception and social manipulation
	/// are disabled. In Gameplay mode, the full social system is available.
	///
	/// This is the feature gate that prevents accidental activation of
	/// deception during the construction phase.
	/// </summary>
	public static class NpcSimulation
	{
		/// <summary>
		/// Current simulation mode. Defaults to Construction.
		/// </summary>
		public static NpcSimulationMode Mode { get; set; } = NpcSimulationMode.Construction;

		/// <summary>
		/// Is deception currently enabled? Only true in Gameplay mode.
		/// </summary>
		public static bool DeceptionEnabled => Mode == NpcSimulationMode.Gameplay;

		/// <summary>
		/// Check if a specific behavior is allowed in the current mode.
		/// </summary>
		public static bool IsAllowed( NpcBehavior behavior )
		{
			return behavior switch
			{
				// Cooperative behaviors — always allowed
				NpcBehavior.Cooperate => true,
				NpcBehavior.Negotiate => true,
				NpcBehavior.ShareResource => true,
				NpcBehavior.RequestHelp => true,
				NpcBehavior.OfferHelp => true,
				NpcBehavior.ReportTruthfully => true,
				NpcBehavior.ResolveConflict => true,
				NpcBehavior.Remember => true,
				NpcBehavior.TrackReputation => true,

				// Deceptive behaviors — only in Gameplay mode
				NpcBehavior.Deceive => DeceptionEnabled,
				NpcBehavior.Manipulate => DeceptionEnabled,
				NpcBehavior.ConcealInformation => DeceptionEnabled,
				NpcBehavior.Sabotage => DeceptionEnabled,
				NpcBehavior.MakeFalseClaim => DeceptionEnabled,
				NpcBehavior.Persuade => DeceptionEnabled,
				NpcBehavior.Betray => DeceptionEnabled,

				_ => false,
			};
		}
	}

	/// <summary>
	/// NPC behaviors that can be enabled or disabled based on simulation mode.
	/// </summary>
	public enum NpcBehavior
	{
		// Cooperative behaviors (always available)
		Cooperate,
		Negotiate,
		ShareResource,
		RequestHelp,
		OfferHelp,
		ReportTruthfully,
		ResolveConflict,
		Remember,
		TrackReputation,

		// Deceptive behaviors (Gameplay mode only)
		// ============================================================
		// FUTURE GAMEPLAY SYSTEM — CURRENTLY DISABLED
		//
		// Deception, lying, misinformation, concealment and manipulation
		// are intentionally unavailable during construction simulation.
		//
		// Construction NPCs are cooperative agents.
		//
		// DO NOT ENABLE THESE BEHAVIORS UNTIL THE GAMEPLAY/SOCIAL PHASE.
		// ============================================================
		Deceive,
		Manipulate,
		ConcealInformation,
		Sabotage,
		MakeFalseClaim,
		Persuade,
		Betray,
	}
}
