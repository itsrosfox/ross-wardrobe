# Material types

A **material type** (`.rmat`) links a material you made in Unity and says how a garment's maps go into its
textures. A garment can be drawn in every material type that fits it, and buyers pick one at the top of the window
(e.g. *Toon* or *PBR*).

Ros' Wardrobe ships one: **PBR**, Unity's Standard lighting with both sides of the cloth drawn, for worlds, tools
and platforms without Poiyomi. Garment packs add their own, usually the toon shader the garment was made for.

## How a look becomes a material

When a look is baked, Ros' Wardrobe:

1. makes the **look colour**: the garment's base colour, recoloured,
2. makes the **normal** map: the garment's, with the patterns' relief added,
3. packs any other textures the material type asks for, channel by channel, from the garment's maps (metalness,
   roughness, occlusion, sheen, opacity, the normal map) and the patterns' surface maps,
4. makes the garment's material from the **linked material**, sets those textures (and any extra settings), and
   saves it in `Assets/Wardrobe Recolors/<Avatar>/<Garment>`.

With **Follow Changes** on (the default), the garment's materials are
[Material Variants](https://docs.unity3d.com/2022.3/Documentation/Manual/materialvariant-concept.html) of the linked
material: only their textures are their own, so what you change on the linked material (a rim colour, the outline,
a lighting setting) shows on every garment in the type at once, worn ones too.

When the garment's own textures are already laid out the way the material type wants them, they're used as they
are, so nothing is packed twice.

## Making one

1. Make a material in Unity, set up the way garments in this type should look: any shader (Poiyomi, lilToon,
   Standard…), lighting, rim, outline, everything except its textures.
2. In the Ros' Wardrobe window's **Setup** tab, click **New Material Type…** under *Material types* and save it in your
   pack's Materials folder (or pick **Assets → Create → Ros' Wardrobe → Material Type**).
3. Give it a **Name** (what buyers see, e.g. *Toon*) and an **Id**.
4. Drag your material into **Material**. (No material yet? **New Material…** makes one next to the file.) Ros'
   Wardrobe fills in the textures from their names; check them in **Textures** (below).
5. Under **Garments**, keep **Every garment**, or pick **Only some garments** and tick them.
6. Click **Save**. On each garment, pick the type in step 6 (*Starts In*) to ship garments in it.

Poiyomi: you can link an unlocked material; garments' materials follow it. Do a test upload of one garment before
shipping, as Poiyomi's Lock In works on each material. If a shader gives trouble with Material Variants, turn
**Follow Changes** off: the garment's materials are then copies, made again whenever the linked material changes.

A linked material in a package (not under `Assets`) can't ship with your garments, so the materials the garments ship
in are copies of it; the ones made in a buyer's project still follow it.

## Textures

**Textures** lists the material's texture properties (the ones its shader uses; tick *Show All* for the rest). Each
says what goes into it:

| Kind | What goes in |
| --- | --- |
| **Keep the material's** | nothing: the texture stays as the linked material has it |
| **Look colour** | the recoloured base colour (with the garment's alpha) |
| **Normal map** | the normal map, patterns' relief included |
| **Packed channels** | a texture made channel by channel: you pick what goes in R, G, B and A |

**Fill In from Names** picks a kind for each texture by its property's name (`_MainTex` → Look colour, `_BumpMap` →
Normal map, `_MetallicGlossMap` → the Standard layout, and so on).

### Packed channels

Each of R, G, B and A gets one source, picked from a menu:

| Group | Sources |
| --- | --- |
| Surface | metalness, roughness, smoothness (1 − roughness), ambient occlusion, sheen |
| Look colour | red, green, blue, brightness (greyscale) |
| Opacity | the garment's opacity (its opacity map, else the base colour's alpha) |
| Normal | X (red), Y (green), Z (blue), octahedral U and V, hemi-octahedral U and V |
| Constant value | a number from 0 to 1, the same everywhere |

Tick **Invert** to put in one minus the source (e.g. a DirectX normal's green channel, or roughness where a shader
wants smoothness). The preview shows the packed texture (RGB and alpha) on a garment that fits.

**Common Layouts** fills all four channels the way common shaders pack them:

- Unity Standard (`_MetallicGlossMap`): R metalness, G occlusion, A smoothness
- URP / HDRP mask map: R metalness, G occlusion, B detail mask, A smoothness
- ORM: R occlusion, G roughness, B metalness
- Poiyomi metallics: R metalness, G smoothness, B reflection, A specular
- one map in every channel (greyscale roughness, smoothness, metalness, occlusion, opacity)
- normal maps: XY in RG, DirectX (green flipped), XY in AG (DXT5nm style), octahedral in RG, hemi-octahedral in RG
- octahedral normal in BA with roughness in R and occlusion in G, as some fur card shaders pack it

The normal sources are the garment's tangent-space normal (its normal map, else flat). Octahedral encoding stores a
whole unit vector in two channels; hemi-octahedral stores the upper half (Z ≥ 0) in two channels at better precision.
Both are written in 0..1 (0.5 = 0).

Under **More** for each texture:

- **Also On**: put the same texture in more properties (e.g. Poiyomi's AO and rim masks use the same packed map).
- **Keyword**: a shader keyword turned on with the texture (e.g. `_NORMALMAP` for Standard). Fill In from Names sets
  it for the shaders it knows.

## Advanced

- **Shader Instead**: make the garments' materials with this shader rather than the linked material's (they're
  copies then, not variants).
- **Floats**, **Colours** and **Keywords**: set on every material made from the type, on top of the linked
  material's. Set them on the material instead where you can; use these for what the type needs regardless of it,
  e.g. `_Cull = 0` to draw both sides.

## Examples

Ros' Shirts' **Toon** type, for a locked Poiyomi material:

```json
{
    "id": "rosden.toon",
    "name": "Toon",
    "version": "1.0.0",
    "author": "Ros' den",
    "description": "Poiyomi Toon as Ros' den garments ship (locked, so Poiyomi doesn't need to be installed).",
    "template": "Toon Template.mat",
    "textures": [
        { "property": "_MainTex", "channels": "albedo" },
        { "property": "_BumpMap", "channels": "normal" },
        {
            "property": "_MochieMetallicMaps",
            "channels": ["metalness", "smoothness", "occlusion", "sheen"],
            "alsoOn": ["_LightingAOMaps", "_RimMask"]
        }
    ]
}
```

The built-in **PBR** type, from a shader:

```json
{
    "id": "rosden.pbr",
    "name": "PBR",
    "version": "1.0.0",
    "author": "Ros' den",
    "shader": "Ros' Wardrobe/Standard Double Sided",
    "textures": [
        { "property": "_MainTex", "channels": "albedo" },
        { "property": "_BumpMap", "channels": "normal", "keyword": "_NORMALMAP" },
        {
            "property": "_MetallicGlossMap",
            "channels": ["metalness", "occlusion", 0, "smoothness"],
            "alsoOn": ["_OcclusionMap"],
            "keyword": "_METALLICGLOSSMAP"
        }
    ],
    "floats": { "_GlossMapScale": 1, "_Cull": 0 }
}
```

A fur card material that wants roughness, occlusion and an octahedral normal in one texture, with DirectX-style
green in its normal map:

```json
{
    "id": "yourname.fur",
    "name": "Fur",
    "template": "Fur.mat",
    "textures": [
        { "property": "_MainTex", "channels": "albedo" },
        {
            "property": "_PackedMap",
            "channels": ["roughness", "occlusion", "normal.oct.u", { "from": "normal.oct.v", "invert": true }]
        }
    ]
}
```

See the [file reference](file-reference.md#rmat) for every field.
