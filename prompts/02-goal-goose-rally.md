# Goal 02 - 라운드 2 거위 패링

기존 프로토타입을 버리고 라운드 2를 다시 만들라는 지시입니다.

---

```text
Rebuild ROUND 3 into a juicy, polished 1v1v1v1 Goose Parry free-for-all. Review it constantly in Unity and in the real WebGL build, push the game feel and visuals hard, and delegate parallel work where useful.

Treat all existing Goose Rally work as a disposable prototype. Audit current scenes, scripts, prefabs, camera logic and state machines, but preserve nothing merely because it exists. Keep only useful parts. Replace conflicting systems instead of stacking more fixes on top. This design is the source of truth.

Use a dedicated GooseRally scene with a real transition from the previous round and a clean transition to final judging. Preserve each competitor’s garden result across scenes. Do not fake the level change inside the mowing scene.

Create one four-player free-for-all in a readable 2x2 grid. This is not four separate 1v1 matches. The player and three NPCs compete simultaneously, each mounted on a mower and protecting a garden behind their fence.

Geese enter up to a global limit. A goose attacks one garden; the defender parries it with the mower and redirects it, bouncing across the ground toward another competitor. Return direction should come from mower facing, impact angle and broad readable aiming sectors, not random targeting.

Each goose can be parried exactly twice. The first parry makes it faster and angrier. The second defeats it in a strong Goose KO and removes it. A miss breaks the correct fence, visibly damages a limited part of that garden, then removes the goose. Refill empty slots after a short delay. Start with one active goose, build to two, and allow three near the climax. The objective is to preserve as much garden as possible before time expires.

NPCs must use the same movement, parry, targeting and damage rules. They should react believably, make mistakes, redirect geese toward different opponents and never teleport or cheat.

Keep the existing low third-person mower camera language. The mower position, heading and velocity are the primary references. The goose must never be a permanent LookAt target. Fix camera ownership so exactly one controller writes the final pose. Remove conflicting Cinemachine targets and rotation writers. Use only a small temporary bias for threats approaching the player, pull back when several geese are active, and use off-screen indicators instead of forcing the camera to stare at a goose.

Make every parry absurdly satisfying: anticipation, hit stop, suspension compression, recoil, tire skid, dirt spray, goose squash and stretch, violent neck motion, feathers, directional trails, layered audio, crowd reactions, camera punches and FOV kicks. Perfect timing should feel dramatically stronger. The second-parry Goose KO must feel like a real elimination, not simple deletion.

Use world-space effects so the arena communicates danger. Geese should disturb grass and dust beneath their path; bounces should leave directional impacts; parries should push leaves and debris; perfect parries should create a pressure wave and vegetation reaction; garden hits should break the correct fence section and leave visible damage. Threatened quadrants should react through flags, props, lighting and ground cues. Keep effects directional, pooled and WebGL-friendly.

Do not make geese feel like balls with goose meshes. They should fly in, land badly, run aggressively, deform when struck, bounce, flap, recover and continue toward the next target.

Use Unity MCP actively to inspect the project, create and save the scene, configure the 2x2 arena, implement the goose lifecycle and active pool, set up NPC behavior, fix the camera, test repeatedly in Play Mode and WebGL. Do not only write scripts and leave setup or testing unfinished.

Review recordings and screenshots for double parries, bad targets, stuck geese, tunnelling, camera snaps, clipping, z-fighting, unreadable effects and broken WebGL behavior. Keep iterating until this feels like a polished centerpiece rather than a prototype.
```
