# Ros' Wardrobe

Sims-style recolouring for avatar clothing in the Unity editor. Pick a look, change colours or gradients per
part of the garment, see it live on the avatar, then apply. Applying bakes a normal texture + material copy,
so it works with any shader (including locked Poiyomi) and uploads to VRChat like any other material. Nothing
from this package ends up in the upload, except the PBR material type's shader when a garment is in PBR (so keep
Ros' Wardrobe installed while you use PBR looks).

Step-by-step guides (using it, making garments, patterns, avatar bases and material types) are on
<https://github.com/itsrosfox/ross-wardrobe>. Licence: [LICENSE.md](LICENSE.md).

## Installing

Through the VRChat Creator Companion: open <https://itsrosfox.github.io/ross-wardrobe/> and click **Add to VCC**
(or *Settings → Packages → Add Repository* with `https://itsrosfox.github.io/ross-wardrobe/index.json`). Then
*Manage Project* on your avatar project and add **Ros' Wardrobe**. Garments that support recolouring need it installed
before you import them.

## For buyers

Open **Tools → Ros' Wardrobe** and pick your avatar in the *Avatar* menu (every avatar in the open scenes). The
window works out which avatar base it's built on (from the base's `.ravt` file, which garment packs ship) and
lists the clothing fitted to that base; *Show garments for other avatars* lists the rest too.

1. On the left, the recolourable clothing for your avatar, by category. Click one to put it on: it replaces
   whatever the avatar wears in the same slot (one Ctrl+Z takes it back off).
2. If the garment can be rendered more than one way (a **Material**, e.g. *Toon* for Poiyomi, *PBR* for Unity's
   Standard shader), pick one at the top. It switches right away, and your looks are applied in that material.
3. Click one of its **Looks**: its colourways show big underneath, plus a **Custom** that keeps the look's
   patterns and lets you change its colours. The **Custom** look at the start of the gallery lets you pick a
   pattern (Solid, Gradient, stripes, checks, weaves, prints…) and colours for every part yourself. Colours that
   allow it can be a **Gradient** along the garment instead; **⇄** flips it.
4. Every change shows on the garment right away, and there's no apply step: the look is baked into a texture and
   material (in `Assets/Wardrobe Recolors/<Avatar>/<Garment>`, outside the garment's folder) when you move on:
   another garment, leaving or closing the window, saving the scene or entering play mode. Ctrl+Z undoes a change.
   Upload the avatar as usual and it works in-game.
5. **Save as Preset…** writes the look as a `.rpre` so it shows up under Looks next time.
6. Select a worn garment to change its look again, or **Take Off** to remove it.

If a garment relies on a tool you haven't installed (usually VRCFury), the window warns you, since it wouldn't
work in-game without it.

The same editor also shows in the Inspector when you select a worn garment.

### Avatar

The **Avatar** section (first in the list) shapes the avatar itself, for avatars whose base `.ravt` describes it:

- **Body**: the base's sliders by group (Body, Head, Tail…), toggles and choices (e.g. ears), and every other shape
  key of the body under *Other shape keys*. Worn garments follow the body's shape keys of the same names.
  **Save as Body Preset…** writes a `.rbody` (in `Assets/Wardrobe Recolors/<Avatar>/Bodies`), shown as a card on
  the left; click one to shape the body like it.
- **Skin**: recolours the fur with a gradient from its darkest parts to its lightest. Pick a skin on the left, or
  set a **Custom skin** and **Apply** or **Save as Skin…** (a `.rskin`). The recoloured textures and materials go
  in `Assets/Wardrobe Recolors/<Avatar>/Skins/<Skin>`; *Original* puts the avatar's own materials back.

These only change the avatar in the editor. In-game its own menu can still change what it animates.

## For creators

Ros' Wardrobe is driven by small config files it finds anywhere in the project and connects by their ids, so
anyone can add to what's there: a pattern for someone else's garment only has to name that garment (and the
avatar its fit is for). Each kind has its own inspector, and **Project Settings → Ros' Wardrobe** (or
*Assets → Ros' Wardrobe → Edit in Project Settings*) lists them all by kind, makes new ones, and updates old ones
after a Ros' Wardrobe update (*Updates*).

A garment is one `.rgarm` per avatar fit:

0. **Garment**: id, name, version, category (Tops, Bottoms, …), slot, the prefab the window puts on the avatar,
   and a thumbnail (leave it empty to have it rendered from the garment).
1. **Avatar**: the base it's fitted to (a `.ravt`). The window only lists it for avatars built on that base.
2. **Material**: which material type (`.rmat`) it starts in. Buyers can pick any type that fits it.
3. **Maps**: the mesh, base colour and normal map, and where its metalness, roughness, occlusion, sheen (and
   opacity) are: a channel of a texture (optionally inverted, e.g. smoothness for roughness) or a value.
   *Detail* sets how much of the base texture's shading shows through recoloured parts.
4. **ID map**: one flat colour per part. *Generate from UV islands* makes one from the mesh (one colour per
   UV island), or paint your own (e.g. Substance Painter). Black means "never recoloured". Anti-aliased edges are fine.
5. **Gradient map**: 16-bit grayscale saying where each pixel sits along a gradient (black = start, white =
   end). *Generate* makes one along an axis of the mesh, or paint your own and import it as R16.
6. **Regions**: names the buyer sees (Body, Sleeves, Collar…). Each region combines one or more parts of the ID
   map: pick a region per part under **Parts**. *One Region per Part* is a quick start.
7. **Presets**: put the garment on, set up a look, *Save as Preset…*, and move the `.rpre` next to the
   garment so it ships with it.

The generated maps and presets are editor-only data and aren't included in avatar uploads.

## Config files

All of them are json, with a `format` (Ros' Wardrobe updates older ones) and a stable `id`. Paths in them are
relative to the file.

- **.rgarm**: a garment fit, as above (`avatar` is `{ "id", "version" }`, `material` the `.rmat` id it starts in,
  `maps` its textures and channels, `bodyHides` the body shape keys it sets in-game to hide skin under it, which the
  Avatar section leaves alone).
- **.ravt**: an avatar base: `name`, the `guids` of its model and prefabs, and `matches`, words in an avatar's
  name or model that mean it's built on this base (for copies the guids don't catch). Optionally, for the Avatar
  section:
  - `body`: the name of the body renderer (default `"Body"`).
  - `sliders`: each a `name`, a `group`, and either `set` (a slider: shape key weights it scales from 0 to these)
    or `options` (a choice: each a `name` and the `set` it applies; options named exactly `Off` and `On` show as a
    toggle). Keys in a `set` are a body shape key (`"Buff": 100`), another renderer's (`"Tail:Thin": 100`), or an
    object's path under the avatar with `true`/`false` (shown or hidden).
  - `fur`: the textures skins recolour, each a `texture` (file name without extension), `range` `[dark, light]` (the
    fur's luminance, 0-255, mapped to the gradient's ends) and optionally `span` (how much of the gradient that
    range covers, default 1). Saturated pixels (eyes, markings) aren't recoloured.
- **.rbody**: a body preset: `name`, `group`, `order`, `avatar` (`{ "id" }`), `sliders` (name → value 0-100),
  `options` (name → option name) and `shapes` (any other body shape key → weight). Sliders and shape keys it doesn't
  name go to 0; choices it doesn't name stay as they are.
- **.rskin**: a skin: `name`, `group`, `order`, `avatar` (`{ "id" }`) and `fur`, a gradient
  (`"#RRGGBB@0, #RRGGBB@1"`) from the fur's darkest parts to its lightest.
- **.rmat**: a material type: a `shader` name or a `template` material to copy, and `textures`, what goes in each
  texture `property`: `"albedo"` (the recoloured base colour), `"normal"`, or 4 channels, each one of `metalness`,
  `roughness`, `smoothness`, `occlusion`, `sheen`, `opacity`, `albedo.r/g/b` or a number. `alsoOn` sets the same
  texture on more properties, `keyword` turns a shader keyword on with it. Ros' Wardrobe packs a texture per
  garment when its own maps aren't laid out that way already. `floats`, `colors` and `keywords` are set on the
  material too, and `garments` limits it to some garments (none = any). The package ships `PBR.rmat`: Unity's
  Standard lighting with both sides of the cloth drawn (its own small shader, as Standard always hides back faces).
- **.rpat**: a pattern (`name`, `group`, up to 4 `colors` slots, each optionally `canGradient`), its `type`, and
  its maps. `garments` lists the garments it's for (a `garment` id, optionally an `avatar` and `version`; none =
  any). `disabled: true` hides it from the picker. The three types:
  - `"simple"` (the default): tiling maps, at `tileSize` millimetres, on any garment: `weights` (RGBA = how much of
    colour 1..4), `normal`, `surface` (R metalness, G roughness, A opacity) and `occlusion`. Stripes, checks, weaves,
    all-over prints.
  - `"hybrid"`: per garment fit (in `garments`), `weights` laid out on its UVs (an id mask: which colour goes where)
    and optionally a `gradient` map of its own (else the garment's) for colours that are gradients. Raglan sleeves,
    colour blocks, fades from a point.
  - `"complete"`: per garment fit, an `albedo` painted on its UVs (alpha = where it covers) and optionally a `mask`
    (R = which texels it may cover); colour 1 shows everywhere else. Graphics and prints that aren't recoloured.
- **.rpre**: a look. `garments` it's for (none = any), `parts` (`region`, or `"*"` for every part no other entry
  names; `pattern`, `colors`, `scale`, `rotate`), `lookName` for its own colouring and `colourways` (a `name` and
  new `colors` per part, same patterns). Colours are `"#RRGGBB"`, or a gradient as `"#RRGGBB@0, #RRGGBB@1"`.
  `group` and `order` arrange the gallery.

Projects made with earlier versions (garment.json with a Wardrobe Garment asset, pattern.json, .preset.json) are
converted under *Updates*, after a backup; scenes and prefabs keep pointing at the converted garments.

The package itself only ships the Solid and Gradient patterns (and their presets) and the PBR material type.
Woven, printed and colour-block patterns ship with the clothing that uses them, in a pack laid out as:

```
<Pack>/Garments/<Garment>/<Garment>.rgarm   + mesh, prefab, maps, materials
<Pack>/Patterns/<Pattern>/<Pattern>.rpat    + maps, <Preset>.rpre; both list the pack's garments
<Pack>/Avatars/<Avatar>.ravt                the base the garments are fitted to
<Pack>/Materials/<Type>.rmat                + its template material, when the pack has its own material type
```

When several packs ship a pattern or preset with the same `id`, it's offered for the garments of all of them.

- **wardrobe.json**: Ros' Wardrobe's own defaults (the default pattern, pattern group order, what imported
  fabrics, flat surfaces and thumbnails start as). The package ships one; a `wardrobe.json` under Assets is read
  on top of it and only needs the values it changes.

Pattern previews and thumbnails are cached in `Library/Ros' Wardrobe/Cache`, so switching garments doesn't redraw them.

## How it works

The bake shader reads the ID map (turned into a region per texel), the gradient map, and a small gradient atlas
(one row per region). For each recoloured texel it outputs `colour × (base luminance ÷ the region's average
luminance)`, so the new colour keeps the fabric's folds, AO and stitching. The live preview renders the same
thing into a render texture shown through a `MaterialPropertyBlock` (never saved). Apply renders it at the base
texture's full source resolution, writes a PNG with the base texture's import settings, and copies the
material.
