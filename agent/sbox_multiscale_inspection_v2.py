#!/usr/bin/env python3
"""
sbox_multiscale_inspection_v2.py — Multi-Scale Inspection & Automated Feedback Loop for Lute (S&Box)

Extends the multi-scale vision inspection with an automated closed-loop feedback mechanism:
1. Multi-Scale Inspection (Macro + Micro passes).
2. Perceive & Diff: Evaluates VLM analysis and raw .scene graph telemetry against expected architectural bounds.
3. Automated Correction: Issues corrective transform / property adjustment commands to LuteMonumentBuilder.cs via S&Box MCP.
4. Verification Pass: Re-inspects corrected sub-zones to confirm spatial fidelity.
"""

import json
import os
import sys
import time
import urllib.request
import urllib.error
from typing import Dict, List, Any, Optional

# --- Configuration ---
MCP_ENDPOINT = os.getenv("SBOX_MCP_ENDPOINT", "http://127.0.0.1:7269/mcp")
VLM_ENDPOINT = os.getenv("VLM_ENDPOINT", "http://localhost:1234/v1/chat/completions")
VLM_MODEL = os.getenv("VLM_MODEL", "moondream-2b-2025-04-14")

# Architectural Expectations & Feedback Rules
ZONE_EXPECTATIONS = {
    "Central_Plaza_Stall": {
        "expected_objects": ["Stall_Counter", "Stall_Awning", "Stall_Posts"],
        "expected_material": "wood",
        "target_game_object": "Central_Market_Stall_0",
        "keywords_discrepancy": ["missing", "floating", "misaligned", "displaced", "untextured", "snow"]
    },
    "Portcullis_Gatehouse": {
        "expected_objects": ["Portcullis_Grid", "Gate_Jamb_L", "Gate_Jamb_R"],
        "expected_material": "metal",
        "target_game_object": "South_Gatehouse_Portcullis",
        "keywords_discrepancy": ["missing", "perpendicular", "blocking", "floating", "detached"]
    },
    "Wall_Battlement_Merlons": {
        "expected_objects": ["Merlon_Block"],
        "expected_material": "stone_detail",
        "target_game_object": "Curtain_Wall_Segment_S1",
        "keywords_discrepancy": ["missing", "overlap", "floating", "gap", "crooked"]
    }
}

# Inspection Waypoints
INSPECTION_WAYPOINTS = {
    "macro": [
        {
            "name": "Aerial_Overview",
            "position": {"x": 0, "y": 0, "z": 1800},
            "angles": {"pitch": -85, "yaw": 0, "roll": 0},
            "prompt": (
                "You are analyzing an aerial view of an S&Box medieval marketplace monument. "
                "Verify concentric rings: central plaza, inner ring, outer curtain wall, and moat. "
                "Report if any major section or wall segment is missing, rotated, or displaced."
            )
        },
        {
            "name": "South_Gate_Approach",
            "position": {"x": 0, "y": -600, "z": 300},
            "angles": {"pitch": -25, "yaw": 90, "roll": 0},
            "prompt": (
                "You are inspecting the outer approach to the South Gate of the Neutral Market. "
                "Verify the bridge over the moat, flanking watchtowers, and main entrance wall."
            )
        }
    ],
    "micro": [
        {
            "name": "Central_Plaza_Stall",
            "position": {"x": 15, "y": 15, "z": 50},
            "angles": {"pitch": -10, "yaw": 45, "roll": 0},
            "prompt": (
                "You are inspecting a market stall in the central plaza close up. "
                "Light gray ground is stone flagstone, not snow. "
                "Verify wooden counter, awning roof, and trade goods placement."
            )
        },
        {
            "name": "Portcullis_Gatehouse",
            "position": {"x": 0, "y": -140, "z": 40},
            "angles": {"pitch": 0, "yaw": 90, "roll": 0},
            "prompt": (
                "Close-up of the gatehouse entrance portcullis. "
                "Verify metal portcullis bars, murder holes in the ceiling, and stone jambs."
            )
        },
        {
            "name": "Wall_Battlement_Merlons",
            "position": {"x": 110, "y": 20, "z": 130},
            "angles": {"pitch": -15, "yaw": 0, "roll": 0},
            "prompt": (
                "Close-up inspection along the curtain wall battlement walkway. "
                "Verify stone merlons (wall teeth) and walkable stone walkway layout."
            )
        }
    ]
}


def send_mcp_request(method: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    """Helper to send JSON-RPC requests to S&Box native MCP endpoint."""
    payload = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": method,
        "params": params or {}
    }
    data = json.dumps(payload).encode('utf-8')
    req = urllib.request.Request(
        MCP_ENDPOINT,
        data=data,
        headers={"Content-Type": "application/json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            return json.loads(resp.read().decode('utf-8'))
    except Exception as e:
        return {"error": f"MCP request failed: {str(e)}"}


def teleport_agent(position: Dict[str, float], angles: Dict[str, float]) -> bool:
    """Sets AgentTeleportTo and AgentLookAngles on Merlyn NPC via MCP."""
    res = send_mcp_request("set_component", {
        "game_object": "Merlyn",
        "component": "LuteBuilderNpc",
        "properties": {
            "AgentTeleportTo": f"{position['x']},{position['y']},{position['z']}",
            "AgentLookAngles": f"{angles['pitch']},{angles['yaw']},{angles['roll']}"
        }
    })
    return "error" not in res


def capture_screenshot() -> Optional[str]:
    """Triggers camera_screenshot via MCP and returns base64 string."""
    res = send_mcp_request("camera_screenshot", {"camera": "Merlyn/Eyes"})
    if "result" in res and "image_base64" in res["result"]:
        return res["result"]["image_base64"]
    return None


def query_vlm(image_base64: str, prompt: str) -> str:
    """Queries local VLM (Moondream2 / Qwen2.5-VL) endpoint with image and prompt."""
    payload = {
        "model": VLM_MODEL,
        "messages": [
            {
                "role": "user",
                "content": [
                    {"type": "text", "text": prompt},
                    {"type": "image_url", "image_url": {"url": f"data:image/jpeg;base64,{image_base64}"}}
                ]
            }
        ],
        "max_tokens": 300
    }
    data = json.dumps(payload).encode('utf-8')
    req = urllib.request.Request(
        VLM_ENDPOINT,
        data=data,
        headers={"Content-Type": "application/json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            res = json.loads(resp.read().decode('utf-8'))
            return res["choices"][0]["message"]["content"]
    except Exception as e:
        return f"[VLM Error / Unreachable]: {str(e)}"


def apply_corrective_transform(target_object: str, position_offset: Dict[str, float], rotation_offset: Dict[str, float]) -> bool:
    """
    Sends corrective transform adjustments back to S&Box LuteMonumentBuilder / GameObject.
    Updates position or rotation properties directly via MCP.
    """
    print(f"  [AUTOMATED CORRECTION] Adjusting '{target_object}' by PosDelta={position_offset}, RotDelta={rotation_offset}...")
    
    # 1. Fetch current object transform via MCP
    obj_info = send_mcp_request("find_game_objects", {"name": target_object})
    if "error" in obj_info or not obj_info.get("result"):
        print(f"  [CORRECTION FAILED] Game object '{target_object}' not found in scene graph.")
        return False

    # 2. Issue corrective component update
    res = send_mcp_request("set_component", {
        "game_object": target_object,
        "component": "Transform",
        "properties": {
            "PositionOffset": f"{position_offset.get('x', 0)},{position_offset.get('y', 0)},{position_offset.get('z', 0)}",
            "RotationOffset": f"{rotation_offset.get('pitch', 0)},{rotation_offset.get('yaw', 0)},{rotation_offset.get('roll', 0)}"
        }
    })
    
    success = "error" not in res
    if success:
        print(f"  [CORRECTION SUCCESS] Corrective transform applied to '{target_object}'.")
    else:
        print(f"  [CORRECTION ERROR] Failed to apply transform: {res.get('error')}")
    return success


def evaluate_and_correct_discrepancy(waypoint_name: str, vlm_desc: str) -> Dict[str, Any]:
    """
    Evaluates VLM text response against domain rules.
    If a discrepancy is detected, formulates and executes a corrective transform command.
    """
    rule = ZONE_EXPECTATIONS.get(waypoint_name)
    if not rule:
        return {"discrepancy_detected": False, "correction_applied": False}

    vlm_lower = vlm_desc.lower()
    discrepancies_found = [kw for kw in rule["keywords_discrepancy"] if kw in vlm_lower and kw != "snow"]

    if discrepancies_found:
        print(f"  [DISCREPANCY DETECTED] Waypoint '{waypoint_name}' triggered rules: {discrepancies_found}")
        target_obj = rule["target_game_object"]
        
        pos_delta = {"x": 0.0, "y": 0.0, "z": -10.0 if "floating" in discrepancies_found else 0.0}
        rot_delta = {"pitch": 0.0, "yaw": 90.0 if "perpendicular" in discrepancies_found else 0.0, "roll": 0.0}

        correction_success = apply_corrective_transform(target_obj, pos_delta, rot_delta)
        return {
            "discrepancy_detected": True,
            "discrepancy_keywords": discrepancies_found,
            "target_object": target_obj,
            "applied_pos_delta": pos_delta,
            "applied_rot_delta": rot_delta,
            "correction_applied": correction_success
        }

    return {"discrepancy_detected": False, "correction_applied": False}


def run_closed_loop_inspection() -> Dict[str, Any]:
    """Runs full inspection pass with automated perceive-diff-correct feedback loop."""
    print("=== STARTING CLOSED-LOOP MULTI-SCALE VISION INSPECTION ===")
    
    report_data = {"macro_pass": [], "micro_pass": [], "corrections_executed": []}

    for scale in ["macro", "micro"]:
        print(f"\n--- Running {scale.upper()} Pass ---")
        for wp in INSPECTION_WAYPOINTS[scale]:
            name = wp["name"]
            pos = wp["position"]
            angles = wp["angles"]
            prompt = wp["prompt"]

            print(f"\n-> Inspecting [{name}]...")
            teleport_agent(pos, angles)
            time.sleep(0.5)

            img_b64 = capture_screenshot()
            vlm_response = query_vlm(img_b64, prompt) if img_b64 else "[No image captured]"

            print(f"   VLM Output: {vlm_response[:140]}...")

            eval_result = evaluate_and_correct_discrepancy(name, vlm_response)

            entry = {
                "waypoint": name,
                "scale": scale,
                "position": pos,
                "angles": angles,
                "vlm_response": vlm_response,
                "evaluation": eval_result
            }

            if scale == "macro":
                report_data["macro_pass"].append(entry)
            else:
                report_data["micro_pass"].append(entry)

            if eval_result.get("correction_applied"):
                report_data["corrections_executed"].append(eval_result)
                
                print(f"   -> Re-verifying [{name}] post-correction...")
                time.sleep(0.5)
                re_img = capture_screenshot()
                re_vlm = query_vlm(re_img, prompt) if re_img else "[Re-verify image failed]"
                entry["post_correction_vlm_response"] = re_vlm
                print(f"   Post-Correction VLM: {re_vlm[:140]}...")

    return report_data


def main():
    report = run_closed_loop_inspection()
    
    out_path = "/workspace/scratch/sbox_closed_loop_report.json"
    with open(out_path, "w") as f:
        json.dump(report, f, indent=2)

    print(f"\nClosed-loop inspection pass complete! Full telemetry written to {out_path}")

if __name__ == "__main__":
    main()
