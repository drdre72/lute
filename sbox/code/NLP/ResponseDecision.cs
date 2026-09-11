using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.NLP
{
	/// <summary>
	/// What an NPC decides to do after evaluating an incoming intent.
	/// This replaces the old "always respond with dialogue" pattern.
	///
	/// The decision is:
	/// - <see cref="Action"/>: what the NPC will do (speak, act, ignore, etc.)
	/// - <see cref="ResponseIntent"/>: if speaking, the intent to express
	/// - <see cref="WorldAction"/>: if acting, a description of the
	///   physical action to take (e.g. "deliver_stone", "move_to_site")
	/// - <see cref="Reason"/>: why this decision was made (for debugging)
	/// </summary>
	public sealed class ResponseDecision
	{
		/// <summary> What the NPC will do. </summary>
		public DecisionAction Action { get; set; } = DecisionAction.Ignore;

		/// <summary>
		/// If Action is Speak, the intent to express. Null if not speaking.
		/// </summary>
		public Intent ResponseIntent { get; set; }

		/// <summary>
		/// If Action is Act, a description of the physical action.
		/// E.g. "deliver_stone:50", "move_to:north_site", "release:east_site".
		/// The GoalAction system interprets this.
		/// </summary>
		public string WorldAction { get; set; } = "";

		/// <summary> Why this decision was made (for debugging/logging). </summary>
		public string Reason { get; set; } = "";

		/// <summary> Short summary for logging. </summary>
		public string Summary =>
			$"{Action}" +
			(ResponseIntent != null ? $" ({ResponseIntent.Type})" : "") +
			(!string.IsNullOrEmpty( WorldAction ) ? $" [{WorldAction}]" : "") +
			(!string.IsNullOrEmpty( Reason ) ? $" — {Reason}" : "");
	}

	/// <summary>
	/// The kind of action an NPC can take in response to a message.
	/// </summary>
	public enum DecisionAction
	{
		/// <summary> Speak — produce a verbal response. </summary>
		Speak,
		/// <summary> Act — take a physical action (move, deliver, build). </summary>
		Act,
		/// <summary> Speak and Act — respond verbally while also acting. </summary>
		SpeakAndAct,
		/// <summary> Ignore — the message doesn't warrant a response. </summary>
		Ignore,
		/// <summary> Defer — acknowledge but delay the response/action. </summary>
		Defer,
		/// <summary> Ask for clarification — the message was ambiguous. </summary>
		AskForClarification,
	}
}
