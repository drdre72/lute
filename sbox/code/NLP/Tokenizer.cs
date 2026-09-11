using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lute.NLP
{
	/// <summary>
	/// Deterministic tokenizer and text normalizer. This is the first
	/// layer of the NLP parsing pipeline:
	///
	///   Raw text
	///      │
	///      ▼
	///   Tokenizer/Normalizer  ← this
	///      │
	///      ▼
	///   Entity recognition
	///      │
	///      ▼
	///   Intent grammar (GrammarParser)
	///      │
	///      ▼
	///   Context resolver
	///      │
	///      ▼
	///   NpcIntent
	///
	/// The tokenizer:
	/// 1. Lowercases and strips whitespace
	/// 2. Expands contractions ("don't" → "do not", "I'll" → "I will")
	/// 3. Splits into tokens (words + punctuation)
	/// 4. Tags tokens with basic POS info (isNegation, isQuestion, isNumber, isPronoun)
	/// 5. Normalizes synonyms ("yeah" → "yes", "ok" → "okay")
	///
	/// Deterministic — same input always produces the same token list.
	/// </summary>
	public static class Tokenizer
	{
		// ── Contraction expansion ──
		static readonly (string contraction, string expansion)[] _contractions =
		{
			( "don't", "do not" ),
			( "doesn't", "does not" ),
			( "didn't", "did not" ),
			( "won't", "will not" ),
			( "can't", "can not" ),
			( "couldn't", "could not" ),
			( "shouldn't", "should not" ),
			( "wouldn't", "would not" ),
			( "isn't", "is not" ),
			( "aren't", "are not" ),
			( "wasn't", "was not" ),
			( "weren't", "were not" ),
			( "hasn't", "has not" ),
			( "haven't", "have not" ),
			( "hadn't", "had not" ),
			( "i'll", "I will" ),
			( "you'll", "you will" ),
			( "he'll", "he will" ),
			( "she'll", "she will" ),
			( "we'll", "we will" ),
			( "they'll", "they will" ),
			( "it'll", "it will" ),
			( "i'm", "I am" ),
			( "you're", "you are" ),
			( "we're", "we are" ),
			( "they're", "they are" ),
			( "i've", "I have" ),
			( "you've", "you have" ),
			( "we've", "we have" ),
			( "they've", "they have" ),
			( "i'd", "I would" ),
			( "you'd", "you would" ),
			( "he'd", "he would" ),
			( "she'd", "she would" ),
			( "we'd", "we would" ),
			( "they'd", "they would" ),
			( "let's", "let us" ),
		};

		// ── Synonym normalization ──
		static readonly Dictionary<string, string> _synonyms = new()
		{
			{ "yeah", "yes" },
			{ "yep", "yes" },
			{ "yup", "yes" },
			{ "sure", "yes" },
			{ "ok", "okay" },
			{ "k", "okay" },
			{ "alright", "all right" },
			{ "nope", "no" },
			{ "nah", "no" },
			{ "gonna", "going to" },
			{ "wanna", "want to" },
			{ "gotta", "got to" },
			{ "lemme", "let me" },
			{ "gimme", "give me" },
		};

		// ── Negation words ──
		static readonly HashSet<string> _negationWords = new()
		{
			"not", "no", "never", "none", "nobody", "nothing",
			"neither", "nor", "without", "cannot",
		};

		// ── Pronouns that need entity resolution ──
		static readonly HashSet<string> _pronouns = new()
		{
			"it", "this", "that", "these", "those",
			"here", "there", "where",
			"he", "she", "they", "we", "us",
		};

		// ── Question words ──
		static readonly HashSet<string> _questionWords = new()
		{
			"what", "where", "who", "when", "why", "how",
			"which", "whose", "whom",
		};

		/// <summary>
		/// Tokenize and normalize raw text into a list of tokens with
		/// basic POS tags. Deterministic.
		/// </summary>
		public static TokenList Tokenize( string text )
		{
			if ( string.IsNullOrWhiteSpace( text ) )
				return new TokenList { OriginalText = "", NormalizedText = "", Tokens = new() };

			// Step 1: Lowercase and trim
			var lower = text.ToLowerInvariant().Trim();

			// Step 2: Expand contractions
			var expanded = ExpandContractions( lower );

			// Step 3: Normalize synonyms
			var normalized = NormalizeSynonyms( expanded );

			// Step 4: Split into raw tokens (words + punctuation)
			var rawTokens = SplitTokens( normalized );

			// Step 5: Tag tokens
			var tokens = TagTokens( rawTokens );

			return new TokenList
			{
				OriginalText = text,
				NormalizedText = normalized,
				Tokens = tokens,
			};
		}

		static string ExpandContractions( string text )
		{
			foreach ( var (contraction, expansion) in _contractions )
			{
				text = text.Replace( contraction, expansion,
					StringComparison.OrdinalIgnoreCase );
			}
			return text;
		}

		static string NormalizeSynonyms( string text )
		{
			var words = text.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			for ( int i = 0; i < words.Length; i++ )
			{
				var w = words[i].Trim( '.', ',', '!', '?' );
				if ( _synonyms.TryGetValue( w, out var replacement ) )
					words[i] = replacement;
			}
			return string.Join( ' ', words );
		}

		static List<string> SplitTokens( string text )
		{
			// Split on whitespace, keeping punctuation as separate tokens
			var tokens = new List<string>();
			var sb = new StringBuilder();

			foreach ( var c in text )
			{
				if ( char.IsWhiteSpace( c ) )
				{
					if ( sb.Length > 0 )
					{
						tokens.Add( sb.ToString() );
						sb.Clear();
					}
				}
				else if ( c == ',' || c == '.' || c == '!' || c == '?' || c == ';' || c == ':' )
				{
					if ( sb.Length > 0 )
					{
						tokens.Add( sb.ToString() );
						sb.Clear();
					}
					tokens.Add( c.ToString() );
				}
				else
				{
					sb.Append( c );
				}
			}

			if ( sb.Length > 0 )
				tokens.Add( sb.ToString() );

			return tokens;
		}

		static List<Token> TagTokens( List<string> rawTokens )
		{
			var tokens = new List<Token>( rawTokens.Count );

			for ( int i = 0; i < rawTokens.Count; i++ )
			{
				var raw = rawTokens[i];
				var token = new Token
				{
					Text = raw,
					Index = i,
				};

				// Number?
				if ( float.TryParse( raw, CultureInfo.InvariantCulture, out _ ) )
				{
					token.IsNumber = true;
				}

				// Negation?
				if ( _negationWords.Contains( raw ) )
				{
					token.IsNegation = true;
				}

				// Pronoun?
				if ( _pronouns.Contains( raw ) )
				{
					token.IsPronoun = true;
					token.NeedsResolution = true;
				}

				// Question word?
				if ( _questionWords.Contains( raw ) )
				{
					token.IsQuestion = true;
				}

				// Punctuation?
				if ( raw.Length == 1 && !char.IsLetterOrDigit( raw[0] ) )
				{
					token.IsPunctuation = true;
					if ( raw == "?" )
						token.IsQuestionMark = true;
				}

				tokens.Add( token );
			}

			return tokens;
		}
	}

	/// <summary>
	/// A single token with basic POS tags.
	/// </summary>
	public sealed class Token
	{
		public string Text { get; set; }
		public int Index { get; set; }
		public bool IsNegation { get; set; }
		public bool IsPronoun { get; set; }
		public bool IsNumber { get; set; }
		public bool IsQuestion { get; set; }
		public bool IsQuestionMark { get; set; }
		public bool IsPunctuation { get; set; }
		public bool NeedsResolution { get; set; }

		/// <summary> Resolved entity (filled by EntityResolver). Null if not resolved. </summary>
		public string ResolvedEntity { get; set; }

		public override string ToString() =>
			$"{Text}{(NeedsResolution ? "[?]" : "")}{(IsNegation ? "[!]" : "")}";
	}

	/// <summary>
	/// The result of tokenization — the normalized text plus the
	/// tagged token list.
	/// </summary>
	public sealed class TokenList
	{
		public string OriginalText { get; set; }
		public string NormalizedText { get; set; }
		public List<Token> Tokens { get; set; }

		/// <summary> Get all non-punctuation tokens as strings. </summary>
		public List<string> Words => Tokens
			.Where( t => !t.IsPunctuation )
			.Select( t => t.Text )
			.ToList();

		/// <summary> Does this text contain a negation word? </summary>
		public bool HasNegation => Tokens.Any( t => t.IsNegation );

		/// <summary> Does this text contain a question? </summary>
		public bool IsQuestion => Tokens.Any( t => t.IsQuestion || t.IsQuestionMark );

		/// <summary> Get all pronouns that need entity resolution. </summary>
		public List<Token> UnresolvedPronouns => Tokens
			.Where( t => t.NeedsResolution && string.IsNullOrEmpty( t.ResolvedEntity ) )
			.ToList();
	}
}
