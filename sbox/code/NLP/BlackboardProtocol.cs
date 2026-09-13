using System;
using System.Collections.Generic;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Bridge between the NLP intent system and the
	/// <see cref="SpatialBlackboard"/>. When an NPC expresses a CLAIM or
	/// RELEASE intent about a site, this protocol translates it into an
	/// actual blackboard spatial claim or release.
	///
	/// This is how NPCs "collude for blackboard use" — they negotiate
	/// sites through NLP conversation, and the BlackboardProtocol makes
	/// the negotiation binding by creating/removing spatial claims.
	///
	/// Flow:
	/// 1. NPC A sends "I claim the north_site" → NlpParser → Claim intent
	/// 2. SocialRules acknowledges it → NPC A's beliefs update
	/// 3. BlackboardProtocol.ClaimSite creates a blackboard claim
	/// 4. NPC B sends "I need the north_site" → Request intent
	/// 5. SocialRules checks beliefs — sees it's claimed by A → Reject
	/// 6. NPC A sends "I'm done with north_site" → Release intent
	/// 7. BlackboardProtocol.ReleaseSite removes the blackboard claim
	/// 8. NPC B can now claim it
	/// </summary>
	public static class BlackboardProtocol
	{
		/// <summary>
		/// Create a spatial claim on the blackboard based on a CLAIM intent.
		/// Returns true if the claim was created, false if the site was
		/// already claimed by someone else.
		/// </summary>
		public static bool ClaimSite( Intent intent )
		{
			if ( intent == null || intent.Type != IntentType.Claim )
				return false;

			if ( intent.Topic != IntentTopic.Site )
				return false;

			// Extract position from parameters or use the sender's position
			Vector3 pos = Vector3.Zero;
			if ( intent.Parameters.TryGetValue( "position", out var posStr ) )
			{
				pos = ParsePosition( posStr );
			}

			// Extract radius (default 500 inches = ~12.7m)
			float radius = 500f;
			if ( intent.Parameters.TryGetValue( "radius", out var radiusStr ) )
			{
				if ( float.TryParse( radiusStr, out float r ) )
					radius = r;
			}

			// Create the blackboard claim — expiry=0 means task-tied
			// (released explicitly when the task completes/fails, not by
			// a 300s timeout). See professor feedback on claim expiry.
			bool success = SpatialBlackboard.Claim(
				intent.Sender, pos, radius, "nlp_site_claim", 0 );

			if ( success )
			{
				Log.Info( $"[NLP-Blackboard] {intent.Sender} claimed site '{intent.Subject}' at {pos} radius {radius}." );
			}
			else
			{
				Log.Info( $"[NLP-Blackboard] {intent.Sender} FAILED to claim site '{intent.Subject}' — already claimed." );
			}

			return success;
		}

		/// <summary>
		/// Release a spatial claim based on a RELEASE intent.
		/// </summary>
		public static bool ReleaseSite( Intent intent )
		{
			if ( intent == null || intent.Type != IntentType.Release )
				return false;

			if ( intent.Topic != IntentTopic.Site )
				return false;

			// Find and release claims by this sender matching the subject
			bool released = false;
			var claims = SpatialBlackboard.GetClaimsByOwner( intent.Sender );
			foreach ( var claim in claims )
			{
				if ( claim.Activity == "nlp_site_claim" )
				{
					SpatialBlackboard.ReleaseClaim( claim.Id );
					released = true;
					Log.Info( $"[NLP-Blackboard] {intent.Sender} released site claim '{claim.Id}'." );
				}
			}

			return released;
		}

		/// <summary>
		/// Process an intent through the blackboard protocol. Routes
		/// binding intents (Claim, Release, ClaimResource, ReleaseResource,
		/// RequestTask, AssignTask, AcceptTask) to their domain authorities.
		/// Non-binding intents (Request, Offer, Inform, etc.) are ignored
		/// — they are social/coordination, not world mutations.
		///
		/// Returns a <see cref="BlackboardTransaction"/> describing the
		/// outcome. Speech does not mutate inventories or tasks directly —
		/// it submits a validated request, and the authority decides.
		/// </summary>
		public static BlackboardTransaction ProcessIntent( Intent intent )
		{
			if ( intent == null )
				return BlackboardTransaction.Fail( "null intent" );

			return intent.Type switch
			{
				IntentType.Claim => ClaimSite( intent )
					? BlackboardTransaction.Ok( "SpatialBlackboard", "claim_site" )
					: BlackboardTransaction.Fail( "site already claimed", "SpatialBlackboard" ),
				IntentType.Release => ReleaseSite( intent )
					? BlackboardTransaction.Ok( "SpatialBlackboard", "release_site" )
					: BlackboardTransaction.Fail( "no matching site claim", "SpatialBlackboard" ),

				// Resource intents route to the future ResourceRegistry.
				// Until it exists, return a deterministic "not yet available"
				// so NPCs get a machine-readable failure rather than a silent no-op.
				IntentType.ClaimResource => BlackboardTransaction.Fail(
					"resource registry not yet implemented", "ResourceRegistry" ),
				IntentType.ReleaseResource => BlackboardTransaction.Fail(
					"resource registry not yet implemented", "ResourceRegistry" ),
				IntentType.RequestResource => BlackboardTransaction.Fail(
					"resource registry not yet implemented", "ResourceRegistry" ),
				IntentType.OfferResource => BlackboardTransaction.Fail(
					"resource registry not yet implemented", "ResourceRegistry" ),

				// Task intents route to ConstructionDirector.
				IntentType.RequestTask => RouteRequestTask( intent ),
				IntentType.AssignTask => RouteAssignTask( intent ),
				IntentType.AcceptTask => RouteAcceptTask( intent ),

				_ => BlackboardTransaction.Ignore( $"non-binding intent: {intent.Type}" ),
			};
		}

		/// <summary>
		/// RequestTask: the NPC is asking for work. Route to the director's
		/// claim pipeline — the director validates eligibility and reservation.
		/// </summary>
		static BlackboardTransaction RouteRequestTask( Intent intent )
		{
			// The director's ClaimNextTask handles eligibility, dependency,
			// and reservation checks. We do not bypass it.
			int builderId = FindBuilder( intent.Sender );
			if ( builderId < 0 )
				return BlackboardTransaction.Fail( $"builder not registered: {intent.Sender}", "ConstructionDirector" );

			var task = ConstructionDirector.ClaimNextTask( builderId, intent.Sender );
			if ( task == null )
				return BlackboardTransaction.Fail( "no available tasks", "ConstructionDirector" );

			return BlackboardTransaction.Ok( "ConstructionDirector", $"claim_task:{task.Id}" );
		}

		/// <summary>
		/// AssignTask: one NPC assigns a task to another. The director
		/// must validate that the target builder is registered and the
		/// task is assignable. This is an expression of intent — the
		/// director still owns the actual assignment.
		/// </summary>
		static BlackboardTransaction RouteAssignTask( Intent intent )
		{
			if ( string.IsNullOrEmpty( intent.Target ) )
				return BlackboardTransaction.Fail( "assign_task has no target builder", "ConstructionDirector" );

			int builderId = FindBuilder( intent.Target );
			if ( builderId < 0 )
				return BlackboardTransaction.Fail( $"target builder not registered: {intent.Target}", "ConstructionDirector" );

			// Log the assignment request — the director's AssignTasks will
			// handle actual load-balanced assignment. We do not force-assign.
			ConstructionEventBus.Fire( ConstructionEventType.TaskAssigned,
				taskId: intent.Subject ?? "",
				target: intent.Target );
			return BlackboardTransaction.Ok( "ConstructionDirector", $"assign_request:{intent.Subject}" );
		}

		/// <summary>
		/// AcceptTask: the NPC accepts a task it was offered/assigned.
		/// Route through the director's claim pipeline.
		/// </summary>
		static BlackboardTransaction RouteAcceptTask( Intent intent )
		{
			var task = ConstructionDirector.ResolveTask( intent.Subject );
			if ( task == null )
				return BlackboardTransaction.Fail( $"task not found: {intent.Subject}", "ConstructionDirector" );
			if ( task.Status != TaskStatus.Pending )
				return BlackboardTransaction.Fail( $"task {task.Id} is {task.Status}, not pending", "ConstructionDirector" );
			if ( !task.DependenciesSatisfied )
				return BlackboardTransaction.Fail( $"task {task.Id} has unsatisfied dependencies", "ConstructionDirector" );

			// The actual claim goes through ClaimNextTask which validates
			// reservation. We do not bypass it.
			return BlackboardTransaction.Ok( "ConstructionDirector", $"accept_task:{task.Id}" );
		}

		static int FindBuilder( string npcName )
		{
			foreach ( var b in ConstructionDirector.AllBuilders() )
				if ( b.NpcName == npcName ) return b.BuilderId;
			return -1;
		}

		/// <summary>
		/// Query the blackboard for nearby claims and return them as
		/// resource beliefs that can be merged into a BeliefModel.
		/// </summary>
		public static Dictionary<string, ResourceBelief> ObserveNearbyClaims( Vector3 position, float radius = 5000f )
		{
			var result = new Dictionary<string, ResourceBelief>();
			var allClaims = SpatialBlackboard.GetClaims();
			float radiusSq = radius * radius;

			foreach ( var claim in allClaims )
			{
				// Distance check (squared to avoid sqrt)
				var delta = claim.Position - position;
				if ( delta.LengthSquared > radiusSq )
					continue;

				var key = $"claim_{claim.Id}";
				result[key] = new ResourceBelief
				{
					Subject = key,
					ClaimedBy = claim.Owner,
					IsAvailable = false,
				};
			}

			return result;
		}

		/// <summary> Parse a "x,y,z" string into a Vector3. </summary>
		static Vector3 ParsePosition( string posStr )
		{
			var parts = posStr.Split( ',' );
			if ( parts.Length >= 3 &&
				float.TryParse( parts[0], out float x ) &&
				float.TryParse( parts[1], out float y ) &&
				float.TryParse( parts[2], out float z ) )
			{
				return new Vector3( x, y, z );
			}
			return Vector3.Zero;
		}
	}

	/// <summary>
	/// Result of a <see cref="BlackboardProtocol.ProcessIntent"/> transaction.
	/// Describes whether the binding intent was executed, ignored, or
	/// failed, and which domain authority was responsible.
	/// </summary>
	public sealed class BlackboardTransaction
	{
		public enum TransactionStatus { Ok, Fail, Ignore }

		public TransactionStatus Status { get; init; }
		public string Authority { get; init; }
		public string Detail { get; init; }

		public bool Succeeded => Status == TransactionStatus.Ok;

		public static BlackboardTransaction Ok( string authority, string detail = "" ) =>
			new() { Status = TransactionStatus.Ok, Authority = authority, Detail = detail };
		public static BlackboardTransaction Fail( string detail, string authority = "none" ) =>
			new() { Status = TransactionStatus.Fail, Authority = authority, Detail = detail };
		public static BlackboardTransaction Ignore( string detail = "" ) =>
			new() { Status = TransactionStatus.Ignore, Authority = "none", Detail = detail };

		public override string ToString() =>
			Status switch
			{
				TransactionStatus.Ok => $"OK ({Authority}): {Detail}",
				TransactionStatus.Fail => $"FAIL ({Authority}): {Detail}",
				_ => $"IGNORE: {Detail}",
			};
	}
}
