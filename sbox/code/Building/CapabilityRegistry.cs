using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Deterministic capability profile for an NPC. A profession is a
	/// capability profile, not a different brain — every NPC uses the same
	/// agent kernel; capabilities determine what work an NPC is eligible
	/// for.
	///
	/// Per PR #6 §3: "Task eligibility must be derived from capabilities +
	/// tools + world state. Avoid logic such as: if (npc.Role ==
	/// "carpenter")". BeliefModel.Role may remain as descriptive/social
	/// information, but capabilities are authoritative for work
	/// eligibility.
	/// </summary>
	public enum NpcCapability
	{
		/// <summary> No capabilities (default for unprofiled NPCs). </summary>
		None = 0,
		/// <summary> Survey terrain, mark plots, validate placement. </summary>
		Survey = 1,
		/// <summary> Mark a build plot / anchor. </summary>
		MarkPlot = 2,
		/// <summary> Gather wood from tree sources. </summary>
		GatherWood = 3,
		/// <summary> Gather stone from quarry sources. </summary>
		GatherStone = 4,
		/// <summary> Gather ore from mine sources. </summary>
		GatherOre = 5,
		/// <summary> Gather clay/straw from deposits/fields. </summary>
		GatherClay = 6,
		/// <summary> Haul materials between locations. </summary>
		Haul = 7,
		/// <summary> Operate a sawmill / process timber. </summary>
		OperateSawmill = 8,
		/// <summary> General carpentry / wooden structures. </summary>
		Carpentry = 9,
		/// <summary> General masonry / brick + stone structures. </summary>
		Masonry = 10,
		/// <summary> Smithing / metal tools + fittings. </summary>
		Smithing = 11,
		/// <summary> Build wooden structures. </summary>
		BuildWood = 12,
		/// <summary> Build masonry structures (walls, foundations). </summary>
		BuildMasonry = 13,
		/// <summary> Repair tools. </summary>
		RepairTool = 14,
		/// <summary> Manage a stockpile / storage. </summary>
		ManageStockpile = 15,
		/// <summary> General construction (can build anything in the
		/// current village task list — the default for VillageBuilder
		/// NPCs that don't yet have a profession profile). </summary>
		GeneralConstruction = 16,
	}

	/// <summary>
	/// A profession definition: a deterministic capability profile plus
	/// preferred tools. Professions are data, not code — the same agent
	/// kernel runs every NPC; the profession just determines which
	/// capabilities and tools it brings.
	/// </summary>
	public sealed class ProfessionDefinition
	{
		/// <summary> Unique profession id (e.g. "mason", "carpenter"). </summary>
		public string Id { get; init; }

		/// <summary> Human-readable name. </summary>
		public string DisplayName { get; init; }

		/// <summary> Capabilities this profession provides, with skill
		/// levels (0.0–1.0). Higher = more proficient. </summary>
		public IReadOnlyDictionary<NpcCapability, float> Skills { get; init; }

		/// <summary> Tool item types this profession prefers (future —
		/// not yet enforced until the inventory system is connected). </summary>
		public IReadOnlySet<string> PreferredTools { get; init; }

		public ProfessionDefinition( string id, string displayName,
			Dictionary<NpcCapability, float> skills,
			HashSet<string> preferredTools = null )
		{
			Id = id;
			DisplayName = displayName;
			Skills = skills ?? new();
			PreferredTools = preferredTools ?? new HashSet<string>();
		}
	}

	/// <summary>
	/// Registry of profession definitions and the mapping from task types
	/// to required capabilities. This is the authoritative source for
	/// "can this NPC do this task?" — not role strings.
	/// </summary>
	public static class CapabilityRegistry
	{
		static readonly Dictionary<string, ProfessionDefinition> _professions = new();

		/// <summary>
		/// Maps VillageBuildTask.TaskType to the set of capabilities that
		/// can perform it. A builder with ANY of the listed capabilities
		/// is eligible. If a task type is not listed, GeneralConstruction
		/// is assumed (backward compatibility for existing village tasks).
		/// </summary>
		static readonly Dictionary<string, HashSet<NpcCapability>> _taskCapabilities = new()
		{
			{ "wall", new() { NpcCapability.BuildMasonry, NpcCapability.Masonry, NpcCapability.GeneralConstruction } },
			{ "gate", new() { NpcCapability.BuildMasonry, NpcCapability.Masonry, NpcCapability.GeneralConstruction } },
			{ "road", new() { NpcCapability.BuildMasonry, NpcCapability.GeneralConstruction } },
			{ "well", new() { NpcCapability.BuildMasonry, NpcCapability.Masonry, NpcCapability.GeneralConstruction } },
			{ "market_square", new() { NpcCapability.GeneralConstruction } },
			{ "cottage", new() { NpcCapability.BuildWood, NpcCapability.Carpentry, NpcCapability.GeneralConstruction } },
			{ "shop", new() { NpcCapability.BuildWood, NpcCapability.Carpentry, NpcCapability.GeneralConstruction } },
			{ "smithy", new() { NpcCapability.BuildMasonry, NpcCapability.BuildWood, NpcCapability.GeneralConstruction } },
			{ "tavern", new() { NpcCapability.BuildWood, NpcCapability.Carpentry, NpcCapability.GeneralConstruction } },
			{ "chapel", new() { NpcCapability.BuildMasonry, NpcCapability.GeneralConstruction } },
			{ "storage", new() { NpcCapability.BuildWood, NpcCapability.GeneralConstruction } },
			{ "guardhouse", new() { NpcCapability.BuildMasonry, NpcCapability.BuildWood, NpcCapability.GeneralConstruction } },
		};

		/// <summary>
		/// Register a profession definition.
		/// </summary>
		public static void Register( ProfessionDefinition prof )
		{
			if ( prof == null ) return;
			_professions[prof.Id] = prof;
		}

		/// <summary> Get a profession by id. </summary>
		public static ProfessionDefinition Get( string id ) =>
			!string.IsNullOrEmpty( id ) && _professions.TryGetValue( id, out var p ) ? p : null;

		/// <summary> All registered professions. </summary>
		public static List<ProfessionDefinition> All() => _professions.Values.ToList();

		/// <summary>
		/// Get the set of capabilities that can perform a task type.
		/// Returns {GeneralConstruction} for unknown task types (backward
		/// compatibility — existing village tasks remain assignable).
		/// </summary>
		public static HashSet<NpcCapability> CapabilitiesForTask( string taskType )
		{
			if ( !string.IsNullOrEmpty( taskType ) && _taskCapabilities.TryGetValue( taskType, out var caps ) )
				return caps;
			return new() { NpcCapability.GeneralConstruction };
		}

		/// <summary>
		/// Check if a builder with the given capabilities can perform a
		/// task type. A builder is eligible if it has ANY of the
		/// capabilities listed for that task type.
		/// </summary>
		public static bool CanPerformTask( HashSet<NpcCapability> builderCaps, string taskType )
		{
			if ( builderCaps == null || builderCaps.Count == 0 )
				return false;

			var taskCaps = CapabilitiesForTask( taskType );
			foreach ( var cap in builderCaps )
			{
				if ( taskCaps.Contains( cap ) )
					return true;
			}
			return false;
		}

		/// <summary>
		/// Initialize the built-in profession catalog. Called once at
		/// startup (from LuteGame.OnStart or JunctionResolverInit).
		/// </summary>
		public static void InitializeDefaults()
		{
			if ( _professions.Count > 0 ) return; // already initialized

			Register( new ProfessionDefinition( "surveyor", "Surveyor",
				new() { { NpcCapability.Survey, 1.0f }, { NpcCapability.MarkPlot, 1.0f } } ) );

			Register( new ProfessionDefinition( "quartermaster", "Quartermaster",
				new() { { NpcCapability.ManageStockpile, 1.0f } } ) );

			Register( new ProfessionDefinition( "lumberjack", "Lumberjack",
				new() { { NpcCapability.GatherWood, 1.0f }, { NpcCapability.OperateSawmill, 0.5f } },
				new() { "Axe" } ) );

			Register( new ProfessionDefinition( "quarryman", "Quarryman",
				new() { { NpcCapability.GatherStone, 1.0f }, { NpcCapability.GatherOre, 0.5f } },
				new() { "Pickaxe" } ) );

			Register( new ProfessionDefinition( "carpenter", "Carpenter",
				new() { { NpcCapability.Carpentry, 1.0f }, { NpcCapability.BuildWood, 1.0f }, { NpcCapability.RepairTool, 0.3f } },
				new() { "Hammer", "Saw" } ) );

			Register( new ProfessionDefinition( "mason", "Mason",
				new() { { NpcCapability.Masonry, 1.0f }, { NpcCapability.BuildMasonry, 1.0f } },
				new() { "Trowel", "Chisel" } ) );

			Register( new ProfessionDefinition( "hauler", "Hauler",
				new() { { NpcCapability.Haul, 1.0f } } ) );

			Register( new ProfessionDefinition( "blacksmith", "Blacksmith",
				new() { { NpcCapability.Smithing, 1.0f }, { NpcCapability.RepairTool, 1.0f } },
				new() { "Hammer", "Tongs" } ) );

			// General builder — the default for existing VillageBuilder NPCs
			// that don't yet have a profession profile. Can do anything in
			// the current village task list.
			Register( new ProfessionDefinition( "builder", "General Builder",
				new() { { NpcCapability.GeneralConstruction, 1.0f } } ) );

			Log.Info( $"Lute: CapabilityRegistry initialized — {_professions.Count} professions." );
		}

		/// <summary>
		/// Get the capabilities for a profession by id. Returns
		/// {GeneralConstruction} for unknown professions (backward
		/// compatibility).
		/// </summary>
		public static HashSet<NpcCapability> CapabilitiesForProfession( string professionId )
		{
			var prof = Get( professionId );
			if ( prof == null || prof.Skills == null || prof.Skills.Count == 0 )
				return new() { NpcCapability.GeneralConstruction };
			return new HashSet<NpcCapability>( prof.Skills.Keys );
		}

		/// <summary> Diagnostic summary. </summary>
		public static string Summary()
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine( $"Lute: CapabilityRegistry — {_professions.Count} professions" );
			foreach ( var prof in _professions.Values.OrderBy( p => p.Id ) )
			{
				var caps = string.Join( ", ", prof.Skills.Keys );
				sb.AppendLine( $"  {prof.Id} ({prof.DisplayName}): {caps}" );
			}
			return sb.ToString();
		}

		[ConCmd( "professions" )]
		static void ProfessionsCmd()
		{
			Log.Info( Summary() );
		}
	}
}
