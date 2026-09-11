using System;
using System.Collections.Generic;
using Sandbox;

namespace Lute.NLP
{
	/// <summary>
	/// Operations that can be performed on the blackboard through
	/// the transaction system. NPCs never directly mutate the world —
	/// they submit blackboard requests that are validated and applied
	/// by the ConstructionDirector.
	/// </summary>
	public enum BlackboardOperation
	{
		/// <summary>Read a blackboard value. </summary>
		Read,
		/// <summary>Claim a task, resource, or spatial area. </summary>
		Claim,
		/// <summary>Release a previously claimed task, resource, or area. </summary>
		Release,
		/// <summary>Update a blackboard value (e.g. task status). </summary>
		Update,
		/// <summary>Request a resource or task from the pool. </summary>
		Request,
		/// <summary>Offer a resource or task to another NPC. </summary>
		Offer,
		/// <summary>Mark a task as complete. </summary>
		Complete,
		/// <summary>Mark a task as failed. </summary>
		Fail,
	}

	/// <summary>
	/// A transaction request submitted by an NPC to the blackboard.
	/// The blackboard is authoritative — NPC conversation never directly
	/// mutates the world. Instead:
	///
	///   NPC says something
	///       ↓
	///   Intent
	///       ↓
	///   Communication Protocol
	///       ↓
	///   Decision
	///       ↓
	///   BlackboardRequest (this)
	///       ↓
	///   Task system / ConstructionDirector
	///       ↓
	///   World
	///
	/// This makes the blackboard deterministic and auditable.
	/// </summary>
	public sealed class BlackboardRequest
	{
		/// <summary> The NPC submitting this request. </summary>
		public string Actor { get; init; }

		/// <summary> The operation to perform. </summary>
		public BlackboardOperation Operation { get; init; }

		/// <summary> The blackboard key (e.g. "WestWall", "materials.stone"). </summary>
		public string Key { get; init; }

		/// <summary> Optional value for Update operations. </summary>
		public object Value { get; init; }

		/// <summary> The simulation tick when this request was created. </summary>
		public long Tick { get; init; }

		/// <summary> Short debug summary. </summary>
		public string Summary =>
			$"[{Tick}] {Actor} {Operation} '{Key}'" +
			( Value != null ? $" = {Value}" : "" );
	}

	/// <summary>
	/// Result of a blackboard transaction.
	/// </summary>
	public sealed class BlackboardResult
	{
		public bool Success { get; init; }
		public string Reason { get; init; } = "";
		public object Value { get; init; }

		public static BlackboardResult Ok( object value = null ) =>
			new() { Success = true, Value = value };

		public static BlackboardResult Fail( string reason ) =>
			new() { Success = false, Reason = reason };
	}
}
