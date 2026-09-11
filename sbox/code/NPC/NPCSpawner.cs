using System.Collections.Generic;
using System.Linq;
using Lute.Building;

namespace Lute.Npc
{
	/// <summary>
	/// Scans the scene for all <see cref="SpawnMarker"/> components and
	/// spawns the appropriate NPC body + controller at each one. Supports
	/// both editor-placed markers (in the .scene file) and code-spawned
	/// markers (created by LuteWorld.Build or any other system).
	///
	/// Today only the "Builder" NPC type is implemented — it spawns a
	/// citizen body + NPCBuilderController wired to a sibling NPCBuilder,
	/// mirroring the proven LuteWorld.SpawnBuilderBody pattern. New NPC
	/// types can be added by extending <see cref="SpawnForMarker"/>.
	/// </summary>
	public static class NPCSpawner
	{
		/// <summary>
		/// Scan the active scene for all SpawnMarkers and spawn an NPC at
		/// each. Call this once during world-gen after the scene is built.
		/// Each marker is consumed (disabled) after spawning so a second
		/// call won't double-spawn.
		/// </summary>
		public static int SpawnAll( Scene scene, GameObject parent )
		{
			int count = 0;
			foreach ( var marker in scene.Components.GetAll<SpawnMarker>() )
			{
				if ( !marker.Enabled && marker.GameObject.Enabled )
					continue; // already consumed

				SpawnForMarker( marker, parent );
				marker.GameObject.Enabled = false; // consume
				count++;
			}
			Log.Info( $"Lute: NPCSpawner spawned {count} NPCs from markers." );
			return count;
		}

		/// <summary>
		/// Spawn the NPC configured by a single marker. Dispatches on
		/// <see cref="SpawnMarker.NpcType"/>. Unknown types are logged
		/// and skipped (no body spawned).
		/// </summary>
		static void SpawnForMarker( SpawnMarker marker, GameObject parent )
		{
			var name = string.IsNullOrWhiteSpace( marker.NpcName )
				? $"{marker.NpcType}_{marker.GameObject.Name}"
				: marker.NpcName;

			switch ( marker.NpcType )
			{
				case "Builder":
					SpawnBuilder( marker, parent, name );
					break;
				default:
					Log.Warning( $"Lute: NPCSpawner unknown NpcType '{marker.NpcType}' on marker '{marker.GameObject.Name}' — skipped." );
					break;
			}
		}

		/// <summary>
		/// Spawn a builder NPC (citizen body + NPCBuilderController + a
		/// sibling NPCBuilder) at the marker's position. The structure is
		/// built at the marker's world position; the body starts a few
		/// meters north so the controller walks to the site.
		/// </summary>
		static void SpawnBuilder( SpawnMarker marker, GameObject parent, string name )
		{
			const float M = 39.37f;
			var sitePos = marker.WorldPosition;

			// Structure GameObject with NPCBuilder.
			var structGo = marker.Scene.CreateObject( true );
			structGo.Name = $"Structure_{name}";
			structGo.SetParent( parent );
			structGo.WorldPosition = sitePos;
			structGo.WorldRotation = Rotation.Identity;

			var builder = structGo.AddComponent<NPCBuilder>();
			builder.WealthFactor = marker.WealthFactor;
			builder.BaseWidth = marker.BaseWidth;
			builder.BaseHeight = marker.BaseHeight;
			builder.CellSize = 100f;
			builder.WallHeight = 200f;
			builder.FloorThickness = 10f;
			builder.BuildInterval = 0.2f;
			builder.WallMaterial = "materials/dev/gray_75.vmat";
			builder.FloorMaterial = "materials/dev/gray_50.vmat";
			builder.LayoutSeed = marker.LayoutSeed;

			// Body GameObject — citizen + colliders + Rigidbody + PlayerController + controller.
			// Spawn close to the ground (z=4, just above the floor at z=0-3) so
		// the PlayerController's short 2-unit ground trace can reach the floor.
		// Spawning at z=64 leaves the body floating ~60 units above the floor
		// and the ground trace never hits.
		SpawnCitizenBody( marker.Scene, parent, name, sitePos + new Vector3( 0f, 5f * M, 4f ), builder );
		}

		/// <summary>
		/// Spawn a citizen body with the standard collider/rigidbody/
		/// PlayerController stack and attach an NPCBuilderController wired
		/// to the given builder. Mirrors LuteWorld.SpawnBuilderBody so the
		/// body setup is identical whether spawned from a marker or directly.
		/// </summary>
		public static GameObject SpawnCitizenBody(
			Scene scene, GameObject parent, string name,
			Vector3 bodyPos, NPCBuilder builder )
		{
			var go = scene.CreateObject( true );
			go.Name = name;
			go.SetParent( parent );
			go.WorldPosition = bodyPos;
			go.WorldRotation = Rotation.Identity;

			// Body — citizen model.
			var bodyGo = scene.CreateObject( true );
			bodyGo.Name = "Body";
			bodyGo.SetParent( go );
			bodyGo.WorldPosition = Vector3.Zero;
			bodyGo.WorldRotation = Rotation.Identity;
			var bodyRenderer = bodyGo.AddComponent<SkinnedModelRenderer>();
			bodyRenderer.Model = Model.Load( "models/citizen/citizen.vmdl" );

			// Colliders — capsule + box (PlayerController will resize these).
			var collidersGo = scene.CreateObject( true );
			collidersGo.Name = "Colliders";
			collidersGo.SetParent( go );
			var capsule = collidersGo.AddComponent<CapsuleCollider>();
			capsule.Radius = 16f;
			capsule.Start = new Vector3( 0, 0, 0 );
			capsule.End = new Vector3( 0, 0, 72f );
			var box = collidersGo.AddComponent<BoxCollider>();
			box.Scale = new Vector3( 32f, 32f, 72f );

			// Rigidbody for physics.
			var rb = go.AddComponent<Rigidbody>();
			rb.Gravity = true;

			// PlayerController — input disabled, NPC moves via WishVelocity.
			var controller = go.AddComponent<PlayerController>();
			controller.UseInputControls = false;
			controller.UseCameraControls = false;
			controller.UseAnimatorControls = true;
			controller.Renderer = bodyRenderer;

			// The controller.
			var npc = go.AddComponent<NPCBuilderController>();
			npc.Builder = builder;

			Log.Info( $"Lute: NPCSpawner citizen body '{name}' at {go.WorldPosition} (site={builder.BuildSiteCenter})." );
			return go;
		}
	}
}
