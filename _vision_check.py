import sys, json, base64, requests
sys.path.insert(0, 'agent')
from sbox_eyes import call_tool

# Move camera further back and higher to see more walls
# Village center: -15748,-15748,0
# East wall at ~-10236
# Position camera at -8000,-15748,500 looking west and slightly down
print("Moving camera for wide wall view...")
r = call_tool('set_editor_camera', {'position': '-8000,-15748,600', 'angles': '20,270,0'})
print(f"Camera: {r}")

print("Taking screenshot...")
r = call_tool('editor_camera_screenshot')
if 'content' in r and r['content']:
    img_data = r['content'][0].get('data', '')
    if img_data:
        with open('scrap/village_wide.png', 'wb') as f:
            f.write(base64.b64decode(img_data))
        print(f'Saved ({len(base64.b64decode(img_data))} bytes)')

        with open('scrap/village_wide.png', 'rb') as f:
            img_b64 = base64.b64encode(f.read()).decode()

        payload = {
            "model": "qwen3-vl-4b-instruct",
            "messages": [
                {
                    "role": "user",
                    "content": [
                        {
                            "type": "text",
                            "text": "This is a view in S&Box game engine looking at a medieval village under construction. Can you see a line of stone walls? Describe all visible structures, their arrangement, colors, and any issues you notice."
                        },
                        {
                            "type": "image_url",
                            "image_url": {"url": f"data:image/png;base64,{img_b64}"}
                        }
                    ]
                }
            ],
            "temperature": 0.3,
            "max_tokens": 500
        }

        print("Sending to vision model...")
        r = requests.post("http://localhost:1234/v1/chat/completions", json=payload, timeout=120)
        r.raise_for_status()
        result = r.json()
        response = result['choices'][0]['message']['content']
        print(f"\n--- Vision Model Analysis ---\n{response}")
