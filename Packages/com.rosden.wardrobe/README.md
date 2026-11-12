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

The **Avatar** section (first in the list) shapes the avatar's body: the base's sliders by group (Body, Head,
Tail…), toggles and choices (e.g. ears) from its `.ravt`, and every other shape key of the body under *Other shape
keys*. Worn garments follow the body's shape keys of the same names. Ros' Wardrobe never changes the body's shape keys
by itself: the shape keys a worn garment suggests hiding under it (e.g. fluff) are listed under *Suggested Hides*, and
only change when you turn them on there. What's set here is what the avatar has in-game.

**Clearance** (*Fix clipping*, on by default) is the last step of fitting, on your avatar: after the body or what's
worn changes (and before a VRChat build), every worn garment is checked against the body as it is (without what's
hidden on it), and wherever the body still comes through the cloth, the cloth is moved out (with its layers,
buttons and stitching). That's one more shape key, *Wardrobe Clearance* at 100, on a copy of the garment's mesh kept
under `Wardrobe Recolors/<Avatar base>/<Garment>/` (one per scene and avatar); without clipping the garment wears its
own mesh. It's made for the body as you set it: shapes the avatar's menu animates in-game aren't cleared.

These only change the avatar in the editor. In-game its own menu can still change what it animates.

## For creators

Ros' Wardrobe is driven by small config files it finds anywhere in the project and connects by their ids, so
anyone can add to what's there: a pattern for someone else's garment only has to name that garment (and the
avatar its fit is for). The window's **Setup** tab lists the project's avatar bases, garments, material types,
patterns and presets, makes new ones and sets each up step by step; selecting a file shows the same setup in the
Inspector (**Assets → Create → Ros' Wardrobe** makes them too). Its **Checks** go through every file and say what's
missing or wrong (a map imported as sRGB, a fit that's out of date, a region with no parts, a control that sets a
shape key the avatar doesn't have…), most with a button that fixes it.

The workflow keeps your tools' exports as they are: export from your cloth tool (e.g. Marvelous Designer) straight
into the garment's folder, overwriting the last export, and press the buttons below again. Keep the cloth tool's
project in a `Source~` folder next to it (Unity ignores folders ending in `~`).

An **avatar base** (`.ravt`) starts from its model: drop it in and the body and its shape keys are read from it.
A table of every body shape key picks the ones buyers get a slider for (*Control*) and the ones garments change shape
with (*Follow*), with a preview slider on the avatar in the scene; toggles and choices are made from what's set on
the avatar (*From Scene*); and bones garments shouldn't move with are picked from its skeleton.

A **garment** is one `.rgarm` per avatar fit, in steps:

1. **Garment**: id, name, version, category (Tops, Bottoms, …), slot and a thumbnail (leave it empty to have it
   rendered from the garment).
2. **Avatar and cloth export**: the base it's fitted to (the window only lists it for avatars built on that base),
   and the *Source Model*, the garment as your cloth tool exports it, rigged to the avatar's skeleton. Its maps are
   found from the textures next to it. *Hides on Body* lists the body's shape keys that hide skin under it: only
   suggestions, which the Avatar section offers while the garment is worn. Neither the window nor the garment's
   own setup changes the body unless you say so.
3. **Maps**: the base colour and normal map (with a DirectX switch that flips its green on import), and where its
   metalness, roughness, occlusion, opacity (and sheen) are: a channel of a texture (optionally inverted, e.g.
   smoothness for roughness) or a value. Point them at your tools' textures as they are.
4. **Fit to Avatar** makes `<Garment>.asset` next to the `.rgarm`: the export's mesh with a copy of each body shape
   key garments follow (so it changes shape with the body sliders), and with weights on bones it shouldn't follow
   (the tail, say) moved to the bone the `.ravt` names. It becomes the garment's mesh and the prefab wears it.
   Fitting again overwrites it in place.

   **Body shapes** lists each body shape key the garment follows. Without anything more, the garment follows the
   body surface under it with each key. For a better fit (big shapes like breasts or a belly, where cloth has to
   drape and not just move with the skin), give a key the garment as your cloth tool exports it with that key on the
   body at 100: the same garment simulated again (Marvelous Designer: Avatar → Blend Shape), the same mesh, vertices
   unchanged. Its movement from the Source Model becomes the garment's shape key, a linear delta, so keys on at once
   add up. *Another Key* adds a body shape key the base doesn't list, with its export. What's still clipping on an
   avatar after that, the Avatar section's Clearance fixes. (In the `.rgarm`: `sims`, each a `source` and the
   `shapes` it was made with, `{ "breasts": 100 }`; an export at 50 as well is an in-between, and one with several
   keys on is a corrective for that mix.)
5. **Prefab**: *Make Prefab* makes `<Garment>.prefab` from the export and the fit, with VRCFury's Armature Link,
   a Blendshape Link to the body and a menu toggle (at *Menu Path*).
6. **Material**: which material type (`.rmat`) it starts in. Buyers can pick any type that fits it. *Make
   Material* writes `<Garment> (<Type>).mat` next to the `.rgarm` from the maps, packing them into the textures
   the type wants (at most *Packed Map Size*), and puts it on the prefab, so the garment ships in it.
7. **Recolouring**: *Make Maps* makes the ID map (one flat colour per UV island; black means "never recoloured")
   and the gradient map (16-bit grayscale: where each pixel sits along a gradient), or paint your own. Then the
   **regions**, the names buyers see (Body, Sleeves, Collar…), each combining one or more parts of the ID map.
   *Copy From Another Fit…* takes them from the same garment fitted to another avatar (same UVs).
8. **Material types, patterns and presets for it**: tick the ones that are for this garment.

*Make Them* runs steps 4 to 7 in one go. For looks: put the garment on, set up a look, *Save as Preset…*, and move
the `.rpre` next to the garment so it ships with it.

A **material type** (`.rmat`) links a material you made (any shader) and says what goes in each of its textures:
the look's colour, the normal map, or channels packed from the garment's maps, any source in any channel (R, G, B,
A), inverted or not, including the normal map octahedrally encoded in two channels. Garments' materials are Material
Variants of the linked material, so changes to it show on every garment in the type.

A **pattern** (`.rpat`) takes its textures dropped in: what each is for is told from its name, and it's imported the
way that needs.

The generated maps and presets are editor-only data and aren't included in avatar uploads.

## Config files

All of them are json with a stable `id`. Paths in them are relative to the file.

- **.rgarm**: a garment fit, as above (`avatar` is `{ "id", "version" }`, `material` the `.rmat` id it starts in,
  `maps` its textures and channels, `menu` where Make Prefab puts its toggle, `bodyHides` the body shape keys that
  hide skin under it: suggestions only, which the Avatar section lists while the garment is worn for you to turn on.
  Neither the window nor the garment's own setup changes the body.
- **.ravt**: an avatar base: `name`, the `guids` of its model and prefabs, and `matches`, words in an avatar's
  name or model that mean it's built on this base (for copies the guids don't catch). Optionally, for the Avatar
  section:
  - `body`: the name of the body renderer (default `"Body"`).
  - `fitting`, for Fit to Avatar: `shapeKeys`, the body shape keys garments copy (none = the body shape keys the
    sliders set), and `mergeBones`, the start of the names of bones garments shouldn't move with and the bone that
    takes their weight instead (`{ "Tail.": "Hips" }`). Fit to Avatar needs the base's model (one of its `guids`) in
    the project.
  - `sliders`: each a `name`, a `group`, and either `set` (a slider: shape key weights it scales from 0 to these)
    or `options` (a choice: each a `name` and the `set` it applies; options named exactly `Off` and `On` show as a
    toggle). Keys in a `set` are a body shape key (`"Buff": 100`), another renderer's (`"Tail:Thin": 100`), or an
    object's path under the avatar with `true`/`false` (shown or hidden).
- **.rmat**: a material type: a `template` material it links (its materials are Material Variants of it unless
  `follow` is `false`) or a `shader` name, and `textures`, what goes in each texture `property`: `"albedo"` (the
  recoloured base colour), `"normal"`, or 4 channels, each one of `metalness`, `roughness`, `smoothness`,
  `occlusion`, `sheen`, `opacity`, `albedo.r/g/b/luminance`, `normal.x/y/z`, `normal.oct.u/v`, `normal.hemioct.u/v`,
  a number, or `{ "from": <one of those>, "invert": true }`. `alsoOn` sets the same texture on more properties,
  `keyword` turns a shader keyword on with it. Ros' Wardrobe packs a texture per garment when its own maps aren't
  laid out that way already. `floats`, `colors` and `keywords` are set on the
  material too, and `garments` limits it to some garments (none = any). The package ships `PBR.rmat`: Unity's
  Standard lighting with both sides of the cloth drawn (its own small shader, as Standard always hides back faces).
- **.rpat**: a pattern (`name`, `group`, up to 4 `colors` slots, each optionally `canGradient`), its `type`, and
  its maps. `garments` lists the garments it's for (a `garment` id, optionally an `avatar` and `version`; none =
  any). The three types:
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

The package itself only ships the Solid and Gradient patterns (and their presets) and the PBR material type.
Woven, printed and colour-block patterns ship with the clothing that uses them, in a pack laid out as:

```
<Pack>/Garments/<Garment>/<Garment>.rgarm   + mesh, prefab, maps, materials
<Pack>/Patterns/<Pattern>/<Pattern>.rpat    + maps, <Preset>.rpre; both list the pack's garments
<Pack>/Avatars/<Avatar>.ravt                the base the garments are fitted to
<Pack>/Materials/<Type>.rmat                + its template material, when the pack has its own material type
```

When several packs ship a pattern or preset with the same `id`, it's offered for the garments of all of them.

Pattern previews and thumbnails are cached in `Library/Ros' Wardrobe/Cache`, so switching garments doesn't redraw them.

## How it works

The bake shader reads the ID map (turned into a region per texel), the gradient map, and a small atlas of the
look's colours (4 slots per region, gradients laid along each row). Each recoloured texel gets its pattern's mix of
those colours; the base texture only gives its alpha and the texels no region covers. Folds and stitching come from
the garment's normal and occlusion maps, with the patterns' relief and surface laid over them. The live preview
renders the same thing into render textures shown through a `MaterialPropertyBlock` (never saved). Baking renders
it at the base texture's full source resolution, writes PNGs with the base texture's import settings, and makes the
material from the material type.
