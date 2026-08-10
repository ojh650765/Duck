# Goal 04 - 스테이지 연결

모든 스테이지를 하나의 흐름으로 잇고 전환을 다듬으라는 지시입니다.

---

```text
Connect every existing gameplay stage into one seamless, highly polished game flow and make every transition feel extremely juicy and intentional. Review the full experience in Unity and in the real WebGL build, and delegate parallel work where useful.

First inspect the actual project and identify the existing stages, their order, entry conditions, exit conditions and persistent data. Do not invent a new game structure or rebuild completed gameplay. Existing stages are the source of truth. This goal is only about connecting them and polishing transitions.

Do not redesign completed stage gameplay. Preserve existing mower controls, cameras, mechanics, environments and scoring unless a minimal integration fix is strictly required. Prefer a dedicated GameFlow / Transition system instead of rewriting each stage.

Every stage should follow OUTRO → TRANSITION → INTRO. Avoid abrupt SceneManager.LoadScene behavior. Hide loading behind camera movement, foreground wipes, fades, environmental animation or other intentional presentation. Never show black flashes, frozen frames, camera pops, missing frames or sudden audio cuts.

Make transitions feel physical and connected to the tournament world rather than using generic fades. Use the mower, character, arena gates, foliage, signs, crowds, banners or foreground props. Examples: the mower drives through a gate, leaves or grass fill the frame, a sign sweeps past camera, or the camera follows movement into the next scene.

Preserve motion between scenes. Whenever possible, match camera direction, character placement and movement so the next stage feels like a continuation of the same event rather than a disconnected minigame.

Give every stage a short polished intro:
- brief arena establishing camera move
- clear objective presentation without a large static instruction screen
- competitors preparing
- countdown
- smooth blend into the normal gameplay camera
- enable player control only after the intro is complete

Give every stage a satisfying outro:
- immediately stop scoring when time ends
- emphasize the final moment with a short slow-motion, sting or camera beat where appropriate
- show the result briefly
- show character and crowd reactions
- transition directly toward the next stage

Make the tournament escalate. Later transitions should use stronger crowd energy, music layers, flags, lights, confetti and faster cinematic pacing. The final-stage entrance should feel important and the transition into judging should feel like a real finale.

Create a persistent match-state system that carries only required data between scenes, such as competitor identity, colors, scores, garden state and damage. Do not rely on scene object references surviving loads. Prevent duplicated managers, cameras, audio systems or stale input after transitions.

Audio must transition as carefully as visuals. Use crossfades, music stems, crowd swells, engine sounds and transition stingers instead of abruptly stopping and restarting audio.

Use world-space effects to make transitions feel alive: leaves, grass, dust, banners, lights, crowds, confetti and environmental reactions. Avoid using the same full-screen fade for every connection.

Use Unity MCP actively to inspect all existing scenes and flow logic, implement the transition architecture, configure each stage connection and test the complete game from beginning to end in Play Mode and WebGL. Do not only write scripts and leave scene references, timelines or transitions unfinished.

Record the entire run and inspect it for black frames, loading hitches, camera snaps, audio cuts, duplicate objects, incorrect spawn positions, input activating too early and inconsistent UI. Keep iterating until every stage and final judging connects without the game ever feeling like it stopped to load another scene.
```
