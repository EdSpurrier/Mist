# Mist for Unity: the plan and the Unity decisions

This is the plan for rebuilding [MIST-UNITY.md](MIST-UNITY.md) in this project, with the choices the
brief left open and the places where it does not map one-to-one onto Unity.

## Project

- **Unity 6000.2.9f1, URP 17.2, render graph.** The older of the two installed editors, so the package
  is known to work from Unity 6.2 up (it opens unchanged in 6.4). URP because it is the Asset Store
  default and its render graph gives clean per-camera passes with history textures. HDRP and Built-in
  are out of scope for v0.1.
- **Embedded UPM package** at `Packages/com.slaydgames.mist` with `Runtime`, `Editor` and `Shaders`
  folders and their own assembly definitions. The project around it is the development and demo host
  (`Assets/Scenes/MistDemo.unity`). This is the normal way to develop a package that will ship on the
  Asset Store, and it keeps the package free of project settings.
- Private GitHub repo `EdSpurrier/Mist`, Unity `.gitignore`, `UserSettings/` ignored (per-user editor
  state lives there), `ProjectSettings/MistPresets.asset` tracked (shared presets).

## Where each part of the brief went

| Brief | Unity |
|---|---|
| 1. One mist volume | `MistVolume` component. Base = transform position, yaw = transform Y rotation, size in fields so it can be clamped. Seed is a serialized random int set on create and re-rolled on duplicate. |
| Renderer-wide settings | `MistRendererFeature.Settings`, serialized on the URP renderer asset. |
| 2. Noise texture | `MistNoiseTexture`: 48^3 R8 Texture3D, built once on the CPU from the brief's code, Repeat + Bilinear. |
| 3. Each frame (CPU) | `MistFrameData`: frustum cull by the turned box's AABB, sort, keep 64 (16 for ground shade), wrap noise offsets to 0..1 in double precision, pack six float4 per volume into a `GraphicsBuffer`. Also packs up to 16 point/spot lights that reach the kept boxes and up to 8 clear zones. |
| 4.1-4.3 Shader | `MistCommon.hlsl` + `Mist.shader` pass "Mist March", a fullscreen raster pass at `resolutionScale`. Spans and the 8-slot bit mask as written. Sun shadow from URP's cascades (`MainLightRealtimeShadow`). |
| 4.4 Temporal / blur / upsample | Three more passes. History is two per-camera `RTHandle`s kept by the pass (dropped on resize, camera cut, or when temporal is off). Blur and upsample weights sample the camera depth at the low-res texel centres. |
| 4.5 Ground shade | A fullscreen pass before the march that multiplies the camera colour by the analytic shade (`Blend DstColor Zero`). |
| 4.6 Clear zones | `MistClearZone` component; heading is the transform's forward. |
| 5. Tools | `MistTool` (global `EditorTool`), `MistOverlay` (Scene view overlay), `MistVolumeEditor` (Inspector), `MistWindow` (panel: template, presets, options), `MistGizmos` (outlines). Unity's Undo, selection and serialization throughout. |
| Template, handle mode, hide-all, animate | `MistEditorSettings`, a `ScriptableSingleton` in `UserSettings/`. |
| Presets | `MistPresetLibrary`, a `ScriptableSingleton` in `ProjectSettings/` with the seven starters. |

## What does not fit Unity, and what was done instead

1. **"Multiply the sun's contribution" for the ground shade.** Unity's lit shaders cannot take an extra
   term, and a screen-space pass cannot separate sunlight from ambient. The pass multiplies the whole
   opaque colour by `lerp(1, shade, groundShadeStrength)` with `groundShadeStrength` = 0.75 by default,
   standing in for the sun's share. Sky pixels are untouched.
2. **Scene lights.** The project's renderer uses Forward+, whose light lists are clustered per screen
   tile and not usable from a fullscreen pass. Lights are packed on the CPU instead (nearest 16 that
   reach the kept boxes), with the brief's own falloff, and the shader picks up to 4 that reach the
   marched stretch of each ray. The volumes' own glow lights are skipped so the glow is not counted twice.
3. **Ambient colour.** Taken from `RenderSettings`: flat colour, tri-light sky/equator blend, or the
   skybox ambient probe sampled upward. Linear.
4. **Depth and matrices.** The ray runs from the near plane to the scene depth using an inverse
   view-projection built the same way URP builds its own (`GetGPUProjectionMatrix * GetViewMatrix`),
   so reversed Z and the Y flip are the same for the march, the temporal reprojection and the depth
   texture. Orthographic cameras take the linear depth path.
5. **Where it composites.** After the skybox, before transparents (`AfterRenderingSkybox`): transparents
   then draw over the mist. There is a setting to move it after transparents.
6. **Live preview in edit mode.** Edit-mode time comes from `EditorApplication.timeSinceStartup`; an
   editor hook repaints Scene views at ~30 Hz while volumes exist and "Animate" is on. "Update only on
   release" makes the pass reuse the last packed data while a slider is held.
7. **Ground height.** Placing and dragging use `HandleUtility.PlaceObject` (hits meshes with or without
   colliders), then physics, then the y plane of the drag start. The Lift field and the Move handles use
   a physics ray down; without colliders the ground is taken as y = 0.
8. **Glow lights.** The point light is a hidden child (`HideAndDontSave`) rebuilt on load, so scenes
   stay clean and nothing is serialized twice.
9. **XR.** Not supported in v0.1 (the targets are plain 2D textures).

## Steps

1. Volume component, noise texture, packer, renderer feature with the march and the composite.
   Check 1 (one box reads as a soft bank, shaded on its sun side, lit by a point light) and Check 4
   (turning the camera does not change the billows).
2. Spans, temporal, depth-aware blur and upsample. Checks 2 (no seam between overlapping boxes) and 3
   (clean edges on a pillar in front, with temporal on and off).
3. Scene lights, ground shade, clear zones, glow lights. Check 5 (presets, the glowing ones light the ground).
4. Editor tools: tool, overlay, window, gizmos, presets, template. Check 6.
