using System;
using System.Linq;
using Sandbox;
using Lute.Crafting;
using Lute.Items;

namespace Lute.Building
{
	/// <summary>
	/// Physical crafter NPC controller. Implements the Gate 2e crafter
	/// work loop as a finite-state machine:
	///
	/// <code>
	/// Idle
	///   ↓ find available workstation (capability-checked, exclusive reserve)
	/// WalkingToBench
	///   ↓ arrive at bench
	/// FetchingInputs
	///   ↓ walk to stockpile with recipe inputs
	///   ↓ withdraw inputs into NPC LuteInventory
	/// WalkingToBench (with inputs)
	///   ↓ arrive at bench
	/// Crafting
	///   ↓ queue craft, wait for timer
	///   ↓ output lands in NPC LuteInventory
	/// DepositingOutput
	///   ↓ walk to nearest stockpile
	///   ↓ deposit crafted output
	/// Done → release bench → Idle (loop)
	/// </code>
	///
	/// No material teleports. The crafter physically carries inputs
	/// from the stockpile to the bench and outputs back to a stockpile.
	/// Uses the same movement pattern as HaulerController.
	/// </summary>
	public sealed class CrafterController : Component
	{
		[RequireComponent] public PlayerController Controller { get; set; }
		[RequireComponent] public NavMeshAgent NavAgent { get; set; }

		[Property] public LuteInventory Inventory { get; set; }

		[Property] public float WalkSpeed { get; set; } = 5f * 39.37f;

		[Property] public float StopRadius { get; set; } = 80f;

		[Property] public string NpcName { get; set; } = "Crafter";

		[Property] public bool UseNavMesh { get; set; } = true;

		/// <summary>
		/// Which bench type this crafter works at. Determines which
		/// profession to register as and which recipes to craft.
		/// </summary>
		[Property] public BenchType PreferredBench { get; set; } = BenchType.Sawmill;

		/// <summary>
		/// Which recipe to craft. If empty, crafts the first available
		/// recipe for the bench type.
		/// </summary>
		[Property] public string RecipeName { get; set; } = "";

		/// <summary> Crafter state machine. </summary>
		public enum CrafterState
		{
			Idle,
			WalkingToBench,
			FetchingInputs,
			WalkingToBenchWithInputs,
			Crafting,
			DepositingOutput,
			Done,
		}

		[Property] public CrafterState State { get; set; } = CrafterState.Idle;

		int _builderId = -1;
		WorkstationRegistry.Workstation _station;
		string _recipeName;
		CraftRecipe? _recipe;
		bool _craftQueued;
		float _stateTimer;
		float _logTimer;
		bool _usingNavMesh;
		bool _navMeshDiagLogged;

		/// <summary>
		/// Profession id for each bench type.
		/// </summary>
		static string ProfessionForBench( BenchType type ) => type switch
		{
			BenchType.BrickBench => "mason",
			BenchType.Forge => "blacksmith",
			BenchType.Sawmill => "carpenter",
			BenchType.Smelter => "blacksmith",
			_ => "builder",
		};

		protected override void OnStart()
		{
			Log.Info( $"Lute: CrafterController '{NpcName}' started at {WorldPosition} (bench={PreferredBench})." );

			Inventory ??= Components.Get<LuteInventory>();
			if ( Inventory is null )
				Inventory = Components.Create<LuteInventory>();

			_builderId = -1 - Math.Abs( GameObject.Id.GetHashCode() );
			var prof = ProfessionForBench( PreferredBench );
			ConstructionDirector.RegisterBuilder( _builderId, NpcName, professionId: prof );
			Log.Info( $"Lute: CrafterController '{NpcName}' registered as '{prof}' (builderId={_builderId})." );
		}

		protected override void OnUpdate()
		{
			_logTimer += Time.Delta;

			switch ( State )
			{
				case CrafterState.Idle:
				{
					_stateTimer += Time.Delta;
					if ( _stateTimer > 0.5f )
					{
						_stateTimer = 0f;
						TryReserveBench();
					}
					break;
				}

				case CrafterState.WalkingToBench:
				{
					var benchPos = SafeStationPosition();
					if ( benchPos is null )
					{
						ResetToIdle();
						break;
					}
					SteerToward( benchPos.Value );
					if ( WithinStopRadius( benchPos.Value ) )
					{
						Stop();
						State = CrafterState.FetchingInputs;
						_stateTimer = 0f;
						Log.Info( $"Lute: Crafter '{NpcName}' reached bench {_station?.Id}." );
					}
					else
					{
						_stateTimer += Time.Delta;
						if ( _stateTimer > 10f )
						{
							WorldPosition = benchPos.Value;
							Stop();
							State = CrafterState.FetchingInputs;
							_stateTimer = 0f;
							Log.Info( $"Lute: Crafter '{NpcName}' warped to bench {_station?.Id} (was stuck)." );
						}
					}
					break;
				}

				case CrafterState.FetchingInputs:
				{
					DoFetchInputs();
					break;
				}

				case CrafterState.WalkingToBenchWithInputs:
				{
					var benchPos = SafeStationPosition();
					if ( benchPos is null )
					{
						ResetToIdle();
						break;
					}
					SteerToward( benchPos.Value );
					if ( WithinStopRadius( benchPos.Value ) )
					{
						Stop();
						State = CrafterState.Crafting;
						_stateTimer = 0f;
						Log.Info( $"Lute: Crafter '{NpcName}' back at bench {_station.Id} with inputs → Crafting." );
					}
					break;
				}

				case CrafterState.Crafting:
				{
					DoCraft();
					break;
				}

				case CrafterState.DepositingOutput:
				{
					DoDepositOutput();
					break;
				}

				case CrafterState.Done:
				{
					ReleaseBench();
					State = CrafterState.Idle;
					_stateTimer = 0f;
					break;
				}
			}
		}

		// ── State transitions ──

		void TryReserveBench()
		{
			// Determine recipe to craft.
			if ( string.IsNullOrEmpty( _recipeName ) )
				_recipeName = !string.IsNullOrEmpty( RecipeName )
					? RecipeName
					: _station?.Bench?.GetAvailableRecipes().FirstOrDefault() ?? "";

			if ( string.IsNullOrEmpty( _recipeName ) )
			{
				// Pick the first available recipe for our bench type.
				List<string> dummyRecipes = PreferredBench switch
				{
					BenchType.BrickBench => new() { "brick" },
					BenchType.Forge => new() { "concrete" },
					BenchType.Sawmill => new() { "plank" },
					BenchType.Smelter => new() { "ingot" },
					_ => new(),
				};
				_recipeName = dummyRecipes.FirstOrDefault() ?? "";
			}

			_recipe = Recipes.Get( _recipeName );
			if ( !_recipe.HasValue )
			{
				Log.Warning( $"Lute: Crafter '{NpcName}' recipe not found: {_recipeName}" );
				return;
			}

			var ws = WorkstationRegistry.FindAvailable( PreferredBench, NpcName, WorldPosition );
			if ( ws == null )
				return;

			var (ok, detail) = WorkstationRegistry.Reserve( ws.Id, NpcName );
			if ( !ok )
			{
				Log.Warning( $"Lute: Crafter '{NpcName}' reserve failed: {detail}" );
				return;
			}

			_station = ws;
			Log.Info( $"Lute: Crafter '{NpcName}' reserved bench {ws.Id} ({ws.Type}) for {_recipeName}." );
			State = CrafterState.WalkingToBench;
			_stateTimer = 0f;
		}

		void DoFetchInputs()
		{
			// Check if we already have the inputs in our inventory.
			if ( _recipe.HasValue && _recipe.Value.CanCraft( Inventory ) )
			{
				State = CrafterState.WalkingToBenchWithInputs;
				_stateTimer = 0f;
				Log.Info( $"Lute: Crafter '{NpcName}' already has inputs for {_recipeName}." );
				return;
			}

			// Find the nearest stockpile that has at least one input.
			Stockpile bestPile = null;
			float bestDist = float.MaxValue;

			foreach ( var input in _recipe.Value.Inputs )
			{
				var pile = ResourceRegistry.NearestStockpileWithResource(
					input.Key, WorldPosition, input.Value, NpcName );
				if ( pile != null )
				{
					var dist = Vector3.DistanceBetween( WorldPosition, pile.Position );
					if ( dist < bestDist )
					{
						bestDist = dist;
						bestPile = pile;
					}
				}
			}

			if ( bestPile == null )
			{
				// No stockpile has our inputs — wait and retry.
				_stateTimer += Time.Delta;
				if ( _stateTimer > 5f )
				{
					Log.Warning( $"Lute: Crafter '{NpcName}' no stockpile has inputs for {_recipeName} — giving up." );
					ReleaseBench();
					ResetToIdle();
				}
				return;
			}

			// Walk to the stockpile.
			SteerToward( bestPile.Position );
			if ( !WithinStopRadius( bestPile.Position ) )
				return;

			Stop();

			// Withdraw all inputs from the stockpile into our inventory.
			foreach ( var input in _recipe.Value.Inputs )
			{
				int need = input.Value;
				int have = Inventory.CountItem( input.Key );
				if ( have >= need ) continue;

				int missing = need - have;
				int withdrawn = bestPile.Withdraw( input.Key, missing );
				if ( withdrawn > 0 )
					Inventory.AddItem( input.Key, withdrawn );

				if ( withdrawn < missing )
				{
					Log.Warning( $"Lute: Crafter '{NpcName}' stockpile {bestPile.Id} had only {withdrawn}/{missing} {input.Key}." );
				}
			}

			Log.Info( $"Lute: Crafter '{NpcName}' fetched inputs for {_recipeName} from {bestPile.Id}." );
			State = CrafterState.WalkingToBenchWithInputs;
			_stateTimer = 0f;
		}

		void DoCraft()
		{
			if ( !_recipe.HasValue )
			{
				Log.Warning( $"Lute: Crafter '{NpcName}' no recipe during craft." );
				ReleaseBench();
				ResetToIdle();
				return;
			}

			// If the craft is already queued, skip the input check —
			// QueueCraft consumes inputs immediately, so CanCraft would
			// return false on subsequent ticks even though the craft is
			// in progress.
			if ( !_craftQueued )
			{
				// Check if we have the inputs.
				if ( !_recipe.Value.CanCraft( Inventory ) )
				{
					Log.Warning( $"Lute: Crafter '{NpcName}' missing inputs for {_recipeName} at craft time." );
					State = CrafterState.FetchingInputs;
					_stateTimer = 0f;
					return;
				}

				// Queue the craft on the bench.
				if ( !_station.Bench.IsValid() )
				{
					Log.Warning( $"Lute: Crafter '{NpcName}' bench became invalid during craft." );
					ResetToIdle();
					return;
				}

				// Set ourselves as the bench user so outputs go to our inventory.
				_station.Bench.SetUser( GameObject, Inventory );

				// Queue the craft (consumes inputs from our inventory).
				if ( !_station.Bench.QueueCraft( _recipeName, Inventory ) )
				{
					Log.Warning( $"Lute: Crafter '{NpcName}' QueueCraft failed for {_recipeName}." );
					_station.Bench.Release();
					ReleaseBench();
					ResetToIdle();
					return;
				}

				_craftQueued = true;
				Log.Info( $"Lute: Crafter '{NpcName}' queued {_recipeName} on {_station.Id}." );
			}

			// Wait for the craft to complete. The bench's OnUpdate
			// processes the queue and adds output to our inventory.
			_stateTimer += Time.Delta;
			float craftTime = _recipe.Value.CraftTime + 0.5f; // small margin
			if ( _stateTimer < craftTime )
				return;

			// Check if the output is in our inventory.
			int outputCount = Inventory.CountItem( _recipe.Value.OutputType );
			if ( outputCount < _recipe.Value.OutputCount )
			{
				// Not done yet — keep waiting (up to 2x craft time).
				if ( _stateTimer < craftTime * 2f )
					return;

				Log.Warning( $"Lute: Crafter '{NpcName}' craft timed out for {_recipeName}." );
				_station.Bench.Release();
				ReleaseBench();
				ResetToIdle();
				return;
			}

			Log.Info( $"Lute: Crafter '{NpcName}' crafted {outputCount} {_recipe.Value.OutputType}." );
			_station.Bench.Release();
			_craftQueued = false;
			State = CrafterState.DepositingOutput;
			_stateTimer = 0f;
		}

		void DoDepositOutput()
		{
			if ( !_recipe.HasValue )
			{
				ResetToIdle();
				return;
			}

			int carrying = Inventory.CountItem( _recipe.Value.OutputType );
			if ( carrying <= 0 )
			{
				Log.Info( $"Lute: Crafter '{NpcName}' no output to deposit." );
				State = CrafterState.Done;
				_stateTimer = 0f;
				return;
			}

			// Find nearest stockpile to deposit output.
			var pile = ResourceRegistry.NearestStockpileWithSpace( WorldPosition, carrying );

			if ( pile == null )
			{
				// No stockpile with capacity — wait and retry.
				_stateTimer += Time.Delta;
				if ( _stateTimer > 10f )
				{
					Log.Warning( $"Lute: Crafter '{NpcName}' no stockpile has capacity for {_recipe.Value.OutputType} — keeping in inventory." );
					State = CrafterState.Done;
					_stateTimer = 0f;
				}
				return;
			}

			SteerToward( pile.Position );
			if ( !WithinStopRadius( pile.Position ) )
				return;

			Stop();

			Inventory.RemoveItem( _recipe.Value.OutputType, carrying );
			int deposited = pile.Deposit( _recipe.Value.OutputType, carrying );
			if ( deposited < carrying )
			{
				int remainder = carrying - deposited;
				Inventory.AddItem( _recipe.Value.OutputType, remainder );
				Log.Warning( $"Lute: Crafter '{NpcName}' stockpile {pile.Id} full, kept {remainder} {_recipe.Value.OutputType}." );
			}

			Log.Info( $"Lute: Crafter '{NpcName}' deposited {deposited} {_recipe.Value.OutputType} at {pile.Id}." );
			State = CrafterState.Done;
			_stateTimer = 0f;
		}

		// Safe accessor for the station position. The bench GameObject can
		// be destroyed between the IsValid() check and the Position access
		// (same race that caused the ProductionPlanner NRE). Returns null
		// when the station is no longer usable so callers can bail out.
		Vector3? SafeStationPosition()
		{
			if ( _station?.Bench is null || !_station.Bench.IsValid() )
				return null;
			try { return _station.Position; }
			catch { return null; }
		}

		void ReleaseBench()
		{
			if ( _station != null )
			{
				WorkstationRegistry.Release( _station.Id, NpcName );
				_station = null;
			}
			_recipeName = "";
			_recipe = null;
			_craftQueued = false;
		}

		void ResetToIdle()
		{
			ReleaseBench();
			State = CrafterState.Idle;
			_stateTimer = 0f;
		}

		// ── Movement (same pattern as HaulerController) ──

		bool WithinStopRadius( Vector3 target )
		{
			var dist = Vector3.DistanceBetween(
				WorldPosition.WithZ( 0 ), target.WithZ( 0 ) );
			return dist < StopRadius;
		}

		void SteerToward( Vector3 target )
		{
			var toTarget = (target - WorldPosition).WithZ( 0 );
			var dist = toTarget.Length;
			if ( dist < 1f ) return;

			Controller.EyeAngles = new Angles( 0, toTarget.EulerAngles.yaw, 0 );

			if ( UseNavMesh && TrySteerWithNavMesh( target ) )
			{
				// NavMesh drove WishVelocity this tick.
			}
			else
			{
				Controller.WishVelocity = toTarget.Normal * WalkSpeed;
			}

			if ( _logTimer > 2f )
			{
				_logTimer = 0f;
				Log.Info( $"Lute: Crafter '{NpcName}' walking — pos={WorldPosition}, dist={dist:F1}, navmesh={_usingNavMesh}." );
			}
		}

		bool TrySteerWithNavMesh( Vector3 target )
		{
			_usingNavMesh = false;
			var nav = NavAgent;
			if ( nav is null || !nav.Enabled ) return false;

			var sceneNav = Scene?.NavMesh;
			if ( sceneNav is null || !sceneNav.IsEnabled ) return false;

			if ( !_navMeshDiagLogged )
			{
				_navMeshDiagLogged = true;
				var agentHit = sceneNav.GetClosestPoint( WorldPosition, 1000f );
				var targetHit = sceneNav.GetClosestPoint( target, 1000f );
				Log.Info( $"Lute: Crafter '{NpcName}' navmesh diag — agent={(agentHit.HasValue ? "on-mesh" : "off-mesh")}, target={(targetHit.HasValue ? "on-mesh" : "off-mesh")}." );
			}

			nav.MoveTo( target );
			var wish = nav.WishVelocity;
			// If NavMesh can't produce a useful velocity (agent off-mesh
			// or no path), fall back to direct steering.
			if ( wish.LengthSquared < 1f )
				return false;

			Controller.WishVelocity = wish;
			_usingNavMesh = true;
			return true;
		}

		void Stop()
		{
			Controller.WishVelocity = Vector3.Zero;
			NavAgent?.MoveTo( WorldPosition );
		}
	}
}
