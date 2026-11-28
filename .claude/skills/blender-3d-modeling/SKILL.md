---
name: blender-3d-modeling
description: >-
  Procedural modelling for this project's chopper parts in Blender (PartForge, Tools/PartForge). Use when making or
  changing a chassis, cockpit, module or prop with the generator, when a hull must look designed and blended (fillets,
  merged engines, flush glass, planned panel lines), or when rendering and checking PartForge models. Adapted from the
  terminal-skills "blender-3d-modeling" skill (Apache-2.0) for PartForge.
---

# Blender modelling for PartForge

PartForge (`Tools/PartForge/partforge`) builds every chopper part in Python with bmesh, in **Unity space** (x right,
y up, z forward, metres; origin = centre of mass), and maps to Blender only at the end (`geo.to_blender`). Read
`Tools/PartForge/README.md` and `ART_DIRECTION.md` first. For the Arcus line, look at the concepts first:
`Tools/PartForge/concepts/README.md` (the owner judges against them).

Run Blender headless (Git Bash):

```
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python <script> -- <args>
```

- Write scripts with the Write tool, not a Bash heredoc (big heredocs with quotes fail).
- Delete the default objects and call `bpy.context.view_layer.update()` before framing a camera.
- `new_from_object` needs `view_layer.update()` first. Registered RNA props are not in `scene.keys()`: register
  `partforge.ui` and read `scene.partforge`.

## Never break the existing parts

- The rugged chassis, cockpits, modules and props must not change unless the owner asks. Before any export, compare an
  **order-independent** fingerprint (sorted rounded vertices, sorted faces with swatch, sockets, ground, gear) of every
  part against a clean copy of `HEAD` (`git archive HEAD Tools/PartForge/partforge | tar -x -C <tmp>`). A raw hash of
  vertex order is not repeatable between runs: do not trust it.
- New looks go in as a maker overlay (`styles.MAKERS`, `styles.with_maker`) applied to that part only, and a part's
  own `form` / `detail` / `planned_lines` on `parts.Part`.
- Only Kit02 is used in Unity. Chassis models match chassis assets by name (`Chassis_<Name>`, `Cockpit_<Name>`).

## The hull method: one filleted skin (`partforge/skin.py`)

Hulls that must look designed (the Arcus line, `partforge/sleek.py`) are **one continuous surface**, not lofts stuck
together:

1. **Tracks** (`skin.tracks`): each section parameter (roof height, chine width, engine radius...) is a list of
   `(z, value)` keys, smoothed with a monotone cubic (no overshoot). Draw side and top silhouettes as tracks.
2. **Section** at each z: a base outline (`skin.chined`, `skin.faceted`, `skin.box`, `skin.rounded`) plus
   `union(shape, k)` steps (engine humps, rotor pylon, wing gloves, a delta flange, a dorsal ridge). The smooth union
   radius `k` is the fillet. Scale `k` with the feature size (`min(k, r * 0.8)`), so a feature that shrinks to zero
   leaves no bump.
3. **Trace**: rays from `centre`; the shape must be star-shaped round it. Put the centre where every feature is visible
   in a straight line (the Halo uses its lower chine height, so the thin delta flange traces).
4. **Thin rows**: dense stations, then rows kept only where the surface bends (`tolerance`), arc-length resampling with
   more points in tight turns (`corner`).
5. **Design lines** are plane cuts (bmesh bisect) in a view (`"side"` (z, y), `"top"` (|x|, z), `"front"` (|x|, y)),
   so every colour edge is a crisp mesh edge:
   - `paint` (two-tone split), `recess` (flush glass set in a dark `rim`), `band` (light lines, frame bars),
     `groove` (panel lines), `vents`.
   - `facing` keeps a line on faces that look into the view; `depth` limits it along the view axis (use it so a top
     line on a flange does not cut the roof).
   - Polylines are simplified (Douglas-Peucker) automatically: every cut costs triangles.
6. Copy into the Builder with `Skin.into(b)`, then `b.snapshot_surface()` before placing surface parts or sockets.

Separate clean pieces stay separate: intake lip rings, nozzles, rotor mast, ducted fans, airfoil fins and wings
(`sleek._surface`: streamwise chords, real airfoil sections), gear legs.

## Lessons (owner feedback, 28 Nov 2026)

- **Rejected:** voxel remesh + decimate or QuadriFlow as the game mesh (sawtooth colour edges, melted engines, rough
  glass edge). Do not ship a remeshed surface.
- **Panel lines must be planned**: structure stations, doors, hatches, cowlings, bays, vents per chassis. No random
  surface panels on planned hulls (`detail = {}`), and no baked seam grid (`Part.planned_lines = True`).
- **Keep grooves flush.** A pressed V-groove dents the smooth shading of a game-resolution mesh; colour does the work
  (depth belongs in a baked normal map).
- **Intakes:** the throat cap must sit ahead of the nacelle's own front face, and the hull in front of an intake must
  be clear (lower the roof or waist the cabin ahead of it), or hull shows inside the ring.
- **Thin sharp edges:** a fillet on a very acute corner eats its length. Give flanges a blunt rounded edge (a few cm).
- **Glass** sits flush in a frame and never crosses another part. Cockpit interiors must fit inside the hull: run the
  poke check after any change near the cockpit and move the eye (`sleek.cockpit_eye`) if needed.
- **Proportions follow the concepts.** The owner liked the coaxial rotors and the tails; disliked bulbous, "childish",
  tacked-on shapes.
- Triangle budget: rugged chassis are 16k-37k; the Arcus hulls are 28k-50k. Ask the owner before going higher.

## Check before showing anything

1. Build one chassis fast and render it: a script that calls `parts.chassis(spec, style)`, `to_object` with the
   part's `form`, `palette.apply_uvs`, the kit material, `build._ensure_stage`, Eevee, then views from a few
   directions. Frame with the object bounds (distance about 2.5 x radius at 50 mm).
2. **Close-ups** of every join, intake, glass edge and flange. Most faults only show close up.
3. Poke check: cast rays (up, left, right, back, down) from every cockpit vertex into the chassis; all must hit.
4. `audit.run(chassis + modules)`: floating pieces, sockets near the hull, gear clearance. The dock props report 6
   problems from before this work.
5. Fingerprint the old parts against `HEAD` (see above).
6. Full export into a scratch folder (not the Unity project) when the unwrap or bake path changed; it takes about 20
   minutes.
7. Show the owner Eevee renders with modules fitted (`build.generate` showcase), with full `D:\` or scratchpad paths.

## Generic bmesh notes (from the original skill)

- Build with `bmesh.new()`, `bm.verts.new`, `bm.faces.new`; free the bmesh when done; `bmesh.ops.recalc_face_normals`
  before writing to a mesh.
- Prefer exact geometry from code to modifier stacks you cannot inspect; bake modifiers with
  `new_from_object(obj.evaluated_get(depsgraph))` when the result must be a plain mesh.
- Smooth shading with a sharp-edge angle and weighted normals (`geo._weighted_normals`) keeps flat panels flat and
  curves smooth.
