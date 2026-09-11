using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// LLM/NLP brain for building NPCs. Connects to a local LLM server
	/// (LM Studio, Ollama, etc.) via OpenAI-compatible API. The LLM acts
	/// as the Architect and Critic — it receives text feedback from three
	/// critique sources and outputs structured JSON (<see cref="BuildingCritique"/>)
	/// that <see cref="BlueprintModifier"/> applies to the task queue.
	///
	/// The LLM never places individual blocks or generates geometry directly.
	/// It only produces small JSON payloads (~50-100 tokens) that modify
	/// pending tasks. The deterministic C# engine handles all geometry.
	///
	/// Critique sources:
	/// 1. Game Master (Director Rules) — global game state directives
	/// 2. Partner NPCs (Multi-Agent Sync) — spatial coordination
	/// 3. Devin AI (Dev/Validation Mode) — automated testing feedback
	/// </summary>
	public sealed class NPCBrain : Component
	{
		/// <summary> LLM server endpoint (OpenAI-compatible). Default: LM Studio. </summary>
		[Property] public string LlmEndpoint { get; set; } = "http://localhost:1234/v1/chat/completions";

		/// <summary> Model name to use. </summary>
		[Property] public string ModelName { get; set; } = "qwen3-vl-4b-instruct";

		/// <summary> Reference to the VillageBuilder whose task queue we modify. </summary>
		[Property] public VillageBuilder Builder { get; set; }

		/// <summary> Enable/disable LLM integration. When off, critiques are ignored. </summary>
		[Property] public bool BrainEnabled { get; set; } = true;

		/// <summary> Max tokens for LLM response. </summary>
		[Property] public int MaxTokens { get; set; } = 200;

		/// <summary> Temperature for LLM (lower = more deterministic). </summary>
		[Property] public float Temperature { get; set; } = 0.3f;

		private readonly Queue<BuildingCritique> _pendingCritiques = new();
		private readonly object _lock = new();

		/// <summary>
		/// Submit a critique from an external source. This is async — it
		/// sends the feedback to the LLM, parses the JSON response, and
		/// queues the resulting BuildingCritique for application on the
		/// main thread in OnUpdate.
		/// </summary>
		public async Task SubmitCritique( string criticSource, string feedbackText )
		{
			if ( !BrainEnabled || Builder == null )
			{
				Log.Info( $"[NPCBrain] Disabled or no builder — ignoring critique from {criticSource}." );
				return;
			}

			Log.Info( $"[NPCBrain] Received critique from {criticSource}: \"{feedbackText}\"" );

			try
			{
				var critique = await QueryLlm( criticSource, feedbackText );
				if ( critique != null )
				{
					critique.CriticSource = criticSource;
					lock ( _lock )
					{
						_pendingCritiques.Enqueue( critique );
					}
					Log.Info( $"[NPCBrain] Queued critique: {critique.Reason}" );
				}
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[NPCBrain] LLM query failed: {ex.Message}" );
			}
		}

		/// <summary>
		/// Apply pending critiques on the main thread. Called from OnUpdate.
		/// This is thread-safe — critiques are queued from async HTTP callbacks
		/// and applied here.
		/// </summary>
		protected override void OnUpdate()
		{
			if ( Builder == null || _pendingCritiques.Count == 0 )
				return;

			BuildingCritique critique;
			lock ( _lock )
			{
				if ( _pendingCritiques.Count == 0 )
					return;
				critique = _pendingCritiques.Dequeue();
			}

			Log.Info( $"[NPCBrain] Applying critique from {critique.CriticSource} to task queue." );
			BlueprintModifier.ApplyCritique( Builder.Tasks, critique );
		}

		/// <summary>
		/// Query the LLM with the feedback text and parse the JSON response.
		/// Uses S&Box's built-in Http API (OpenAI-compatible chat completions).
		/// </summary>
		async Task<BuildingCritique> QueryLlm( string criticSource, string feedbackText )
		{
			// Build the system prompt that forces JSON output
			var currentTask = Builder.CurrentTask;
			var taskInfo = currentTask != null
				? $"Current task: '{currentTask.Name}' ({currentTask.TaskType}), wealth={currentTask.WealthFactor:F1}, style={currentTask.Style?.Name ?? "none"}"
				: "No active task.";

			int pendingCount = Builder.Tasks?.Count( t => t.Status == 0 ) ?? 0;
			int doneCount = Builder.Tasks?.Count( t => t.Status == 2 ) ?? 0;

			var systemPrompt = $@"You are the architect brain for a building NPC in a medieval game.
Village status: {doneCount} tasks complete, {pendingCount} pending.
{taskInfo}

Critique from {criticSource}: ""{feedbackText}""

Respond strictly with valid JSON matching this schema (no other text):
{{
    ""Reason"": ""brief summary of the change"",
    ""WealthModifier"": 1.0,
    ""StyleTag"": ""Romanesque|Gothic|Classical|Vernacular|Fortress|"",
    ""AddModules"": [""Watchtower""],
    ""RemoveModules"": [],
    ""MaterialOverride"": """",
    ""TargetTaskIndex"": -1
}}";

			// Build the OpenAI-compatible request payload
			var requestBody = new
			{
				model = ModelName,
				messages = new[]
				{
					new { role = "user", content = systemPrompt }
				},
				temperature = Temperature,
				max_tokens = MaxTokens,
			};

			var jsonContent = Http.CreateJsonContent( requestBody );

			// Send request using S&Box Http API
			var responseString = await Http.RequestStringAsync(
				LlmEndpoint,
				"POST",
				jsonContent,
				headers: new Dictionary<string, string>
				{
					{ "Content-Type", "application/json" }
				} );

			if ( string.IsNullOrEmpty( responseString ) )
			{
				Log.Warning( "[NPCBrain] Empty response from LLM." );
				return null;
			}

			// Parse OpenAI chat completion response
			var responseJson = JsonSerializer.Deserialize<JsonElement>( responseString );
			var content = responseJson
				.GetProperty( "choices" )[0]
				.GetProperty( "message" )
				.GetProperty( "content" )
				.GetString();

			if ( string.IsNullOrEmpty( content ) )
			{
				Log.Warning( "[NPCBrain] Empty content in LLM response." );
				return null;
			}

			Log.Info( $"[NPCBrain] LLM response: {content}" );

			// Extract JSON from the response (LLM may wrap it in markdown)
			var jsonStr = ExtractJson( content );
			if ( jsonStr == null )
			{
				Log.Warning( $"[NPCBrain] Could not extract JSON from LLM response: {content}" );
				return null;
			}

			var critique = JsonSerializer.Deserialize<BuildingCritique>( jsonStr, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
			} );

			return critique;
		}

		/// <summary>
		/// Extract JSON object from LLM response text. Handles markdown
		/// code blocks and plain JSON.
		/// </summary>
		static string ExtractJson( string text )
		{
			text = text.Trim();

			// Try to find JSON in markdown code block
			var start = text.IndexOf( '{' );
			var end = text.LastIndexOf( '}' );

			if ( start >= 0 && end > start )
				return text.Substring( start, end - start + 1 );

			return null;
		}
	}

	/// <summary>
	/// Console commands for the village builder NPC brain system.
	/// These allow external tools (Devin, Game Master, other NPCs) to
	/// send critiques and control the village builder via the console.
	/// </summary>
	public static class VillageBrainCommands
	{
		/// <summary>
		/// Console command to send a critique to the village builder's NPCBrain.
		/// Usage: village_critique "DevinAI" "Reduce chapel wealth by 0.8x and add a Watchtower"
		/// Usage: village_critique "GameMaster" "A raid is coming! Change all pending buildings to Fortress style"
		/// Usage: village_critique "NPC_Alpha" "I'm extending my forge 200 units East. Remove your East wall."
		/// </summary>
		[ConCmd( "village_critique" )]
		public static async void VillageCritiqueCommand( string criticSource, string feedbackText )
		{
			var brain = Game.ActiveScene.GetAllComponents<NPCBrain>().FirstOrDefault();
			if ( brain == null )
			{
				Log.Warning( "[village_critique] No NPCBrain found in scene." );
				return;
			}

			if ( string.IsNullOrEmpty( criticSource ) || string.IsNullOrEmpty( feedbackText ) )
			{
				Log.Warning( "[village_critique] Usage: village_critique \"source\" \"feedback text\"" );
				return;
			}

			Log.Info( $"[village_critique] Sending critique from {criticSource}: {feedbackText}" );
			await brain.SubmitCritique( criticSource, feedbackText );
		}

		/// <summary>
		/// Console command to directly apply a style change to all pending tasks
		/// without going through the LLM. Useful for quick testing.
		/// Usage: village_style "Gothic"
		/// Usage: village_style "Fortress"
		/// </summary>
		[ConCmd( "village_style" )]
		public static void VillageStyleCommand( string styleTag )
		{
			var builder = Game.ActiveScene.GetAllComponents<VillageBuilder>().FirstOrDefault();
			if ( builder == null )
			{
				Log.Warning( "[village_style] No VillageBuilder found in scene." );
				return;
			}

			var style = styleTag.ToLowerInvariant() switch
			{
				"romanesque" => ArchitecturalStyle.Romanesque,
				"gothic" => ArchitecturalStyle.Gothic,
				"classical" => ArchitecturalStyle.Classical,
				"vernacular" => ArchitecturalStyle.Vernacular,
				"fortress" or "fortified" => ArchitecturalStyle.Fortress,
				_ => null,
			};

			if ( style == null )
			{
				Log.Warning( $"[village_style] Unknown style '{styleTag}'. Available: Romanesque, Gothic, Classical, Vernacular, Fortress" );
				return;
			}

			int changed = 0;
			foreach ( var task in builder.Tasks )
			{
				if ( task.Status == 0 )
				{
					task.Style = style;
					changed++;
				}
			}

			Log.Info( $"[village_style] Applied '{style.Name}' to {changed} pending tasks." );
		}

		/// <summary>
		/// Console command to show village status summary.
		/// Usage: village_status
		/// </summary>
		[ConCmd( "village_status" )]
		public static void VillageStatusCommand()
		{
			var builder = Game.ActiveScene.GetAllComponents<VillageBuilder>().FirstOrDefault();
			if ( builder == null )
			{
				Log.Warning( "[village_status] No VillageBuilder found in scene." );
				return;
			}

			int done = builder.Tasks.Count( t => t.Status == 2 );
			int inProgress = builder.Tasks.Count( t => t.Status == 1 );
			int pending = builder.Tasks.Count( t => t.Status == 0 );
			var current = builder.CurrentTask;

			Log.Info( $"[village_status] {done}/{builder.Tasks.Count} complete, {inProgress} in progress, {pending} pending. Current: {current?.Name ?? "none"}. Elapsed: {builder.ElapsedTime/60:F1} min." );

			var nextPending = builder.Tasks.Where( t => t.Status == 0 ).Take( 5 );
			Log.Info( "[village_status] Next pending tasks:" );
			foreach ( var t in nextPending )
			{
				var styleName = t.Style?.Name ?? "none";
				Log.Info( $"  - {t.Name} ({t.TaskType}) style={styleName} wealth={t.WealthFactor:F1}" );
			}
		}
	}
}
