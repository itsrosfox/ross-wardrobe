# File reference

Ros' Wardrobe reads small json files anywhere under `Assets` (and in packages), and links them by id. Make and edit
them in **Tools → Ros' Wardrobe → Setup** (or **Assets → Create → Ros' Wardrobe**, then select the file: the Inspector
shows the same setup). They're plain json, so a text editor works too; the Setup tab's **Checks** tell you when
something in one is wrong.

Common to all of them:

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

## .rgarm

A garment fitted to one avatar base. Fits of the same garment for other bases are their own `.rgarm` files with the
same `id`. See [Making a garment](making-a-garment.md).

| Field | |
| --- | --- |
| `id`, `name`, `version`, `author` | the garment |
| `category` | where the window lists it: Tops, Bottoms, Shoes, Accessories… |
| `slot` | putting a garment on takes off the one worn in the same slot. Empty = never swapped. |
| `bodyHides` | body shape keys that hide skin under it (e.g. `TShirt`). Suggestions: the Avatar section offers to turn them on when it's worn. |
| `avatar` | `{ "id", "version" }`: the `.ravt` it's fitted to, and which of its versions (`"1"` = 1.x, empty = any) |
| `source` | the cloth tool's export, rigged to the avatar's skeleton, that Fit to Avatar and Make Prefab start from |
| `sims` | exports of the garment with body shape keys on: `[{ "source", "shapes": { "Buff": 100 } }]`. One key = that key's shape on the garment; several = a corrective. |
| `prefab` | what the window puts on the avatar (Make Prefab makes it) |
| `menu` | where Make Prefab puts its toggle in the avatar's menu. Empty = `Clothing/<name>`. |
| `thumbnail` | picture in the window. Empty = rendered from the garment. |
| `mesh` | the mesh the prefab wears (Fit to Avatar makes it), which the ID and gradient maps are made from, e.g. `"Hoodie.asset"` or `"Hoodie.fbx#Hoodie"` |
| `material` | the `.rmat` id it starts in. Empty = the first that fits. |
| `mapSize` | largest size of the textures Make Material packs. Empty = the base colour's size. |
| `maps` | `baseColor` and `normal` (textures); `metalness`, `roughness`, `occlusion`, `sheen`, `opacity`, each `{ "texture", "channel": "R"/"G"/"B"/"A", "invert" }`, or a number (the same value everywhere) |
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
| `body` | the body renderer's path under the avatar (default `"Body"`) |
| `sliders` | the Avatar section's sliders and choices: each a `name`, a `group`, and `set` (a slider) or `options` (a choice: each a `name` and a `set`; options named `Off` and `On` show as a toggle) |
| `fitting` | `shapeKeys`: the body shape keys garments follow (empty = every body key the sliders set); `mergeBones`: `{ "<bone name start>": "<bone>" }`, bones garments shouldn't move with and the bone that takes their weight |

A `set` maps a body shape key (`"Buff": 100`), another renderer's shape key (`"Tail:Thin": 100`), or an object's path
under the avatar (`"Physbones/Ear.L": true`, shown or hidden).

## .rpat

A pattern. See [Patterns and looks](patterns-and-looks.md).

| Field | |
| --- | --- |
| `id`, `name`, `version`, `author`, `description` | the pattern. The description shows under it in the window. |
| `type` | `"simple"` (default, tiling), `"hybrid"` (on a garment's UVs) or `"complete"` (a picture on a garment's UVs) |
| `group` | where the picker lists it |
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
| `template` | the material it links: garments' materials are made from it, with its settings |
| `follow` | `true` (default): garments' materials are Material Variants of `template`, so changes to it show on them. `false`: copies. |
| `shader` | a shader name, used instead of the template's (or with no template); its materials are copies |
| `textures` | each a `property` and its `channels`: `"albedo"`, `"normal"`, or 4 channel sources (R, G, B, A). Optionally `alsoOn` (more properties) and `keyword`. A property the file doesn't list keeps the template's texture. |
| `floats`, `colors`, `keywords` | set on the material too |
| `garments` | which garments it's for. None = any. |

A channel source is a name, a number (0 to 1, the same everywhere), or `{ "from": "<name>", "invert": true }` for one
minus it. The names:

| Name | |
| --- | --- |
| `metalness`, `roughness`, `smoothness`, `occlusion`, `sheen` | the garment's surface maps (smoothness = 1 − roughness) |
| `opacity` | the garment's opacity map, else the base colour's alpha |
| `albedo.r`, `albedo.g`, `albedo.b`, `albedo.luminance` | the look's colour, a channel or its brightness |
| `normal.x`, `normal.y`, `normal.z` | the normal map's tangent-space vector, 0..1 |
| `normal.oct.u`, `normal.oct.v` | the normal octahedrally encoded in two channels |
| `normal.hemioct.u`, `normal.hemioct.v` | the normal hemi-octahedrally encoded (upper half only, finer) |

## Caches and output

- Baked looks: `Assets/Wardrobe Recolors/<Avatar>/<Garment>`, with saved looks in `…/Presets`.
- Pattern previews and thumbnails are cached in `Library/Ros' Wardrobe/Cache`, so they aren't redrawn when you
  switch garments.
