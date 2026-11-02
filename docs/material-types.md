# Material types

A **material type** (`.rmat`) says how a look becomes a material: which shader, which settings, and what goes in
each texture. A garment can be drawn in every material type that fits it, and buyers pick one at the top of the
window (e.g. *Toon* or *PBR*).

Ros' Wardrobe ships one: **PBR**, Unity's Standard lighting with both sides of the cloth drawn, for worlds, tools
and platforms without Poiyomi. Garment packs add their own, usually the toon shader the garment was made for.

## How a look becomes a material

When a look is baked, Ros' Wardrobe:

1. makes the **albedo**: the garment's base colour, recoloured,
2. makes the **normal** map: the garment's, with the patterns' relief added,
3. packs any other textures the material type asks for, channel by channel, from the garment's maps (metalness,
   roughness, occlusion, sheen, opacity) and the patterns' surface maps,
4. copies the **template** material (or makes one with the **shader**), sets the textures, floats, colours and
   keywords, and saves it in `Assets/Wardrobe Recolors/<Avatar>/<Garment>`.

When the garment's own textures are already laid out the way the material type wants them, they're used as they
are, so nothing is packed twice.

## Making one

1. Make a material with your shader, set up the way your garment should look: lighting, rim, outline, everything
   except its textures. This is the **template**.
   - **Poiyomi:** lock the template material, so buyers don't need Poiyomi installed. Ship the locked shader with
     the pack, as you would for any Poiyomi garment.
2. Pick **Assets → Create → Ros' Wardrobe → Material Type** and name the file.
3. Select it. In the Inspector, drop in the **Template** (or just a **Shader**), and set a **Name** (what buyers
   see, e.g. *Toon*) and an **Id**. A new type puts the albedo in `_MainTex` and the normal map in `_BumpMap`;
   change them if your shader uses other properties.
4. Add the rest of the textures (below), list the **Garments** it's for (none = every garment), and click **Save**.
5. On your garment's `.rgarm`, set **Starts In** to the new type.

## Textures

Each entry under **Textures** fills one texture property of the material:

| Channels | What goes in |
| --- | --- |
| `albedo` | the recoloured base colour (with the garment's alpha) |
| `normal` | the normal map, patterns' relief included |
| 4 channels | a packed texture, one source per channel (R, G, B, A) |

Sources for a packed texture's channels: `metalness`, `roughness`, `smoothness` (1 − roughness), `occlusion`,
`sheen`, `opacity`, `albedo.r`, `albedo.g`, `albedo.b`, or a number (0 to 1) for a channel that's the same
everywhere.

- **Also On**: put the same texture in more properties (e.g. Poiyomi's AO and rim masks use the same packed map).
- **Keyword**: a shader keyword turned on with the texture (e.g. `_NORMALMAP` for Standard).

## Settings

**Floats**, **Colours** and **Keywords** are set on every material made from the type, on top of the template's.
Use them for what the type needs regardless of the template, e.g. `_Cull = 0` to draw both sides.

## Examples

Ros' Shirts' **Toon** type, for a locked Poiyomi template:

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

See the [file reference](file-reference.md#rmat) for every field.
