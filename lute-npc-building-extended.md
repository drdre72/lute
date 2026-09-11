# Lute — Extended NPC Building System

Builds on the original `BuildingGrammar` / `NPCBuilder` guide. Adds room subdivision, a cancellation-safe build loop, structure loading, and bounds-based colliders.

---

## 1. Room-Subdivided `BuildingGrammar.cs`

Replaces the single-rectangle layout with a BSP-style recursive split. Higher `wealthFactor` values produce more interior walls (more rooms) instead of just a bigger box.

```csharp
using Sandbox;
using System;
using System.Collections.Generic;

public struct Vector2Int
{
    public int X;
    public int Y;
    public Vector2Int( int x, int y ) { X = x; Y = y; }
}

public struct RectInt
{
    public int X, Y, Width, Height;
    public RectInt( int x, int y, int w, int h ) { X = x; Y = y; Width = w; Height = h; }
}

public class BuildingGrammar
{
    private static readonly Random _rng = new();

    // Rooms smaller than this on either axis are never split further.
    private const int MinRoomSize = 3;

    public Dictionary<Vector2Int, string> GenerateLayout( int baseWidth, int baseHeight, float wealthFactor )
    {
        var layout = new Dictionary<Vector2Int, string>();
        int width = (int)(baseWidth * wealthFactor);
        int height = (int)(baseHeight * wealthFactor);

        // Outer perimeter
        for ( int x = 0; x < width; x++ )
        {
            for ( int y = 0; y < height; y++ )
            {
                var pos = new Vector2Int( x, y );
                layout[pos] = (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                    ? "WALL"
                    : "FLOOR";
            }
        }

        // Front door
        layout[new Vector2Int( width / 2, 0 )] = "DOOR";

        // Recursion depth scales with wealth: richer NPCs get more subdivided homes.
        // 1.0-1.9 -> 0 splits (single room), 2.0-2.9 -> ~1 split, 3.0+ -> 2+ splits
        int maxDepth = wealthFactor >= 3f ? 3 : wealthFactor >= 2f ? 2 : wealthFactor >= 1.5f ? 1 : 0;

        var interior = new RectInt( 1, 1, width - 2, height - 2 );
        SubdivideAndCarve( interior, maxDepth, layout );

        return layout;
    }

    private void SubdivideAndCarve( RectInt area, int depth, Dictionary<Vector2Int, string> layout )
    {
        bool canSplitH = area.Height >= MinRoomSize * 2 + 1;
        bool canSplitV = area.Width >= MinRoomSize * 2 + 1;

        if ( depth <= 0 || (!canSplitH && !canSplitV) )
            return; // leaf room — leave as open FLOOR, already carved by perimeter pass

        // Prefer splitting the longer axis so rooms stay roughly square.
        bool splitHorizontally = canSplitH && (!canSplitV || area.Height > area.Width);

        if ( splitHorizontally )
        {
            int splitY = area.Y + MinRoomSize + _rng.Next( area.Height - MinRoomSize * 2 );
            for ( int x = area.X; x < area.X + area.Width; x++ )
                layout[new Vector2Int( x, splitY )] = "WALL";

            // Doorway through the new partition so rooms stay reachable
            int doorX = area.X + _rng.Next( area.Width );
            layout[new Vector2Int( doorX, splitY )] = "DOOR";

            SubdivideAndCarve( new RectInt( area.X, area.Y, area.Width, splitY - area.Y ), depth - 1, layout );
            SubdivideAndCarve( new RectInt( area.X, splitY + 1, area.Width, area.Y + area.Height - splitY - 1 ), depth - 1, layout );
        }
        else
        {
            int splitX = area.X + MinRoomSize + _rng.Next( area.Width - MinRoomSize * 2 );
            for ( int y = area.Y; y < area.Y + area.Height; y++ )
                layout[new Vector2Int( splitX, y )] = "WALL";

            int doorY = area.Y + _rng.Next( area.Height );
            layout[new Vector2Int( splitX, doorY )] = "DOOR";

            SubdivideAndCarve( new RectInt( area.X, area.Y, splitX - area.X, area.Height ), depth - 1, layout );
            SubdivideAndCarve( new RectInt( splitX + 1, area.Y, area.X + area.Width - splitX - 1, area.Height ), depth - 1, layout );
        }
    }
}
```

**Why it's built this way:** the perimeter pass runs first and is untouched, so exterior walls/door logic from the original version still works. Subdivision only touches the interior rect, and `depth` — not room count directly — is what scales with wealth, since depth compounds (each level roughly doubles room count) and gives you a natural falloff instead of needing to hand-tune a room count.

---

## 2. Cancellation-Safe `NPCBuilder.cs`

Adds a `CancellationTokenSource` tied to the component's lifecycle, so a build in progress stops cleanly if the NPC is destroyed, reassigned, or the job system pulls them off the task.

```csharp
using Sandbox;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class NPCBuilder : Component
{
    [Property] public float BuildInterval { get; set; } = 0.5f;
    [Property] public float WealthFactor { get; set; } = 1.0f;
    [Property] public Model WallModel { get; set; }
    [Property] public Model FloorModel { get; set; }

    private Queue<KeyValuePair<Vector2Int, string>> _buildQueue = new();
    private CancellationTokenSource _cts;

    protected override void OnStart()
    {
        _cts = new CancellationTokenSource();

        var grammar = new BuildingGrammar();
        var layout = grammar.GenerateLayout( 4, 4, WealthFactor );

        foreach ( var kvp in layout )
            _buildQueue.Enqueue( kvp );

        _ = ConstructSequence( _cts.Token );
    }

    protected override void OnDestroy()
    {
        // Stops the async loop from touching a GameObject that no longer exists.
        _cts?.Cancel();
    }

    // Call this from your job/AI system when an NPC is reassigned mid-build.
    public void CancelConstruction()
    {
        _cts?.Cancel();
    }

    private async Task ConstructSequence( CancellationToken token )
    {
        try
        {
            while ( _buildQueue.Count > 0 )
            {
                token.ThrowIfCancellationRequested();

                var step = _buildQueue.Dequeue();
                SpawnSegment( step.Key, step.Value );

                await Task.DelaySeconds( BuildInterval );
            }
        }
        catch ( System.OperationCanceledException )
        {
            // Expected path when OnDestroy or CancelConstruction fires mid-build.
            // Remaining _buildQueue entries are simply dropped here — hand them
            // to a replacement builder if you want work to resume elsewhere.
        }
    }

    private void SpawnSegment( Vector2Int gridPos, string pieceType )
    {
        var segmentObject = Scene.CreateObject();
        segmentObject.Name = $"Structure_{pieceType}_{gridPos.X}_{gridPos.Y}";

        Vector3 localOffset = new Vector3( gridPos.X * 100f, gridPos.Y * 100f, 0f );
        segmentObject.Transform.Position = Transform.Position + localOffset;

        var renderer = segmentObject.Components.Create<ModelRenderer>();

        if ( pieceType == "WALL" && WallModel != null )
            renderer.Model = WallModel;
        else if ( pieceType == "FLOOR" && FloorModel != null )
            renderer.Model = FloorModel;
        else
            renderer.Model = CreateFallbackProceduralMesh( pieceType );

        AttachBoundsCollider( segmentObject, renderer, pieceType );
    }

    // Sizes the collider from the actual model bounds when one is available,
    // instead of a fixed size that stops matching once buildings aren't simple boxes.
    private void AttachBoundsCollider( GameObject segmentObject, ModelRenderer renderer, string pieceType )
    {
        var collider = segmentObject.Components.Create<BoxCollider>();

        if ( renderer.Model != null && renderer.Model.Bounds.Size.Length > 0f )
        {
            collider.Scale = renderer.Model.Bounds.Size;
        }
        else
        {
            collider.Scale = new Vector3( 100f, 100f, pieceType == "WALL" ? 200f : 10f );
        }
    }

    private Model CreateFallbackProceduralMesh( string pieceType )
    {
        var mb = new Model.Builder();
        mb.AddMesh( Mesh.CreateCube( new Vector3( 100f, 100f, 100f ), Material.Load( "materials/default/white.vmat" ) ) );
        return mb.Create();
    }
}
```

---

## 3. Save/Load Pair (`StructureSaveData.cs`)

The original guide only wrote the JSON. This adds the read-back path and an instant (non-animated) rebuild, since replaying the build delay on every scene load would be wrong — a load should restore the finished structure immediately.

```csharp
using Sandbox;
using System.Collections.Generic;
using System.Linq;

public class StructureSaveData
{
    public Vector3 WorldOrigin { get; set; }
    public Dictionary<string, string> GridData { get; set; } = new();
}

public static class StructurePersistence
{
    public static void SaveStructure( string filename, Vector3 origin, Dictionary<Vector2Int, string> layout )
    {
        var data = new StructureSaveData { WorldOrigin = origin };

        foreach ( var kvp in layout )
            data.GridData[$"{kvp.Key.X},{kvp.Key.Y}"] = kvp.Value;

        FileSystem.Data.WriteJson( filename, data );
    }

    public static Dictionary<Vector2Int, string> LoadLayout( string filename )
    {
        var data = FileSystem.Data.ReadJson<StructureSaveData>( filename );
        var layout = new Dictionary<Vector2Int, string>();

        foreach ( var kvp in data.GridData )
        {
            var parts = kvp.Key.Split( ',' );
            var pos = new Vector2Int( int.Parse( parts[0] ), int.Parse( parts[1] ) );
            layout[pos] = kvp.Value;
        }

        return layout;
    }

    public static Vector3 LoadOrigin( string filename )
    {
        return FileSystem.Data.ReadJson<StructureSaveData>( filename ).WorldOrigin;
    }
}
```

Add this method to `NPCBuilder` to rebuild instantly from a save file (no queue, no delay — the structure just appears complete):

```csharp
public void RebuildFromSave( string filename )
{
    Transform.Position = StructurePersistence.LoadOrigin( filename );
    var layout = StructurePersistence.LoadLayout( filename );

    foreach ( var kvp in layout )
        SpawnSegment( kvp.Key, kvp.Value );
}
```

---

## Implementation Instructions

**File placement.** In your s&box project, put these under `Code/` (s&box compiles everything in that folder automatically — no manual registration needed):
- `Code/Building/BuildingGrammar.cs`
- `Code/Building/NPCBuilder.cs`
- `Code/Building/StructureSaveData.cs`

Keep `Vector2Int` and `RectInt` in `BuildingGrammar.cs` as shown (or split into their own files if other systems need them) — just don't declare them twice across files, s&box's Roslyn hot-reload will throw a duplicate-type error.

**Wiring it up in the editor.**
1. Create or select the NPC's `GameObject` in the Scene editor.
2. Add Component → search `NPCBuilder`.
3. In the Inspector, assign `WallModel` / `FloorModel` from your project's model assets. Leave them blank first to verify the fallback cube path works before you wire in real art.
4. Set `WealthFactor` — try 1.0, 2.0, and 3.0 on three separate test NPCs to see the room-subdivision tiers side by side.

**Test order (do this before wiring into your job system):**
1. Place one NPC with `NPCBuilder` in an empty test scene, `WealthFactor = 1.0`. Press Play — confirm the single-room box builds segment-by-segment at the `BuildInterval` you set.
2. Bump `WealthFactor` to 2.5–3.0 and re-test — confirm interior partition walls and doorways appear and every room is reachable (walk it in-game; a subdivision bug that skips a doorway will wall off a room).
3. Call `_cts.Cancel()` (or destroy the NPC) mid-build via a debug console command — confirm the loop stops without throwing an unhandled exception in the s&box log.
4. Call `StructurePersistence.SaveStructure(...)` after a build finishes, clear the scene, then call `RebuildFromSave(...)` on a fresh `NPCBuilder` — confirm the structure reappears instantly at the same world position with no per-segment delay.

**Gotchas specific to this system:**
- `Model.Builder.Create()` is relatively expensive — the fallback path runs it once per segment. Fine for testing, but before scaling to many NPCs building simultaneously, cache one fallback `Model` per piece type instead of rebuilding it per-segment.
- The BSP subdivision uses `System.Random` seeded per-call, so two NPCs with identical `WealthFactor` will get different room layouts — that's intentional (visual variety per building), but if you want deterministic/reproducible layouts (e.g., for a specific quest building), seed `_rng` from the NPC's ID instead of using the shared static instance.
- `RebuildFromSave` reuses `SpawnSegment`, which reads `WallModel`/`FloorModel` from the component instance doing the rebuilding — make sure whatever `NPCBuilder` you call it on has those Properties assigned, or it'll silently fall back to procedural cubes.
- Doorway carving only guarantees local reachability between two directly-split rooms, not global reachability across 3+ splits. For `WealthFactor >= 3` (depth 3, up to 8 rooms), it's worth adding a flood-fill reachability check after generation as a safety net before you rely on this for gameplay.
