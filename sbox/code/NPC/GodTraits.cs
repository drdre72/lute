namespace Lute.NPC;

/// <summary>
/// Greek/Roman gods that NPCs draw their backstory and powers from.
/// Each god grants specific traits and skills to the NPC.
/// </summary>
public enum GodPatron
{
	None,
	Hephaestus,  // Smith god — crafting, forging, tool-making
	Demeter,     // Harvest goddess — farming, gathering, fertility
	Athena,      // Wisdom/crafting goddess — building, strategy, masonry
}

/// <summary>
/// NPC role in the village. Determines behavior, tools, and workflow.
/// </summary>
public enum NpcRole
{
	None,
	Builder,    // Athena — builds walls, crafts bricks, holds own tools
	Farmer,      // Demeter — gathers raw materials from farming nodes
	Smith,       // Hephaestus — crafts tools, containers, concrete
}

/// <summary>
/// God-given traits that modify NPC capabilities.
/// NPCs are "maxed out" with dev privileges but tools gate skill usage.
/// </summary>
public static class GodTraits
{
	/// <summary>
	/// Get the display name for a god patron.
	/// </summary>
	public static string GetGodName( GodPatron god )
	{
		return god switch
		{
			GodPatron.Hephaestus => "Hephaestus",
			GodPatron.Demeter => "Demeter",
			GodPatron.Athena => "Athena",
			_ => "Mortal",
		};
	}

	/// <summary>
	/// Get the god's domain/title.
	/// </summary>
	public static string GetGodTitle( GodPatron god )
	{
		return god switch
		{
			GodPatron.Hephaestus => "God of the Forge",
			GodPatron.Demeter => "Goddess of the Harvest",
			GodPatron.Athena => "Goddess of Wisdom and Craft",
			_ => "",
		};
	}

	/// <summary>
	/// Get the god's blessing description.
	/// </summary>
	public static string GetGodBlessing( GodPatron god )
	{
		return god switch
		{
			GodPatron.Hephaestus => "Forge mastery — crafts tools and containers with divine precision. Smithing queue is blessed with efficiency.",
			GodPatron.Demeter => "Harvest blessing — gathers raw materials with abundance. Farming nodes respawn swiftly under her watch.",
			GodPatron.Athena => "Builder's wisdom — constructs with perfect geometry. Crafts bricks and places them with strategic insight.",
			_ => "",
		};
	}

	/// <summary>
	/// Get the default role for a god patron.
	/// </summary>
	public static NpcRole GetDefaultRole( GodPatron god )
	{
		return god switch
		{
			GodPatron.Hephaestus => NpcRole.Smith,
			GodPatron.Demeter => NpcRole.Farmer,
			GodPatron.Athena => NpcRole.Builder,
			_ => NpcRole.None,
		};
	}

	/// <summary>
	/// Get the starting tools for a role.
	/// </summary>
	public static List<Items.ItemType> GetStartingTools( NpcRole role )
	{
		return role switch
		{
			NpcRole.Builder => new() { Items.ItemType.Spade, Items.ItemType.Trowel },
			NpcRole.Farmer => new() { Items.ItemType.Pickaxe, Items.ItemType.Shovel },
			NpcRole.Smith => new() { Items.ItemType.Pickaxe }, // smith has a pickaxe for ore
			_ => new(),
		};
	}

	/// <summary>
	/// Tool durability for each role's primary tool.
	/// Spade: 300 bricks before breaking (as specified).
	/// </summary>
	public static int GetToolDurability( Items.ItemType tool )
	{
		return tool switch
		{
			Items.ItemType.Spade => 300,    // 300 bricks placed
			Items.ItemType.Trowel => 500,   // 500 mortar applications
			Items.ItemType.Pickaxe => 1000, // 1000 mining hits
			Items.ItemType.Shovel => 800,   // 800 digging actions
			_ => 0,
		};
	}
}
