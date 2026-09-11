using System;
using System.Collections.Generic;

namespace Lute.Building
{
	/// <summary>
	/// Architectural element within a monument volume. The architectural
	/// grammar decomposes a <see cref="MonumentVolume"/> into a tree of
	/// these elements (bays, columns, arches, windows, doors, cornices),
	/// which the detail grammar then turns into <see cref="BlueprintPiece"/>s.
	///
	/// This is the Phase 4 intermediate layer the professor asked for:
	///
	///   Massing (volumes)
	///     -> ArchitecturalGrammar (this: ArchitecturalElement tree)
	///       -> StyleGrammar (period constraints)
	///         -> DetailGrammar (pieces)
	///           -> Blueprint
	///
	/// Instead of jumping straight from "Nave volume" to 14,000 floor/
	/// wall/roof pieces, we first say "Nave = 6 bays, each bay = 2 columns
	/// + 1 arch + 1 window + clerestory", and only then emit pieces.
	/// </summary>
	public class ArchitecturalElement
	{
		/// <summary> Element kind (see <see cref="ArchitecturalElementKind"/>). </summary>
		public ArchitecturalElementKind Kind { get; set; }

		/// <summary> Display name (e.g. "Bay 3", "NorthColumn"). </summary>
		public string Name { get; set; }

		/// <summary> Position relative to the parent volume's origin (inches). </summary>
		public Vector3 Offset { get; set; }

		/// <summary> Yaw rotation in degrees. </summary>
		public float Rotation { get; set; }

		/// <summary> Width in inches (X). </summary>
		public float Width { get; set; }

		/// <summary> Depth in inches (Y). </summary>
		public float Depth { get; set; }

		/// <summary> Height in inches (Z). </summary>
		public float Height { get; set; }

		/// <summary> Material override (empty = inherit from volume). </summary>
		public string MaterialOverride { get; set; } = "";

		/// <summary> Child elements (e.g. a Bay contains Columns + Arch + Window). </summary>
		public List<ArchitecturalElement> Children { get; set; } = new();

		/// <summary> Add a child element. Returns this for chaining. </summary>
		public ArchitecturalElement AddChild( ArchitecturalElement child )
		{
			Children.Add( child );
			return this;
		}
	}

	/// <summary> Kinds of architectural elements the grammar can emit. </summary>
	public enum ArchitecturalElementKind
	{
		Volume,         // root of a volume's element tree
		Bay,            // a structural bay (column-to-column division)
		Column,         // vertical support
		Arch,           // spanning element over an opening
		Window,         // opening with glazing
		Door,           // opening for passage
		Wall,           // solid wall segment
		Floor,          // horizontal slab
		Roof,           // roof slab/slope
		Cornice,        // horizontal molding band
		Pediment,       // triangular gable (facade top)
		Statue,         // decorative figure
		Lantern,        // crowning element on a dome
		Drum,           // cylindrical base of a dome
		Rib,            // dome ribbing
		Merlon,         // raised battlement section
		Architrave,     // flat beam atop columns
	}

	/// <summary>
	/// Architectural grammar: decomposes a <see cref="MonumentMassing"/>
	/// into a tree of <see cref="ArchitecturalElement"/>s. This is the
	/// layer between massing and detail — it captures architectural
	/// structure (bays, colonnades, drum+dome+rings) without committing to
	/// final piece coordinates yet.
	///
	/// The grammar is deterministic (no RNG) and period-aware: it reads
	/// the volume's <see cref="MonumentVolumeType"/> and roof type to
	/// decide how to subdivide. A Box becomes bays; a Colonnade becomes
	/// spaced columns + architrave; a Dome becomes drum + rings + lantern.
	/// </summary>
	public static class MonumentGrammar
	{
		/// <summary>
		/// Decompose an entire massing into an architectural element tree
		/// (one root <see cref="ArchitecturalElement"/> per volume).
		/// </summary>
		public static List<ArchitecturalElement> Decompose( MonumentMassing massing )
		{
			var roots = new List<ArchitecturalElement>();
			foreach ( var vol in massing.Volumes )
				roots.Add( DecomposeVolume( massing, vol ) );
			return roots;
		}

		/// <summary> Decompose a single volume into its element tree. </summary>
		public static ArchitecturalElement DecomposeVolume( MonumentMassing m, MonumentVolume vol )
		{
			float cs = m.CellSize;
			float w = vol.WidthCells * cs;
			float d = vol.DepthCells * cs;
			float h = vol.Stories * m.StoryHeight;

			var root = new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Volume,
				Name = vol.Name,
				Offset = vol.Offset,
				Rotation = 0,
				Width = w,
				Depth = d,
				Height = h,
				MaterialOverride = vol.WallMaterialOverride,
			};

			switch ( vol.Type )
			{
				case MonumentVolumeType.Box:
					DecomposeBox( root, m, vol );
					break;
				case MonumentVolumeType.Dome:
					DecomposeDome( root, m, vol );
					break;
				case MonumentVolumeType.Tower:
					DecomposeTower( root, m, vol );
					break;
				case MonumentVolumeType.Colonnade:
					DecomposeColonnade( root, m, vol );
					break;
			}

			return root;
		}

		// ── Box: subdivide into bays along the width ──

		static void DecomposeBox( ArchitecturalElement root, MonumentMassing m, MonumentVolume vol )
		{
			float cs = m.CellSize;
			float sh = m.StoryHeight;
			int bayCount = Math.Max( 1, vol.WidthCells / 2 ); // 2-cell bays
			float bayWidth = vol.WidthCells * cs / bayCount;

			for ( int story = 0; story < vol.Stories; story++ )
			{
				float z = story * sh;

				// Floor slab for this story
				root.AddChild( new ArchitecturalElement
				{
					Kind = ArchitecturalElementKind.Floor,
					Name = $"Floor_S{story}",
					Offset = new Vector3( 0, 0, z ),
					Width = vol.WidthCells * cs,
					Depth = vol.DepthCells * cs,
					Height = m.FloorThickness,
				} );

				// Bays along the width (north and south walls)
				for ( int b = 0; b < bayCount; b++ )
				{
					float bx = b * bayWidth;
					var bay = new ArchitecturalElement
					{
						Kind = ArchitecturalElementKind.Bay,
						Name = $"Bay_S{story}_N{b}",
						Offset = new Vector3( bx, 0, z ),
						Width = bayWidth,
						Depth = cs,
						Height = sh,
					};
					DecomposeBay( bay, m, vol, sh );
					root.AddChild( bay );

					var bayS = new ArchitecturalElement
					{
						Kind = ArchitecturalElementKind.Bay,
						Name = $"Bay_S{story}_S{b}",
						Offset = new Vector3( bx, (vol.DepthCells - 1) * cs, z ),
						Width = bayWidth,
						Depth = cs,
						Height = sh,
					};
					DecomposeBay( bayS, m, vol, sh );
					root.AddChild( bayS );
				}

				// East/west end walls (solid, no bays)
				for ( int y = 1; y < vol.DepthCells - 1; y++ )
				{
					root.AddChild( new ArchitecturalElement
					{
						Kind = ArchitecturalElementKind.Wall,
						Name = $"Wall_S{story}_E_{y}",
						Offset = new Vector3( 0, y * cs, z ),
						Width = cs,
						Depth = cs,
						Height = sh,
					} );
					root.AddChild( new ArchitecturalElement
					{
						Kind = ArchitecturalElementKind.Wall,
						Name = $"Wall_S{story}_W_{y}",
						Offset = new Vector3( (vol.WidthCells - 1) * cs, y * cs, z ),
						Width = cs,
						Depth = cs,
						Height = sh,
					} );
				}
			}

			// Roof element (type chosen by vol.RoofType; detail grammar
			// expands it into actual roof pieces).
			root.AddChild( new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Roof,
				Name = "Roof",
				Offset = new Vector3( 0, 0, vol.Stories * sh ),
				Width = vol.WidthCells * cs,
				Depth = vol.DepthCells * cs,
				Height = 0,
			} );
		}

		static void DecomposeBay( ArchitecturalElement bay, MonumentMassing m, MonumentVolume vol, float storyH )
		{
			// A bay = two corner columns + an arch + a window above
			float cs = m.CellSize;
			float colW = cs * 0.4f;

			bay.AddChild( new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Column,
				Name = "ColL",
				Offset = new Vector3( 0, 0, 0 ),
				Width = colW,
				Depth = colW,
				Height = storyH,
			} );
			bay.AddChild( new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Column,
				Name = "ColR",
				Offset = new Vector3( bay.Width - colW, 0, 0 ),
				Width = colW,
				Depth = colW,
				Height = storyH,
			} );
			bay.AddChild( new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Arch,
				Name = "Arch",
				Offset = new Vector3( colW, 0, storyH * 0.7f ),
				Width = bay.Width - colW * 2,
				Depth = cs,
				Height = storyH * 0.3f,
			} );
			bay.AddChild( new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Window,
				Name = "Window",
				Offset = new Vector3( colW, 0, storyH * 0.4f ),
				Width = bay.Width - colW * 2,
				Depth = cs * 0.2f,
				Height = storyH * 0.25f,
			} );
		}

		// ── Dome: drum + concentric rings + lantern ──

		static void DecomposeDome( ArchitecturalElement root, MonumentMassing m, MonumentVolume vol )
		{
			float cs = m.CellSize;
			float sh = m.StoryHeight;
			int r = vol.DomeRadiusCells;
			float diameter = r * 2 * cs;

			// Drum (cylindrical base)
			root.AddChild( new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Drum,
				Name = "Drum",
				Offset = new Vector3( 0, 0, 0 ),
				Width = diameter,
				Depth = diameter,
				Height = vol.Stories * sh,
			} );

			// Dome rings (concentric, shrinking)
			int rings = Math.Max( 2, r );
			for ( int ring = 0; ring < rings; ring++ )
			{
				float ringR = r - ( ring * r / rings );
				if ( ringR < 1 ) ringR = 1;
				root.AddChild( new ArchitecturalElement
				{
					Kind = ArchitecturalElementKind.Rib,
					Name = $"Ring_{ring}",
					Offset = new Vector3( 0, 0, vol.Stories * sh + ring * sh * 0.2f ),
					Width = ringR * 2 * cs,
					Depth = ringR * 2 * cs,
					Height = sh * 0.2f,
				} );
			}

			// Lantern on top
			root.AddChild( new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Lantern,
				Name = "Lantern",
				Offset = new Vector3( 0, 0, vol.Stories * sh + rings * sh * 0.2f ),
				Width = cs * 2,
				Depth = cs * 2,
				Height = sh * 0.5f,
			} );
		}

		// ── Tower: stacked stories with merlons ──

		static void DecomposeTower( ArchitecturalElement root, MonumentMassing m, MonumentVolume vol )
		{
			float cs = m.CellSize;
			float sh = m.StoryHeight;

			for ( int story = 0; story < vol.Stories; story++ )
			{
				float z = story * sh;
				root.AddChild( new ArchitecturalElement
				{
					Kind = ArchitecturalElementKind.Floor,
					Name = $"Floor_S{story}",
					Offset = new Vector3( 0, 0, z ),
					Width = vol.WidthCells * cs,
					Depth = vol.DepthCells * cs,
					Height = m.FloorThickness,
				} );
				// Perimeter walls as solid wall elements
				root.AddChild( new ArchitecturalElement
				{
					Kind = ArchitecturalElementKind.Wall,
					Name = $"Walls_S{story}",
					Offset = new Vector3( 0, 0, z ),
					Width = vol.WidthCells * cs,
					Depth = vol.DepthCells * cs,
					Height = sh,
				} );
			}

			// Merlons (battlement) on top
			int merlons = Math.Max( 2, vol.WidthCells );
			for ( int i = 0; i < merlons; i += 2 )
			{
				root.AddChild( new ArchitecturalElement
				{
					Kind = ArchitecturalElementKind.Merlon,
					Name = $"Merlon_{i}",
					Offset = new Vector3( i * cs, 0, vol.Stories * sh ),
					Width = cs,
					Depth = cs,
					Height = sh * 0.3f,
				} );
			}
		}

		// ── Colonnade: spaced columns + architrave ──

		static void DecomposeColonnade( ArchitecturalElement root, MonumentMassing m, MonumentVolume vol )
		{
			float cs = m.CellSize;
			float sh = m.StoryHeight;
			int spacing = Math.Max( 1, vol.ColumnSpacing );

			// Floor strip
			root.AddChild( new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Floor,
				Name = "Floor",
				Offset = new Vector3( 0, 0, 0 ),
				Width = vol.WidthCells * cs,
				Depth = cs,
				Height = m.FloorThickness,
			} );

			for ( int story = 0; story < vol.Stories; story++ )
			{
				float z = story * sh;
				for ( int x = 0; x < vol.WidthCells; x += spacing )
				{
					root.AddChild( new ArchitecturalElement
					{
						Kind = ArchitecturalElementKind.Column,
						Name = $"Col_S{story}_{x}",
						Offset = new Vector3( x * cs, 0, z ),
						Width = cs * 0.4f,
						Depth = cs * 0.4f,
						Height = sh,
					} );
				}
			}

			// Architrave on top
			root.AddChild( new ArchitecturalElement
			{
				Kind = ArchitecturalElementKind.Architrave,
				Name = "Architrave",
				Offset = new Vector3( 0, 0, vol.Stories * sh ),
				Width = vol.WidthCells * cs,
				Depth = cs,
				Height = sh * 0.2f,
			} );
		}
	}
}
