using System;
using System.Linq;
using Sandbox;

namespace Lute.Building
{
	/// <summary>
	/// Production settlement planner. Drives adaptive settlement planning
	/// by periodically evaluating settlement needs and generating structure
	/// requests. This is the production owner of the need-evaluation loop.
	///
	/// Per the professor's review:
	/// "World state creates needs; construction demand creates logistics
	/// demand. Then remove Gate3Benchmark and prove the settlement
	/// continues functioning."
	///
	/// The desired architecture:
	/// <code>
	/// SettlementStateEvaluator
	///         ↓
	/// SettlementNeedBoard
	///
	/// Construction demand
	///         ↓
	/// LogisticsSupplyPlanner
	///         ↓
	/// LogisticsBoard
	/// </code>
	///
	/// while:
	/// <code>
	/// Gate3Benchmark
	///         ↓
	/// configure scenario / inject test failure / assert outcomes / report
	/// </code>
	///
	/// No production simulation should stop functioning if Gate3Benchmark
	/// is removed from the scene.
	/// </summary>
	public sealed class SettlementPlanner : Component
	{
		/// <summary> How often to evaluate settlement needs (seconds). </summary>
		[Property] public float EvaluateInterval { get; set; } = 10f;

		float _timer;
		bool _started;

		protected override void OnStart()
		{
			Log.Info( "Lute: SettlementPlanner started — production need-evaluation loop." );

			// Clear the need board for a fresh play session. Static state
			// persists across S&Box play sessions, so we must reset to
			// avoid stale _lastEvaluation from a prior session making
			// Evaluate() return early forever.
			SettlementNeedBoard.Clear();

			// Enable material gating in production — the closed-loop
			// economy requires materials to be hauled to build sites.
			ConstructionDirector.EnforceMaterialGating = true;
			Log.Info( "Lute: SettlementPlanner — EnforceMaterialGating = true (production default)." );
		}

		protected override void OnUpdate()
		{
			// Wait a few seconds for ResourceBootstrap + NPCSpawner to finish
			// before starting need evaluation.
			if ( !_started )
			{
				_timer += Time.Delta;
				if ( _timer < 3f ) return;
				_started = true;
				_timer = 0f;
				Log.Info( "Lute: SettlementPlanner — starting need evaluation." );
			}

			// Drive the need-evaluation loop. This generates structure
			// requests from settlement needs, which the Surveyor resolves
			// by selecting sites and registering tasks with the
			// ConstructionDirector.
			_timer += Time.Delta;
			if ( _timer >= EvaluateInterval )
			{
				_timer = 0f;
				SettlementNeedBoard.Evaluate( Time.Now );
			}
		}
	}
}
