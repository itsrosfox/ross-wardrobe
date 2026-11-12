# Making a garment

This guide takes a garment from your cloth tool's export to a recolourable garment pack that buyers open in Tools →
Ros' Wardrobe. Everything happens in the window's **Setup** tab, one step after another; you don't need any
scripts. It assumes you know how to make clothing for a VRChat avatar.

**What you'll end up with:**

```
<Pack>/
  Avatars/<Avatar>.ravt                     which avatars the garments fit (one per avatar base)
  Garments/<Garment>/
    <Garment>.fbx                           your cloth tool's export
    <Garment>_diffuse.png, _normal.png…     its textures, as your tools exported them
    <Garment>.rgarm                         the garment's Ros' Wardrobe file
    <Garment>.asset                         the fitted mesh (made by Fit to Avatar)
    <Garment>.prefab                        what buyers drop on their avatar (made by Make Prefab)
    <Garment> (<Type>).mat                  its material (made by Make Material)
    <Garment> ID.png, Gradient.png          its recolouring maps (made by Make Maps)
  Patterns/<Pattern>/<Pattern>.rpat         patterns that come with the pack (optional)
               + <Look>.rpre                 looks made with them
  Materials/<Type>.rmat                     your material type, if the pack has its own (e.g. Poiyomi)
```

## 0. What you need

- A Unity 2022.3 avatar project with the VRChat SDK, set up with VCC.
- **Ros' Wardrobe** (see [Installing](../README.md#installing)) and **VRCFury** (Make Prefab uses it for the
  Armature Link, the Blendshape Link and the menu toggle).
- The avatar base you're making the garment for, imported into the project.
- Your garment exported from your cloth tool (e.g. Marvelous Designer's FBX), rigged to the avatar's skeleton, and
  its textures.

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

**Body shapes.**
You don't make the garment's shape keys: Fit to Avatar gives the garment a shape key for each body shape key the
avatar base lists, so it follows the body. For a better fit on a big shape (Buff, Chest…), export the garment again
from your cloth tool with that key on the body at 100 (Marvelous Designer: *Avatar → Blend Shape*), and give that
export to the key in step 4.

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

**Surface maps:** metalness, roughness (or smoothness), ambient occlusion, sheen and opacity. Keep them as your
tools export them: each can be its own texture, a channel of a packed one (e.g. an ORM texture), or a single value.
Ros' Wardrobe packs them the way each material type needs.

Texture size: looks are baked at the base colour's size. 2048 or 4096 is typical.

## 3. Make sure the avatar base has a `.ravt`

Ros' Wardrobe only lists a garment for avatars built on the base it was fitted to. A `.ravt` describes that base:
its model, its body and which of its shape keys garments follow. If no one has made one for your base yet, make it
now: see [Adding an avatar base](adding-an-avatar-base.md). It takes a few minutes.

Ship the `.ravt` in your pack (in `<Pack>/Avatars`). Several packs shipping the same `.ravt` is fine: they share
the id.

## 4. Set the garment up

1. Open **Tools → Ros' Wardrobe** and its **Setup** tab.
2. Under **Garments**, click **New Garment…** and save the `.rgarm` in the garment's folder, next to its export.
3. The setup opens on the right, in steps. Each step's mark says where it stands: **✓** done, **!** needs you,
   **○** waits for an earlier step. Problems the checks find are listed at the top, each with a button that fixes
   it where it can.

You can also select the `.rgarm` in the Project window: the Inspector shows the same setup.

**1. Garment**
- **Name**: what buyers see.
- **Id**: a stable id like `yourname.hoodie` (**Use …** suggests one from the name). Patterns and looks refer to the
  garment by it, so **never change it once shipped**. Fits of the same garment for other avatars use the **same
  id**.
- **Version**: your garment's version. Patterns can be limited to a major version, in case you change the UVs.
- **Category**: where the window lists it (Tops, Bottoms, Shoes…; the ▾ suggests some).
- **Slot**: putting on a garment takes off the one worn in the same slot (Top, Bottom, Shoes…). Leave it empty for
  things that are never swapped out.
- **Thumbnail**: leave it empty to have it rendered from the garment.

**2. Avatar and cloth export**
- **Made For**: the avatar base.
- **Source Model**: drop in your cloth tool's export. Its maps are found from the textures next to it (Marvelous
  Designer's `_diffuse_`, `_normal_`, `_roughness_`, `_metalness_`, `_ambientOcclusion_`, `_opacity_`, and the usual
  names other tools use); **Find Maps** looks again.
- **Hides on Body**: **Add** the body's shape keys that hide skin under the garment (e.g. *TShirt*). They're only
  suggestions: when the garment is put on, the window's Avatar section offers to turn them on. The body changes only
  when the buyer says so.

**3. Maps**
- **Base Colour** and **Normal**. If your normal map is DirectX style (green down, as Substance and Unreal export),
  tick **DirectX (Green Down)** and Unity flips it on import.
- **Roughness**, **Metalness**, **Occlusion**, **Opacity** (and **Sheen** under *More*): the texture and the channel
  it's in, or no texture and a value. For a smoothness (gloss) map, tick **Inverted**. For an ORM texture, use the
  same texture with R for occlusion, G for roughness and B for metalness.

**4. Fit to avatar**
Lists the body shape keys the garment will follow, each with an optional export made with that key on the body (see
step 1). **Fit to Avatar** makes `<Garment>.asset`, the mesh the prefab wears, with those shape keys. After a new
export, overwrite the file and click **Fit Again**; the step says when the fit is out of date.

**5. Prefab**
**Make Prefab** makes `<Garment>.prefab` from the export and the fit, with VRCFury's:
- **Armature Link**, linking its armature to the avatar's,
- **Blendshape Link** to the avatar's body, so it follows the body's sliders in-game,
- a **Toggle** in the avatar's menu at **Menu Path** (default *Clothing/&lt;Garment&gt;*), saved and on by default.

Making it again replaces the prefab, so what you added to it by hand is lost (you're asked first).

**6. Material**
- **Starts In**: the material type it starts and ships in. *PBR* comes with Ros' Wardrobe; see
  [Material types](material-types.md) for others.
- **Make Material** writes `<Garment> (<Type>).mat` next to the `.rgarm`, packing the maps the way the type needs,
  and puts it on the prefab. When the type links a material in your pack with *Follow Changes* on, it's a Material
  Variant of it, so what you change on that material shows on the garment.

**7. Recolouring**
**Make Maps** makes the ID map (one flat colour per UV island, and each part's real-world size for tiling patterns)
and the gradient map (*Top → Bottom*; change the direction under *Maps and sizes*). Then name the **regions**:

1. A region is what buyers recolour: *Body*, *Sleeves*, *Collar*… Making the maps starts one region per part.
2. Under **Parts**, each part has a swatch and a share of the texture. Pick which region each one belongs to: put
   both sleeves in *Sleeves*, or keep *Left Sleeve* and *Right Sleeve* apart for more freedom.
3. Rename the regions (the ▾ suggests names) and remove the ones you don't need (**✕**).

For a fit of a garment you've already set up on another avatar, click **Copy From Another Fit…** instead and pick
that fit: each part goes in the region most of it is in there, with the same region names, so its looks apply. It
works because the pattern pieces have the same UVs on every avatar. It tells you which parts it couldn't place (pick
their region by hand) and which regions got no part, e.g. a cuff band painted into the other fit's ID map: paint it
into this one's too.

A garment can have up to 31 regions. Renaming a region also renames it in the garment's looks, so they keep working.

**8. Material types, patterns and presets for it**
Tick the material types and patterns that are for this garment: the file then lists its id. Files that list no
garments are for every garment. Hybrid and complete patterns need maps for the garment: **Open** one to add them.

**Make Them** at the top runs steps 4 to 7 in one go when several are left. The buttons that make something save the
garment; for the rest, click **Save**.

### Painting your own maps

The generated ID and gradient maps suit most garments. Paint your own (e.g. in Substance Painter) for parts that
aren't whole UV islands, like a contrast panel:

- **ID map**: one **flat colour per part**, with no gradients or noise inside a part; **black** = never recoloured
  (buttons, zips, labels, prints you want to keep); anti-aliased edges are fine. Save it at the base colour's size
  or smaller and drop it into **ID Map** (under *Maps and sizes*). Make the maps once anyway: that's what measures
  the parts for tiling patterns. Or set **Metres per UV** by hand.
- **Gradient map**: a **16-bit grayscale** image (import it as R16), black where gradients start and white where
  they end. A painted one can follow the garment, e.g. from the collar out to the cuffs.

## 5. Try it

1. In the window's **Wardrobe** tab, pick your avatar and click your garment to put it on.
2. Open **Custom** and try a few patterns and colours on each region. Check the gradient direction, the size of
   tiling patterns, and that nothing you marked black gets recoloured.
3. Try each material type the garment can be drawn in.
4. Do a test upload, and check the garment in-game with a look on.

## 6. Make looks

Looks are what make the garment fun to open: ready-made outfits buyers click through.

1. With the garment on, make a look in the window.
2. Click **Save as Preset…**, and move the `.rpre` next to the garment (or into the pattern's folder) so it ships
   with the pack.
3. To add colourways (the same look in other colours), open the `.rpre` in Setup → Presets and add them.

Patterns made for your garment (stripes, colour blocks, prints) are covered in
[Patterns and looks](patterns-and-looks.md).

## 7. Ship it

- Check the Setup tab's **Checks**: errors and warnings there are what buyers would run into.
- Package it the way you package your other assets: Ros' Wardrobe doesn't export packs. Ship the `.rgarm`, `.ravt`,
  fitted mesh, prefab, material, ID and gradient maps, textures, material types, patterns and presets. Leave out
  `Assets/Wardrobe Recolors`, which holds your own looks, not the garment's.
- **Never ship the avatar base's own files** (its model, prefab, textures, materials): they're its creator's, and
  buyers have them already. The `.ravt` names the base's files by GUID and doesn't contain them, and nothing Ros'
  Wardrobe makes for a garment needs them. Before you upload, check that the package you made has none of the
  avatar's folders in it (Unity's *Include dependencies* can pull them in).
- Ship a copy of every pattern your garments support, including ones someone else made (keep its id, and add your
  garments to its list). Buyers then get the pattern with your pack even if they don't have the other one.
- Tell buyers to **install Ros' Wardrobe through VCC before importing your pack**:
  `https://itsrosfox.github.io/ross-wardrobe/`. You may also include an unmodified copy of Ros' Wardrobe; see
  [LICENSE.md](../LICENSE.md).
- Optional: add a **Wardrobe Recolor** component to the prefab (*Add Component → Ros' den → Wardrobe Recolor*)
  with **Garment** set to your `.rgarm`. Buyers who drag the prefab onto the avatar by hand, instead of using the
  window, then get the recolour editor in the Inspector. It's editor-only and isn't uploaded.
- Let people know it works with Ros' Wardrobe. Open an issue on this repository with a link, and it can go on the
  README's list.

## Fitting the same garment to another avatar

Make another `.rgarm` for the new fit with the **same id** and the new **Made For**, next to that avatar's export.
Keep the pattern pieces' UVs as they are when you move the garment to the new avatar in your cloth tool. Then, in
step 7, **Copy From Another Fit…** gives it the first fit's regions.
Patterns and looks that name only the garment's id (tiling patterns, most looks) work on every fit. Hybrid and
complete patterns are laid out on UVs, so they need maps per fit.

## Updating a garment

- Bump **Version**. If the UVs changed, bump the major version (1.x → 2.0) and update the patterns that list it.
- Keep the **id** and the region names, so buyers' looks still apply.
- After a new export, **Fit Again**, then **Make Again** the material and maps if they say they're out of date.
