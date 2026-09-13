# SW Corner Analysis — 2026-09-13

## Baseline (commit 4473ced — "previous push, closest")

State: junction-based corner frame with `TryGetCornerFrame` double-add bug
fixed. Headers placed at:

```
center = junction
    + ownerInward * (BrickModuleY * 0.5f + wythe * BrickModuleY)
    + nonOwnerInward * (brickLength * 0.5f)
```

- Header placed for ALL wythes (one per wythe per owner course).
- `ShouldButt` skips non-owner corner bricks (leaves gaps on non-owner courses).
- Topology validator reports `IsValid=true, IsBonded=true, BondDepth=9.84`.

## GPT Vision Findings (3 screenshots, 08:46–08:47)

### Screenshot 08:46:27
- **Projections:** Yes — perpendicular wall bricks project past exterior
  plane by ~half a brick, forming overhangs.
- **Non-corner uniformity:** Right wall face is uniform and co-planar away
  from corner.
- **Corner:** Not clean — brick ends interleave and overhang, ragged edge
  with visible voids.
- **Stair-step:** Yes — projecting brick ends create stair-step/comb pattern
  where every other course sticks out.
- **Gaps:** Yes — one-course-high opening on right wall (~2 brick lengths
  wide) where bricks are missing; smaller single-brick gap near corner.

### Screenshot 08:46:47
- **Projections:** Yes — many courses stick out past exterior planes by
  a third to half a brick length.
- **Non-corner uniformity:** No — several entire rows pushed forward while
  others recessed; facade not uniform.
- **Corner:** Jagged, interleaving seam with alternating projections.
- **Stair-step:** Yes — clear stair-step/comb of projections.
- **Gaps:** Deep recesses/gaps between overhanging rows read as voids.

### Screenshot 08:47:02
- **Projections:** Yes — 2-3 bricks per course cantilever out, forming
  long shelf-like overhangs.
- **Non-corner uniformity:** Away from corner, uniform and aligned with
  consistent running bond.
- **Corner:** Not clean — notched recess showing brick sides and interior
  voids.
- **Stair-step:** Yes — every course ends in tooth-like projections,
  alternating between two faces.
- **Gaps:** Yes — repeated floating/gap rows where corner bricks missing,
  leaving horizontal slits and voids.

## Root Cause Analysis

Two distinct defects are present:

### 1. Header overhang (owner courses)
The header body size (`BrickBodySize = 9.45 x 4.53 x 2.17` inches) is
slightly smaller than the module size (`BrickModuleX = 9.84`,
`BrickModuleY = 4.92`). The header center is offset by `BrickModuleY/2`
along `ownerInward`, but the body extends `bDepth/2` each way. Since
`bDepth (4.53) < BrickModuleY (4.92)`, the header body actually sits
*inside* the wall — but the `nonOwnerInward` offset uses `brickLength/2`
(module), while the body extends `bLen/2` (body). Since `bLen (9.45) <
BrickModuleX (9.84)`, the header is slightly *short* along nonOwnerInward,
leaving a small gap at the far end. The net effect is a slight projection
of ~0.2 inches at the corner on owner courses.

### 2. Gap rows (non-owner courses)
`ShouldButt` skips the corner brick on non-owner courses, leaving a gap.
The owner wall's header (on owner courses) fills the corner, but on
non-owner courses there is nothing — creating alternating header/gap
rows that read as a stair-step/comb pattern. This is the dominant
visible defect.

### 3. Multi-wythe header stacking
Headers are placed for ALL wythes at the same `ownerInward` offset
(`BrickModuleY/2`), but the `wythe * BrickModuleY` term shifts each
wythe's header further inward. However, all wythes get a header at the
corner, which may cause visible stacking on the wall face.

## Proposed Fix: Custom Corner Piece (Brick_Corner)

Instead of placing a standard header brick at the corner, craft a
**custom corner piece** (`Brick_Corner` or `BrickForm.Corner`) that:

1. **Fills the full corner volume** — spans both wall faces at the
   junction, eliminating both the header overhang AND the non-owner gap
   in a single piece.

2. **Is height-matched** — placed on EVERY course (not just owner
   courses), so there are no alternating gap rows. The corner piece
   replaces both the owner header AND the non-owner skipped brick.

3. **Has an L-shaped or square footprint** — sized to the wall
   thickness (`BrickModuleY` x `BrickModuleY`) so it sits flush within
   both wall planes without projecting.

4. **Alternates orientation by course** — on even courses the long
   axis aligns with one wall, on odd courses with the other, creating
   a true quoin bond pattern.

### Implementation sketch

```csharp
enum BrickForm { Full, Half, Quarter, Header, Corner }  // add Corner

// In PlaceCornerHeader -> PlaceCornerPiece
// Called on EVERY course (not just owner courses), wythe 0 only.
// The piece is a square footprint (wallDepth x wallDepth) centered
// on the junction, rotated to align with the owner/non-owner axes.
bool PlaceCornerPiece( int row, float z, int wythe )
{
    if ( !TryGetCornerFrame( out var junction, out var ownerInward,
                             out var nonOwnerInward ) )
        return false;

    // Square footprint: wallDepth x wallDepth, flush at junction
    float wallDepth = BrickModuleY;
    Vector3 center = junction
        + ownerInward * (wallDepth * 0.5f + wythe * BrickModuleY)
        + nonOwnerInward * (wallDepth * 0.5f);
    center.z = z;

    // Alternate yaw by course for quoin bond
    bool isOdd = (row % 2 == 1);
    float yaw = isOdd
        ? MathF.Atan2( nonOwnerInward.y, nonOwnerInward.x ) * 180f / MathF.PI
        : MathF.Atan2( ownerInward.y, ownerInward.x ) * 180f / MathF.PI;

    // Square body: wallDepth x wallDepth x brickH
    float bH = BrickBodySize.z;
    Vector3 size = new Vector3( wallDepth, wallDepth, bH );
    SpawnBox( center, size, WallMaterial, true, _villageRoot, yaw,
        PieceAnchor.Base, BrickForm.Corner, BrickOrientation.Header );
    return true;
}
```

### Benefits
- Eliminates gap rows (corner piece on every course).
- Eliminates header overhang (square footprint flush at junction).
- Eliminates multi-wythe stacking (wythe 0 only, other wythes use
  normal stretchers that butt against the corner piece).
- Creates a true quoin bond pattern (alternating orientation).
- Simplifies the `ShouldButt` logic — non-owner courses no longer skip;
  they place normal stretchers that butt against the corner piece.

### Trade-offs
- Requires a new `BrickForm.Corner` enum value and render-size handling
  in `SpawnBox`.
- The corner piece is a custom shape (square) not a standard brick
  proportion — may need a custom model or a scaled box.
- Topology validator (`WallCornerTopologyProbe`) needs updating to
  recognize `BrickForm.Corner` as a valid bond piece.

## Next Steps

1. Implement `BrickForm.Corner` and `PlaceCornerPiece`.
2. Call `PlaceCornerPiece` on EVERY course (not just owner courses),
   wythe 0 only.
3. Remove `ShouldButt` skip for corner bricks — non-owner courses place
   normal stretchers that butt against the corner piece.
4. Update `WallCornerTopologyProbe` to recognize `BrickForm.Corner`.
5. Rebuild, restart, accelerate, verify with GPT vision.
