# Project Instructions

Read and follow @docs/GAME_VISION.md before planning or implementing work.

## Project

- Build a real 3D game in Unity 6 using URP.
- The final target is an exported WebGL build running in a real browser.
- Do not replace the project with Three.js, Babylon.js, PlayCanvas, React,
  or a fake browser prototype.
- Do not build the opening story cutscene yet.
- Focus on the playable mowing loop, map, visual identity, result reveal,
  judges, retry flow, browser performance, and polish.

## Unity MCP

- Use the connected Unity MCP actively.
- Inspect the existing project before making changes.
- Create and modify scenes, GameObjects, prefabs, materials, lighting,
  cameras, components, and map layout directly in Unity.
- Do not only write scripts and leave scene assembly to the user.
- Save modified scenes and assets.
- Enter Play Mode and inspect the Console after meaningful changes.
- Fix compilation errors before continuing.
- Capture screenshots from the actual gameplay camera to review composition.
- Test exported WebGL builds in a browser regularly.

## Blender

- Blender is installed on this laptop.
- Read the Blender skill before using Blender.
- Assign or queue a dedicated Blender subagent for important 3D assets.
- Use Blender for the duck, lawn mower, animal judges, important landmarks,
  and any asset that looks blocky, generic, crude, or obviously procedural.
- Hero assets must have intentional silhouettes, proportions, topology,
  pivots, materials, and animation-ready construction.
- Do not accept primitive placeholders as final assets.

## Working Method

- Treat the game premise as a starting point, not a complete specification.
- Think critically and identify missing gameplay, feedback, animation,
  environmental, presentation, and quality requirements independently.
- Delegate independent tasks to parallel subagents where appropriate.
- Integrate their work into one coherent visual and mechanical direction.
- Prefer iterative blockout, screenshot review, asset replacement,
  browser testing, and refinement over one-pass implementation.
- Never declare completion merely because a feature technically works.

## Quality

- The mower must be enjoyable without relying on presentation.
- Grass cutting must look and feel physical rather than like texture erasure.
- The map must feel intentionally composed, alive, and readable.
- The overhead reveal and animal judging sequence must provide strong payoff.
- Avoid default Unity presentation, random prop scattering, asset-flip visuals,
  exposed placeholders, and obviously procedural hero assets.
- Target stable 60fps in a reasonable desktop browser.
- Fix underlying visual problems rather than hiding them with camera angles.

## Verification

- Review the actual WebGL build, not only the Unity Editor.
- Capture frame sheets across gameplay, environment views, reveal, judges,
  UI, transitions, and retries.
- Inspect for rendering artifacts, clipping, poor scale, weak composition,
  camera intersections, broken animation, repetitive placement, and UI issues.
- Use an independent judge subagent for visual criticism.
- Address its criticism and repeat the review without weakening its standards.