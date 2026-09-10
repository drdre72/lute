"""Append visual pass progress entry to PROGRESS_LOG.md"""
import os

path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'PROGRESS_LOG.md')

entry = """
- [2026-09-10] MARKET VISUAL PASS. Replaced all flat dev materials with custom PBR materials generated procedurally via agent/generate_textures.py (PIL, 512x512 tileable). 7 new .vmat files using complex.shader: stone_wall (block-pattern stone, high UV tiling for walls/towers/corners), stone_detail (low tiling for merlons/well rim), wood (plank grain for stalls/benches/posts), wood_house (higher tiling for housing), metal (dark iron, metalness=0.8, for portcullis), roof (terracotta tiles for awnings/well roof), plaza (flagstone for floors/bridges). Moat upgraded from black_cheap to water_dark (real water shader). Each material has color + normal + roughness maps. Lighting improved: plaza lanterns now cast shadows (radius 800), 4 gate torch lights added (warm, shadowed), central well light added. Visual details (61 new objects): 8 colored banners on poles atop watchtowers (red N / blue E / green S / gold W), 32 market goods boxes on stall counters (amber/leather/grain/produce tints), 8 dark door openings on houses facing plaza. Total monument children: 616 to 677. Vision verified via Moondream2: correctly identified stone walls ("brick or concrete"), wooden stalls ("brown wooden structures with red roofs"), metal portcullis ("black metal bars"), and tower banners ("colorful banners hanging from their tops"). Known Moondream limitation: light plaza stone triggers "snow" hallucination; some high-overview images return empty responses.
"""

with open(path, 'a') as f:
    f.write(entry)

print('Progress log updated')
