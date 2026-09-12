namespace Lute.Crafting;

using Lute.Items;

/// <summary>
/// A crafting recipe. Defines inputs, output, and crafting time.
/// </summary>
public struct CraftRecipe
{
	public string Name;
	public ItemType OutputType;
	public int OutputCount;
	public int OutputDurability; // for tools
	public Dictionary<ItemType, int> Inputs;
	public float CraftTime; // seconds

	public bool CanCraft( LuteInventory inv )
	{
		foreach ( var kvp in Inputs )
		{
			if ( inv.CountItem( kvp.Key ) < kvp.Value )
				return false;
		}
		return true;
	}

	public bool ConsumeInputs( LuteInventory inv )
	{
		if ( !CanCraft( inv ) )
			return false;
		foreach ( var kvp in Inputs )
			inv.RemoveItem( kvp.Key, kvp.Value );
		return true;
	}
}

/// <summary>
/// Static recipe registry. All recipes in the game.
/// </summary>
public static class Recipes
{
	public static readonly Dictionary<string, CraftRecipe> All = new()
	{
		// Brick (crafted by Builders at a brick bench)
		// 2 clay + 1 straw = 1 brick
		["brick"] = new CraftRecipe
		{
			Name = "Brick",
			OutputType = ItemType.Brick,
			OutputCount = 1,
			Inputs = new() { { ItemType.Clay, 2 }, { ItemType.Straw, 1 } },
			CraftTime = 2f,
		},

		// Concrete (crafted by Smith at forge)
		// 3 stone + 1 water = 1 concrete
		["concrete"] = new CraftRecipe
		{
			Name = "Concrete",
			OutputType = ItemType.Concrete,
			OutputCount = 1,
			Inputs = new() { { ItemType.Stone, 3 }, { ItemType.Water, 1 } },
			CraftTime = 5f,
		},

		// Spade (crafted by Smith at forge)
		// 2 ore + 1 wood = 1 spade (300 durability)
		["spade"] = new CraftRecipe
		{
			Name = "Spade",
			OutputType = ItemType.Spade,
			OutputCount = 1,
			OutputDurability = 300,
			Inputs = new() { { ItemType.Ore, 2 }, { ItemType.Wood, 1 } },
			CraftTime = 10f,
		},

		// Pickaxe (crafted by Smith at forge)
		// 3 ore + 2 wood = 1 pickaxe (1000 durability)
		["pickaxe"] = new CraftRecipe
		{
			Name = "Pickaxe",
			OutputType = ItemType.Pickaxe,
			OutputCount = 1,
			OutputDurability = 1000,
			Inputs = new() { { ItemType.Ore, 3 }, { ItemType.Wood, 2 } },
			CraftTime = 15f,
		},

		// Shovel (crafted by Smith at forge)
		// 2 ore + 1 wood = 1 shovel (800 durability)
		["shovel"] = new CraftRecipe
		{
			Name = "Shovel",
			OutputType = ItemType.Shovel,
			OutputCount = 1,
			OutputDurability = 800,
			Inputs = new() { { ItemType.Ore, 2 }, { ItemType.Wood, 1 } },
			CraftTime = 10f,
		},

		// Trowel (crafted by Smith at forge)
		// 1 ore + 1 wood = 1 trowel (500 durability)
		["trowel"] = new CraftRecipe
		{
			Name = "Trowel",
			OutputType = ItemType.Trowel,
			OutputCount = 1,
			OutputDurability = 500,
			Inputs = new() { { ItemType.Ore, 1 }, { ItemType.Wood, 1 } },
			CraftTime = 8f,
		},

		// Storage Crate (crafted by Smith at forge)
		// 5 wood = 1 storage crate
		["storage_crate"] = new CraftRecipe
		{
			Name = "Storage Crate",
			OutputType = ItemType.StorageCrate,
			OutputCount = 1,
			Inputs = new() { { ItemType.Wood, 5 } },
			CraftTime = 12f,
		},
	};

	public static CraftRecipe? Get( string name )
	{
		return All.TryGetValue( name, out var r ) ? r : null;
	}
}

/// <summary>
/// Bench types. Each bench can craft specific recipes.
/// </summary>
public enum BenchType
{
	BrickBench,   // Builders craft bricks here
	Forge,        // Smith crafts tools, concrete, containers here
}

/// <summary>
/// Component for a crafting bench. NPCs interact with it to craft items.
/// Has a crafting queue with a timer.
/// </summary>
public sealed class CraftingBench : Component
{
	[Property] public BenchType Bench { get; set; } = BenchType.BrickBench;

	/// <summary> Who is currently using this bench. </summary>
	[Property] public GameObject CurrentUser { get; set; }

	/// <summary> Crafting queue: recipe names pending. </summary>
	public List<string> CraftQueue { get; private set; } = new();

	/// <summary> Current craft progress (0 to CraftTime). </summary>
	public float CraftProgress { get; private set; }

	/// <summary> Current recipe being crafted. </summary>
	private CraftRecipe? _currentRecipe;
	private LuteInventory _userInventory;

	/// <summary>
	/// Get the recipes available at this bench type.
	/// </summary>
	public List<string> GetAvailableRecipes()
	{
		return Bench switch
		{
			BenchType.BrickBench => new() { "brick" },
			BenchType.Forge => new() { "concrete", "spade", "pickaxe", "shovel", "trowel", "storage_crate" },
			_ => new(),
		};
	}

	/// <summary>
	/// Queue a recipe for crafting. The user's inventory must have inputs.
	/// </summary>
	public bool QueueCraft( string recipeName, LuteInventory inventory )
	{
		var recipe = Recipes.Get( recipeName );
		if ( !recipe.HasValue )
			return false;

		if ( !recipe.Value.CanCraft( inventory ) )
			return false;

		// Consume inputs immediately, queue the output
		recipe.Value.ConsumeInputs( inventory );
		CraftQueue.Add( recipeName );
		return true;
	}

	protected override void OnUpdate()
	{
		if ( CraftQueue.Count == 0 )
		{
			CraftProgress = 0;
			_currentRecipe = null;
			return;
		}

		// Start crafting the next item
		if ( !_currentRecipe.HasValue )
		{
			var recipeName = CraftQueue[0];
			_currentRecipe = Recipes.Get( recipeName );
			CraftProgress = 0;
		}

		if ( _currentRecipe.HasValue )
		{
			CraftProgress += Time.Delta;
			if ( CraftProgress >= _currentRecipe.Value.CraftTime )
			{
				// Craft complete — add to user inventory
				var recipe = _currentRecipe.Value;
				if ( _userInventory != null )
				{
					_userInventory.AddItem( recipe.OutputType, recipe.OutputCount, recipe.OutputDurability );
				}
				CraftQueue.RemoveAt( 0 );
				_currentRecipe = null;
				CraftProgress = 0;
			}
		}
	}

	/// <summary>
	/// Assign a user to this bench. Their inventory receives crafted items.
	/// </summary>
	public void SetUser( GameObject user, LuteInventory inventory )
	{
		CurrentUser = user;
		_userInventory = inventory;
	}

	/// <summary>
	/// Release the bench.
	/// </summary>
	public void Release()
	{
		CurrentUser = null;
		_userInventory = null;
	}
}
