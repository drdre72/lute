using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.NLP
{
	/// <summary>
	/// NLP round-trip property test, per professor's Gate 0 hardening notes.
	///
	/// Property: for construction coordination intents,
	/// <c>NlpParser.Parse(SpeechGenerator.Generate(intent))</c> should
	/// preserve the <see cref="IntentType"/> (and ideally the subject).
	/// If the round-trip degrades a RequestResource into a generic
	/// Request, or a RequestHelp into a generic Request, the specific
	/// intent is lost and the coordination protocol is unreliable.
	///
	/// This is a runtime test invoked via the <c>nlp_roundtrip</c>
	/// console command. It logs PASS/FAIL per case and a summary. It
	/// does not use xUnit (the project has no test framework) — it is
	/// a deterministic self-check that can be run in the editor or play
	/// mode.
	/// </summary>
	public static class NlpRoundTripTest
	{
		[ConCmd( "nlp_roundtrip" )]
		static void RunCmd()
		{
			Run();
		}

		/// <summary>
		/// Run the round-trip test suite. Returns the number of failures.
		/// Logs each case and a summary.
		/// </summary>
		public static int Run()
		{
			var cases = BuildCases();
			int pass = 0, fail = 0;

			Log.Info( "Lute: NLP Round-Trip Test — starting " + cases.Count + " cases" );

			foreach ( var c in cases )
			{
				// Generate text from the intent
				var text = SpeechGenerator.Generate( c.Intent );
				// Parse it back
				var parsed = NlpParser.Parse( text, sender: "tester", target: "other" );

				bool typeOk = parsed.Type == c.Intent.Type;
				// Subject preservation is best-effort — the generator may
				// phrase it differently. We check that the subject keyword
				// appears in the parsed subject OR the original text.
				bool subjectOk = string.IsNullOrEmpty( c.Intent.Subject ) ||
					parsed.Subject.Contains( c.Intent.Subject ) ||
					text.ToLowerInvariant().Contains( c.Intent.Subject.ToLowerInvariant() );

				bool ok = typeOk && subjectOk;
				if ( ok ) pass++;
				else fail++;

				Log.Info( $"  {(ok ? "PASS" : "FAIL")} {c.Label}: {c.Intent.Type}({c.Intent.Topic}) '{c.Intent.Subject}'" +
					$" -> \"{text}\" -> {parsed.Type}({parsed.Topic}) '{parsed.Subject}'" +
					( ok ? "" : $" [expected {c.Intent.Type}, got {parsed.Type}]" ) );
			}

			Log.Info( $"Lute: NLP Round-Trip Test — {pass} passed, {fail} failed, {cases.Count} total" );
			return fail;
		}

		static List<TestCase> BuildCases()
		{
			var cases = new List<TestCase>();

			// ── Core coordination intents ──
			cases.Add( new( "RequestResource-Brick",
				new Intent { Type = IntentType.RequestResource, Topic = IntentTopic.Material,
					Subject = "brick", Parameters = new() { ["amount"] = "20" } } ) );

			cases.Add( new( "RequestResource-Wood",
				new Intent { Type = IntentType.RequestResource, Topic = IntentTopic.Material,
					Subject = "wood", Parameters = new() { ["amount"] = "10" } } ) );

			cases.Add( new( "OfferResource-Plank",
				new Intent { Type = IntentType.OfferResource, Topic = IntentTopic.Material,
					Subject = "plank", Parameters = new() { ["amount"] = "5" } } ) );

			cases.Add( new( "ClaimResource-Stone",
				new Intent { Type = IntentType.ClaimResource, Topic = IntentTopic.Material,
					Subject = "stone" } ) );

			cases.Add( new( "ReleaseResource-Brick",
				new Intent { Type = IntentType.ReleaseResource, Topic = IntentTopic.Material,
					Subject = "brick" } ) );

			cases.Add( new( "RequestHelp-Wall",
				new Intent { Type = IntentType.RequestHelp, Topic = IntentTopic.Task,
					Subject = "wall" } ) );

			cases.Add( new( "OfferHelp-Gate",
				new Intent { Type = IntentType.OfferHelp, Topic = IntentTopic.Task,
					Subject = "gate" } ) );

			cases.Add( new( "AssignTask-Cottage",
				new Intent { Type = IntentType.AssignTask, Topic = IntentTopic.Task,
					Subject = "cottage" } ) );

			cases.Add( new( "AcceptTask-Wall",
				new Intent { Type = IntentType.AcceptTask, Topic = IntentTopic.Task,
					Subject = "wall" } ) );

			cases.Add( new( "RequestTask",
				new Intent { Type = IntentType.RequestTask, Topic = IntentTopic.Task,
					Subject = "" } ) );

			cases.Add( new( "ReportCompletion-Wall",
				new Intent { Type = IntentType.ReportCompletion, Topic = IntentTopic.Task,
					Subject = "wall" } ) );

			cases.Add( new( "ReportProblem-Safety",
				new Intent { Type = IntentType.ReportProblem, Topic = IntentTopic.Safety,
					Subject = "collapse" } ) );

			// ── Social intents (round-trip not required to be exact,
			// but type should be stable for greetings/farewells) ──
			cases.Add( new( "Greet",
				new Intent { Type = IntentType.Greet, Topic = IntentTopic.None,
					Subject = "" } ) );

			cases.Add( new( "Farewell",
				new Intent { Type = IntentType.Farewell, Topic = IntentTopic.None,
					Subject = "" } ) );

			// ── Claim/Release site ──
			cases.Add( new( "Claim-NorthSite",
				new Intent { Type = IntentType.Claim, Topic = IntentTopic.Site,
					Subject = "north_site" } ) );

			cases.Add( new( "Release-NorthSite",
				new Intent { Type = IntentType.Release, Topic = IntentTopic.Site,
					Subject = "north_site" } ) );

			return cases;
		}

		sealed class TestCase
		{
			public string Label { get; init; }
			public Intent Intent { get; init; }
			public TestCase( string label, Intent intent ) { Label = label; Intent = intent; }
		}
	}
}
