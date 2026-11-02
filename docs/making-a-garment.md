# Making a garment

This guide takes a garment from a skinned mesh to a recolourable garment pack that buyers open in Tools → Ros'
Wardrobe. It assumes you already know how to make clothing for a VRChat avatar. Ros' Wardrobe adds a few maps and
one config file on top of that.

**What you'll end up with:**

```
<Pack>/
  Avatars/<Avatar>.ravt                     which avatars the garments fit (one per avatar base)
  Garments/<Garment>/
    <Garment>.fbx, <Garment>.prefab         your garment, set up for VRChat as usual
    <Garment>_BaseColor.png, _Normal.png…   its textures
    <Garment> ID.png                        which part of the garment each texel belongs to
    <Garment> Gradient.png                  where each texel sits along a gradient
    <Garment>.rgarm                         the garment's Ros' Wardrobe config
  Patterns/<Pattern>/<Pattern>.rpat         patterns that come with the pack (optional)
               + <Look>.rpre                 looks made with them
  Materials/<Type>.rmat                     your material type, if the pack has its own (e.g. Poiyomi)
```

## 0. What you need

- A Unity 2022.3 avatar project with the VRChat SDK, set up with VCC.
- **Ros' Wardrobe** (see [Installing](../README.md#installing)) and **VRCFury**.
- The avatar base you're making the garment for.
- Your garment: modelled, UV-unwrapped, skinned to the avatar's armature, and
  textured.

## 1. Prepare the mesh

A few things make recolouring work well. Plan them before you texture.

**UVs: one island per panel of cloth.**
Ros' Wardrobe splits the garment into parts by UV island: the front, the back, each sleeve, the collar, the cuffs.
Marvelous Designer's pattern pieces already map to one island each. If you unwrap by hand, cut seams where the real
garment has them.

**UVs: the same texel density everywhere.**
Tiling patterns (stripes, checks, weaves) are drawn at real-world size. Ros' Wardrobe measures each part's UVs
against the mesh, so islands can be scaled differently. Still, keeping them at similar density keeps the pattern
equally sharp all over.

**Shape keys: named like the body's.**
When the buyer moves a body slider (Buff, Chonky, Thin…), the garment's shape keys of the **same names** follow,
in the editor and, with VRCFury's Blendshape Link, in-game. Make a shape key on the garment for every body shape it
needs to follow, and name it exactly like the body's.

**A hide key on the body (if the base has one).**
Many bases have shape keys that shrink the body under clothing (e.g. *TShirt*). Note which ones your garment should
set; they go in the `.rgarm` later.

## 2. Make the textures

**Base colour: shading, not colour.**
Ros' Wardrobe keeps the light and dark of your base colour and replaces its colour. Each recoloured texel becomes

```
new colour × (texel's brightness ÷ the part's average brightness)
```

So folds, ambient occlusion, stitching and fabric texture all carry over to every look. Bake those into a
**neutral, light** base colour (white or light grey cloth). A colourful base texture works too, but its hue is
thrown away, and very dark areas stay dark in every colour.

**Normal map:** as usual. Patterns that have relief (knits, weaves) add theirs on top.

**Surface maps:** metalness, roughness (or smoothness), ambient occlusion, sheen and opacity. Each can be its own
texture, a channel of a packed texture, or a single value. Ros' Wardrobe packs them the way your material type
needs.

Texture size: looks are baked at the base colour's size. 2048 or 4096 is typical.

## 3. Set the garment up in Unity

Set up the garment as you would for any VRChat outfit:

1. Import the `.fbx` and textures, and make a material for it.
2. Make a **prefab** of the garment with **VRCFury** components:
   - **Armature Link**, linking its armature to the avatar's,
   - **Blendshape Link** to the avatar's body, so it follows the body's sliders in-game,
   - a **Toggle** for the radial menu, with a *Blendshape* action that sets the body's hide key(s) while it's
     worn.
3. Drag it onto your avatar and check that it works (Gesture Manager, or a test build).

This prefab is what Ros' Wardrobe puts on the avatar when a buyer clicks the garment.

## 4. Make sure the avatar base has a `.ravt`

Ros' Wardrobe only lists a garment for avatars built on the base it was fitted to. A `.ravt` file describes that
base. If your pack is for a base no one has described yet, make one now: see
[Adding an avatar base](adding-an-avatar-base.md). It takes a minute, and it's also where body sliders are set
up.

Ship the `.ravt` in your pack (in `<Pack>/Avatars`). Several packs shipping the same `.ravt` is fine: they share
the id.

## 5. Create the `.rgarm`

1. In the garment's folder, pick **Assets → Create → Ros' Wardrobe → Garment** and name the file.
2. Select it: the Inspector is its editor. Set the **Prefab**, and under **Maps** the **Mesh**, **Base Colour** and
   **Normal**.
3. Fill in the rest, then click **Save**:
   - **Name**: what buyers see.
   - **Id**: a stable id like `yourname.hoodie`. Patterns and looks refer to the garment by it, so **never
     change it once shipped**. Fits of the same garment for other avatars use the **same id**.
   - **Category**: where the window lists it (Tops, Bottoms, Shoes, Accessories…).
   - **Slot**: putting on a garment takes off the one worn in the same slot (Top, Bottom, Shoes…). Leave it empty
     for things that are never swapped out.
   - **Made For**: the avatar base.
   - **Starts In**: the material type it starts in. *PBR* comes with Ros' Wardrobe; see
     [Material types](material-types.md) for others.
4. Click **Create**. The `.rgarm` opens in the editor on the right. Everything there can be changed later; click
   **Save** to write it.

## 6. Fill in the garment

In the `.rgarm`'s editor:

**Garment**
- **Version**: your garment's version. Patterns can be limited to a major version, in case you change the UVs.
- **Hides on body**: the body shape keys the garment's Toggle sets to hide skin under it (e.g. `TShirt`). Your
  VRCFury setup does it in-game; Ros' Wardrobe needs to know so the Avatar section leaves them alone.
- **Thumbnail**: leave it empty to have it rendered from the garment.

**Maps**
- **Mesh**, **Base Colour**, **Normal**: filled in from the prefab.
- **Metalness**, **Roughness**, **Occlusion**, **Sheen**, **Opacity**: drop in the texture and pick its channel,
  or leave the texture empty and set a value. For a smoothness map, pick it as Roughness and tick **Inverted**.

## 7. Make the ID map

The ID map says which part of the garment each texel belongs to: one flat colour per part.

**The quick way:** next to **ID Map**, pick a size and click **Generate from UV islands**. Ros' Wardrobe gives
every UV island its own colour, writes `<Garment> ID.png` next to the `.rgarm`, and measures each part's real-world
size so tiling patterns come out at the right scale.

**Painting your own** (e.g. in Substance Painter) gives you full control, for example parts that aren't whole UV
islands, like a contrast panel:

- one **flat colour per part**, with no gradients or noise inside a part,
- **black** = never recoloured (buttons, zips, labels, prints you want to keep),
- anti-aliased edges are fine,
- save it at the base colour's size or smaller and drop it into **ID Map**.

Run *Generate from UV islands* once anyway, even if you then replace the map: it's what measures the parts for
tiling patterns. Or set **Metres per UV** by hand.

## 8. Make the gradient map

The gradient map says where each texel sits along a gradient, for colours that are gradients (black = start,
white = end).

**The quick way:** next to **Gradient Map**, pick a size and a direction (*Top → Bottom* suits most tops) and
click **Generate**.

**Painting your own:** a **16-bit grayscale** image (import it as R16), black where gradients start and white where
they end. A painted one can follow the garment, e.g. from the collar out to the cuffs.

## 9. Name the regions

Regions are what buyers recolour: *Body*, *Sleeves*, *Collar*… Each region is one or more parts of the ID map.

1. Click **One Region per Part** to start with a region for every part.
2. Under **Parts**, each part has a swatch and a share of the texture. Pick which region each one belongs to. For
   example, put both sleeves in *Sleeves*, or keep *Left Sleeve* and *Right Sleeve* apart for more freedom.
3. Rename the regions to what buyers should see, and remove the ones you don't need (**✕**).

A garment can have up to 31 regions. Renaming a region also renames it in the garment's looks, so they keep
working.

## 10. Try it

1. Open **Tools → Ros' Wardrobe**, pick your avatar, and click your garment to put it on.
2. Open **Custom** and try a few patterns and colours on each region. Check the gradient direction, the size of
   tiling patterns, and that nothing you marked black gets recoloured.
3. Try each material type the garment can be drawn in.
4. Do a test upload, and check the garment in-game with a look on.

## 11. Make looks

Looks are what make the garment fun to open: ready-made outfits buyers click through.

1. With the garment on, make a look in the window.
2. Click **Save as Preset…**, and move the `.rpre` next to the garment (or into the pattern's folder) so it ships
   with the pack.
3. To add colourways (the same look in other colours), select the `.rpre` and add them in the Inspector.

Patterns made for your garment (stripes, colour blocks, prints) are covered in
[Patterns and looks](patterns-and-looks.md).

## 12. Ship it

- Export the pack as a `.unitypackage`: the `.rgarm`, `.ravt`, ID and gradient maps, prefab, mesh, textures,
  patterns and presets. Leave out `Assets/Wardrobe Recolors`, which holds your own looks, not the garment's.
- Tell buyers to **install Ros' Wardrobe through VCC before importing your pack**:
  `https://itsrosfox.github.io/ross-wardrobe/`. You may also include an unmodified copy of Ros' Wardrobe; see
  [LICENSE.md](../LICENSE.md).
- Optional: add a **Wardrobe Recolor** component to the prefab (*Add Component → Ros' den → Wardrobe Recolor*)
  with **Garment** set to your `.rgarm`. Buyers who drag the prefab onto the avatar by hand, instead of using the
  window, then get the recolour editor in the Inspector. It's editor-only and isn't uploaded.
- Let people know it works with Ros' Wardrobe. Open an issue on this repository with a link, and it can go on the
  README's list.

## Fitting the same garment to another avatar

Make another `.rgarm` for the new fit with the **same id** and the new **Made For**. Patterns and looks that name
only the garment's id (tiling patterns, most looks) work on every fit. Hybrid and complete patterns are laid out on
UVs, so they need maps per fit.

## Updating a garment

- Bump **Version**. If the UVs changed, bump the major version (1.x → 2.0) and update the patterns that list it.
- Keep the **id** and the region names, so buyers' looks still apply.
