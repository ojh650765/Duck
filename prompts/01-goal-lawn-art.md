# Goal 01 - 게임 전체

프로젝트를 시작한 최초 지시입니다. 라운드 1 전체와 경기장 · 심사 · WebGL 빌드가 여기서 나왔습니다.

---

```text
Build a highly polished 3D game in Unity 6 using URP and export it as WebGL. Do not replace it with Three.js, Babylon.js, PlayCanvas, React, or a fake browser prototype.

The game is about a duck driving a lawn mower and cutting giant pictures into grass under a time limit. When time ends, reveal the result from above and have animal judges score it. Treat this as a starting point, not a complete design document. Think for yourself about what makes it fun, tactile, readable, funny, replayable, and visually memorable.

Do not build the opening story cutscene yet. Focus on the playable loop, mower feel, grass cutting, map, result reveal, judges, UI, audio, retry flow, performance, and art direction.

Use Unity MCP actively. Inspect and modify the real project, create scenes, place the map, configure components, build prefabs and materials, set up cameras and lighting, enter Play Mode, fix errors, save scenes, capture screenshots, and build WebGL. Do not only write scripts and leave scene assembly to me.

The mower must be enjoyable to control. Create tension between speed and accuracy. Tune steering, momentum, drift, braking, boost, collisions, camera feedback, animation, particles, and audio until driving is fun by itself.

Grass cutting must feel physical, not like a flat texture being erased. Use an efficient mask, render texture, shader, mesh, or hybrid solution. Cut and uncut grass should differ clearly in height, density, motion, color, debris, wheel tracks, and edge treatment while remaining readable from above.

Create a compact but rich competition map with strong scale, sightlines, readability, and composition. The world should feel alive through spectators, animals, plants, water, distant activity, reactions, and environmental storytelling. Do not scatter props randomly or leave an empty test field.

You have access to Blender, running on this laptop. Read the Blender skill and have a dedicated subagent use it to model high-quality assets. I think Blender work cannot be parallelized, so queue it for that agent. Use Blender for the duck, mower, judges, major landmarks, and anything that looks blocky, generic, crude, placeholder-like, or obviously procedural. I want intentional, beautiful, expressive, animation-ready 3D models with strong silhouettes and thoughtful proportions.

Procedural generation is welcome for grass, foliage, debris, and secondary dressing, but it must never make the game look random, repetitive, blocky, or unfinished.

Delegate work to parallel subagents where useful, including gameplay, rendering, environment layout, Blender assets, animation, UI, audio, optimization, and QA. Let them inspect problems and propose improvements, then integrate their work into one coherent game.

Continuously test the WebGL build in a browser. Target stable 60fps on a reasonable desktop without making the world empty. Optimize with instancing, batching, pooling, LOD, culling, texture budgets, and profiling.

Capture frame sheets across gameplay, map views, the overhead reveal, judges, UI, transitions, and retries. Find and fix z-fighting, banding, clipping, weak lighting, bad scale, repetitive placement, unreadable UI, camera intersections, and WebGL-specific artifacts.

Set up an independent judge subagent. Have it compare screenshots and footage against polished stylized commercial 3D games and give specific criticism of models, materials, lighting, grass, animation, environment, composition, camera work, UI, transitions, readability, performance, and cohesion. Address the criticism, rebuild, capture new evidence, and repeat. Do not weaken the judge’s criteria.

Do not stop when the systems merely work. Keep iterating until the mower is fun, grass cutting is satisfying, the map feels alive, Blender assets look intentional, the reveal and judges create a strong payoff, retries are immediate, WebGL runs reliably, and the whole game feels authored rather than generated.
```
