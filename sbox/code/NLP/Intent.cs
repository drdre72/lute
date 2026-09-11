using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.NLP
{
	/// <summary>
	/// Semantic intent types that NPCs can express or recognize.
	/// These are the deterministic vocabulary that replaces free-text
	/// LLM processing. Every NPC utterance maps to exactly one
	/// <see cref="IntentType"/> plus structured parameters.
	///
	/// The intent vocabulary covers the coordination needs of a
	/// building-focused NPC society:
	/// - Resource/site coordination (REQUEST, OFFER, CLAIM, RELEASE)
	/// - Social bonding (GREET, FAREWELL, THANK, APOLOGIZE)
	/// - Negotiation (PROPOSE, COUNTER, ACCEPT, REJECT)
	/// - Information exchange (INFORM, ASK, WARN)
	/// - Task coordination (VOLUNTEER, ASSIGN, DECLINE, REPORT)
	/// </summary>
	public enum IntentType
	{
		/// <summary>Initial greeting — establishes contact. </summary>
		Greet,
		/// <summary>Closing — ends the conversation politely. </summary>
		Farewell,
		/// <summary>Ask the other NPC for something (a resource, site, help). </summary>
		Request,
		/// <summary>Offer something to the other NPC. </summary>
		Offer,
		/// <summary>Accept a request or offer. </summary>
		Accept,
		/// <summary>Reject a request or offer. </summary>
		Reject,
		/// <summary>Propose a deal or arrangement. </summary>
		Propose,
		/// <summary>Counter-propose — modify a prior proposal. </summary>
		Counter,
		/// <summary>Inform the other NPC of a fact. </summary>
		Inform,
		/// <summary>Ask a question. </summary>
		Ask,
		/// <summary>Warn the other NPC about a danger or problem. </summary>
		Warn,
		/// <summary>Thank the other NPC. </summary>
		Thank,
		/// <summary>Apologize to the other NPC. </summary>
		Apologize,
		/// <summary>Claim a spatial resource (site, material source). </summary>
		Claim,
		/// <summary>Release a previously claimed resource. </summary>
		Release,
		/// <summary>Volunteer for a task. </summary>
		Volunteer,
		/// <summary>Assign a task to the other NPC. </summary>
		Assign,
		/// <summary>Decline a task assignment. </summary>
		Decline,
		/// <summary>Report status or completion. </summary>
		Report,
		/// <summary>Acknowledge a message without committing. </summary>
		Acknowledge,

		// ── Extended intents (professor Phase 1 feedback) ──
		// These add finer-grained construction coordination semantics
		// while keeping the original intents for backward compatibility.

		/// <summary>Request help with a task or problem. </summary>
		RequestHelp,
		/// <summary>Offer help to another NPC. </summary>
		OfferHelp,
		/// <summary>Accept an offer of help. </summary>
		AcceptHelp,
		/// <summary>Decline an offer of help. </summary>
		DeclineHelp,

		/// <summary>Report a problem or blocker. </summary>
		ReportProblem,
		/// <summary>Report task completion. </summary>
		ReportCompletion,

		/// <summary>Claim a resource (material, tool, area). </summary>
		ClaimResource,
		/// <summary>Release a previously claimed resource. </summary>
		ReleaseResource,

		/// <summary>Request a resource from another NPC. </summary>
		RequestResource,
		/// <summary>Offer a resource to another NPC. </summary>
		OfferResource,

		/// <summary>Request a task assignment. </summary>
		RequestTask,
		/// <summary>Offer a task to another NPC. </summary>
		OfferTask,

		/// <summary>Assign a task to another NPC. </summary>
		AssignTask,
		/// <summary>Accept a task assignment. </summary>
		AcceptTask,

		/// <summary>Report current location. </summary>
		ReportLocation,
		/// <summary>Report availability for work. </summary>
		ReportAvailability,

		/// <summary>Agree with a statement or proposal. </summary>
		Agree,
		/// <summary>Disagree with a statement or proposal. </summary>
		Disagree,

		// ============================================================
		// FUTURE GAMEPLAY SYSTEM — CURRENTLY DISABLED
		//
		// Deception, lying, misinformation, concealment and manipulation
		// are intentionally unavailable during construction simulation.
		//
		// Construction NPCs are cooperative agents.
		//
		// DO NOT ENABLE THESE INTENTS UNTIL THE GAMEPLAY/SOCIAL PHASE.
		// ============================================================
		// Lie,
		// Mislead,
		// ConcealInformation,
	}

	/// <summary>
	/// The topic or domain an intent refers to. This lets the social
	/// rules engine apply the right rules (e.g. a CLAIM about a SITE
	/// triggers blackboard reservation logic, while an INFORM about
	/// WEATHER is just social chatter).
	/// </summary>
	public enum IntentTopic
	{
		/// <summary>No specific topic — social greeting/farewell. </summary>
		None,
		/// <summary>A construction site or spatial claim. </summary>
		Site,
		/// <summary>Building materials (stone, wood, etc.). </summary>
		Material,
		/// <summary>A specific task or job. </summary>
		Task,
		/// <summary>Another NPC. </summary>
		NPC,
		/// <summary>Weather or environmental conditions. </summary>
		Weather,
		/// <summary>Safety or danger. </summary>
		Safety,
		/// <summary>General information. </summary>
		Information,
		/// <summary>A trade or deal. </summary>
		Trade,
	}

	/// <summary>
	/// A structured semantic intent — the deterministic representation
	/// of an NPC utterance. The NLP parser converts free text into
	/// <see cref="Intent"/> objects, and the speech generator converts
	/// <see cref="Intent"/> objects back into free text.
	///
	/// Every Intent has:
	/// - A type (what the speaker wants to do)
	/// - A topic (what domain it's about)
	/// - A subject (the specific thing — e.g. "north_gate_site")
	/// - Parameters (structured key-value pairs — e.g. radius=500)
	/// - A confidence (how sure the parser is, 0-1)
	/// - The original text (for debugging/logging)
	/// </summary>
	public sealed class Intent
	{
		/// <summary> What the speaker is doing. </summary>
		public IntentType Type { get; set; } = IntentType.Acknowledge;

		/// <summary> What domain the intent is about. </summary>
		public IntentTopic Topic { get; set; } = IntentTopic.None;

		/// <summary>
		/// The specific subject — e.g. "north_gate_site", "oak_planks",
		/// "build_wall_3". Free-form string, matched against beliefs.
		/// </summary>
		public string Subject { get; set; } = "";

		/// <summary>
		/// Structured parameters. Keys are domain-specific:
		/// - Site: "position" (Vector3 string), "radius" (float string)
		/// - Material: "type" (string), "amount" (int string)
		/// - Task: "task_id" (string), "pieces" (int string)
		/// </summary>
		public Dictionary<string, string> Parameters { get; set; } = new();

		/// <summary>
		/// Parser confidence (0.0 to 1.0). Low confidence means the
		/// parser wasn't sure — the social rules engine may ask for
		/// clarification instead of acting.
		/// </summary>
		public float Confidence { get; set; } = 1.0f;

		/// <summary> The original text this intent was parsed from. </summary>
		public string OriginalText { get; set; } = "";

		/// <summary> Who sent this intent (NPC name). </summary>
		public string Sender { get; set; } = "";

		/// <summary> Who this intent is addressed to (NPC name, or "" for broadcast). </summary>
		public string Target { get; set; } = "";

		/// <summary> Timestamp when this intent was created. </summary>
		public float Timestamp { get; set; }

		/// <summary> Short debug summary. </summary>
		public string Summary =>
			$"{Type}({Topic}) '{Subject}' conf={Confidence:F2}" +
			(Parameters.Count > 0
				? $" [{string.Join( ",", Parameters.Keys )}]"
				: "");

		/// <summary> Create a simple intent with no parameters. </summary>
		public static Intent Simple( IntentType type, IntentTopic topic = IntentTopic.None,
			string subject = "", string sender = "", string target = "" )
		{
			return new Intent
			{
				Type = type,
				Topic = topic,
				Subject = subject,
				Sender = sender,
				Target = target,
				Confidence = 1.0f,
			};
		}
	}
}
