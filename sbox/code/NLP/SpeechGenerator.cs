using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.NLP
{
	/// <summary>
	/// Generates natural language text from <see cref="Intent"/> objects.
	/// This is the reverse of <see cref="NlpParser"/> — it turns structured
	/// semantic intents back into readable sentences.
	///
	/// The generator uses templates with parameter substitution. Each
	/// (IntentType, IntentTopic) pair maps to one or more templates. The
	/// generator picks deterministically based on the sender's role and
	/// the parameters present.
	///
	/// No LLM, no randomness — the same intent always produces the same
	/// text (modulo template variation, which is also deterministic via
	/// a hash of the intent).
	/// </summary>
	public static class SpeechGenerator
	{
		/// <summary>
		/// Generate natural language text from an intent.
		/// </summary>
		public static string Generate( Intent intent )
		{
			if ( intent == null )
				return "";

			var subject = !string.IsNullOrEmpty( intent.Subject ) ? intent.Subject : "";
			var topic = intent.Topic;

			string text = intent.Type switch
			{
				IntentType.Greet => Pick( "Hello there.", "Greetings.", "Good day to you.", intent ),
				IntentType.Farewell => Pick( "Farewell.", "Goodbye for now.", "Until next time.", intent ),
				IntentType.Request => GenerateRequest( intent, subject, topic ),
				IntentType.Offer => GenerateOffer( intent, subject, topic ),
				IntentType.Accept => Pick( "Agreed.", "I accept.", "Sounds good to me.", intent ),
				IntentType.Reject => Pick( "I'm afraid I can't do that.", "No, I must decline.", "I can't agree to that.", intent ),
				IntentType.Propose => GeneratePropose( intent, subject, topic ),
				IntentType.Counter => GenerateCounter( intent, subject, topic ),
				IntentType.Inform => GenerateInform( intent, subject, topic ),
				IntentType.Ask => GenerateAsk( intent, subject, topic ),
				IntentType.Warn => GenerateWarn( intent, subject, topic ),
				IntentType.Thank => Pick( "Thank you.", "I appreciate that.", "My thanks.", intent ),
				IntentType.Apologize => Pick( "I apologize.", "Sorry about that.", "My apologies.", intent ),
				IntentType.Claim => GenerateClaim( intent, subject, topic ),
				IntentType.Release => GenerateRelease( intent, subject, topic ),
				IntentType.Volunteer => GenerateVolunteer( intent, subject, topic ),
				IntentType.Assign => GenerateAssign( intent, subject, topic ),
				IntentType.Decline => Pick( "I can't take that on.", "I'll have to pass.", "Not this time, sorry.", intent ),
				IntentType.Report => GenerateReport( intent, subject, topic ),
				IntentType.Acknowledge => Pick( "Understood.", "Acknowledged.", "Got it.", "Noted.", intent ),

				// ── Extended construction coordination intents ──
				IntentType.RequestHelp => GenerateRequestHelp( intent, subject, topic ),
				IntentType.OfferHelp => GenerateOfferHelp( intent, subject, topic ),
				IntentType.AcceptHelp => Pick( "I accept your help.", "Thank you, I could use that.", "Yes, please help.", intent ),
				IntentType.DeclineHelp => Pick( "I don't need help right now.", "I can manage, thanks.", "No help needed at the moment.", intent ),
				IntentType.ReportProblem => GenerateReportProblem( intent, subject, topic ),
				IntentType.ReportCompletion => GenerateReportCompletion( intent, subject, topic ),
				IntentType.ClaimResource => GenerateClaimResource( intent, subject, topic ),
				IntentType.ReleaseResource => GenerateReleaseResource( intent, subject, topic ),
				IntentType.RequestResource => GenerateRequestResource( intent, subject, topic ),
				IntentType.OfferResource => GenerateOfferResource( intent, subject, topic ),
				IntentType.RequestTask => GenerateRequestTask( intent, subject, topic ),
				IntentType.OfferTask => GenerateOfferTask( intent, subject, topic ),
				IntentType.AssignTask => GenerateAssignTask( intent, subject, topic ),
				IntentType.AcceptTask => GenerateAcceptTask( intent, subject, topic ),
				IntentType.ReportLocation => GenerateReportLocation( intent, subject, topic ),
				IntentType.ReportAvailability => GenerateReportAvailability( intent, subject, topic ),
				IntentType.Agree => Pick( "I agree.", "That's right.", "Correct.", intent ),
				IntentType.Disagree => Pick( "I disagree.", "That's not right.", "I don't think so.", intent ),

				_ => "Understood.",
			};

			return text;
		}

		// ── Specific generators ──

		static string GenerateRequest( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Material && intent.Parameters.TryGetValue( "amount", out var amount ) )
				return $"I need {amount} {subject}. Can you spare them?";
			if ( topic == IntentTopic.Site )
				return $"I need the {subject}. Can I take it?";
			if ( topic == IntentTopic.Task )
				return $"I need help with the {subject}. Can you assist?";
			return $"I need {subject}. Can you help?";
		}

		static string GenerateOffer( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Material && intent.Parameters.TryGetValue( "amount", out var amount ) )
				return $"I can offer {amount} {subject} if you need them.";
			if ( topic == IntentTopic.Site )
				return $"I'm offering the {subject} — do you want it?";
			if ( topic == IntentTopic.Task )
				return $"I can help with the {subject} if you'd like.";
			return $"I'm offering {subject}.";
		}

		static string GeneratePropose( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Trade )
				return $"I propose we trade — you take the {subject} and I take what's left.";
			if ( topic == IntentTopic.Site )
				return $"I propose we share the {subject} — I take the north half, you take the south.";
			return $"I propose we work together on the {subject}.";
		}

		static string GenerateCounter( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Trade )
				return $"Instead, how about I keep the {subject} and you take the next one?";
			return $"What if we do the {subject} differently — I handle the first part, you the rest?";
		}

		static string GenerateInform( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Site )
			{
				if ( intent.Parameters.TryGetValue( "claimed_by", out var claimer ) && !string.IsNullOrEmpty( claimer ) )
					return $"The {subject} is claimed by {claimer}.";
				if ( intent.Parameters.TryGetValue( "available", out var avail ) )
					return $"The {subject} is {(avail == "True" ? "available" : "not available")}.";
			}
			if ( topic == IntentTopic.Safety )
				return $"Heads up — there's a {subject} concern at the site.";
			return $"FYI — the {subject} is ready.";
		}

		static string GenerateAsk( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Site )
				return $"Is the {subject} available?";
			if ( topic == IntentTopic.Material )
				return $"Do you have any {subject} to spare?";
			if ( topic == IntentTopic.Task )
				return $"What's the status of the {subject}?";
			return $"What can you tell me about {subject}?";
		}

		static string GenerateWarn( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Safety )
				return $"Watch out — {subject} risk at the site. Be careful.";
			return $"Careful — there's an issue with {subject}.";
		}

		static string GenerateClaim( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Site )
			{
				if ( intent.Parameters.TryGetValue( "position", out var pos ) )
					return $"I'm claiming the {subject} at {pos}.";
				return $"I claim the {subject}.";
			}
			return $"I'm taking the {subject}.";
		}

		static string GenerateRelease( Intent intent, string subject, IntentTopic topic )
		{
			return $"I'm done with the {subject}. It's available now.";
		}

		static string GenerateVolunteer( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Task )
				return $"I'll take the {subject}. I can handle it.";
			return $"I volunteer for the {subject}.";
		}

		static string GenerateAssign( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Task )
				return $"You take the {subject}. It's your job now.";
			return $"I'm assigning you the {subject}.";
		}

		static string GenerateReport( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Task )
				return $"I've finished the {subject}. It's done.";
			return $"Status report: {subject} complete.";
		}

		// ── Extended construction coordination generators ──

		static string GenerateRequestHelp( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Task )
				return $"I need help with the {subject}. Can you assist?";
			if ( topic == IntentTopic.Site )
				return $"I need help at the {subject}. Can you come?";
			return $"I need help with {subject}.";
		}

		static string GenerateOfferHelp( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Task )
				return $"I can help with the {subject} if you need.";
			if ( topic == IntentTopic.Site )
				return $"I'm available to help at the {subject}.";
			return $"I'm offering to help with {subject}.";
		}

		static string GenerateReportProblem( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Safety )
				return $"Problem: {subject} is a safety risk. We need to address it.";
			if ( topic == IntentTopic.Task )
				return $"I'm blocked on the {subject}. Can someone help?";
			if ( topic == IntentTopic.Site )
				return $"There's a problem at the {subject}. It needs attention.";
			return $"Problem reported: {subject}.";
		}

		static string GenerateReportCompletion( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Task )
				return $"Task complete: the {subject} is finished.";
			if ( topic == IntentTopic.Site )
				return $"The {subject} is done and ready.";
			return $"Completed: {subject}.";
		}

		static string GenerateClaimResource( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Material )
				return $"I'm claiming the {subject} for my task.";
			return $"I claim {subject}.";
		}

		static string GenerateReleaseResource( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Material )
				return $"I'm done with the {subject}. It's available now.";
			return $"I release {subject}.";
		}

		static string GenerateRequestResource( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Material && intent.Parameters.TryGetValue( "amount", out var amount ) )
				return $"I need {amount} {subject}. Can you spare any?";
			return $"I need {subject}. Do you have any?";
		}

		static string GenerateOfferResource( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Material && intent.Parameters.TryGetValue( "amount", out var amount ) )
				return $"I have {amount} {subject} to spare if you need them.";
			return $"I'm offering {subject}. Do you need it?";
		}

		static string GenerateRequestTask( Intent intent, string subject, IntentTopic topic )
		{
			return $"I need a task. What should I build?";
		}

		static string GenerateOfferTask( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Task )
				return $"Here's a task: build the {subject}. Can you take it?";
			return $"I have a task for you: {subject}.";
		}

		static string GenerateAssignTask( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Task )
				return $"You're assigned to build the {subject}. It's your job now.";
			return $"I'm assigning you to {subject}.";
		}

		static string GenerateAcceptTask( Intent intent, string subject, IntentTopic topic )
		{
			if ( topic == IntentTopic.Task )
				return $"I'll build the {subject}. I accept the task.";
			return $"I accept {subject}.";
		}

		static string GenerateReportLocation( Intent intent, string subject, IntentTopic topic )
		{
			if ( intent.Parameters.TryGetValue( "position", out var pos ) )
				return $"I'm at {pos}.";
			return $"I'm at the {subject}.";
		}

		static string GenerateReportAvailability( Intent intent, string subject, IntentTopic topic )
		{
			return Pick( "I'm available for work.", "I'm free and ready to build.", "I can take a task now.", intent );
		}

		// ── Deterministic template picker ──

		/// <summary>
		/// Pick one of several templates deterministically, based on a
		/// hash of the intent. This gives slight variation without
		/// randomness — the same intent always picks the same template.
		/// </summary>
		static string Pick( string a, string b, string c, Intent intent )
		{
			return Pick( new[] { a, b, c }, intent );
		}

		static string Pick( string a, string b, string c, string d, Intent intent )
		{
			return Pick( new[] { a, b, c, d }, intent );
		}

		static string Pick( string[] options, Intent intent )
		{
			if ( options.Length == 0 )
				return "";
			if ( options.Length == 1 )
				return options[0];

			// Deterministic hash-based selection — excludes timestamp
			// so the same semantic intent always picks the same template.
			int hash = 0;
			var key = $"{intent.Type}_{intent.Topic}_{intent.Subject}";
			foreach ( var ch in key )
				hash = (hash * 31 + ch) & 0x7FFFFFFF;

			// Include parameter values in the hash for more variation
			foreach ( var kvp in intent.Parameters.OrderBy( k => k.Key ) )
			{
				foreach ( var ch in kvp.Value )
					hash = (hash * 31 + ch) & 0x7FFFFFFF;
			}

			return options[hash % options.Length];
		}
	}
}
