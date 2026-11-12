# Adding an avatar base

A `.ravt` file describes an avatar base (Winterpaw's Masculine Canine, Rexouium, Nardoragon…). It does three jobs:

1. **Recognising avatars.** It tells Ros' Wardrobe which avatars in a scene are built on the base, so the window
   lists the garments fitted to it.
2. **Fitting garments.** It says which of the body's shape keys garments follow, and which bones they shouldn't
   move with.
3. **The Avatar section** (optional). It lists the base's body sliders, toggles and choices, so buyers can shape the
   body in the window.

The `.ravt` ships with the garment packs for that base. Several packs shipping the same `.ravt` is fine: they share
its id, and Ros' Wardrobe uses one.

> Check first whether someone has already made a `.ravt` for the base, and reuse its **id**, so garments from
> different creators show up together for the same avatars.

## Making one

1. Import the avatar base into your project, and put the avatar in a scene (for the previews).
2. Open **Tools → Ros' Wardrobe → Setup**, click **New Avatar Base…** under *Avatar bases*, and save it in your
   pack's `Avatars` folder. (Or pick **Assets → Create → Ros' Wardrobe → Avatar Base**; selecting the file shows the
   same setup in the Inspector.)
3. Follow the steps. Each one's mark says where it stands: **✓** done, **!** needs you, **○** waits for an earlier
   step. Click **Save** when you're done.

**1. Avatar model**
Drop the base's model (`.fbx`) or prefab in (from the Project window, or the avatar from the scene: its model is
used). **Use … from the scene** does it for avatars already in the scene. Ros' Wardrobe reads the rest from it: the
name, an id, the **Body** (the renderer with the body's shape keys; pick another if it guessed wrong) and a word to
recognise copies by. Add the PC and Quest prefabs too if they're separate files. The files have the same GUID in
every project the base is imported into, so avatars built from them are recognised reliably.

- **Also Recognise**: words that mean an avatar is this base when its files don't (renamed copies, avatars exported
  from another project). Ros' Wardrobe looks for them in the avatar's name and its prefab's and model's names.

**2. Details**
- **Name**: shown in the window.
- **Id**: a stable id like `creator.base-name` (e.g. `winterpaw.masculine-canine`). Garments name the base by it, so
  **never change it once shipped**.
- **Version**: the base's version, only if garments need to tell versions apart (e.g. a 2.0 with a new body mesh).
  Garments can then ask for `"1"` (1.x) or `"2"`.

**3. Body shape keys**
Every shape key of the body, in a table you can search (face keys like visemes are hidden until you tick *Face*):

- **Control**: buyers get a slider for it in the Avatar section (it makes a slider that sets just that key; rename
  and group it in step 4).
- **Follow**: garments change shape with it. Fit to Avatar gives garments a shape key for each one ticked. With none
  ticked, garments follow every key the controls set.
- **Preview**: moves the key on the avatar in the scene, to see what it does. Nothing else here changes the body.

Keys that hide skin under clothing (e.g. *TShirt*) aren't picked here: each garment picks them in its *Hides on
Body*.

**4. Controls buyers get**
What buyers change in the window's Avatar section, in order, grouped (Body, Head, Tail…). **Add Control** adds:

- a **Slider**, which turns its shape keys from 0 up to the weights given,
- a **Toggle**, with an *Off* and an *On* option,
- a **Choice**, with several options (e.g. ear styles).

Each option sets shape keys (**Add Shape Key** lists the body's and every other renderer's) and objects (drop an
object of the avatar in the scene into the field next to it: an outfit part, a physbone set, a prop). Set the avatar
in the scene the way an option looks and click **From Scene** to fill in its values.

**5. Bones garments don't move with**
Bones garments shouldn't be weighted to (tail, breast or hair bones), by the start of their name, and the bone that
takes their weight instead. **Add Bones** lists the avatar's bones; the parent bone is suggested.

**6. In this project**
Which avatars in the open scenes are recognised as this base, and which garments are fitted to it.

## In the file

The steps write the `.ravt`'s json; you can also edit it in a text editor. A slider:

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

A choice; options named exactly `Off` and `On` show as a toggle:

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
| `"Tail:Thin"` | 0 to 100 | a shape key of another renderer (`renderer path:shape key`) |
| `"Physbones/Dynamics.Ear.L"` | `true` / `false` | shows or hides an object, by its path under the avatar |

- `body` names the body renderer if it isn't called `Body`.
- Shape keys of the body that no control uses still show in the Avatar section, under *Other shape keys*.
- Worn garments follow the body's shape keys of the same name. A slider that sets shape keys garments have too
  (e.g. `Buff`) reshapes them with the body.

## Shipping it

Put the `.ravt` in `<Pack>/Avatars`. If you made a `.ravt` for a base no one had covered, consider sharing it
with the base's creator or opening an issue here, so every creator for that base can use the same id.

See the [file reference](file-reference.md#ravt) for every field.
