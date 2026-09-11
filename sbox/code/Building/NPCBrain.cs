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

			// Multi-builder summary: show each builder's current task.
			var allBuilders = Game.ActiveScene.GetAllComponents<VillageBuilder>().ToList();
			if ( allBuilders.Count > 1 )
			{
				foreach ( var b in allBuilders )
				{
					var ct = b.CurrentTask;
					Log.Info( $"  [builder {b.BuilderId}/{b.TotalBuilders}] {ct?.Name ?? "idle"} ({ct?.TaskType ?? "-"}) at {ct?.Position}" );
				}
			}

			var nextPending = builder.Tasks.Where( t => t.Status == 0 ).Take( 5 );
			Log.Info( "[village_status] Next pending tasks:" );
			foreach ( var t in nextPending )
			{
				var styleName = t.Style?.Name ?? "none";
				Log.Info( $"  - {t.Name} ({t.TaskType}) style={styleName} wealth={t.WealthFactor:F1}" );
			}
		}

		/// <summary>
		/// Console command to show the spatial blackboard state.
		/// Usage: village_blackboard
		/// </summary>
		[ConCmd( "village_blackboard" )]
		public static void BlackboardStatusCommand()
		{
			Log.Info( $"[blackboard] {SpatialBlackboard.GetSummary()}" );

			// Show active NPCs and positions
			var positions = SpatialBlackboard.GetAllPositions();
			if ( positions.Count > 0 )
			{
				Log.Info( "[blackboard] Active NPCs:" );
				foreach ( var kvp in positions )
					Log.Info( $"  - {kvp.Key} at {kvp.Value}" );
			}

			// Show active claims
			var claims = SpatialBlackboard.GetClaims();
			if ( claims.Count > 0 )
			{
				Log.Info( "[blackboard] Active claims:" );
				foreach ( var c in claims )
					Log.Info( $"  - {c.Owner}: {c.Activity} at {c.Position} r={c.Radius:F0}" );
			}

			// Show recent messages
			var messages = SpatialBlackboard.GetAllMessages( SpatialBlackboard.CurrentTime - 60f );
			if ( messages.Count > 0 )
			{
				Log.Info( "[blackboard] Recent messages:" );
				foreach ( var m in messages )
					Log.Info( $"  - [{m.From}->{m.To}] {m.Type}: {m.Content}" );
			}
		}

		/// <summary>
		/// Console command to post a spatial message to the blackboard.
		/// Usage: village_say "VillageBuilderNPC" "NPC_Beta" "I'm building a wall at the north gate"
		/// </summary>
		[ConCmd( "village_say" )]
		public static void SayCommand( string fromNpc, string toNpc, string message )
		{
			SpatialBlackboard.PostMessage( fromNpc, toNpc, "manual", message );
			Log.Info( $"[village_say] {fromNpc} -> {toNpc}: {message}" );
		}

		/// <summary>
		/// Console command to show the ConstructionDirector state: task
		/// counts, per-builder assignments, blocked/failed tasks, and
		/// dependency status.
		/// Usage: director_status
		/// </summary>
		[ConCmd( "director_status" )]
		public static void DirectorStatusCommand()
		{
			Log.Info( $"[director_status] {ConstructionDirector.StatusSummary()}" );
		}

		/// <summary>
		/// Console command to export a village task's blueprint to JSON.
		/// Generates the blueprint (via StyleGrammar or BuildingGrammar),
		/// validates it, and saves to FileSystem.Data.
		/// Usage: blueprint_export "Chapel"     (by task name)
		/// Usage: blueprint_export "#5"         (by task index)
		/// </summary>
		[ConCmd( "blueprint_export" )]
		public static void BlueprintExportCommand( string taskIdentifier )
		{
			var builder = Game.ActiveScene.GetAllComponents<VillageBuilder>().FirstOrDefault();
			if ( builder == null )
			{
				Log.Warning( "[blueprint_export] No VillageBuilder found in scene." );
				return;
			}

			// Find the task by name or index
			VillageBuildTask task = null;
			if ( taskIdentifier.StartsWith( "#" ) && int.TryParse( taskIdentifier.Substring( 1 ), out int idx ) )
			{
				if ( idx >= 0 && idx < builder.Tasks.Count )
					task = builder.Tasks[idx];
			}
			else
			{
				task = builder.Tasks.FirstOrDefault( t => t.Name.Equals( taskIdentifier, StringComparison.OrdinalIgnoreCase ) );
			}

			if ( task == null )
			{
				Log.Warning( $"[blueprint_export] Task '{taskIdentifier}' not found." );
				return;
			}

			// Generate the blueprint
			var rng = task.LayoutSeed > 0 ? new Random( task.LayoutSeed ) : new Random();
			Blueprint bp;
			if ( task.Style is not null )
			{
				var styleGrammar = new StyleGrammar( task.Style, rng );
				bp = styleGrammar.Generate( task.Position, task.Rotation,
					task.BaseWidth, task.BaseHeight, task.WealthFactor,
					builder.CellSize, builder.WallHeight, builder.FloorThickness );
			}
			else
			{
				var grammar = new BuildingGrammar( rng );
				var layout = grammar.GenerateLayout( task.BaseWidth, task.BaseHeight, task.WealthFactor );
				bp = Blueprint.FromGridLayout( layout, task.Position, task.Rotation,
					builder.CellSize, builder.WallHeight, builder.FloorThickness,
					builder.BuildingWallMaterial, builder.BuildingFloorMaterial );
			}

			bp.Name = task.Name;

			// Validate
			BlueprintValidator.ValidateAndLog( bp, task.Name );

			// Save to file
			string filename = $"blueprints/{task.Name}.json";
			bp.SaveToFile( filename );

			// Also log a summary
			Log.Info( $"[blueprint_export] Task: {task.Name} ({task.TaskType}), style: {task.Style?.Name ?? "none"}, pieces: {bp.PieceCount}" );
			Log.Info( $"[blueprint_export] Saved to {filename}. Use blueprint_import to load and inspect." );
		}

		/// <summary>
		/// Console command to import a blueprint from JSON and log its contents.
		/// Usage: blueprint_import "blueprints/Chapel.json"
		/// </summary>
		[ConCmd( "blueprint_import" )]
		public static void BlueprintImportCommand( string filename )
		{
			var bp = Blueprint.LoadFromFile( filename );
			if ( bp == null )
				return;

			Log.Info( $"[blueprint_import] Loaded: {bp.Name}, origin: {bp.Origin}, rotation: {bp.Rotation}" );
			Log.Info( $"[blueprint_import] Pieces: {bp.PieceCount}, cellSize: {bp.CellSize}, wallHeight: {bp.WallHeight}" );

			// Log piece type distribution
			var byType = bp.Pieces.GroupBy( p => p.PieceType ).OrderByDescending( g => g.Count() );
			foreach ( var g in byType )
				Log.Info( $"  {g.Key}: {g.Count()} pieces" );

			// Validate the imported blueprint
			BlueprintValidator.ValidateAndLog( bp, "imported" );
		}

		/// <summary>
		/// Console command to generate and export a monument blueprint.
		/// Usage: monument_export "StPetersBasilica"
		/// </summary>
		[ConCmd( "monument_export" )]
		public static void MonumentExportCommand( string monumentName )
		{
			MonumentMassing massing = null;

			switch ( monumentName.ToLowerInvariant() )
			{
				case "stpeters" or "stpetersbasilica":
					massing = MonumentBlueprintProducer.StPetersBasilica( Vector3.Zero );
					break;
				default:
					Log.Warning( $"[monument_export] Unknown monument '{monumentName}'. Available: StPetersBasilica" );
					return;
			}

			var bp = MonumentBlueprintProducer.Generate( massing );

			// Validate
			BlueprintValidator.ValidateAndLog( bp, massing.Name );

			// Save to file
			string filename = $"blueprints/monument_{massing.Name}.json";
			bp.SaveToFile( filename );

			// Log volume breakdown
			Log.Info( $"[monument_export] Monument: {massing.Name}, volumes: {massing.Volumes.Count}, total pieces: {bp.PieceCount}" );
			foreach ( var vol in massing.Volumes )
			{
				var volPieces = bp.Pieces.Count( p => true ); // total; per-volume count would need tagging
				Log.Info( $"  {vol.Name}: {vol.WidthCells}x{vol.DepthCells}x{vol.Stories} ({vol.Type}, {vol.RoofType})" );
			}
			Log.Info( $"[monument_export] Saved to {filename}." );
		}

		/// <summary>
		/// Console command to generate a monument via the architectural
		/// grammar path (massing -> element tree -> detail -> Blueprint)
		/// and export it. This is the Phase 4 path: instead of jumping
		/// straight from volumes to pieces, decompose into bays,
		/// columns, arches, windows, drum/rings/lantern first.
		/// Usage: monument_grammar "StPetersBasilica"
		/// </summary>
		[ConCmd( "monument_grammar" )]
		public static void MonumentGrammarCommand( string monumentName )
		{
			MonumentMassing massing = null;

			switch ( monumentName.ToLowerInvariant() )
			{
				case "stpeters" or "stpetersbasilica":
					massing = MonumentBlueprintProducer.StPetersBasilica( Vector3.Zero );
					break;
				default:
					Log.Warning( $"[monument_grammar] Unknown monument '{monumentName}'. Available: StPetersBasilica" );
					return;
			}

			// Decompose into architectural element tree
			var trees = MonumentGrammar.Decompose( massing );
			int totalElements = 0;
			foreach ( var tree in trees )
				totalElements += CountElements( tree );

			Log.Info( $"[monument_grammar] {massing.Name}: {trees.Count} volume(s), {totalElements} architectural elements." );
			foreach ( var tree in trees )
				Log.Info( $"  - {tree.Name} ({tree.Kind}): {tree.Children.Count} children" );

			// Generate the blueprint from the grammar path
			var bp = MonumentBlueprintProducer.GenerateFromGrammar( massing );

			// Validate
			BlueprintValidator.ValidateAndLog( bp, massing.Name );

			// Save
			string filename = $"blueprints/monument_{massing.Name}_grammar.json";
			bp.SaveToFile( filename );
			Log.Info( $"[monument_grammar] Pieces: {bp.PieceCount}, hash: {bp.Hash[..8]}, saved to {filename}." );
		}

		/// <summary>
		/// Console command to show the AuthorityPipeline state: recent
		/// AI proposals and their validation/approval/execution status.
		/// Usage: authority_status
		/// </summary>
		[ConCmd( "authority_status" )]
		public static void AuthorityStatusCommand()
		{
			Log.Info( $"[authority_status] {AuthorityPipeline.Summary( 20 )}" );
		}

		/// <summary>
		/// Console command to list all registered blueprint versions in
		/// the <see cref="BlueprintRegistry"/>. Shows id, versions,
		/// piece count, and hash prefix for each.
		/// Usage: blueprint_versions
		/// </summary>
		[ConCmd( "blueprint_versions" )]
		public static void BlueprintVersionsCommand()
		{
			var ids = BlueprintRegistry.Ids();
			if ( ids.Count == 0 )
			{
				Log.Info( "[blueprint_versions] No blueprints registered." );
				return;
			}

			Log.Info( $"[blueprint_versions] {ids.Count} blueprint id(s) registered:" );
			foreach ( var id in ids )
			{
				var versions = BlueprintRegistry.Versions( id );
				var latest = BlueprintRegistry.GetLatest( id );
				Log.Info( $"  {id}: v{string.Join( ",", versions )} — latest v{latest.Version} ({latest.PieceCount} pieces, hash={latest.Hash?[..8] ?? "none"})" );
			}
		}

		static int CountElements( ArchitecturalElement el )
		{
			int n = 1;
			foreach ( var c in el.Children )
				n += CountElements( c );
			return n;
		}
	}
}
