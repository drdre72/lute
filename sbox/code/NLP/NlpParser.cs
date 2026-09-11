using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Deterministic natural language parser. Converts free text into
	/// <see cref="Intent"/> objects using keyword matching, pattern
	/// recognition, and topic extraction. No LLM, no cloud, no randomness
	/// — the same input always produces the same intent.
	///
	/// The parser works in three passes:
	/// 1. Intent type detection — match keywords/phrases to an
	///    <see cref="IntentType"/>.
	/// 2. Topic detection — match domain keywords to an
	///    <see cref="IntentTopic"/>.
	/// 3. Subject + parameter extraction — pull out the specific thing
	///    the intent is about (site name, material type, position, etc.)
	///
	/// Confidence is high (0.9-1.0) when multiple signals agree, medium
	/// (0.5-0.7) when only one signal matches, and low (0.2-0.3) when
	/// the parser falls back to defaults.
	/// </summary>
	public static class NlpParser
	{
		// ── Intent type keywords ──
		// Ordered by specificity — longer phrases checked first.
		static readonly (string[] keywords, IntentType type)[] _intentPatterns =
		{
			( new[] { "hello", "hi ", "greetings", "good morning", "good day", "hey" }, IntentType.Greet ),
			( new[] { "bye", "farewell", "goodbye", "see you", "later" }, IntentType.Farewell ),
			( new[] { "i need", "give me", "can i have", "i want", "requesting", "request" }, IntentType.Request ),
			( new[] { "i offer", "i can give", "i have extra", "offering", "offer" }, IntentType.Offer ),
			( new[] { "i accept", "agreed", "deal", "sounds good", "yes, i'll", "accept" }, IntentType.Accept ),
			( new[] { "no, i", "i refuse", "i decline", "no way", "reject", "decline" }, IntentType.Reject ),
			( new[] { "i propose", "let's do", "how about we", "proposing", "propose" }, IntentType.Propose ),
			( new[] { "instead", "counter", "what if we", "alternatively" }, IntentType.Counter ),
			( new[] { "i saw", "i heard", "did you know", "fyi", "informing", "inform" }, IntentType.Inform ),
			( new[] { "what is", "where is", "who has", "when will", "is there", "asking", "ask" }, IntentType.Ask ),
			( new[] { "watch out", "danger", "careful", "be careful", "warning", "warn" }, IntentType.Warn ),
			( new[] { "thank you", "thanks", "appreciate", "grateful" }, IntentType.Thank ),
			( new[] { "sorry", "my apologies", "apologize", "my bad" }, IntentType.Apologize ),
			( new[] { "i claim", "claiming", "dibs on", "i'm taking", "claim" }, IntentType.Claim ),
			( new[] { "i release", "releasing", "done with", "no longer need", "release" }, IntentType.Release ),
			( new[] { "i'll do", "i can do", "volunteer", "i volunteer", "i'll take" }, IntentType.Volunteer ),
			( new[] { "you do", "you take", "i'm assigning", "assigned to you", "assign" }, IntentType.Assign ),
			( new[] { "i can't do", "i won't", "i decline the", "decline" }, IntentType.Decline ),
			( new[] { "finished", "done with", "completed", "status report", "report" }, IntentType.Report ),
			( new[] { "understood", "acknowledged", "got it", "noted", "roger" }, IntentType.Acknowledge ),

			// ── Extended construction coordination intents ──
			( new[] { "i need help", "help me", "can you help", "need a hand", "requesting help" }, IntentType.RequestHelp ),
			( new[] { "i can help", "i'll help", "offering help", "let me help", "i can assist" }, IntentType.OfferHelp ),
			( new[] { "i accept your help", "accept help", "yes, help", "i'll take your help" }, IntentType.AcceptHelp ),
			( new[] { "i don't need help", "decline help", "no help needed", "i can manage" }, IntentType.DeclineHelp ),
			( new[] { "problem with", "blocked by", "is blocked", "can't build", "obstacle" }, IntentType.ReportProblem ),
			( new[] { "task complete", "finished building", "construction done", "built the", "completed the" }, IntentType.ReportCompletion ),
			( new[] { "i claim resource", "claiming resource", "dibs on resource" }, IntentType.ClaimResource ),
			( new[] { "i release resource", "releasing resource", "done with resource" }, IntentType.ReleaseResource ),
			( new[] { "i need resource", "requesting resource", "give me resource", "need materials" }, IntentType.RequestResource ),
			( new[] { "i have resource", "offering resource", "extra resource", "spare resource" }, IntentType.OfferResource ),
			( new[] { "i need a task", "requesting task", "give me a task", "what should i build" }, IntentType.RequestTask ),
			( new[] { "you should build", "i'm offering task", "here's a task", "take this task" }, IntentType.OfferTask ),
			( new[] { "you are assigned", "assigned to build", "your task is" }, IntentType.AssignTask ),
			( new[] { "i accept task", "i'll build the", "i'll take the task", "accepting task" }, IntentType.AcceptTask ),
			( new[] { "i'm at", "my location is", "currently at", "position is" }, IntentType.ReportLocation ),
			( new[] { "i'm available", "ready to work", "i'm free", "available for" }, IntentType.ReportAvailability ),
			( new[] { "i agree", "agreed", "that's right", "correct" }, IntentType.Agree ),
			( new[] { "i disagree", "that's wrong", "incorrect", "i don't agree" }, IntentType.Disagree ),
		};

		// ── Topic keywords ──
		static readonly (string[] keywords, IntentTopic topic)[] _topicPatterns =
		{
			( new[] { "site", "plot", "location", "ground", "area", "spot", "place", "north", "south", "east", "west" }, IntentTopic.Site ),
			( new[] { "stone", "wood", "plank", "timber", "material", "nail", "iron", "brick", "thatch" }, IntentTopic.Material ),
			( new[] { "task", "job", "wall", "gate", "road", "cottage", "shop", "chapel", "tower", "build" }, IntentTopic.Task ),
			( new[] { "npc", "builder", "merchant", "guard", "villager", "smith", "carpenter" }, IntentTopic.NPC ),
			( new[] { "weather", "rain", "storm", "wind", "snow", "fog", "cold", "hot" }, IntentTopic.Weather ),
			( new[] { "danger", "unsafe", "collapse", "fall", "fire", "flood", "structural" }, IntentTopic.Safety ),
			( new[] { "trade", "deal", "exchange", "buy", "sell", "price", "cost", "pay" }, IntentTopic.Trade ),
		};

		/// <summary>
		/// Parse a text string into an <see cref="Intent"/>. Deterministic
		/// — same input always yields same output.
		/// </summary>
		public static Intent Parse( string text, string sender = "", string target = "" )
		{
			if ( string.IsNullOrWhiteSpace( text ) )
				return Intent.Simple( IntentType.Acknowledge, sender: sender, target: target );

			var lower = text.ToLowerInvariant().Trim();

			// Pass 1: Intent type
			var (type, typeConf) = MatchIntentType( lower );

			// Pass 2: Topic
			var (topic, topicConf) = MatchTopic( lower );

			// Pass 3: Subject + parameters
			var subject = ExtractSubject( lower, topic );
			var parameters = ExtractParameters( lower, topic );

			// Confidence: average of type and topic confidence, with a
			// floor for fallbacks.
			float confidence = (typeConf + topicConf) * 0.5f;
			if ( type == IntentType.Acknowledge && typeConf < 0.3f )
				confidence = 0.2f; // very unsure

			return new Intent
			{
				Type = type,
				Topic = topic,
				Subject = subject,
				Parameters = parameters,
				Confidence = confidence,
				OriginalText = text,
				Sender = sender,
				Target = target,
				Timestamp = SpatialBlackboard.CurrentTime,
			};
		}

		static (IntentType type, float conf) MatchIntentType( string lower )
		{
			foreach ( var (keywords, type) in _intentPatterns )
			{
				foreach ( var kw in keywords )
				{
					if ( lower.Contains( kw, StringComparison.OrdinalIgnoreCase ) )
						return (type, 0.9f);
				}
			}

			// Fallback: if it ends with '?' treat as Ask
			if ( lower.Contains( '?' ) )
				return (IntentType.Ask, 0.5f);

			// Default: Acknowledge with low confidence
			return (IntentType.Acknowledge, 0.2f);
		}

		static (IntentTopic topic, float conf) MatchTopic( string lower )
		{
			foreach ( var (keywords, topic) in _topicPatterns )
			{
				foreach ( var kw in keywords )
				{
					if ( lower.Contains( kw, StringComparison.OrdinalIgnoreCase ) )
						return (topic, 0.9f);
				}
			}

			return (IntentTopic.None, 0.3f);
		}

		/// <summary>
		/// Extract the subject — the specific thing the intent is about.
		/// For sites: the direction or named location.
		/// For materials: the material type.
		/// For tasks: the task type (wall, gate, road, cottage, etc.)
		/// </summary>
		static string ExtractSubject( string lower, IntentTopic topic )
		{
			switch ( topic )
			{
				case IntentTopic.Site:
					// Look for direction words
					if ( lower.Contains( "north" ) ) return "north_site";
					if ( lower.Contains( "south" ) ) return "south_site";
					if ( lower.Contains( "east" ) ) return "east_site";
					if ( lower.Contains( "west" ) ) return "west_site";
					if ( lower.Contains( "center" ) ) return "center_site";
					return "site";

				case IntentTopic.Material:
					foreach ( var kw in new[] { "stone", "wood", "plank", "timber", "iron", "brick", "thatch", "nail" } )
					{
						if ( lower.Contains( kw ) )
							return kw;
					}
					return "material";

				case IntentTopic.Task:
					foreach ( var kw in new[] { "wall", "gate", "road", "cottage", "shop", "chapel", "tower", "bridge" } )
					{
						if ( lower.Contains( kw ) )
							return kw;
					}
					return "task";

				case IntentTopic.NPC:
					foreach ( var kw in new[] { "builder", "merchant", "guard", "villager", "smith", "carpenter" } )
					{
						if ( lower.Contains( kw ) )
							return kw;
					}
					return "npc";

				case IntentTopic.Safety:
					foreach ( var kw in new[] { "collapse", "fire", "flood", "fall", "structural" } )
					{
						if ( lower.Contains( kw ) )
							return kw;
					}
					return "safety";

				default:
					return "";
			}
		}

		/// <summary>
		/// Extract structured parameters from the text. Looks for
		/// numbers near keywords (e.g. "500 units" → radius=500),
		/// position strings ("x,y,z"), and amounts ("10 planks" → amount=10).
		/// </summary>
		static Dictionary<string, string> ExtractParameters( string lower, IntentTopic topic )
		{
			var parameters = new Dictionary<string, string>();

			// Extract numbers — first number found is the "primary" value
			var words = lower.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			for ( int i = 0; i < words.Length; i++ )
			{
				// "radius of 500" or "500 radius"
				if ( words[i] == "radius" || (i > 0 && words[i - 1] == "radius") )
				{
					var num = FindNearbyNumber( words, i );
					if ( num.HasValue )
						parameters["radius"] = num.Value.ToString( CultureInfo.InvariantCulture );
				}

				// "amount" or "N planks/stone/etc"
				if ( words[i] == "amount" || (i > 0 && words[i - 1] == "amount") )
				{
					var num = FindNearbyNumber( words, i );
					if ( num.HasValue )
						parameters["amount"] = num.Value.ToString();
				}

				// Bare number before a material keyword
				if ( i > 0 && int.TryParse( words[i - 1], out int amount ) )
				{
					if ( topic == IntentTopic.Material )
						parameters["amount"] = amount.ToString();
					if ( topic == IntentTopic.Task )
						parameters["pieces"] = amount.ToString();
				}
			}

			// Extract position if present (format: "x,y,z" or "at x y z")
			var posMatch = System.Text.RegularExpressions.Regex.Match(
				lower, @"(-?\d+\.?\d*)\s*,\s*(-?\d+\.?\d*)\s*,\s*(-?\d+\.?\d*)" );
			if ( posMatch.Success )
			{
				parameters["position"] = $"{posMatch.Groups[1].Value},{posMatch.Groups[2].Value},{posMatch.Groups[3].Value}";
			}

			return parameters;
		}

		static int? FindNearbyNumber( string[] words, int index )
		{
			// Check word before and after
			for ( int offset = -1; offset <= 1; offset += 2 )
			{
				int i = index + offset;
				if ( i < 0 || i >= words.Length )
					continue;
				var w = words[i].Trim( '.', ',', '!', '?' );
				if ( int.TryParse( w, out int n ) )
					return n;
				if ( float.TryParse( w, CultureInfo.InvariantCulture, out float f ) )
					return (int)f;
			}
			return null;
		}
	}
}
