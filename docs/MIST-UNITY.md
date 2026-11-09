# Volumetric mist volumes for Unity: what it must do, and the tools to place it

This brief describes placeable volumetric mist boxes and the editor tools to place and tune them, from another engine of mine, to be rebuilt in Unity so the mist **looks and behaves the same**.

It gives the full logic: the data, the noise, the shaders and the tools' behaviour. The Unity choices are left to you:
- the render pipeline (URP, HDRP or Built-in) and how the pass plugs in (Renderer Feature, Custom Pass, command buffer);
- compute or fragment shaders;
- the editor framework (custom Inspector, EditorTool, Overlays, UI Toolkit or IMGUI);
- how volumes are stored (components in the scene, ScriptableObjects, a level file).

Shaders are written in HLSL. **Units:** distances are tuned for 1 unit ≈ 1 metre.

---

## 1. One mist volume

A mist volume is a box **turned about the vertical axis only (yaw)**, filled with noisy, drifting, lit mist.

| Field | Meaning | Default | Allowed range |
|---|---|---|---|
| position | The box's **base** (bottom centre) | | |
| width, height, depth | Full size along its own x, y, z | 8, 2, 8 | w/d 0.5–128, h 0.25–32 |
| yaw | Turn about +y, degrees (Unity's own Y rotation) | 0 | 0–360 |
| color | Tint of the light it scatters. Stored as a normal (sRGB) colour; the shader uses its linear value | (0.85, 0.9, 1) | 0–1 |
| density | Extinction per unit at its thickest ("Thickness") | 0.6 | 0–8 |
| softness | How far in from each side it fades in, as a share of the half size (0: hard walls) | 0.45 | 0–1 |
| falloff | Thinning toward the top (0: even, 1: nothing at the top) | 0.6 | 0–1 |
| glowColor, glowStrength | Light the mist gives off itself. It follows the billows and lights nothing around it | (0.4, 0.8, 1), 0 | strength 0–20 |
| light | Optional: intensity of a real point light inside the box, so glowing mist lights the ground (0: none) | 0 | 0–40 |
| noise | How much noise breaks it into billows (0: smooth) | 0.65 | 0–1 |
| noiseScale | Billow size, units | 2.5 | 0.2–40 |
| speed, direction | Drift speed (units/s) and heading (degrees, 0 = toward +z, 90 = toward +x) | 0.5, 60 | 0–20 |
| rise | Upward drift, units/s | 0.05 | −5 to 5 |
| churn | How fast the billows roll over in place | 0.3 | 0–10 |
| seed | Which billows. **Must be stable**: give each volume a random integer when it's created and serialize it. **Not** `GetInstanceID()` or draw order, which change | | |
| hidden | Kept but not drawn | false | |

Clamp every value to its range whenever it's loaded or set.

**Renderer-wide settings:**

| Setting | Meaning | Default |
|---|---|---|
| enabled | Draw the mist at all | on |
| steps | Maximum march steps per stretch of mist (quality, 4–64) | 24 |
| selfShadow | A short look toward the sun per sample, so the billows are shaded | on |
| lights | Scene point and spot lights scatter in the mist | on |
| groundShade | The ground under the boxes is darkened | on |
| sun | How strongly the sun lights the mist | 0.45 |
| ambient | Share of the sky or ambient colour it takes | 0.6 |
| resolutionScale | Resolution of the mist pass (0.25–1) | 0.5 |
| temporal | Blend each frame with the last | on |
| blurPasses | Depth-aware blur passes, 0–3 | 1 |

---

## 2. The noise texture

The noise is a **48³ single-channel 3D texture** that tiles. Build it once, in C# at load time or baked as an asset, with bilinear filtering and Repeat wrap. It's inverted Worley noise (round billows) blended with a little value noise:

```csharp
static float Hash3(int x, int y, int z, int seed) {
    unchecked {
        int h = x * 374761393 + y * 668265263 + z * 2147483647 + seed * 1442695041;
        h = (int)((uint)(h ^ (int)((uint)h >> 13)) * 1274126177u);
        return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296f;
    }
}
// Tileable value noise: a lattice of f cells per side that wraps.
static float ValueNoise(int x, int y, int z, int size, int f, int seed) {
    float sx = x / (float)size * f, sy = y / (float)size * f, sz = z / (float)size * f;
    int xi = Mathf.FloorToInt(sx), yi = Mathf.FloorToInt(sy), zi = Mathf.FloorToInt(sz);
    float S(float t) => t * t * (3 - 2 * t);
    float ux = S(sx - xi), uy = S(sy - yi), uz = S(sz - zi);
    int W(int v) => ((v % f) + f) % f;
    float H(int i, int j, int k) => Hash3(W(xi + i), W(yi + j), W(zi + k), seed);
    return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(H(0,0,0), H(1,0,0), ux), Mathf.Lerp(H(0,1,0), H(1,1,0), ux), uy),
                      Mathf.Lerp(Mathf.Lerp(H(0,0,1), H(1,0,1), ux), Mathf.Lerp(H(0,1,1), H(1,1,1), ux), uy), uz);
}
// value(x,y,z) = worley*0.65 + ValueNoise(..., 8, 21)*0.23 + ValueNoise(..., 16, 22)*0.12
//   worley = 1 - min(1, sqrt(nearestSqDist) * 1.1), with 6x6x6 feature points (one per cell, at
//   cell + (Hash3(x,y,z,1), Hash3(x,y,z,2), Hash3(x,y,z,3))), searching the 27 neighbouring cells with wrap,
//   each feature point moved to the unwrapped neighbour cell's place so the texture tiles.
```

One repeat of the texture holds **5 billows**, so it spans `noiseScale × 5` units.

---

## 3. Each frame (CPU)

1. **Pick:**
   - Keep the volumes that are enabled, not hidden, have density > 0 and have a non-zero size.
   - Frustum-cull each one by its world box (for a turned box: `ex = |cos|·hw + |sin|·hd`, `ez = |sin|·hw + |cos|·hd`).
   - Sort by distance to the camera and keep the nearest **64**. The first **16** also drive the ground shade.
2. **Noise offsets**, in texture units, wrapped to 0..1 so precision holds in long sessions:
   ```csharp
   float scale = Mathf.Max(0.05f, noiseScale) * 5f;
   float a = direction * Mathf.Deg2Rad;
   float Wrap(float x) => x - Mathf.Floor(x);
   Vector3 drift = new Vector3(Wrap(-Mathf.Sin(a) * speed * t / scale), Wrap(-rise * t / scale), Wrap(-Mathf.Cos(a) * speed * t / scale));
   float churnPhase = Wrap(-churn * t / scale);
   ```
3. **Pack** each volume into six `float4`s, in a structured buffer or arrays (centre = base + up × height/2, colours in linear):

   | float4 | x | y | z | w |
   |---|---|---|---|---|
   | A | centre x | centre y | centre z | cos(yaw) |
   | B | half width | half height | half depth | sin(yaw) |
   | C | color r | color g | color b | density |
   | D | glow r × strength | glow g × strength | glow b × strength | softness |
   | E | drift x | drift y | drift z | 1 / (noiseScale × 5) |
   | F | churnPhase | noise | falloff | seed |

`cos` and `sin` are of the yaw in radians. The world-to-box formula below matches Unity's own Y rotation, so yaw is just `transform.eulerAngles.y`.

---

## 4. The shader (a fullscreen pass after opaques, before post)

### 4.1 Box tests
```hlsl
float3 VolLocal(int i, float3 p) {                       // world -> box frame
    float c = A[i].w, s = B[i].w;  float3 d = p - A[i].xyz;
    return float3(c*d.x - s*d.z, d.y, s*d.x + c*d.z);
}
float3 VolDir(int i, float3 v) { float c = A[i].w, s = B[i].w; return float3(c*v.x - s*v.z, v.y, s*v.x + c*v.z); }
float2 VolHit(int i, float3 o, float3 dir) {             // slab test; t1 <= t0 means a miss
    float3 h = B[i].xyz, lo = VolLocal(i, o), ld = VolDir(i, dir);
    float3 inv = 1.0 / (abs(ld) < 1e-6 ? 1e-6 : ld);
    float3 ta = (-h - lo) * inv, tb = (h - lo) * inv;
    float3 tmin = min(ta, tb), tmax = max(ta, tb);
    return float2(max(max(tmin.x, tmin.y), max(tmin.z, 0)), min(min(tmax.x, tmax.y), tmax.z));
}
```

### 4.2 Density at a point
```hlsl
float VolShape(int i, float3 p) {
    float3 l = VolLocal(i, p), h = B[i].xyz, d = h - abs(l);
    if (min(d.x, min(d.y, d.z)) <= 0) return 0;
    float3 f = smoothstep(0, max(1e-3, h * D[i].w), d);
    float top = smoothstep(0, 1, (l.y + h.y) / max(2*h.y, 1e-3));
    return f.x * f.y * f.z * (1 - F[i].z * top);
}
float VolDensity(int i, float3 p) {
    float s = VolShape(i, p);  if (s <= 0) return 0;
    float k = F[i].y;
    if (k <= 0) return C[i].w * s;
    float3 q  = p * E[i].w + E[i].xyz + F[i].w * 0.137;
    float n1  = Noise3D.SampleLevel(sampler_Repeat, q, 0).r;
    float3 q2 = q * 2 + float3(0.37, F[i].x, 0.61) + (n1 - 0.5) * 0.35;   // the fine octave churns up, bent by the coarse
    float n   = smoothstep(0.15, 0.85, n1*0.65 + Noise3D.SampleLevel(sampler_Repeat, q2, 0).r*0.35);
    return C[i].w * saturate((s*(1 + k*0.3) - (1 - n)*k*1.2) / max(1 - k*0.6, 0.1));   // eats into the middle too
}
```

### 4.3 The march, per pixel
- **Ray:** from the camera's **near plane** to the scene depth: rebuild the world position from Unity's depth texture, minding reversed Z. With no depth, march out to the far distance. Starting at the near plane keeps orthographic cameras working.
- **Jitter:**
  ```hlsl
  float ign(float2 p) { return frac(52.9829189 * frac(0.06711056*p.x + 0.00583715*p.y)); }
  float jitter = frac(ign(pixelCoord) + _FrameJitter);   // _FrameJitter = frac(frameIndex * 0.6180339887) when temporal, else 0
  ```
- **Finding boxes:**
  - Slab-test every box and clip each hit to the scene depth.
  - Keep up to **8** hits, insertion-sorted by where the ray enters each one.
  - Merge the hits into **spans**: a box plus every later box that enters before the span ends.
  - March each span once with all its boxes together, keeping a bit mask over the ray's 8 slots and an id table. Density adds up; colour and glow are density-weighted averages.
  - **This is what keeps overlapping boxes from showing seams.**
- **Steps per span:** `clamp(ceil(len / 0.08), 2, steps)`.
- **Per sample:**
  ```hlsl
  float3 L = sunDirectionTowardSun;
  float g = 0.45;                                          // Henyey-Greenstein, gentle forward lobe
  float phase = (1 - g*g) / pow(1 + g*g - 2*g*dot(rayDir, L), 1.5) * 0.3 + 0.55;
  float3 sunCol = sunColor * sunIntensity * _Sun * phase;
  float ls = clamp(0.3 * smallestHalfSizeInSpan, 0.1, 3.0);

  float3 p = o + rayDir * (t0 + stepLen * (s + jitter));
  float d = SpanDensity(p, out col, out glow);
  if (d > 0.001) {
      float sunT = MainLightShadow(p);                     // the sun's shadow cascades at p (1 if none)
      if (_SelfShadow && sunT > 0)
          sunT *= exp(-(SpanDensity(p + L*ls)*ls + SpanDensity(p + L*ls*3)*ls*2));   // two taps toward the sun
      float3 c = (sunCol*sunT + ambientColor*_Ambient + SceneLights(p)) * col + glow;
      float st = exp(-d * stepLen);                         // Beer–Lambert
      inscatter += trans * c * (1 - st);
      trans *= st;
      if (trans < 0.01) break;                              // stop all spans
  }
  ```
- **Scene lights:** up to **4** point or spot lights per pixel that reach the marched stretch. The mist scatters every way, so there's no phase term. Falloff:
  `win = 1 − (d²/r²)²`, light = `color × intensity × win² / (d² + 1)`, and spots × `smoothstep(cosOuter, cosInner, dot(−toLight, spotDir))`.
  - Match this to Unity's own light attenuation if you prefer, so the mist agrees with the surfaces.
- **Output:** `(inscatter.rgb, trans)` into an RGBHalf/ARGBHalf target at `resolutionScale`.
- **Composite:** `scene = scene * fog.a + fog.rgb`, after AO and before bloom and tone mapping.

### 4.4 Making the low resolution hold up

These run in this order: **march → temporal → blur → upsample in the composite.**

1. **Temporal:**
   - Reproject each pixel's world point (from depth) into **last frame's** view-projection, and sample the history there. Off screen, use the current value.
   - Clamp the history to the min/max of this pixel and its 4 neighbours, padded by 25%: `clamp(hist, lo − (hi−lo)·0.25, hi + (hi−lo)·0.25)`.
   - `result = lerp(hist, cur, blend)`, with `blend = historyValid ? min(0.85, 0.15 + cameraMovedThisFrame·1.5) : 1`.
   - Use two ping-pong history targets. **The history stores the unblurred result.** Drop it on resize and camera cuts.
2. **Blur:** separable, horizontal then vertical, with 5 taps at offsets `0, ±1.384615, ±3.230769` and weights `0.227027, 0.316216, 0.070270`.
   - Each tap is also weighted by `exp(−|viewZ(tap) − z0| / (0.03·|z0| + 0.2))`, so there's no halo over silhouettes.
3. **Upsample:** at each full-resolution pixel, take the 4 low-resolution texels round it with bilinear weights. Multiply each weight by `1 − smoothstep(0.015, 0.08, |viewZ(texel) − z| / max(z, 0.1))`, where `viewZ(texel)` is the depth at that texel's centre.
   - If the weights sum to almost nothing, use the texel nearest in depth.
   - Without this, mist bleeds over the edges of things in front of it.

### 4.5 Shade on the ground (optional, renderer-wide `groundShade`)

- The sun is dimmed where it passes through boxes. For the 16 nearest boxes, slab-test the ray from the ground point toward the sun:
  ```hlsl
  float s = 0;
  for (int i = 0; i < count16; i++) { float2 r = VolHit(i, worldPos, L); if (r.y > r.x) s += C[i].w * (r.y - r.x) * (1 - D[i].w*0.5) * (1 - F[i].z*0.5); }
  float shade = exp(-s * 0.6);   // multiply the sun's contribution by this
  ```
- This is analytic, with no noise: a soft shadow, not a picture of the billows.
- Unity's lit shaders can't easily take an extra term. One way is a screen-space pass on the opaque image, before the mist, using depth. Choose whatever fits the pipeline.

### 4.6 Clear zones (optional)

A clear zone is a wedge or disc on the ground, and everything above it, where the mist thins away: `(x, z, heading, halfAngle (≥π: a disc), range, soft 0.3, strength 0..1)`. Multiply the density, including in the self-shadow taps, by:

```hlsl
float keep = 1;
for each clear: r = length(p.xz - c.xz); if (r < range) {
    float cs = r > 1e-3 ? dot((p.xz - c.xz)/r, float2(cos(heading), sin(heading))) : 1;   // heading 0 = +x
    float side = halfAngle >= PI ? 1 : smoothstep(cos(halfAngle), cos(halfAngle) + (1 - cos(halfAngle))*soft, cs);
    float end  = 1 - smoothstep(range*(1 - soft), range, r);
    keep *= 1 - strength * side * end;
}
```

---

## 5. The editor tools: how they must behave

These are the behaviours from my engine's map editor. Build them the Unity way: a component with a custom Inspector, an EditorTool or Scene view Overlay, Handles. Unity's own Undo, selection and serialization replace hand-made versions wherever they cover it.

**Placing and selecting:**
- **A "Mist" placing mode:** clicking the ground in the Scene view places a new box at the hit point.
  - The new box uses the current **new-box template**, with its base at ground height + **lift**. The default lift is −0.3: slightly sunk, so it hugs the ground.
  - The new box is selected.
- **Clicking a box selects it:** pick with a ray-box test against the turned boxes, nearest entry wins. Clicking empty ground while a box is selected deselects it.
- **Dragging a selected box** moves it across the ground, keeping its height above the ground. Record the lift when the drag starts, and set `y = ground(x, z) + lift` as it moves.
- **Delete** removes the selected box. **Undo / redo** cover every change, and **one whole drag is one undo step**.

**Handles on the selected box**, in three modes:
- **Move:** x, y, z arrows. Moving on x or z keeps the height above the ground; y moves it straight up and down.
- **Turn:** a ring about up, changing yaw only.
- **Size:** a handle on each of the box's own sides (width, height, depth), plus a centre handle that scales all three, growing as you drag right or up. Clamp to the ranges.

**Inspector or panel**, three groups. They edit the selected box, or the template when nothing is selected:

| Box | Look | Rolling |
|---|---|---|
| Width 0.5–64 | Colour | Billows 0–1 (0 shows "smooth") |
| Depth 0.5–64 | Thickness 0–4 | Billow size 0.3–12 |
| Height 0.25–16 | Soft sides 0–100% | Drift 0–5 |
| Lift −3–12 | Thin at top 0–100% | Drift direction 0–359° |
| Turn 0–359° | Glow colour | Rise −1–2 |
| | Glow 0–10 | Churn 0–3 |
| | Ground light 0–20 | |

**The rest of the tools:**
- **Live preview:** the mist updates as sliders move, in edit mode too, not only in Play mode. Offer a switch to update only on release, for slow machines.
- **Per box:**
  - **Hide/Show:** keeps the box but stops drawing it; its outline goes faint.
  - **Use for new boxes:** copies this box's look, size and lift into the template.
- **The template** and the handle mode are remembered between sessions.
- **Presets:** named looks with apply, save as new, update, rename, delete and reset.
  - A preset stores height, lift, colour, density, softness, falloff, glow colour and strength, light, noise, noiseScale, speed, direction, rise and churn.
  - It does **not** store width, depth or turn, so it fits any box.
  - The starter presets (colours sRGB; drift direction 60° unless the table says otherwise):

| Preset | Height | Lift | Colour | Density | Soft | Falloff | Glow (colour × strength), light | Noise | Noise scale | Speed | Rise | Churn |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Morning mist | 1.5 | −0.3 | 0.92, 0.95, 1 | 0.45 | 0.6 | 0.7 | none | 0.6 | 3 | 0.3 | 0.02 | 0.2 |
| Fog bank | 4 | −0.3 | 0.86, 0.88, 0.9 | 2.2 | 0.65 | 0.4 | none | 0.5 | 5 | 0.25 | 0 | 0.15 |
| Swamp fog | 1.2 | −0.4 | 0.62, 0.74, 0.55 | 1.2 | 0.5 | 0.75 | none | 0.75 | 2 | 0.2 | 0.03 | 0.5 |
| Toxic glow | 1.8 | −0.3 | 0.45, 0.95, 0.35 | 0.9 | 0.5 | 0.6 | 0.35, 1, 0.25 × 3; light 4 | 0.8 | 2 | 0.6 | 0.15 | 0.8 |
| Violet haze | 2.5 | −0.2 | 0.75, 0.58, 1 | 0.7 | 0.6 | 0.55 | 0.7, 0.42, 1 × 2; light 3 | 0.65 | 3 | 0.35 | 0.1 | 0.4 |
| Smoke drift | 3 | 0 | 0.36, 0.34, 0.33 | 1.4 | 0.55 | 0.35 | none | 0.85 | 2.5 | 1.2 (dir 45°) | 0.4 | 0.6 |
| Lava steam | 3 | −0.2 | 0.92, 0.78, 0.68 | 0.8 | 0.55 | 0.5 | 1, 0.45, 0.15 × 2.5; light 5 | 0.8 | 2 | 0.4 | 0.6 | 0.7 |

- **Outlines:** draw each box's edges in the Scene view: pale blue for normal boxes, faint for hidden ones, yellow and thicker for the selected one. Draw the handles over the mist so it never hides them.
- **A "hide all mist" toggle** in the Scene view, to see the ground under it. Outlines stay, and the setting is remembered.
- **Glow that lights things:** when `light > 0`, the box keeps a point light at its centre, at `y = base + min(height/2, 1.5)`. It takes the glow colour, with intensity = `light` and range = `max(width, depth) × 0.6 + 2`.

---

## 6. Gotchas (all hit in practice)

- **Seed by a stable id** stored with the volume. Otherwise the billows reshape whenever volumes go in or out of view, or the scene reloads.
- **Colours** are picked in sRGB; send linear values to the shader.
- **Wrap the noise offsets** to 0..1 on the CPU, or the billows go blocky after a long session.
- **Merge overlapping boxes into spans.** Marching them one by one gives hard seams.
- **Make the blur and the upsample depth-aware,** or the mist leaves halos round silhouettes.
- **The history holds the unblurred march;** drop it on resize and camera cuts.
- **Let the noise eat the middle too,** not just the edges. Otherwise it reads as a solid box with fuzzy sides.
- **Mind Unity's depth conventions:** reversed Z, and the per-platform flip of UV y when rebuilding world positions.

## 7. Checks

1. One default box on flat ground reads as a low, soft, slowly rolling bank. It is shaded on its sun side, and lit by a point light placed inside it.
2. Two overlapping boxes blend with no seam where they meet.
3. A tree or wall in front of the mist has a clean edge, with no halo, at 0.5 resolution, with temporal on and with it off.
4. Turning the camera doesn't change any box's billows.
5. Every preset roughly matches its name. Toxic glow, Violet haze and Lava steam glow, and their lights light the ground.
6. Place, select, drag, turn, size, delete, undo, redo, hide, presets and "use for new boxes" all behave as in section 5, in edit mode.
