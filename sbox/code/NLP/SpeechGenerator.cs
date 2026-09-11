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

			// Deterministic hash-based selection
			int hash = 0;
			var key = $"{intent.Type}_{intent.Subject}_{intent.Timestamp}";
			foreach ( var ch in key )
				hash = (hash * 31 + ch) & 0x7FFFFFFF;

			return options[hash % options.Length];
		}
	}
}
