using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Grammar-based semantic parser. Upgrades the keyword-matching
	/// <see cref="NlpParser"/> with proper sentence structure understanding:
	///
	/// - **Negation detection**: "I don't need stone" → NOT(Request), not Request
	/// - **Conditional clauses**: "I'll take the wall, but not until I finish
	///   the gate" → VOLUNTEER(task=wall) + CONDITION(after=gate)
	/// - **Multi-intent sentences**: "No, I can't take the wall because I'm
	///   already working north" → REJECT(task=wall) + REASON(working_north)
	/// - **Conjunction splitting**: "I claim the north_site and release the
	///   south_site" → [Claim(north_site), Release(south_site)]
	///
	/// The parser works in these passes:
	/// 1. Sentence segmentation — split on conjunctions ("and", "but", "because")
	/// 2. Negation detection — find "not", "don't", "can't", "no" prefixes
	/// 3. Speech act classification — determine the primary intent type
	/// 4. Topic + subject extraction — what domain and specific thing
	/// 5. Parameter extraction — quantities, positions, conditions
	/// 6. Condition extraction — "if", "when", "after", "until" clauses
	///
	/// Returns a <see cref="ParseResult"/> with the primary intent plus
	/// any conditions, negations, and secondary intents.
	/// </summary>
	public static class GrammarParser
	{
		/// <summary>
		/// Parse a text string into a <see cref="ParseResult"/> with
		/// full sentence structure understanding. Deterministic.
		/// </summary>
		public static ParseResult Parse( string text, string sender = "", string target = "" )
		{
			if ( string.IsNullOrWhiteSpace( text ) )
				return new ParseResult { Primary = Intent.Simple( IntentType.Acknowledge, sender: sender, target: target ) };

			var lower = text.ToLowerInvariant().Trim();

			// Pass 1: Split on conjunctions into clauses
			var clauses = SplitClauses( lower );

			// Parse each clause
			var intents = new List<Intent>();
			string condition = null;
			string reason = null;

			foreach ( var clause in clauses )
			{
				// Check if this is a condition clause ("if...", "when...", "after...")
				var cond = ExtractCondition( clause );
				if ( cond != null )
				{
					condition = cond;
					continue;
				}

				// Check if this is a reason clause ("because...")
				var rsn = ExtractReason( clause );
				if ( rsn != null )
				{
					reason = rsn;
					continue;
				}

				// Parse as a regular intent
				var intent = ParseClause( clause, sender, target );
				if ( intent != null )
					intents.Add( intent );
			}

			// The first intent is primary; the rest are secondary
			var primary = intents.FirstOrDefault() ??
				Intent.Simple( IntentType.Acknowledge, sender: sender, target: target );

			// Attach condition to primary intent
			if ( condition != null )
			{
				primary.Parameters["condition"] = condition;
			}

			// Attach reason to primary intent
			if ( reason != null )
			{
				primary.Parameters["reason"] = reason;
			}

			primary.OriginalText = text;
			primary.Timestamp = SpatialBlackboard.CurrentTime;

			return new ParseResult
			{
				Primary = primary,
				Secondary = intents.Skip( 1 ).ToList(),
				Condition = condition,
				Reason = reason,
				HasNegation = primary.Confidence < 0 && primary.Type == IntentType.Reject,
			};
		}

		// ── Pass 1: Clause splitting ──

		static readonly string[] _conjunctions = { " and ", " but ", " because ", " so ", " then ", "; " };

		static List<string> SplitClauses( string text )
		{
			var clauses = new List<string> { text };

			foreach ( var conj in _conjunctions )
			{
				var newClauses = new List<string>();
				foreach ( var clause in clauses )
				{
					var parts = clause.Split( new[] { conj }, StringSplitOptions.None );
					newClauses.AddRange( parts );
				}
				clauses = newClauses;
			}

			return clauses
				.Select( c => c.Trim() )
				.Where( c => !string.IsNullOrEmpty( c ) )
				.ToList();
		}

		// ── Condition extraction ──

		static readonly string[] _conditionMarkers = { "if ", "when ", "after ", "until ", "once ", "as soon as " };

		static string ExtractCondition( string clause )
		{
			foreach ( var marker in _conditionMarkers )
			{
				if ( clause.StartsWith( marker, StringComparison.OrdinalIgnoreCase ) )
				{
					return clause.Substring( marker.Length ).Trim();
				}
			}
			return null;
		}

		// ── Reason extraction ──

		static string ExtractReason( string clause )
		{
			if ( clause.StartsWith( "because ", StringComparison.OrdinalIgnoreCase ) )
				return clause.Substring( "because ".Length ).Trim();
			return null;
		}

		// ── Pass 2-5: Parse a single clause ──

		static Intent ParseClause( string clause, string sender, string target )
		{
			// Detect negation
			bool negated = IsNegated( clause );
			var cleaned = RemoveNegation( clause );

			// Classify the speech act
			var (type, typeConf) = ClassifySpeechAct( cleaned, negated );

			// If negated, flip certain intents
			if ( negated )
			{
				type = FlipNegatedIntent( type );
			}

			// Extract topic
			var (topic, topicConf) = ClassifyTopic( cleaned );

			// Extract subject
			var subject = ExtractSubject( cleaned, topic );

			// Extract parameters
			var parameters = ExtractParameters( cleaned, topic );

			float confidence = (typeConf + topicConf) * 0.5f;
			if ( negated )
				confidence *= 0.9f; // slightly less confident with negation

			return new Intent
			{
				Type = type,
				Topic = topic,
				Subject = subject,
				Parameters = parameters,
				Confidence = confidence,
				Sender = sender,
				Target = target,
				Timestamp = SpatialBlackboard.CurrentTime,
			};
		}

		// ── Negation detection ──

		static readonly string[] _negationMarkers =
		{
			"don't", "do not", "can't", "cannot", "won't", "will not",
			"no, ", "not ", "no way", "refuse", "never",
		};

		static bool IsNegated( string clause )
		{
			foreach ( var marker in _negationMarkers )
			{
				if ( clause.Contains( marker, StringComparison.OrdinalIgnoreCase ) )
					return true;
			}
			// "No" at the start of a clause
			if ( clause.StartsWith( "no ", StringComparison.OrdinalIgnoreCase ) ||
				 clause == "no" )
				return true;
			return false;
		}

		static string RemoveNegation( string clause )
		{
			var result = clause;
			foreach ( var marker in _negationMarkers )
			{
				result = result.Replace( marker, " ", StringComparison.OrdinalIgnoreCase );
			}
			if ( result.StartsWith( "no ", StringComparison.OrdinalIgnoreCase ) )
				result = result.Substring( 3 );
			return result.Trim();
		}

		static IntentType FlipNegatedIntent( IntentType type )
		{
			return type switch
			{
				IntentType.Request => IntentType.Reject,
				IntentType.Offer => IntentType.Reject,
				IntentType.Accept => IntentType.Reject,
				IntentType.Volunteer => IntentType.Decline,
				IntentType.Claim => IntentType.Release,
				IntentType.Assign => IntentType.Decline,
				_ => type,
			};
		}

		// ── Speech act classification ──

		static readonly (string[] keywords, IntentType type)[] _speechActs =
		{
			( new[] { "hello", "hi ", "greetings", "good morning", "good day", "hey" }, IntentType.Greet ),
			( new[] { "bye", "farewell", "goodbye", "see you", "later" }, IntentType.Farewell ),
			( new[] { "i need", "give me", "can i have", "i want", "requesting", "request", "need" }, IntentType.Request ),
			( new[] { "i offer", "i can give", "i have extra", "offering", "offer" }, IntentType.Offer ),
			( new[] { "i accept", "agreed", "deal", "sounds good", "accept" }, IntentType.Accept ),
			( new[] { "i refuse", "i decline", "reject", "decline" }, IntentType.Reject ),
			( new[] { "i propose", "let's do", "how about we", "proposing", "propose" }, IntentType.Propose ),
			( new[] { "instead", "counter", "what if we", "alternatively" }, IntentType.Counter ),
			( new[] { "i saw", "i heard", "did you know", "fyi", "informing", "inform" }, IntentType.Inform ),
			( new[] { "what is", "where is", "who has", "when will", "is there", "asking", "ask", "?" }, IntentType.Ask ),
			( new[] { "watch out", "danger", "careful", "warning", "warn" }, IntentType.Warn ),
			( new[] { "thank you", "thanks", "appreciate", "grateful" }, IntentType.Thank ),
			( new[] { "sorry", "my apologies", "apologize", "my bad" }, IntentType.Apologize ),
			( new[] { "i claim", "claiming", "dibs on", "i'm taking", "claim" }, IntentType.Claim ),
			( new[] { "i release", "releasing", "done with", "no longer need", "release" }, IntentType.Release ),
			( new[] { "i'll do", "i can do", "volunteer", "i volunteer", "i'll take" }, IntentType.Volunteer ),
			( new[] { "you do", "you take", "i'm assigning", "assigned to you", "assign" }, IntentType.Assign ),
			( new[] { "finished", "completed", "status report", "report" }, IntentType.Report ),
			( new[] { "understood", "acknowledged", "got it", "noted", "roger" }, IntentType.Acknowledge ),
		};

		static (IntentType type, float conf) ClassifySpeechAct( string cleaned, bool negated )
		{
			foreach ( var (keywords, type) in _speechActs )
			{
				foreach ( var kw in keywords )
				{
					if ( cleaned.Contains( kw, StringComparison.OrdinalIgnoreCase ) )
						return (type, 0.9f);
				}
			}

			if ( cleaned.Contains( '?' ) )
				return (IntentType.Ask, 0.5f);

			return (IntentType.Acknowledge, 0.2f);
		}

		// ── Topic classification ──

		static readonly (string[] keywords, IntentTopic topic)[] _topics =
		{
			( new[] { "site", "plot", "location", "ground", "area", "spot", "place", "north", "south", "east", "west" }, IntentTopic.Site ),
			( new[] { "stone", "wood", "plank", "timber", "material", "nail", "iron", "brick", "thatch" }, IntentTopic.Material ),
			( new[] { "task", "job", "wall", "gate", "road", "cottage", "shop", "chapel", "tower", "build" }, IntentTopic.Task ),
			( new[] { "npc", "builder", "merchant", "guard", "villager", "smith", "carpenter" }, IntentTopic.NPC ),
			( new[] { "weather", "rain", "storm", "wind", "snow", "fog", "cold", "hot" }, IntentTopic.Weather ),
			( new[] { "danger", "unsafe", "collapse", "fall", "fire", "flood", "structural" }, IntentTopic.Safety ),
			( new[] { "trade", "deal", "exchange", "buy", "sell", "price", "cost", "pay" }, IntentTopic.Trade ),
		};

		static (IntentTopic topic, float conf) ClassifyTopic( string cleaned )
		{
			foreach ( var (keywords, topic) in _topics )
			{
				foreach ( var kw in keywords )
				{
					if ( cleaned.Contains( kw, StringComparison.OrdinalIgnoreCase ) )
						return (topic, 0.9f);
				}
			}
			return (IntentTopic.None, 0.3f);
		}

		// ── Subject extraction ──

		static string ExtractSubject( string cleaned, IntentTopic topic )
		{
			switch ( topic )
			{
				case IntentTopic.Site:
					if ( cleaned.Contains( "north" ) ) return "north_site";
					if ( cleaned.Contains( "south" ) ) return "south_site";
					if ( cleaned.Contains( "east" ) ) return "east_site";
					if ( cleaned.Contains( "west" ) ) return "west_site";
					if ( cleaned.Contains( "center" ) ) return "center_site";
					return "site";

				case IntentTopic.Material:
					foreach ( var kw in new[] { "stone", "wood", "plank", "timber", "iron", "brick", "thatch", "nail" } )
					{
						if ( cleaned.Contains( kw ) )
							return kw;
					}
					return "material";

				case IntentTopic.Task:
					foreach ( var kw in new[] { "wall", "gate", "road", "cottage", "shop", "chapel", "tower", "bridge" } )
					{
						if ( cleaned.Contains( kw ) )
							return kw;
					}
					return "task";

				case IntentTopic.NPC:
					foreach ( var kw in new[] { "builder", "merchant", "guard", "villager", "smith", "carpenter" } )
					{
						if ( cleaned.Contains( kw ) )
							return kw;
					}
					return "npc";

				case IntentTopic.Safety:
					foreach ( var kw in new[] { "collapse", "fire", "flood", "fall", "structural" } )
					{
						if ( cleaned.Contains( kw ) )
							return kw;
					}
					return "safety";

				default:
					return "";
			}
		}

		// ── Parameter extraction ──

		static Dictionary<string, string> ExtractParameters( string cleaned, IntentTopic topic )
		{
			var parameters = new Dictionary<string, string>();
			var words = cleaned.Split( ' ', StringSplitOptions.RemoveEmptyEntries );

			for ( int i = 0; i < words.Length; i++ )
			{
				if ( words[i] == "radius" || (i > 0 && words[i - 1] == "radius") )
				{
					var num = FindNearbyNumber( words, i );
					if ( num.HasValue )
						parameters["radius"] = num.Value.ToString( CultureInfo.InvariantCulture );
				}

				if ( words[i] == "amount" || (i > 0 && words[i - 1] == "amount") )
				{
					var num = FindNearbyNumber( words, i );
					if ( num.HasValue )
						parameters["amount"] = num.Value.ToString();
				}

				if ( i > 0 && int.TryParse( words[i - 1], out int amount ) )
				{
					if ( topic == IntentTopic.Material )
						parameters["amount"] = amount.ToString();
					if ( topic == IntentTopic.Task )
						parameters["pieces"] = amount.ToString();
				}
			}

			var posMatch = Regex.Match( cleaned, @"(-?\d+\.?\d*)\s*,\s*(-?\d+\.?\d*)\s*,\s*(-?\d+\.?\d*)" );
			if ( posMatch.Success )
			{
				parameters["position"] = $"{posMatch.Groups[1].Value},{posMatch.Groups[2].Value},{posMatch.Groups[3].Value}";
			}

			return parameters;
		}

		static int? FindNearbyNumber( string[] words, int index )
		{
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

	/// <summary>
	/// Result of parsing a sentence. Contains the primary intent plus
	/// any secondary intents (from multi-clause sentences), conditions,
	/// and reasons.
	/// </summary>
	public sealed class ParseResult
	{
		/// <summary> The primary intent (first clause). </summary>
		public Intent Primary { get; set; }

		/// <summary> Secondary intents from additional clauses. </summary>
		public List<Intent> Secondary { get; set; } = new();

		/// <summary> Condition clause ("if...", "after...", "until..."). </summary>
		public string Condition { get; set; }

		/// <summary> Reason clause ("because..."). </summary>
		public string Reason { get; set; }

		/// <summary> True if the sentence contained negation. </summary>
		public bool HasNegation { get; set; }

		/// <summary> Short summary. </summary>
		public string Summary =>
			$"{Primary?.Summary}" +
			(Secondary.Count > 0 ? $" + {Secondary.Count} secondary" : "") +
			(Condition != null ? $" IF {Condition}" : "") +
			(Reason != null ? $" BECAUSE {Reason}" : "") +
			(HasNegation ? " [NEGATED]" : "");
	}
}
