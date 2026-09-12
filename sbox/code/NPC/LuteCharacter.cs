namespace Lute.NPC;

using Lute.Items;
using Lute.Crafting;
using Lute.Farming;

/// <summary>
/// Core character component for Lute NPCs.
/// Combines god-backstory, role, inventory, and tool management.
/// Builders are also crafters — they hold their own tools.
/// Farmers are dedicated gatherers.
/// Smiths craft tools and containers at a forge.
/// </summary>
public sealed class LuteCharacter : Component
{
	/// <summary> Which god this NPC is blessed by. </summary>
	[Property] public GodPatron God { get; set; } = GodPatron.Athena;

	/// <summary> This NPC's role in the village. </summary>
	[Property] public NpcRole Role { get; set; } = NpcRole.Builder;

	/// <summary> NPC display name. </summary>
	[Property] public string CharacterName { get; set; } = "Villager";

	/// <summary> This NPC's inventory. </summary>
	public LuteInventory Inventory { get; private set; }

	/// <summary> Currently equipped tool (from hotbar). </summary>
	public ItemType EquippedTool => Inventory?.GetSelectedHotbarItem().Type ?? ItemType.Clay;

	/// <summary> Is this NPC currently crafting at a bench? </summary>
	public bool IsCrafting { get; private set; }

	/// <summary> The bench this NPC is using (if any). </summary>
	public CraftingBench CurrentBench { get; private set; }

	protected override void OnStart()
	{
		Inventory = Components.GetOrCreate<LuteInventory>();

		// Assign default role from god if not set
		if ( Role == NpcRole.None )
			Role = GodTraits.GetDefaultRole( God );

		// Give starting tools (dev privileges — maxed out)
		GiveStartingTools();

		Log.Info( $"Lute: {CharacterName} awakened under {GodTraits.GetGodName( God )} — {GodTraits.GetGodTitle( God )}. Role: {Role}. Blessing: {GodTraits.GetGodBlessing( God )}" );
	}

	/// <summary>
	/// Give the NPC their starting tools based on role.
	/// Tools are "maxed out" with dev privileges.
	/// </summary>
	void GiveStartingTools()
	{
		var tools = GodTraits.GetStartingTools( Role );
		foreach ( var tool in tools )
		{
			int durability = GodTraits.GetToolDurability( tool );
			Inventory.AddItem( tool, 1, durability );
		}

		// Give builders some starting materials for crafting bricks
		if ( Role == NpcRole.Builder )
		{
			Inventory.AddItem( ItemType.Clay, 50 );
			Inventory.AddItem( ItemType.Straw, 25 );
		}

		// Give farmers some starting storage
		if ( Role == NpcRole.Farmer )
		{
			// Farmers start empty — they gather
		}
	}

	/// <summary>
	/// Equip a tool to the selected hotbar slot.
	/// </summary>
	public bool EquipTool( ItemType toolType )
	{
		int slot = Inventory.FindTool( toolType );
		if ( slot < 0 )
			return false;

		// Put it in the selected hotbar slot
		Inventory.Hotbar[Inventory.SelectedHotbarSlot] = slot;
		return true;
	}

	/// <summary>
	/// Use the currently equipped tool. Returns false if broken or no tool.
	/// </summary>
	public bool UseEquippedTool()
	{
		return Inventory.UseSelectedTool();
	}

	/// <summary>
	/// Gather from a resource node. Must have the right tool equipped.
	/// </summary>
	public int GatherFrom( ResourceNode node )
	{
		if ( !node.IsAvailable )
			return 0;

		var requiredTool = node.GetRequiredTool();
		if ( requiredTool != ItemType.Clay )
		{
			int toolSlot = Inventory.FindTool( requiredTool );
			if ( toolSlot < 0 )
			{
				Log.Info( $"Lute: {CharacterName} cannot gather — needs {ItemDefs.GetDisplayName( requiredTool )}" );
				return 0;
			}
		}

		int gathered = node.Gather( Inventory );
		if ( gathered > 0 )
			Log.Info( $"Lute: {CharacterName} gathered {gathered} {ItemDefs.GetDisplayName( node.GetYieldType() )}" );
		return gathered;
	}

	/// <summary>
	/// Craft an item at a bench. Returns true if queued successfully.
	/// </summary>
	public bool CraftAt( CraftingBench bench, string recipeName )
	{
		if ( !bench.GetAvailableRecipes().Contains( recipeName ) )
			return false;

		var recipe = Recipes.Get( recipeName );
		if ( !recipe.HasValue )
			return false;

		if ( !recipe.Value.CanCraft( Inventory ) )
		{
			Log.Info( $"Lute: {CharacterName} cannot craft {recipeName} — missing materials" );
			return false;
		}

		bench.SetUser( GameObject, Inventory );
		bool queued = bench.QueueCraft( recipeName, Inventory );
		if ( queued )
		{
			IsCrafting = true;
			CurrentBench = bench;
			Log.Info( $"Lute: {CharacterName} started crafting {recipeName} at {bench.Bench}" );
		}
		return queued;
	}

	/// <summary>
	/// Place a brick during construction. Uses the spade's durability.
	/// Returns true if the brick was placed (spade not broken).
	/// </summary>
	public bool PlaceBrick()
	{
		if ( !Inventory.HasTool( ItemType.Spade ) )
		{
			Log.Warning( $"Lute: {CharacterName} has no working spade — cannot place bricks!" );
			return false;
		}

		if ( Inventory.CountItem( ItemType.Brick ) <= 0 )
		{
			Log.Warning( $"Lute: {CharacterName} has no bricks — must craft more!" );
			return false;
		}

		// Use the spade
		int spadeSlot = Inventory.FindTool( ItemType.Spade );
		bool spadeOk = Inventory.Slots[spadeSlot].UseTool();

		// Consume a brick
		Inventory.RemoveItem( ItemType.Brick, 1 );

		if ( !spadeOk )
		{
			Log.Info( $"Lute: {CharacterName}'s spade broke after placing bricks! Needs a new one from the Smith." );
			return false;
		}

		return true;
	}

	/// <summary>
	/// Get a status string for UI display.
	/// </summary>
	public string GetStatus()
	{
		var god = GodTraits.GetGodName( God );
		var role = Role.ToString();
		var tool = EquippedTool != ItemType.Clay ? ItemDefs.GetDisplayName( EquippedTool ) : "empty";
		return $"{CharacterName} [{role}] — Blessed by {god}. Holding: {tool}";
	}
}
