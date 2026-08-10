# Goal 03 - 라운드 3 영역 점령

완성된 스테이지를 건드리지 않고 새 스테이지를 추가하라는 지시입니다.

---

```text
Build a new, isolated 1v1v1v1 territory-control stage where the player and three NPC gardeners compete to cover the largest percentage of the arena with their own character color. Make it visually spectacular, review it in Unity and WebGL, and delegate parallel work where useful.

This is a new stage only. Do not redesign or visually alter any completed stage, scene or game mode. Preserve existing mower controls, characters, camera behavior, shared art and game flow. Prefer scene-specific components. If shared code must change, preserve behavior elsewhere and regression-test completed stages.

All four competitors remain mounted on lawn mowers. As each mower drives, the ground beneath and behind it changes into that competitor’s color and garden style. Do not make this look like flat paint. Use colored grass, flowers, leaves, turf patterns and stylized gardening details so claimed territory feels alive.

Driving over neutral ground claims it. Driving over an opponent’s ground replaces their ownership with the current character’s color. Overwriting enemy territory should feel much stronger than claiming empty ground. When the timer ends, calculate each competitor’s percentage of valid ground and declare the largest area the winner.

Design the arena for territory control, not as an empty square. Create loops, intersections, narrow contested routes, broad zones, shortcuts, ramps and a valuable center. Players should choose between safe expansion, stealing territory, defending routes and contesting high-value space.

Build an accurate WebGL-friendly ownership system using a render texture, splat map, grid or another efficient method. Do not spawn thousands of permanent decals, meshes or GameObjects. Territory must remain crisp and measurable without gaps, flickering, z-fighting, color bleeding or double counting.

The NPCs must follow the same rules as the player. Give them different strategies: outer expansion, aggressive stealing and center control. They should react to the map state, make believable mistakes and never teleport, claim remote areas or cheat.

Keep the existing low third-person mower camera. Follow mower heading and velocity naturally. Do not force the camera to stare at the arena center or constantly track opponents. Pull back during crowded moments and keep vehicle control independent from camera framing.

Make claiming territory extremely juicy. Use grass bending, rapid vegetation growth, flowers blooming, colored leaves, soil spray, tire tracks, animated borders and visible ownership waves. Large steals should trigger stronger world-space effects, audio, crowd reactions and environmental responses. Mower collisions should create suspension recoil, tire skid, debris and brief camera punch without stun-locking anyone.

Make the arena feel alive through reactive flags, spectators, drifting leaves and environmental motion. During the final seconds, intensify music, wind, crowd energy and world effects. When time expires, lock ownership, raise the camera into an overhead reveal, calculate all four percentages and announce the winner.

You have access to Blender. Read the Blender skill and queue a Blender subagent to improve assets that remain blocky, generic or procedural. The arena and gardening details should look intentionally designed.

Use Unity MCP actively to inspect the project, create and save the stage, configure the arena, implement territory rendering and scoring, create NPC behavior, connect the stage to the existing flow and test repeatedly in Play Mode and WebGL. Do not only write scripts and leave scene assembly or testing unfinished.

Review screenshots and recordings for unreadable borders, weak feedback, camera problems, AI failures, artifacts and performance spikes, then fix them. Use an impartial judge subagent to compare the result against polished commercial territory-control and party games, and iterate until the stage feels strategic, readable and highly satisfying.
```
