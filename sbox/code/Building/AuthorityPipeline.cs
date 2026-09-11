using System;
using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary>
	/// Capability level for an AI agent (LLM or otherwise). Higher levels
	/// grant more authority over the simulation but require stronger
	/// validation. This is the professor's Phase 5 recommendation: an
	/// AI should never have unrestricted authority over the simulation.
	///
	/// The pipeline enforces that every proposal is validated against
	/// the agent's capability level before it can execute:
	///
	///   LEVEL 0 Observe   — read-only; can inspect world/blueprints
	///   LEVEL 1 Suggest    — can propose non-binding suggestions
	///   LEVEL 2 Modify    — can modify a Blueprint (validated)
	///   LEVEL 3 Execute   — can execute a validated Blueprint
	///   LEVEL 4 WorldState — can modify runtime world state (validated)
	///   LEVEL 5 Persistent — can modify persistent game state (validated)
	///
	/// Each higher level inherits the lower levels' permissions.
	/// </summary>
	public enum AgentCapability
	{
		Observe = 0,
		Suggest = 1,
		ModifyBlueprint = 2,
		ExecuteBlueprint = 3,
		ModifyWorldState = 4,
		ModifyPersistentState = 5,
	}

	/// <summary>
	/// Status of a proposal as it moves through the authority pipeline.
	/// </summary>
	public enum ProposalStatus
	{
		Pending,      // submitted, not yet validated
		Validated,    // passed validation
		Approved,     // approved for execution
		Executing,    // execution in progress
		Completed,    // execution finished successfully
		Rejected,     // failed validation or approval
		Failed,       // execution failed
		RolledBack,   // execution failed and rolled back to prior version
	}

	/// <summary>
	/// A structured proposal from an AI agent. This is the only way an
	/// LLM is allowed to affect the simulation: it submits a proposal,
	/// the pipeline validates it against the agent's capability level,
	/// and only an approved, validated proposal can execute.
	///
	/// The proposal carries:
	/// - the agent's identity and capability level,
	/// - the kind of action (observe/suggest/modify/execute/world/persist),
	/// - the target blueprint id+version (for modify/execute),
	/// - the structured payload (BuildingCritique, Blueprint, or command),
	/// - the validation result and approval state.
	/// </summary>
	public class AgentProposal
	{
		/// <summary> Unique proposal id. </summary>
		public string Id { get; set; }

		/// <summary> Agent identity (e.g. "DevinAI", "Merlyn", "NPC_Builder_0"). </summary>
		public string AgentId { get; set; }

		/// <summary> Capability level the agent claims. </summary>
		public AgentCapability Capability { get; set; } = AgentCapability.Observe;

		/// <summary> What kind of action is proposed. </summary>
		public ProposalAction Action { get; set; }

		/// <summary> Target blueprint id (for modify/execute). </summary>
		public string TargetBlueprintId { get; set; }

		/// <summary> Target blueprint version (for modify/execute). </summary>
		public int TargetBlueprintVersion { get; set; }

		/// <summary> Structured critique payload (for modify actions). </summary>
		public BuildingCritique Critique { get; set; }

		/// <summary> Proposed new/modified blueprint (for modify/execute). </summary>
		public Blueprint Blueprint { get; set; }

		/// <summary> Free-form command string (for world/persist actions). </summary>
		public string Command { get; set; }

		/// <summary> Human-readable rationale (for logs). </summary>
		public string Reason { get; set; }

		/// <summary> Current pipeline status. </summary>
		public ProposalStatus Status { get; set; } = ProposalStatus.Pending;

		/// <summary> Validation result (set by the pipeline). </summary>
		public BlueprintValidationResult Validation { get; set; }

		/// <summary> Rejection reason (if Status == Rejected). </summary>
		public string RejectionReason { get; set; }

		/// <summary> Timestamp (UTC) of submission. </summary>
		public string SubmittedUtc { get; set; }

		/// <summary> Timestamp (UTC) of resolution. </summary>
		public string ResolvedUtc { get; set; }
	}

	/// <summary>
	/// The kind of action a proposal requests. Must be permitted by the
	/// agent's <see cref="AgentCapability"/> level.
	/// </summary>
	public enum ProposalAction
	{
		Observe,           // read-only inspection
		Suggest,           // non-binding suggestion
		ModifyBlueprint,   // modify a blueprint (validated)
		ExecuteBlueprint,  // execute a validated blueprint
		ModifyWorldState,   // modify runtime world state
		ModifyPersistentState, // modify persistent game state
	}

	/// <summary>
	/// The AI authority pipeline. Enforces the professor's Phase 5 model:
	///
	///   Observe -> Propose -> Validate -> Approve -> Execute
	///
	/// with hard boundaries between the LLM and the simulation. An LLM
	/// can never directly spawn geometry, mutate world state, or alter
	/// persistence. It can only submit <see cref="AgentProposal"/>s,
	/// which the pipeline validates against the agent's capability
	/// level and the blueprint validator before any execution happens.
	///
	/// If execution fails, the pipeline rolls back to the prior
	/// blueprint version via <see cref="BlueprintRegistry"/>.
	/// </summary>
	public static class AuthorityPipeline
	{
		static readonly List<AgentProposal> _history = new();
		static int _nextId;

		/// <summary> All proposals, oldest first (audit log). </summary>
		public static IReadOnlyList<AgentProposal> History => _history;

		/// <summary> Clear history (test/reset). </summary>
		public static void Reset()
		{
			_history.Clear();
			_nextId = 0;
		}

		/// <summary>
		/// Submit a proposal and run it through validation. Returns the
		/// proposal with Status updated. Does NOT execute — call
		/// <see cref="ExecuteApproved"/> separately to perform the
		/// actual world change. This separation is deliberate: it lets
		/// a human (or a deterministic approver) review before execution.
		/// </summary>
		public static AgentProposal Submit( AgentProposal proposal )
		{
			proposal.Id = $"proposal_{_nextId++}";
			proposal.SubmittedUtc = DateTime.UtcNow.ToString( "o" );
			_history.Add( proposal );

			// 1. Capability check: action must be permitted by capability
			if ( !IsActionPermitted( proposal.Capability, proposal.Action ) )
			{
				Reject( proposal, $"Agent capability {proposal.Capability} does not permit action {proposal.Action}." );
				return proposal;
			}

			// 2. Validate blueprint (for modify/execute actions)
			if ( proposal.Action == ProposalAction.ModifyBlueprint ||
				 proposal.Action == ProposalAction.ExecuteBlueprint )
			{
				if ( proposal.Blueprint == null )
				{
					Reject( proposal, "Modify/Execute proposal has no Blueprint payload." );
					return proposal;
				}

				proposal.Validation = BlueprintValidator.Validate( proposal.Blueprint );
				if ( !proposal.Validation.IsValid )
				{
					Reject( proposal, $"Blueprint validation failed: {proposal.Validation.ErrorCount} error(s)." );
					return proposal;
				}
			}

			// 3. Validated
			proposal.Status = ProposalStatus.Validated;
			Log.Info( $"Lute: AuthorityPipeline proposal {proposal.Id} ({proposal.Action}) by {proposal.AgentId} validated." );
			return proposal;
		}

		/// <summary>
		/// Approve a validated proposal. Only validated proposals can be
		/// approved. In a fully automated system this can be called
		/// immediately after Submit; in a human-in-the-loop system a
		/// reviewer calls it after inspecting the proposal.
		/// </summary>
		public static bool Approve( string proposalId )
		{
			var p = Find( proposalId );
			if ( p == null || p.Status != ProposalStatus.Validated )
				return false;

			p.Status = ProposalStatus.Approved;
			Log.Info( $"Lute: AuthorityPipeline proposal {proposalId} approved." );
			return true;
		}

		/// <summary>
		/// Execute an approved proposal. For ModifyBlueprint, registers
		/// the new version. For ExecuteBlueprint, registers and marks
		/// for execution by the ConstructionDirector. On execution
		/// failure, rolls back to the prior blueprint version.
		/// </summary>
		public static bool ExecuteApproved( string proposalId )
		{
			var p = Find( proposalId );
			if ( p == null || p.Status != ProposalStatus.Approved )
				return false;

			p.Status = ProposalStatus.Executing;
			p.ResolvedUtc = DateTime.UtcNow.ToString( "o" );

			try
			{
				switch ( p.Action )
				{
					case ProposalAction.ModifyBlueprint:
						// Register the new version (keeps history)
						p.Blueprint.BumpVersion();
						BlueprintRegistry.Register( p.Blueprint );
						Log.Info( $"Lute: AuthorityPipeline {proposalId} modified blueprint {p.Blueprint.Id} -> v{p.Blueprint.Version}." );
						break;

					case ProposalAction.ExecuteBlueprint:
						p.Blueprint.BumpVersion();
						BlueprintRegistry.Register( p.Blueprint );
						// In a full integration, hand off to ConstructionDirector
						// here. For now we register and log; the director
						// picks up registered blueprints.
						Log.Info( $"Lute: AuthorityPipeline {proposalId} executed blueprint {p.Blueprint.Id} v{p.Blueprint.Version} ({p.Blueprint.PieceCount} pieces)." );
						break;

					case ProposalAction.Observe:
					case ProposalAction.Suggest:
						// No world change
						break;

					case ProposalAction.ModifyWorldState:
					case ProposalAction.ModifyPersistentState:
						// Higher-level actions — require explicit integration
						// with the world/persistence layer. Logged but not
						// auto-executed; a future integration point.
						Log.Info( $"Lute: AuthorityPipeline {proposalId} {p.Action} — logged, requires world/persistence integration." );
						break;
				}

				p.Status = ProposalStatus.Completed;
				return true;
			}
			catch ( Exception e )
			{
				Log.Warning( $"Lute: AuthorityPipeline {proposalId} execution failed: {e.Message}. Rolling back." );
				p.Status = ProposalStatus.Failed;

				// Roll back to prior version if we have one
				if ( !string.IsNullOrEmpty( p.TargetBlueprintId ) && p.TargetBlueprintVersion > 0 )
				{
					var restored = BlueprintRegistry.Rollback( p.TargetBlueprintId, p.TargetBlueprintVersion );
					if ( restored != null )
					{
						p.Status = ProposalStatus.RolledBack;
						Log.Info( $"Lute: AuthorityPipeline rolled back {p.TargetBlueprintId} to v{p.TargetBlueprintVersion} (new v{restored.Version})." );
					}
				}
				return false;
			}
		}

		/// <summary> Reject a proposal (manual or auto). </summary>
		public static void Reject( AgentProposal p, string reason )
		{
			p.Status = ProposalStatus.Rejected;
			p.RejectionReason = reason;
			p.ResolvedUtc = DateTime.UtcNow.ToString( "o" );
			Log.Warning( $"Lute: AuthorityPipeline rejected {p.Id}: {reason}" );
		}

		/// <summary> Find a proposal by id. </summary>
		public static AgentProposal Find( string id ) =>
			_history.Find( p => p.Id == id );

		/// <summary> Recent proposals (last n). </summary>
		public static List<AgentProposal> Recent( int n = 10 ) =>
			_history.Count <= n
				? new( _history )
				: _history.GetRange( _history.Count - n, n );

		/// <summary>
		/// Check whether a capability level permits an action. Higher
		/// levels inherit lower levels' permissions.
		/// </summary>
		public static bool IsActionPermitted( AgentCapability cap, ProposalAction action )
		{
			return action switch
			{
				ProposalAction.Observe => cap >= AgentCapability.Observe,
				ProposalAction.Suggest => cap >= AgentCapability.Suggest,
				ProposalAction.ModifyBlueprint => cap >= AgentCapability.ModifyBlueprint,
				ProposalAction.ExecuteBlueprint => cap >= AgentCapability.ExecuteBlueprint,
				ProposalAction.ModifyWorldState => cap >= AgentCapability.ModifyWorldState,
				ProposalAction.ModifyPersistentState => cap >= AgentCapability.ModifyPersistentState,
				_ => false,
			};
		}

		/// <summary> Console-friendly summary of recent proposals. </summary>
		public static string Summary( int n = 10 )
		{
			var recent = Recent( n );
			var sb = $"AuthorityPipeline: {recent.Count} recent proposal(s).";
			foreach ( var p in recent )
				sb += $"\n  [{p.Status}] {p.Id} {p.Action} by {p.AgentId} (cap={p.Capability}){( p.Validation != null ? $" val={p.Validation.IsValid}" : "")}";
			return sb;
		}
	}
}
