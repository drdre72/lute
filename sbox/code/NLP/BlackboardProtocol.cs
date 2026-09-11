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
		/// Process an intent through the blackboard protocol. If it's a
		/// CLAIM or RELEASE about a site, execute the corresponding
		/// blackboard operation. Other intents are ignored.
		///
		/// Returns true if the protocol took action.
		/// </summary>
		public static bool ProcessIntent( Intent intent )
		{
			if ( intent == null )
				return false;

			return intent.Type switch
			{
				IntentType.Claim => ClaimSite( intent ),
				IntentType.Release => ReleaseSite( intent ),
				_ => false,
			};
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
}
