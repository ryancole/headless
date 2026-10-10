# Art style

**Target look: 0x10c's prototype builds, which read a lot like Halo: Combat Evolved.** Crisp early-2000s 3D:
chunky, bevelled sci-fi architecture lit by a few strong real-time lights in mostly dark rooms, with bright
glowing screens and saturated accents against a muted world.

This was taken from four 0x10c screenshots (2012–13 prototype and internal multiplayer builds). They're
Mojang's images, so they're described here rather than committed:

1. A ship corridor with a raised walkway and floor trenches. An orange boxy robot with a laser, a red
   first-person gun, a blue wall terminal, a green-lit doorway.
2. The earliest prototype: an almost black room, one point light, grey spheres, a yellow "0x10c" sign and a
   glowing blue terminal.
3. A cockpit: a bevelled console with three glowing pixelated monitors, below a window onto black space and
   a planet.
4. A large interior room. A yellow robot crewmate with a screen for a face, ceiling grid, a bright hatch
   throwing white light wedges onto the floor, blue console lights, a minimal HUD ("Ammo: 100%").

## Principles

- **Muted world, saturated things.** Environments are grey-green, olive and gunmetal. Colour is reserved for
  what matters: tools, robots and crew, screens, warning lights. If everything is colourful, nothing is.
- **Darkness is a feature.** Low ambient light; rooms are lit by placed lights, and corners go genuinely
  dark. Light should feel like a resource, which suits a ship whose power can fail.
- **Solid and industrial.** Blocky modular pieces, chamfered edges, inset panel seams. Nothing organic.
- **Crisp, not retro.** This is clean 2003-era 3D, not a PS1/lo-fi filter. The only deliberately pixelated
  things are the screens.

## Geometry

- Low-poly, built from modular kits: wall panels (tall rectangles), floor plates, ceiling grids, railings,
  trenches, doors and frames, consoles.
- Every hard edge gets a chamfer or bevel; panels have inset seams. Chunky proportions: thick walls,
  heavy doorframes, raised walkways.
- Ship interiors are built in ship-local space (see CLAUDE.md), on a consistent grid so pieces snap together.

## Textures and materials

- Small textures (around 128–256 px per panel), **smooth (linear) filtering with mipmaps**. Detail is painted
  in: panel lines, rivets, scuffs, grime and darkening in corners (baked into the texture rather than relying
  on screen-space effects).
- Godot `StandardMaterial3D`. Mostly matte (high roughness), a little specular on metal. Let the texture carry
  the look; avoid complex PBR materials.

## Palette

| Role | Colours |
|---|---|
| Structure | grey-green, olive, gunmetal, dull steel; light grey-white floor plates |
| Accents | orange, red-orange (tools, robots), yellow (crew, signage) |
| Screens and indicators | cyan/blue terminal light, green displays, small blue console lights |
| Outside | pure black space; simple, readable planets |

## Lighting

- A few strong `SpotLight3D` / `OmniLight3D` per room, **with shadows**, kept fairly hard-edged (crisp
  shadows from railings, crates and held items).
- Very low ambient light. No fill light that flattens the room.
- **Glow (bloom) on**, so light sources and screens bloom slightly.
- **Fake light shafts**: bright, unshaded, additive wedge meshes under hatches and lamps, as in screenshot 4.
  A light volumetric fog is optional, but the meshes are the look.
- Emissive materials for screens, indicator lights and lamps.

## Screens

- In-world monitors are low resolution and **pixel-sharp**: each is rendered into its own small
  `SubViewport` (128×96, like 0x10c's monitors), shown with nearest-neighbour filtering on an emissive
  material.
- Simple terminal aesthetics: a solid-colour background, a blocky font, few colours.
- They should show real ship state from the server, which makes them both the style and the interface.

## Characters, first-person view and HUD

- Crew and robots are boxy and chunky, with simple silhouettes and saturated colours. A screen-face is in style.
- First-person held items are chunky and colourful (Halo-style), drawn by a separate camera layer so they never
  clip into walls.
- HUD is minimal: a tiny `+` crosshair, plain white text in a corner. No ornate frames. Most information belongs
  on in-world screens instead.

## Godot settings to start from

- Renderer: Forward+.
- `WorldEnvironment`: very low ambient light, glow enabled, a filmic-style tonemap, no auto-exposure. Keep SSAO
  off or subtle (grime is in the textures).
- Lights: shadows on, low blur. Positional shadow atlas sized for a handful of shadowed lights per room.
- Anti-aliasing: MSAA 2× or 4×. The edges should be crisp, not smeared.
- Camera: a field of view around 70–75°.

## Not this

- PS1-style vertex jitter, dithering, or low-resolution pixelated rendering of the whole screen.
- Bright, evenly lit rooms; heavy ambient occlusion or depth-of-field.
- Glossy modern PBR, high-poly or photoreal assets, ornate HUDs.
