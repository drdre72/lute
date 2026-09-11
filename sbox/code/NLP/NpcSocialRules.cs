using System;

namespace Lute.NLP
{
	/// <summary>
	/// Social rules for NPC behavior, including the deception feature gate.
	///
	/// Construction phase:
	///   NPCs communicate cooperatively and truthfully.
	///   Deception is intentionally disabled.
	///   Enable only for gameplay/social simulation.
	///
	/// This class provides the CanDeceive() check that the SocialRules
	/// engine uses to filter out deceptive intents from the candidate set.
	/// </summary>
	public sealed class NpcSocialRules
	{
		// Construction phase:
		// NPCs communicate cooperatively and truthfully.
		//
		// Deception is intentionally disabled.
		// Enable only for gameplay/social simulation.

		/// <summary>
		/// Whether deception is enabled. Defaults to false (Construction mode).
		/// When false, all deceptive intents are filtered out.
		/// </summary>
		public bool DeceptionEnabled => NpcSimulation.DeceptionEnabled;

		/// <summary>
		/// Check if a specific NPC can deceive in the current simulation mode.
		/// Always returns false in Construction mode.
		/// </summary>
		public bool CanDeceive( NpcContext npc )
		{
			if ( !DeceptionEnabled )
				return false;

			// In Gameplay mode, check personality trait
			return npc?.Personality?.Deception >= 0.5f;
		}

		/// <summary>
		/// Filter a list of candidate intents, removing any deceptive ones
		/// if deception is disabled.
		/// </summary>
		public bool IsIntentAllowed( IntentType type )
		{
			// All construction-phase intents are allowed
			// Deceptive intents are commented out in the enum, so they
			// can never be produced. This is a safety check.
			return true;
		}
	}

	/// <summary>
	/// Context for an NPC being evaluated by the social rules.
	/// </summary>
	public sealed class NpcContext
	{
		public string Name { get; set; }
		public NpcPersonality Personality { get; set; }
		public BeliefModel Beliefs { get; set; }
	}

	/// <summary>
	/// NPC personality traits. These affect expression (word choice,
	/// formality, verbosity) but NOT intelligence or decision-making.
	/// Personality modifies selection, not semantics.
	/// </summary>
	public sealed class NpcPersonality
	{
		/// <summary> How cooperative the NPC is (0-1). </summary>
		public float Cooperation { get; set; } = 0.8f;

		/// <summary> How patient the NPC is (0-1). </summary>
		public float Patience { get; set; } = 0.5f;

		/// <summary> How verbose the NPC is (0-1). </summary>
		public float Verbosity { get; set; } = 0.3f;

		/// <summary> How confident the NPC is (0-1). </summary>
		public float Confidence { get; set; } = 0.7f;

		/// <summary> How formal the NPC speaks (0-1). </summary>
		public float Formality { get; set; } = 0.2f;

		// ============================================================
		// FUTURE GAMEPLAY SYSTEM — CURRENTLY DISABLED
		//
		// Deception, lying, misinformation, concealment and manipulation
		// are intentionally unavailable during construction simulation.
		//
		// Construction NPCs are cooperative agents.
		//
		// DO NOT ENABLE THESE TRAITS UNTIL THE GAMEPLAY/SOCIAL PHASE.
		// ============================================================

		/// <summary> Deception skill (0-1). Only used in Gameplay mode. </summary>
		public float Deception { get; set; } = 0f;

		/// <summary> Persuasion skill (0-1). Only used in Gameplay mode. </summary>
		public float Persuasion { get; set; } = 0f;

		/// <summary> Manipulation skill (0-1). Only used in Gameplay mode. </summary>
		public float Manipulation { get; set; } = 0f;
	}
}
