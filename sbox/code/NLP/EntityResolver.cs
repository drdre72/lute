using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Lute.Building;

namespace Lute.NLP
{
	/// <summary>
	/// Entity resolver — resolves pronouns and references to concrete
	/// objects in the world. This is the layer that makes NPC conversation
	/// feel natural without requiring an LLM.
	///
	/// Example:
	///   "Put those there."
	///
	/// The parser initially has:
	///   action = PUT
	///   object = THOSE
	///   destination = THERE
	///
	/// The entity resolver looks at:
	///   - recent conversation
	///   - recently referenced entities
	///   - NPC attention target
	///   - nearby objects
	///   - current task
	///   - blackboard
	///   - shared construction context
	///
	/// and resolves:
	///   object = WallSegment_183
	///   destination = NorthFoundation
	///
	/// The entity resolver works with the ContextResolver, which maintains
	/// the conversation context (recently referenced entities, attention
	/// targets, etc.).
	/// </summary>
	public static class EntityResolver
	{
		// ── Known entity types ──
		static readonly Dictionary<string, string[]> _entityKeywords = new()
		{
			{ "wall", new[] { "wall", "walls", "rampart", "battlement", "parapet" } },
			{ "gate", new[] { "gate", "gates", "doorway", "entrance", "portal" } },
			{ "tower", new[] { "tower", "towers", "watchtower", "turret" } },
			{ "road", new[] { "road", "roads", "path", "street", "way" } },
			{ "cottage", new[] { "cottage", "cottages", "house", "houses", "home", "homes" } },
			{ "shop", new[] { "shop", "shops", "store", "market", "stall" } },
			{ "smithy", new[] { "smithy", "forge", "blacksmith", "anvil" } },
			{ "tavern", new[] { "tavern", "inn", "pub", "bar" } },
			{ "chapel", new[] { "chapel", "church", "temple", "shrine" } },
			{ "bridge", new[] { "bridge", "bridges", "crossing" } },
			{ "well", new[] { "well", "wells", "fountain", "spring" } },
			{ "foundation", new[] { "foundation", "foundations", "base", "footing" } },
		};

		// ── Direction words → position offsets ──
		static readonly Dictionary<string, string> _directions = new()
		{
			{ "north", "north" },
			{ "south", "south" },
			{ "east", "east" },
			{ "west", "west" },
			{ "center", "center" },
			{ "central", "center" },
			{ "middle", "center" },
		};

		/// <summary>
		/// Resolve a token list's pronouns and references to concrete
		/// entities using the conversation context.
		///
		/// Returns a resolved entity map: token index → entity ID.
		/// </summary>
		public static Dictionary<int, string> Resolve( TokenList tokens, NpcContext context )
		{
			var resolved = new Dictionary<int, string>();

			if ( tokens == null || context == null )
				return resolved;

			// 1. Resolve pronouns using conversation context
			foreach ( var token in tokens.UnresolvedPronouns )
			{
				var entity = ResolvePronoun( token.Text, context );
				if ( entity != null )
				{
					resolved[token.Index] = entity;
					token.ResolvedEntity = entity;
				}
			}

			// 2. Resolve entity keywords (e.g. "wall" → "Wall_E_14")
			foreach ( var token in tokens.Tokens )
			{
				if ( token.IsPunctuation || token.IsNumber )
					continue;

				var entity = ResolveEntityKeyword( token.Text, context );
				if ( entity != null && !resolved.ContainsKey( token.Index ) )
				{
					resolved[token.Index] = entity;
					token.ResolvedEntity = entity;
				}
			}

			// 3. Resolve directions
			foreach ( var token in tokens.Tokens )
			{
				if ( _directions.TryGetValue( token.Text, out var direction ) )
				{
					var entity = ResolveDirection( direction, context );
					if ( entity != null && !resolved.ContainsKey( token.Index ) )
					{
						resolved[token.Index] = entity;
						token.ResolvedEntity = entity;
					}
				}
			}

			return resolved;
		}

		/// <summary>
		/// Resolve a pronoun to a concrete entity using conversation context.
		/// </summary>
		static string ResolvePronoun( string pronoun, NpcContext context )
		{
			// "it" / "that" / "this" → most recently referenced entity
			if ( pronoun is "it" or "that" or "this" )
			{
				return context.Beliefs?.LastReferencedEntity;
			}

			// "those" / "these" → most recently referenced plural entity
			if ( pronoun is "those" or "these" )
			{
				return context.Beliefs?.LastReferencedEntity;
			}

			// "there" / "here" → most recently referenced location
			if ( pronoun is "there" or "here" )
			{
				return context.Beliefs?.LastReferencedLocation;
			}

			// "where" → query for location
			if ( pronoun == "where" )
			{
				return context.Beliefs?.LastReferencedLocation;
			}

			return null;
		}

		/// <summary>
		/// Resolve an entity keyword (e.g. "wall") to a specific entity
		/// using the NPC's current task and nearby objects.
		/// </summary>
		static string ResolveEntityKeyword( string word, NpcContext context )
		{
			foreach ( var (entityType, keywords) in _entityKeywords )
			{
				if ( keywords.Contains( word ) )
				{
					// Check if the NPC's current task involves this entity type
					var currentTask = context.Beliefs?.CurrentTask;
					if ( !string.IsNullOrEmpty( currentTask ) &&
						 currentTask.Contains( entityType, StringComparison.OrdinalIgnoreCase ) )
					{
						return currentTask;
					}

					// Check recently referenced entities
					foreach ( var refEntity in context.Beliefs?.RecentEntities ?? new() )
					{
						if ( refEntity.Contains( entityType, StringComparison.OrdinalIgnoreCase ) )
							return refEntity;
					}

					// Return the generic entity type
					return entityType;
				}
			}

			return null;
		}

		/// <summary>
		/// Resolve a direction to a location/site using the blackboard.
		/// </summary>
		static string ResolveDirection( string direction, NpcContext context )
		{
			// Check if there's a task/site at this direction
			var claims = SpatialBlackboard.GetClaims();
			foreach ( var claim in claims )
			{
				if ( claim.Activity?.Contains( direction, StringComparison.OrdinalIgnoreCase ) == true )
					return claim.Activity;
			}

			// Return the direction as a site identifier
			return $"{direction}_site";
		}
	}

	/// <summary>
	/// Context resolver — maintains conversation context and provides it
	/// to the entity resolver. This tracks:
	/// - Recently referenced entities (for pronoun resolution)
	/// - NPC attention target
	/// - Nearby objects from the blackboard
	/// - Current task context
	/// - Shared construction context
	///
	/// The context resolver is called after the intent grammar produces
	/// a preliminary intent. It fills in resolved entity IDs and
	/// location IDs that the grammar couldn't determine on its own.
	/// </summary>
	public static class ContextResolver
	{
		/// <summary>
		/// Resolve an intent's unresolved references using conversation context.
		/// Modifies the intent in-place, filling in Subject, ObjectId,
		/// LocationId, and Parameters with resolved values.
		/// </summary>
		public static void Resolve( Intent intent, NpcContext context )
		{
			if ( intent == null || context == null )
				return;

			// Resolve Subject if it's a pronoun
			if ( !string.IsNullOrEmpty( intent.Subject ) )
			{
				intent.Subject = ResolveReference( intent.Subject, context )
					?? intent.Subject;
			}

			// Resolve location parameter
			if ( intent.Parameters.TryGetValue( "location", out var location ) )
			{
				var resolved = ResolveReference( location, context );
				if ( resolved != null )
					intent.Parameters["location"] = resolved;
			}

			// Resolve object parameter
			if ( intent.Parameters.TryGetValue( "object", out var obj ) )
			{
				var resolved = ResolveReference( obj, context );
				if ( resolved != null )
					intent.Parameters["object"] = resolved;
			}

			// Update conversation context with this intent's references
			UpdateContext( intent, context );
		}

		/// <summary>
		/// Resolve a reference string. If it's a pronoun, look it up in
		/// the conversation context. Otherwise, return null (already resolved).
		/// </summary>
		static string ResolveReference( string reference, NpcContext context )
		{
			if ( string.IsNullOrEmpty( reference ) )
				return null;

			var lower = reference.ToLowerInvariant();

			// Pronoun resolution
			if ( lower is "it" or "that" or "this" or "those" or "these" )
				return context.Beliefs?.LastReferencedEntity;

			if ( lower is "there" or "here" )
				return context.Beliefs?.LastReferencedLocation;

			// Direction resolution
			if ( lower is "north" or "south" or "east" or "west" or "center" )
				return $"{lower}_site";

			return null;
		}

		/// <summary>
		/// Update the conversation context with entities referenced in
		/// this intent. This enables pronoun resolution in future turns.
		/// </summary>
		static void UpdateContext( Intent intent, NpcContext context )
		{
			if ( context.Beliefs == null )
				return;

			// Track the subject as a recently referenced entity
			if ( !string.IsNullOrEmpty( intent.Subject ) )
			{
				context.Beliefs.LastReferencedEntity = intent.Subject;
				context.Beliefs.AddRecentEntity( intent.Subject );
			}

			// Track location references
			if ( intent.Parameters.TryGetValue( "location", out var location ) )
			{
				context.Beliefs.LastReferencedLocation = location;
			}
		}
	}
}
