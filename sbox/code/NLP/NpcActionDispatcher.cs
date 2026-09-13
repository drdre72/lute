using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Typed action request produced by parsing a <see cref="ResponseDecision.WorldAction"/>
	/// string. Each action type carries only the parameters relevant to it,
	/// so the dispatcher can route to the correct domain authority without
	/// interpreting free-form strings.
	///
	/// Speech does not mutate inventories or tasks directly. The dispatcher
	/// parses the action string into a typed request, validates it, and
	/// submits it to the appropriate authority. The authority decides
	/// whether/how to execute.
	/// </summary>
	public sealed class NpcActionRequest
	{
		/// <summary> The typed action verb. </summary>
		public NpcActionVerb Verb { get; init; }

		/// <summary> The NPC that requested the action (sender). </summary>
		public string NpcName { get; init; }

		/// <summary> Primary subject (task id, site name, resource type, etc.) </summary>
		public string Subject { get; init; }

		/// <summary> Optional secondary target (e.g. target NPC for help). </summary>
		public string Target { get; init; }

		/// <summary> Optional numeric amount (e.g. resource quantity). </summary>
		public int? Amount { get; init; }

		/// <summary> Optional position (x,y,z). </summary>
		public Vector3? Position { get; init; }

		/// <summary> Raw action string (for logging). </summary>
		public string RawAction { get; init; }

		/// <summary> Short summary for logging. </summary>
		public string Summary =>
			$"{Verb}({Subject ?? ""})" +
			(Target != null ? $" -> {Target}" : "") +
			(Amount.HasValue ? $" x{Amount}" : "") +
			(Position.HasValue ? $" @ {Position.Value}" : "");
	}

	/// <summary>
	/// Typed action verbs that the dispatcher recognizes. Adding a new
	/// verb here is the only way to introduce a new world action — the
	/// dispatcher rejects unknown verbs deterministically.
	/// </summary>
	public enum NpcActionVerb
	{
		/// <summary> Unknown / unparseable action. </summary>
		Unknown,
		/// <summary> Deliver a resource to a destination. </summary>
		Deliver,
		/// <summary> Accept a task assignment. </summary>
		AcceptTask,
		/// <summary> Claim a resource (material, tool, area). </summary>
		ClaimResource,
		/// <summary> Release a previously claimed resource. </summary>
		ReleaseResource,
		/// <summary> Check a safety concern at a location. </summary>
		CheckSafety,
		/// <summary> Consider a released site for future work. </summary>
		ConsiderSite,
		/// <summary> Help another NPC with a task. </summary>
		Help,
		/// <summary> Move to a location. </summary>
		MoveTo,
	}

	/// <summary>
	/// Result of dispatching an <see cref="NpcActionRequest"/> to a domain
	/// authority. The authority returns success/failure with a reason —
	/// the NPC's cognition uses this to decide next steps.
	/// </summary>
	public sealed class NpcActionResult
	{
		public bool Success { get; init; }
		public string Reason { get; init; }
		public string Authority { get; init; }

		public static NpcActionResult Ok( string authority ) =>
			new() { Success = true, Authority = authority };

		public static NpcActionResult Fail( string reason, string authority = null ) =>
			new() { Success = false, Reason = reason, Authority = authority ?? "none" };

		public override string ToString() =>
			Success ? $"OK ({Authority})" : $"FAIL ({Authority}): {Reason}";
	}

	/// <summary>
	/// Parses <see cref="ResponseDecision.WorldAction"/> strings into typed
	/// <see cref="NpcActionRequest"/> objects and dispatches them to the
	/// appropriate domain authority. This is the sole bridge between NPC
	/// communication decisions and world mutation.
	///
	/// Design rules (per AGENTS.md three-domain separation):
	/// - Speech does not mutate inventories or tasks directly.
	/// - The dispatcher parses into typed requests, never executes raw strings.
	/// - Each verb routes to exactly one domain authority.
	/// - Unknown verbs are rejected, not improvised.
	/// </summary>
	public static class NpcActionDispatcher
	{
		/// <summary>
		/// Parse a WorldAction string into a typed request. Does not
		/// execute anything — just produces the typed request.
		/// </summary>
		public static NpcActionRequest Parse( string worldAction, string npcName )
		{
			if ( string.IsNullOrWhiteSpace( worldAction ) )
				return new NpcActionRequest { Verb = NpcActionVerb.Unknown, NpcName = npcName, RawAction = worldAction };

			var lower = worldAction.Trim();
			var colonIdx = lower.IndexOf( ':' );
			string verbStr = colonIdx > 0 ? lower.Substring( 0, colonIdx ) : lower;
			string rest = colonIdx > 0 ? lower.Substring( colonIdx + 1 ) : "";

			var verb = ParseVerb( verbStr );
			var subject = rest.Trim();
			string target = null;
			int? amount = null;
			Vector3? position = null;

			// Subject may contain "target:amount" or "target@x,y,z"
			if ( !string.IsNullOrEmpty( subject ) )
			{
				// Extract position if present (format: "x,y,z")
				var posMatch = System.Text.RegularExpressions.Regex.Match(
					subject, @"(-?\d+\.?\d*)\s*,\s*(-?\d+\.?\d*)\s*,\s*(-?\d+\.?\d*)" );
				if ( posMatch.Success )
				{
					position = new Vector3(
						float.Parse( posMatch.Groups[1].Value, CultureInfo.InvariantCulture ),
						float.Parse( posMatch.Groups[2].Value, CultureInfo.InvariantCulture ),
						float.Parse( posMatch.Groups[3].Value, CultureInfo.InvariantCulture ) );
					subject = subject.Substring( 0, posMatch.Index ).Trim();
				}

				// Extract "target:amount" if present
				var subColon = subject.IndexOf( ':' );
				if ( subColon > 0 )
				{
					var after = subject.Substring( subColon + 1 );
					subject = subject.Substring( 0, subColon );
					if ( int.TryParse( after, out int amt ) )
						amount = amt;
					else
						target = after;
				}
			}

			return new NpcActionRequest
			{
				Verb = verb,
				NpcName = npcName,
				Subject = subject,
				Target = target,
				Amount = amount,
				Position = position,
				RawAction = worldAction,
			};
		}

		/// <summary>
		/// Dispatch a typed request to the appropriate domain authority.
		/// Returns the authority's success/failure result. This is the
		/// sole entry point for world mutation from NPC communication.
		/// </summary>
		public static NpcActionResult Dispatch( NpcActionRequest request )
		{
			if ( request == null || request.Verb == NpcActionVerb.Unknown )
				return NpcActionResult.Fail( "unknown or null action" );

			try
			{
				return request.Verb switch
				{
					NpcActionVerb.AcceptTask => DispatchAcceptTask( request ),
					NpcActionVerb.Help => DispatchHelp( request ),
					NpcActionVerb.CheckSafety => DispatchCheckSafety( request ),
					NpcActionVerb.ConsiderSite => DispatchConsiderSite( request ),
					NpcActionVerb.MoveTo => DispatchMoveTo( request ),
					// Resource/logistics verbs route to future registries.
					// Until those exist, return a deterministic "not yet
					// available" rather than improvising.
					NpcActionVerb.Deliver => NpcActionResult.Fail( "logistics board not yet implemented", "LogisticsBoard" ),
					NpcActionVerb.ClaimResource => NpcActionResult.Fail( "resource registry not yet implemented", "ResourceRegistry" ),
					NpcActionVerb.ReleaseResource => NpcActionResult.Fail( "resource registry not yet implemented", "ResourceRegistry" ),
					_ => NpcActionResult.Fail( $"unhandled verb: {request.Verb}" ),
				};
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Lute: NpcActionDispatcher threw on {request.Summary}: {ex.Message}" );
				return NpcActionResult.Fail( $"exception: {ex.Message}" );
			}
		}

		/// <summary>
		/// Convenience: parse + dispatch in one call.
		/// </summary>
		public static NpcActionResult Execute( string worldAction, string npcName )
		{
			var req = Parse( worldAction, npcName );
			var result = Dispatch( req );
			Log.Info( $"Lute: NpcActionDispatcher {npcName} {req.Summary} -> {result}" );
			return result;
		}

		// ── Per-verb dispatchers ──

		static NpcActionResult DispatchAcceptTask( NpcActionRequest req )
		{
			// AcceptTask routes to ConstructionDirector. The task must
			// exist, be pending, and have satisfied dependencies.
			var task = ConstructionDirector.ResolveTask( req.Subject );
			if ( task == null )
				return NpcActionResult.Fail( $"task not found: {req.Subject}", "ConstructionDirector" );
			if ( task.Status != TaskStatus.Pending )
				return NpcActionResult.Fail( $"task {task.Id} is {task.Status}, not pending", "ConstructionDirector" );
			if ( !task.DependenciesSatisfied )
				return NpcActionResult.Fail( $"task {task.Id} has unsatisfied dependencies", "ConstructionDirector" );

			// The actual claim goes through the normal director pipeline
			// (ClaimNextTask / TryAcquire) — we do not bypass reservation.
			// This request is an expression of intent; the director still
			// validates and authorizes.
			return NpcActionResult.Ok( "ConstructionDirector" );
		}

		static NpcActionResult DispatchHelp( NpcActionRequest req )
		{
			// Help: target NPC + optional task. For now, this is an
			// expression of intent logged for the director. The director
			// may reassign the helper to the target's task line.
			if ( string.IsNullOrEmpty( req.Target ) && string.IsNullOrEmpty( req.Subject ) )
				return NpcActionResult.Fail( "help request has no target or task", "ConstructionDirector" );

			ConstructionEventBus.Fire( ConstructionEventType.NpcOfferedHelp,
				taskId: req.Subject ?? "",
				actor: req.NpcName,
				parameters: new() { { "target", req.Target ?? "" } } );
			return NpcActionResult.Ok( "ConstructionDirector" );
		}

		static NpcActionResult DispatchCheckSafety( NpcActionRequest req )
		{
			// CheckSafety: inspect a location via SpatialRegistry. Does
			// not mutate anything — returns whether the area is occupied.
			if ( req.Position.HasValue )
			{
				var occupant = SpatialRegistry.WhatOccupies( req.Position.Value );
				if ( occupant != null )
					return NpcActionResult.Ok( "SpatialRegistry" );
				return NpcActionResult.Fail( "area is clear", "SpatialRegistry" );
			}
			// No position — just log the safety check request.
			return NpcActionResult.Ok( "SpatialRegistry" );
		}

		static NpcActionResult DispatchConsiderSite( NpcActionRequest req )
		{
			// ConsiderSite: a site was released. Log it for the director's
			// future assignment logic. No mutation.
			ConstructionEventBus.Fire( ConstructionEventType.ReservationReleased,
				taskId: req.Subject ?? "",
				actor: req.NpcName );
			return NpcActionResult.Ok( "ConstructionDirector" );
		}

		static NpcActionResult DispatchMoveTo( NpcActionRequest req )
		{
			// MoveTo is handled by the NPC's own controller (navigation),
			// not by a domain authority. The dispatcher just validates
			// that a position was provided.
			if ( !req.Position.HasValue )
				return NpcActionResult.Fail( "move_to has no position", "NpcController" );
			return NpcActionResult.Ok( "NpcController" );
		}

		// ── Verb parsing ──

		static NpcActionVerb ParseVerb( string verbStr )
		{
			return verbStr.ToLowerInvariant() switch
			{
				"deliver" => NpcActionVerb.Deliver,
				"accept_task" => NpcActionVerb.AcceptTask,
				"claim_resource" => NpcActionVerb.ClaimResource,
				"release_resource" => NpcActionVerb.ReleaseResource,
				"check_safety" => NpcActionVerb.CheckSafety,
				"consider_site" => NpcActionVerb.ConsiderSite,
				"help" => NpcActionVerb.Help,
				"move_to" => NpcActionVerb.MoveTo,
				_ => NpcActionVerb.Unknown,
			};
		}
	}
}
