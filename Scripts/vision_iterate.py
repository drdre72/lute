#!/usr/bin/env python3
"""
Autonomous Vision-Driven World Builder Iteration Loop

1. Builds the world using world_builder.py logic
2. Captures a screenshot via /screenshot endpoint
3. Sends the screenshot to Mistral Vision API for evaluation
4. Parses structured feedback (score, issues, suggestions)
5. If score < threshold, adjusts parameters and rebuilds
6. Loops until the LLM is satisfied with Rust-level quality

Usage: python3 vision_iterate.py --api-key YOUR_MISTRAL_KEY [--max-iterations 5]
"""

import requests
import json
import random
import math
import time
import sys
import os
import base64
import argparse

# Import world_builder functions
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from world_builder import (
    SERVER, wait_for_server, clear_world, terrain_sculpt, terrain_paint,
    get_terrain_height, list_props, place, batch_place,
    PropLibrary, MapGen
)

MISTRAL_API = "https://api.mistral.ai/v1/chat/completions"
MISTRAL_MODEL = "mistral-large-latest"
SATISFACTION_THRESHOLD = 8  # out of 10

# Evaluation prompt — strict Rust-level quality criteria
EVAL_SYSTEM_PROMPT = """You are an expert game level designer specializing in survival game maps like Rust, Ark, and Conan Exiles.
You evaluate procedurally generated game worlds for quality, coherence, and visual appeal.

You will be shown a screenshot of a procedurally generated Unreal Engine world.
Evaluate it against Rust-level quality standards:

1. TERRAIN: Does the terrain have natural variation? Hills, valleys, elevation changes? Or is it flat/boring?
2. FOREST DENSITY: Are forests dense enough to feel natural? Rust maps have thick forests. Are trees clustered realistically?
3. MONUMENT PLACEMENT: Are monuments/buildings visible and well-placed? Do they feel like landmarks?
4. ROAD NETWORK: Is there a coherent road system connecting points of interest?
5. SPATIAL DISTRIBUTION: Are props spread across the map or clumped in one corner? Is there good use of the 3km canvas?
6. VISUAL VARIETY: Is there variety in prop types (trees, rocks, bushes, structures)?
7. NATURAL TRANSITIONS: Do zones blend naturally? Forests thin out at edges, rocks near water, etc.
8. OVERALL COMPOSITION: Does it look like a playable, engaging survival map?

Respond in EXACTLY this JSON format:
{
  "score": <1-10 integer>,
  "terrain_quality": "<brief assessment>",
  "forest_quality": "<brief assessment>",
  "monument_quality": "<brief assessment>",
  "road_quality": "<brief assessment>",
  "spatial_distribution": "<brief assessment>",
  "visual_variety": "<brief assessment>",
  "natural_transitions": "<brief assessment>",
  "overall": "<brief assessment>",
  "key_issues": ["<issue 1>", "<issue 2>", ...],
  "suggestions": ["<actionable suggestion 1>", "<actionable suggestion 2>", ...],
  "satisfied": <true/false>
}

Be HARSH. A score of 8+ means it genuinely looks like a Rust map. Most procedural maps will score 3-6.
Only say "satisfied": true if score >= 8."""

def capture_screenshot():
    """Capture screenshot from the engine and return base64 string."""
    try:
        r = requests.get(f"{SERVER}/screenshot", timeout=30)
        if r.status_code == 200:
            data = r.json()
            b64 = data.get("base64", "")
            if b64:
                print(f"  Screenshot captured: {data.get('size', 0)} bytes")
                return b64
            else:
                # Try reading from the saved path
                path = data.get("path", "")
                if path and os.path.exists(path):
                    with open(path, "rb") as f:
                        encoded = base64.b64encode(f.read()).decode()
                        return f"data:image/jpeg;base64,{encoded}"
                print(f"  ! Screenshot file not ready at {path}")
        else:
            print(f"  ! Screenshot failed: HTTP {r.status_code}")
    except Exception as e:
        print(f"  ! Screenshot error: {e}")
    return None

def evaluate_world(api_key, screenshot_b64, iteration):
    """Send screenshot to Mistral Vision API for evaluation."""
    print(f"\n--- Evaluating World (Iteration {iteration}) ---")

    user_content = [
        {
            "type": "text",
            "text": f"This is iteration #{iteration} of a procedural world build. "
                    f"Evaluate this screenshot against Rust-level survival map quality. "
                    f"Be specific about what you see — terrain shape, tree density, "
                    f"building placement, road visibility, and spatial distribution. "
                    f"Respond in the exact JSON format specified."
        },
        {
            "type": "image_url",
            "image_url": {"url": screenshot_b64}
        }
    ]

    payload = {
        "model": MISTRAL_MODEL,
        "messages": [
            {"role": "system", "content": EVAL_SYSTEM_PROMPT},
            {"role": "user", "content": user_content}
        ],
        "temperature": 0.3,
        "max_tokens": 2000
    }

    try:
        r = requests.post(
            MISTRAL_API,
            headers={
                "Authorization": f"Bearer {api_key}",
                "Content-Type": "application/json"
            },
            json=payload,
            timeout=120
        )

        if r.status_code == 200:
            data = r.json()
            content = data["choices"][0]["message"]["content"]
            print(f"  Raw LLM response length: {len(content)} chars")

            # Parse JSON from response (handle markdown code blocks)
            json_str = content
            if "```json" in json_str:
                json_str = json_str.split("```json")[1].split("```")[0]
            elif "```" in json_str:
                json_str = json_str.split("```")[1].split("```")[0]

            # Try to find JSON object
            if not json_str.strip().startswith("{"):
                start = json_str.find("{")
                end = json_str.rfind("}") + 1
                if start >= 0 and end > start:
                    json_str = json_str[start:end]

            evaluation = json.loads(json_str)
            return evaluation
        else:
            print(f"  ! Mistral API error: HTTP {r.status_code}")
            print(f"  Response: {r.text[:500]}")
    except json.JSONDecodeError as e:
        print(f"  ! JSON parse error: {e}")
        print(f"  Content: {content[:500] if 'content' in dir() else 'N/A'}")
    except Exception as e:
        print(f"  ! Evaluation error: {e}")

    return None

def print_evaluation(ev):
    """Pretty-print the evaluation results."""
    score = ev.get("score", 0)
    satisfied = ev.get("satisfied", False)

    print(f"\n{'='*60}")
    print(f"  SCORE: {score}/10  |  SATISFIED: {satisfied}")
    print(f"{'='*60}")

    fields = [
        "terrain_quality", "forest_quality", "monument_quality",
        "road_quality", "spatial_distribution", "visual_variety",
        "natural_transitions", "overall"
    ]
    for f in fields:
        val = ev.get(f, "N/A")
        label = f.replace("_", " ").title()
        print(f"  {label:25s}: {val}")

    issues = ev.get("key_issues", [])
    if issues:
        print(f"\n  Key Issues:")
        for i, issue in enumerate(issues, 1):
            print(f"    {i}. {issue}")

    suggestions = ev.get("suggestions", [])
    if suggestions:
        print(f"\n  Suggestions:")
        for i, s in enumerate(suggestions, 1):
            print(f"    {i}. {s}")

    print(f"{'='*60}\n")

def apply_suggestions_to_builder(ev, gen):
    """Adjust world builder parameters based on LLM suggestions."""
    suggestions = ev.get("suggestions", [])
    issues = ev.get("key_issues", [])
    all_feedback = " ".join(suggestions + issues).lower()

    changes = []

    # Forest density adjustments
    if any(kw in all_feedback for kw in ["dense", "more trees", "thicker", "forest", "sparse", "not enough trees"]):
        old_radius = MapGen.MAP_RADIUS if hasattr(MapGen, 'MAP_RADIUS') else 1400
        changes.append("Increasing forest density")
        # We'll modify the generate method's behavior via instance flags
        if not hasattr(gen, 'forest_multiplier'):
            gen.forest_multiplier = 1
        gen.forest_multiplier = min(gen.forest_multiplier + 1, 4)

    # Terrain elevation
    if any(kw in all_feedback for kw in ["terrain", "elevation", "hill", "valley", "flat", "mountain", "height"]):
        changes.append("Increasing terrain elevation variation")
        if not hasattr(gen, 'terrain_strength_mult'):
            gen.terrain_strength_mult = 1
        gen.terrain_strength_mult = min(gen.terrain_strength_mult + 0.5, 3.0)

    # Spatial spread
    if any(kw in all_feedback for kw in ["spread", "distribution", "clumped", "clustered", "corner", "canvas", "area", "3km"]):
        changes.append("Improving spatial distribution across map")
        if not hasattr(gen, 'spread_mult'):
            gen.spread_mult = 1
        gen.spread_mult = min(gen.spread_mult + 0.3, 2.0)

    # Rock/variety
    if any(kw in all_feedback for kw in ["rock", "variety", "bush", "detail", "decoration"]):
        changes.append("Adding more prop variety")
        if not hasattr(gen, 'variety_mult'):
            gen.variety_mult = 1
        gen.variety_mult = min(gen.variety_mult + 1, 3)

    if changes:
        print(f"  Applying adjustments: {'; '.join(changes)}")
    else:
        print(f"  No specific parameter adjustments identified from feedback")

    return changes

def save_iteration_log(iteration, ev, screenshot_b64):
    """Save iteration results to disk for tracking."""
    log_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "iteration_logs")
    os.makedirs(log_dir, exist_ok=True)

    # Save evaluation JSON
    log_path = os.path.join(log_dir, f"iteration_{iteration}.json")
    with open(log_path, "w") as f:
        json.dump(ev, f, indent=2)

    # Save screenshot
    if screenshot_b64 and "," in screenshot_b64:
        img_data = screenshot_b64.split(",")[1]
        img_path = os.path.join(log_dir, f"iteration_{iteration}.jpg")
        with open(img_path, "wb") as f:
            f.write(base64.b64decode(img_data))

    print(f"  Saved log: {log_path}")

def run_build_cycle(iteration, gen):
    """Run a single build cycle: clear, sculpt, build."""
    print(f"\n{'#'*60}")
    print(f"# BUILD CYCLE {iteration}")
    print(f"{'#'*60}")

    print("\n=== Clearing Existing World ===")
    clear_world()
    time.sleep(2)

    # Re-generate with current parameters
    gen.placed = 0
    gen.road_points = []
    gen.monuments = []

    gen.generate()
    time.sleep(3)  # Let rendering settle

def main():
    global MISTRAL_MODEL

    parser = argparse.ArgumentParser(description="Vision-driven world builder iteration loop")
    parser.add_argument("--api-key", required=True, help="Mistral API key")
    parser.add_argument("--max-iterations", type=int, default=5, help="Maximum iterations")
    parser.add_argument("--model", default=MISTRAL_MODEL, help="Mistral model to use")
    args = parser.parse_args()

    MISTRAL_MODEL = args.model

    print("=" * 60)
    print("  AUTONOMOUS VISION-DRIVEN WORLD BUILDER")
    print(f"  Model: {MISTRAL_MODEL}")
    print(f"  Max iterations: {args.max_iterations}")
    print(f"  Satisfaction threshold: {SATISFACTION_THRESHOLD}/10")
    print("=" * 60)

    # Wait for server
    if not wait_for_server():
        print("ERROR: Server not responding.")
        sys.exit(1)

    # Gather props
    print("\nGathering all props...")
    all_props = list_props("", 2000)
    print(f"Total props available: {len(all_props)}")

    if not all_props:
        print("ERROR: No props found.")
        sys.exit(1)

    lib = PropLibrary(all_props)
    gen = MapGen(lib)

    # Iteration loop
    best_score = 0
    best_iteration = 0

    for iteration in range(1, args.max_iterations + 1):
        # Build
        run_build_cycle(iteration, gen)

        # Capture screenshot
        print("\n=== Capturing Screenshot ===")
        screenshot_b64 = capture_screenshot()

        if not screenshot_b64:
            print("  ! No screenshot available, skipping evaluation")
            continue

        # Evaluate
        evaluation = evaluate_world(args.api_key, screenshot_b64, iteration)

        if not evaluation:
            print("  ! Evaluation failed, retrying screenshot...")
            time.sleep(5)
            screenshot_b64 = capture_screenshot()
            if screenshot_b64:
                evaluation = evaluate_world(args.api_key, screenshot_b64, iteration)

        if not evaluation:
            print("  ! Evaluation failed twice, continuing to next iteration")
            continue

        # Print results
        print_evaluation(evaluation)

        # Save logs
        save_iteration_log(iteration, evaluation, screenshot_b64)

        score = evaluation.get("score", 0)
        satisfied = evaluation.get("satisfied", False)

        if score > best_score:
            best_score = score
            best_iteration = iteration
            print(f"  *** New best score: {best_score}/10 (iteration {best_iteration}) ***")

        # Check satisfaction
        if satisfied and score >= SATISFACTION_THRESHOLD:
            print(f"\n{'*'*60}")
            print(f"  SATISFIED! Score: {score}/10 on iteration {iteration}")
            print(f"  World meets Rust-level quality standards.")
            print(f"{'*'*60}")
            break

        # Apply suggestions for next iteration
        if iteration < args.max_iterations:
            print("\n=== Adjusting Parameters for Next Iteration ===")
            apply_suggestions_to_builder(evaluation, gen)

            # Print what the LLM saw
            overall = evaluation.get("overall", "")
            if overall:
                print(f"\n  LLM overall assessment: {overall}")

            print(f"\n  Preparing iteration {iteration + 1}...")
            time.sleep(3)

    # Final summary
    print(f"\n{'='*60}")
    print(f"  ITERATION COMPLETE")
    print(f"  Best score: {best_score}/10 (iteration {best_iteration})")
    print(f"  Total iterations: {iteration}")
    if best_score >= SATISFACTION_THRESHOLD:
        print(f"  Result: SATISFIED — Rust-level quality achieved!")
    else:
        print(f"  Result: Not yet at Rust-level quality.")
        print(f"  Review iteration_logs/ for detailed feedback.")
    print(f"{'='*60}")

if __name__ == "__main__":
    main()
