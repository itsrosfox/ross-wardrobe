# File reference

Ros' Wardrobe reads small json files anywhere under `Assets` (and in packages), and links them by id. Each kind
has its own editor in **Edit → Project Settings → Ros' Wardrobe**, and you can also edit them in a text editor.

Common to all of them:

- `format`: the file layout's version. After a Ros' Wardrobe update, *Project Settings → Ros' Wardrobe → Updates*
  rewrites older files in the current layout (backing them up to `Library/Ros' Wardrobe/Backups` first).
- `id`: a stable id other files refer to it by, like `yourname.hoodie`. **Don't change it once shipped.** Ids
  are compared without regard to case.
- Paths to textures, meshes and prefabs are **relative to the file**. A mesh inside a model is
  `"Model.fbx#Mesh name"`.
- Colours are `"#RRGGBB"`. A gradient is `"#RRGGBB@0, #RRGGBB@1"` (a colour at each position, 0 to 1).
- Garment lists (`garments`) are objects of `garment` (an id), optionally `avatar` (a `.ravt` id) and `version`
  (a major version: `"1"` = 1.x). An empty list means any garment.

| File | What it is |
| --- | --- |
| [`.rgarm`](#rgarm) | a garment fitted to one avatar base |
| [`.ravt`](#ravt) | an avatar base |
| [`.rpat`](#rpat) | a pattern |
| [`.rpre`](#rpre) | a look (preset) |
| [`.rmat`](#rmat) | a material type |
| [`.rbody`](#rbody) | a body preset |
| [`.rskin`](#rskin) | a skin |
| [`wardrobe.json`](#wardrobejson) | Ros' Wardrobe's own defaults |

## .rgarm

A garment fitted to one avatar base. Fits of the same garment for other bases are their own `.rgarm` files with the
same `id`. See [Making a garment](making-a-garment.md).

| Field | |
| --- | --- |
| `id`, `name`, `version`, `author` | the garment |
| `category` | where the window lists it: Tops, Bottoms, Shoes, Accessories… |
| `slot` | putting a garment on takes off the one worn in the same slot. Empty = never swapped. |
| `bodyHides` | body shape keys the garment's own setup (VRCFury) sets in-game to hide skin under it. The Avatar section leaves them alone. |
| `avatar` | `{ "id", "version" }`: the `.ravt` it's fitted to, and which of its versions (`"1"` = 1.x, empty = any) |
| `prefab` | what the window puts on the avatar |
| `thumbnail` | picture in the window. Empty = rendered from the garment. |
| `mesh` | the mesh the ID and gradient maps are made from, e.g. `"Hoodie.fbx#Hoodie"` |
| `material` | the `.rmat` id it starts in. Empty = the first that fits. |
| `maps` | `baseColor` and `normal` (textures); `metalness`, `roughness`, `occlusion`, `sheen`, `opacity`, each `{ "texture", "channel": "R"/"G"/"B"/"A", "invert" }` or `{ "value" }` |
| `idMap` | one flat colour per part. Black = never recoloured. |
| `gradientMap` | 16-bit grayscale (R16): position along a gradient, black = start, white = end |
| `metresPerUv` | metres of cloth per UV unit, for parts without their own scale |
| `regions` | `[{ "name", "parts": ["#RRGGBB", …] }]`: what buyers recolour, each one or more ID map colours (up to 31 regions) |
| `partScales` | each part's real size (`part`, `metresPerUv`), filled in by *Generate from UV islands* |

## .ravt

An avatar base. See [Adding an avatar base](adding-an-avatar-base.md).

| Field | |
| --- | --- |
| `id`, `name`, `version`, `author`, `description` | the base |
| `guids` | GUIDs of the base's model and prefabs. An avatar made from one of them is this base. |
| `matches` | words in an avatar's name, or its prefab's or model's, that mean it's this base |
| `body` | the body renderer's name (default `"Body"`) |
| `sliders` | the Avatar section's sliders and choices: each a `name`, a `group`, and `set` (a slider) or `options` (a choice: each a `name` and a `set`; options named `Off` and `On` show as a toggle) |
| `fur` | textures skins recolour: each a `texture` (file name, no extension), `range` `[dark, light]` (brightness 0-255 mapped onto the gradient) and optionally `span` (how much of the gradient the range covers, default 1) |

A `set` maps a body shape key (`"Buff": 100`), another renderer's shape key (`"Tail:Thin": 100`), or an object's path
under the avatar (`"Physbones/Ear.L": true`, shown or hidden).

## .rpat

A pattern. See [Patterns and looks](patterns-and-looks.md).

| Field | |
| --- | --- |
| `id`, `name`, `version`, `author`, `description` | the pattern. The description shows under it in the window. |
| `type` | `"simple"` (default, tiling), `"hybrid"` (on a garment's UVs) or `"complete"` (a picture on a garment's UVs) |
| `group` | where the picker lists it |
| `disabled` | `true` hides it from the picker |
| `colors` | up to 4 slots: `name`, `color`, `canGradient`, and optionally `useGradient` and `gradient` (what it starts as) |
| `tileSize` | `[width, height]` of one tile in millimetres (simple patterns) |
| `weights` | tiling: R, G, B, A = how much of colour 1..4 |
| `normal`, `normalStrength` | tiling relief, added to the garment's normal map |
| `height`, `heightStrength` | tiling relief as a height map (R, white = raised) |
| `normalMode` | `"blend"` (default: added to the garment's relief) or `"replace"` |
| `surface` | tiling: R metalness, G roughness, A opacity |
| `occlusion` | tiling: R, multiplied into the garment's ambient occlusion |
| `sheen` | how much rim light / sheen the cloth gets (0 to 1, default 0.3) |
| `garments` | which garments it's for. Hybrid and complete patterns add maps on each fit's UVs: `weights` and `gradient` (hybrid), `albedo` (alpha = coverage) and `mask` (R = where it may cover) (complete), and optionally `normal`, `height`, `surface`, `occlusion`. |

## .rpre

A look.

| Field | |
| --- | --- |
| `id`, `name`, `version`, `author`, `description` | the look |
| `lookName` | the name of its own colouring ("Navy & White") |
| `group`, `order` | arrange the Looks gallery |
| `garments` | which garments it's for. None = any. |
| `parts` | each a `region` (a region name, several as `"Collar, Hem"`, or `"*"` for every region no other part names), `pattern` (an id), `colors`, `scale`, `rotate` |
| `colourways` | each a `name` and `parts` of `region` and new `colors` (same patterns) |

## .rmat

A material type. See [Material types](material-types.md).

| Field | |
| --- | --- |
| `id`, `name`, `version`, `author`, `description` | the type. The name is what buyers pick. |
| `template` | a material to copy (its settings kept) |
| `shader` | a shader name, used instead of the template's (or with no template) |
| `textures` | each a `property` and its `channels`: `"albedo"`, `"normal"`, or 4 sources (`metalness`, `roughness`, `smoothness`, `occlusion`, `sheen`, `opacity`, `albedo.r/g/b`, or a number). Optionally `alsoOn` (more properties) and `keyword`. |
| `floats`, `colors`, `keywords` | set on the material too |
| `garments` | which garments it's for. None = any. |

## .rbody

A body preset, saved from the Avatar section.

| Field | |
| --- | --- |
| `id`, `name`, `group`, `order` | the preset |
| `avatar` | `{ "id" }`: the base it's for |
| `sliders` | slider name → value 0-100 |
| `options` | choice name → option name |
| `shapes` | any other body shape key → weight |

Sliders and shape keys it doesn't name go to 0; choices it doesn't name stay as they are.

## .rskin

A skin, saved from the Avatar section.

| Field | |
| --- | --- |
| `id`, `name`, `group`, `order` | the skin |
| `avatar` | `{ "id" }`: the base it's for |
| `fur` | a gradient from the fur's darkest parts to its lightest, e.g. `"#2B2622@0, #E8E2D6@1"` |

## wardrobe.json

Ros' Wardrobe's own defaults: the default pattern, the order of pattern groups, what imported fabrics, flat surfaces
and thumbnails start as. The package ships one. A `wardrobe.json` under `Assets` is read on top of it, and only needs
the values it changes:

```json
{
    "format": 1,
    "patternGroups": ["Plain", "Blocks", "Graphics", "Stripes", "Checks", "Weaves", "Knits", "Prints"]
}
```

## Caches and output

- Baked looks: `Assets/Wardrobe Recolors/<Avatar>/<Garment>`, with saved looks in `…/Presets`.
- Bodies and skins: `Assets/Wardrobe Recolors/<Avatar>/Bodies` and `…/Skins/<Skin>`.
- Pattern previews and thumbnails are cached in `Library/Ros' Wardrobe/Cache`, so they aren't redrawn when you
  switch garments.
