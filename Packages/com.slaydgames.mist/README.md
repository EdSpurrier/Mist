# Mist - volumetric mist volumes for URP

Placeable boxes of noisy, drifting, lit volumetric mist, with Scene view tools to place and tune them.
A port of the GridFall engine's mist to Unity 6 URP (render graph).

## Setup

1. Add the package (it is embedded in this project under `Packages/com.slaydgames.mist`).
2. **Tools > Mist > Add Mist To URP Renderers** adds the `Mist` renderer feature to every URP renderer
   in use. The renderer-wide settings (steps, self shadow, lights, ground shade, sun, ambient,
   resolution scale, temporal, blur passes) live on that feature.
3. **Tools > Mist > Mist Window** opens the panel; **Mist Tool** in the Scene view toolbar (or the
   Mist overlay) turns on placing.

## Using it

- **Place:** with the Mist tool active and *Place* on, click the ground. The new box uses the
  new-box template and sits at ground + lift (default lift −0.3, so it hugs the ground).
- **Select:** click a box. Click empty ground to deselect (with *Place* off).
- **Drag:** drag a selected box across the ground; it keeps its height above the ground.
- **Handles:** Move (x/z keep the lift, y is free), Turn (yaw ring), Size (a handle per side and the
  top, plus a centre handle that scales all three).
- **Inspector / Mist window:** Box, Look and Rolling groups edit the selection, or the template when
  nothing is selected. Hide/Show, *Use for new boxes*, and presets (apply, save as new, update,
  rename, delete, reset).
- **Hide all mist** in the Scene view overlay skips drawing the mist in Scene views; outlines stay.
- **Mist Clear Zone** component: a disc or wedge on the ground where the mist thins away.

## How it draws

Per camera: volumes are frustum-culled, sorted by distance (nearest 64 marched, nearest 16 shade
the ground), packed into a structured buffer and marched in a fullscreen pass at `resolutionScale`.
Overlapping boxes are merged into spans and marched together, so they never show seams. The result
is reprojected against the previous frame (temporal), blurred depth-aware, and upsampled depth-aware
onto the camera colour, after the sky and before transparents by default.

Lights: URP's main light (with its shadow cascades) and up to 4 point/spot lights per pixel.
Volumes with *Ground light* > 0 keep a hidden point light inside them so their glow lights the ground.

## Notes

- Units are metres. Boxes only turn about the vertical axis; transform scale is ignored.
- Requires Unity 6000.0+ with URP 17 and the render graph (compatibility mode is not supported).
- No XR support yet.
