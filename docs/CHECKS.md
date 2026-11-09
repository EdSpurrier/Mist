# Section 7 checks, run 2026-11-09 in Unity 6000.2.9f1

All captures are Game camera renders of `Assets/Scenes/MistDemo.unity` at the renderer defaults
(resolution 0.5, temporal on, 1 blur pass, 24 steps) unless stated. Images are in `docs/checks/`.

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | One default box on flat ground reads as a low, soft, slowly rolling bank; shaded on its sun side; lit by a point light inside it | Pass | `c1_sun_t0.png` and `c1_sun_t4.png` (4 s apart, sun from the left, point light off) show the bank rolling and the ground shade under it. `c1_pointlight.png` adds a 1.5 intensity point light inside the box. |
| 2 | Two overlapping boxes blend with no seam | Pass | `c2_overlap.png`: a bluish and a warm box, the second turned 30 degrees, meeting without a seam. |
| 3 | A pillar in front of the mist has a clean edge, no halo, at 0.5 resolution, temporal on and off | Pass | `c3_edge_temporal.png` and `c3_edge_fresh.png`, 14 degree field of view on a pillar inside the default box. |
| 4 | Turning the camera does not change any box's billows | Pass | `c4_a.png` and `c4_b.png`: drift, rise and churn set to 0, temporal off, camera orbited 90 degrees. The billows are the same world-space shapes. By construction the noise is sampled at world positions. |
| 5 | Every preset roughly matches its name; Toxic glow, Violet haze and Lava steam glow and light the ground | Pass | `c5_row_final.png`: the seven starters in a row (Preset Row in the demo scene). `c5_glow.png`: dusk close-up, lit ground spots under the glowing three. |
| 6 | Place, select, drag, turn, size, delete, undo, redo, hide, presets, use for new boxes, in edit mode | Pass (by synthetic input) | Mouse events sent to the Scene view with the Mist tool active: a click on the ground placed a box from the template and selected it; a drag moved it along the ground keeping its lift; one undo removed the place-and-drag as a single step and redo restored it at the dragged position; a click on an existing box selected it; the Delete command removed the selection and undo/redo restored and removed it again. Hide, Use for new boxes, preset apply/save/rename/delete and the template were exercised from code. Turn and Size handles were not driven by synthetic input. |

## Notes from the run

- The mist is bright when a strong point light sits inside it. Unity point light intensities are
  higher than the source engine's, so demo lights of 1.5 to 2 read better than 4; this is scene
  tuning, not a renderer change.
- With the default sun of 0.45 and ambient 0.6 the mist leans toward the sky colour in daylight.
  Both are renderer settings on the Mist feature.
- Stopping Play mode reverts edit-mode changes made since the scene was last saved. Save before
  playing when you have placed boxes.
