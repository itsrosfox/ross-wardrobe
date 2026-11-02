# Adding an avatar base

A `.ravt` file describes an avatar base (Winterpaw's Masculine Canine, Rexouium, Nardoragon…). It does two jobs:

1. **Recognising avatars.** It tells Ros' Wardrobe which avatars in a scene are built on the base, so the window
   lists the garments fitted to it. Every garment pack needs this part.
2. **The Avatar section** (optional). It lists the base's body sliders, toggles and choices, and which textures
   are fur, so buyers can shape the body and recolour the fur in the window.

The `.ravt` ships with the garment packs for that base. Several packs shipping the same `.ravt` is fine: they share
its id, and Ros' Wardrobe uses one.

> Check first whether someone has already made a `.ravt` for the base, and reuse its **id**, so garments from
> different creators show up together for the same avatars.

## Making one

1. Import the avatar base into your project.
2. Open **Edit → Project Settings → Ros' Wardrobe**, tab **Avatars**, and click **New Avatar…**.
3. Drop in the base's **Model or Prefab**. That fills in the name and id, and records the model so avatars
   built from it are recognised.
4. Pick a folder (e.g. `<Pack>/Avatars`) and click **Create**.

In its editor:

- **Name**: shown in the window.
- **Id**: a stable id like `creator.base-name` (e.g. `winterpaw.masculine-canine`). Garments name the base by it,
  so **never change it once shipped**.
- **Version**: the base's version, only if garments need to tell versions apart (e.g. a 2.0 with a new body mesh).
  Garments can then ask for `"1"` (1.x) or `"2"`.
- **Files**: the base's model (`.fbx`) and prefab(s). An avatar whose prefab or model is one of these files is
  this base. The files have the same GUID in every project the base is imported into, so this is reliable. Add the
  PC and Quest prefabs if they're separate.
- **Names**: words that mean an avatar is this base, when its files don't (renamed copies, avatars exported from
  another project). Ros' Wardrobe looks for them in the avatar's name and its prefab's and model's names.

The editor shows which avatars in the open scenes are recognised as this base, and which garments are fitted to it.

That's all a garment pack needs. The rest of this page is the Avatar section.

## Body sliders, toggles and choices

The Avatar section's **Body** shows the base's own shape options, grouped (Body, Head, Tail…). They're listed in the
`.ravt`'s `sliders`, which you write in the json (open the file in a text editor). Each entry has a `name`, a
`group`, and either `set` or `options`.

**A slider** (0 to 100) scales the shape keys in `set` from 0 up to the weights given:

```json
{
    "name": "Buff",
    "group": "Body",
    "set": {
        "Buff": 100,
        "Big Face - Buff": 100,
        "MulletHair:BodyShapeFix": 100
    }
}
```

**A choice** picks one of several `options`, each with its own `set`. Options named exactly `Off` and `On` show as a
toggle:

```json
{
    "name": "Ears",
    "group": "Head",
    "options": [
        { "name": "Default", "set": { "Fox": 0, "Jackal": 0 } },
        { "name": "Fox",     "set": { "Fox": 100, "Jackal": 0 } },
        { "name": "Jackal",  "set": { "Fox": 0, "Jackal": 100 } }
    ]
}
```

```json
{
    "name": "Floppy Left Ear",
    "group": "Head",
    "options": [
        { "name": "Off", "set": { "Floppy Left": 0,   "Physbones/Dynamics.Ear.L": true,  "Physbones/Dynamics.FloppyEar.L": false } },
        { "name": "On",  "set": { "Floppy Left": 100, "Physbones/Dynamics.Ear.L": false, "Physbones/Dynamics.FloppyEar.L": true } }
    ]
}
```

What a `set` can contain:

| Key | Value | What it does |
| --- | --- | --- |
| `"Buff"` | 0 to 100 | a shape key of the body |
| `"Tail:Thin"` | 0 to 100 | a shape key of another renderer (`renderer name:shape key`) |
| `"Physbones/Dynamics.Ear.L"` | `true` / `false` | shows or hides an object, by its path under the avatar |

- `body` names the body renderer if it isn't called `Body`.
- Shape keys of the body that no slider uses still show, under *Other shape keys*.
- Worn garments follow the body's shape keys of the same name. A slider that sets shape keys garments have too
  (e.g. `Buff`) reshapes them with the body.

Buyers save body shapes as `.rbody` presets. You can ship some with the `.ravt`: shape the body in the window, *Save
as Body Preset…*, and move the file into your pack.

## Fur recolouring

The Avatar section's **Skin** recolours the fur with a gradient, from the fur's darkest parts to its lightest. List
the textures to recolour in `fur`:

```json
"fur": [
    { "texture": "T_WolfBody_BC", "range": [68, 245] },
    { "texture": "T_WolfTail1_BC", "range": [74, 245] },
    { "texture": "T_Gray_Mane_BC", "range": [14, 81], "span": 0.45 }
]
```

- `texture`: the texture's file name, without its extension. Every material on the avatar that uses it is
  recoloured.
- `range`: the fur's brightness, 0 to 255: `[darkest, lightest]`. Brightness in this range is mapped onto the skin's
  gradient, from start to end. Measure it in an image editor on the texture's darkest and lightest fur (not the
  eyes, nose or pads).
- `span` (optional, default 1): how much of the gradient the range covers. Use it for parts that should stay in the
  dark end, like a darker mane (0.45 = the first 45 % of the gradient).
- Saturated pixels (eyes, markings, coloured details) aren't recoloured, so they keep their colour on every skin.

Skins are saved as `.rskin`. Ship a few with the `.ravt`: make one under *Custom skin*, *Save as Skin…*, and move it
into your pack.

## Shipping it

Put the `.ravt` (and any `.rbody` and `.rskin`) in `<Pack>/Avatars`. If you made a `.ravt` for a base no one had
covered, consider sharing it with the base's creator or opening an issue here, so every creator for that base can
use the same id.

See the [file reference](file-reference.md#ravt) for every field.
