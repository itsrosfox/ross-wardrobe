# Ros' Wardrobe

Sims-style recolouring for avatar clothing in the Unity editor. Pick a look, change colours or gradients per
part of the garment, see it live on the avatar, then apply. Applying bakes a normal texture + material copy,
so it works with any shader (including locked Poiyomi) and uploads to VRChat like any other material. Nothing
from this package ends up in the upload.

## Installing

Through the VRChat Creator Companion: open <https://itsrosfox.github.io/ross-wardrobe/> and click **Add to VCC**
(or *Settings → Packages → Add Repository* with `https://itsrosfox.github.io/ross-wardrobe/index.json`). Then
*Manage Project* on your avatar project and add **Ros' Wardrobe**. Garments that support recolouring need it installed
before you import them.

## For buyers

1. Select the garment in your scene (the object with **Wardrobe Recolor** on it).
2. Click a look under **Looks**, or change the regions under **Colours**:
   - **Original**: leave this part as it ships.
   - **Colour**: pick any colour, or click one of the swatches. Fabric shading and detail are kept.
   - **Gradient**: edit the gradient; **⇄** flips it.
3. The garment previews your changes while it's selected.
4. **Save as Preset…** keeps the look so it shows up under Looks next time.
5. **Apply to Garment** bakes it. The texture and material are written to a `Recolors` folder next to the garment
   and put on it. Apply again after changing anything. Pick **Original** and apply to go back.

## For creators

Each garment gets one **Wardrobe Garment** asset (*Create → Ros' den → Wardrobe Garment*), shipped with it.

1. **Source**: the garment mesh, its base colour texture, and its material. The recolour copies this material
   and swaps one texture (*Texture Property*, `_MainTex` for Poiyomi). *Detail* sets how much of the base
   texture's shading shows through recoloured parts.
2. **ID map**: one flat colour per part. *Generate from UV islands* makes one from the mesh (one colour per
   UV island), or paint your own (e.g. Substance Painter). Black means "never recoloured". Anti-aliased edges are fine.
3. **Gradient map**: 16-bit grayscale saying where each pixel sits along a gradient (black = start, white =
   end). *Generate* makes one along an axis of the mesh, or paint your own and import it as R16.
4. **Regions**: names the buyer sees (Body, Sleeves, Collar…). Each region combines one or more parts of the ID
   map: pick a region per part under **Parts**. *One Region per Part* is a quick start.
5. **Presets**: add the garment's **Wardrobe Recolor** component to the prefab, set up looks, *Save as Preset…*,
   then drag the presets into the garment's Presets list so they ship first.

The generated maps and presets are editor-only data and aren't included in avatar uploads.

## How it works

The bake shader reads the ID map (turned into a region per texel), the gradient map, and a small gradient atlas
(one row per region). For each recoloured texel it outputs `colour × (base luminance ÷ the region's average
luminance)`, so the new colour keeps the fabric's folds, AO and stitching. The live preview renders the same
thing into a render texture shown through a `MaterialPropertyBlock` (never saved). Apply renders it at the base
texture's full source resolution, writes a PNG with the base texture's import settings, and copies the
material.
