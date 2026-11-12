# Patterns and looks

**Patterns** (`.rpat`) are what a part of a garment can be made of: Solid, Gradient, stripes, checks, knits, colour
blocks, prints. **Looks** (`.rpre`, also called presets) are ready-made outfits: a pattern and colours for each part
of a garment, plus **colourways**, the same look in other colours.

Ros' Wardrobe itself ships only **Solid** and **Gradient** and their looks. Everything else ships with the clothing
that uses it. Patterns and looks are linked to garments by id, so you can make them for your own garments or add
them to someone else's.

## The three kinds of pattern

| Kind | Laid out | Good for | Works on |
| --- | --- | --- | --- |
| **Simple** (tiling) | a small tile repeated over the cloth at real-world size | stripes, checks, weaves, knits, dots, all-over prints | any garment (or the ones it lists) |
| **Hybrid** | on one garment's UVs | colour blocks, raglan sleeves, panels, fades from a point | the garment fits it lists |
| **Complete** | a picture on one garment's UVs | graphics, logos, prints that aren't recoloured | the garment fits it lists |

Each pattern has up to **4 colour slots** (*Colour*, or *Body* and *Sleeves*, or *Ground*, *Stripe*…). Buyers set
each slot's colour, and slots marked *can gradient* can be gradients too.

## Making a pattern

1. Make a folder for the pattern and its textures (`<Pattern>/`).
2. In **Tools → Ros' Wardrobe → Setup**, click **New Pattern…** under *Patterns* and save it in that folder. (Or
   pick **Assets → Create → Ros' Wardrobe → Pattern**; selecting the file shows the same setup in the Inspector.)
3. Follow the steps:
   1. **Pattern**: its **Name**, **Id** (e.g. `yourname.pinstripe`; **Use …** suggests one) and **Group** (where the
      picker lists it: Plain, Blocks, Graphics, Stripes, Checks, Weaves, Knits, Prints).
   2. **Type**: simple, hybrid or complete (below).
   3. **Textures**: drag the pattern's textures onto the drop area. Each goes where its name says (`weights`,
      `normal`, `height`, `surface`/`orm`/`roughness`, `ao`, `mask`, `albedo`/`print`…; a texture with none of these
      is the weights, or a complete pattern's picture), and is imported the way that needs: normal maps as normal
      maps, the rest without sRGB, tiling ones repeating. A weights map adds the colours it uses. Drop a texture on a
      field to change it.
   4. **Colours**: the colour slots, their names and what they start as.
   5. **Garments**: **Every garment**, or **Only some garments** with the ones it's for ticked. Hybrid and complete
      patterns get each garment's maps here (drop them on its drop area).
   6. **Preview**: the pattern on a garment it fits (save to see your changes).
4. Click **Save**. Then put a garment on in the window, open **Custom**, and try the pattern on a part.

### Simple (tiling) patterns

A tile that repeats over the cloth. **Tile Size (mm)** is how big one tile is on the garment, in millimetres, so the
same gingham is the same size on a shirt and on trousers. Ros' Wardrobe knows each part's real size from the
garment's ID map.

Its maps, all optional and all tiling:

| Map | What it holds |
| --- | --- |
| **Weights** | R, G, B, A = how much of colour 1, 2, 3, 4 each texel is. None = colour 1 everywhere. |
| **Normal** | relief, added to the garment's normal map (*Normal Strength*) |
| **Height** | R = relief as a height map, white = raised (*Height Strength*). Often easier than a normal map for knits. |
| **Surface** | R metalness, G roughness, A opacity (holes, for mesh or lace) |
| **Occlusion** | R, multiplied into the garment's ambient occlusion |

Also: **Sheen** (how much rim light the cloth gets: higher for velvet and wool, lower for cotton), and the normal
mode, where the pattern's relief can **replace** the garment's normal map instead of adding to it.

Tips:

- Draw a weights tile so its colours add up to 1 in every texel. For a two-colour stripe, R = 1 in one stripe and
  G = 1 in the other.
- Garment textures have roughly 2 to 4 texels per millimetre of cloth. Details under about 1.5 mm blur or shimmer
  (moiré), so draw fine weaves bigger than life. Buyers can still scale patterns down in the window.
- A knit is a fabric, not a colouring: one colour slot plus a height map. The same garment becomes a smooth jersey
  or a chunky rib depending on the pattern.

### Fabrics from Marvelous Designer or CLO

Select a folder in the Project window, then **Assets → Ros' Wardrobe → Import Marvelous Designer Fabric…** and pick a
`.zfab`. Ros' Wardrobe makes a simple pattern from the fabric's relief (normal map) and surface (metalness, roughness,
opacity), in one colour slot. The fabric's own colour texture isn't used: that's what buyers choose. Importing the
same fabric again updates its maps and keeps your settings.

### Hybrid patterns

Laid out on one garment's UVs, for designs that depend on the garment's shape. Under **Garments**, pick **Only some
garments** and tick each garment it's for; under *Per garment*, each gets its own (and can be limited to one avatar
or version there):

- **Weights**: on the garment's UVs, R, G, B, A = colour 1..4. It's an id mask: which colour goes where. Paint it
  over the garment's UV layout, e.g. in Substance Painter, with R where the body colour goes and G on the sleeves.
- **Gradient** (optional): its own position along the colours' gradients, instead of the garment's gradient map.
  A fade from the chest outwards, say.

Hybrid patterns can also have tiling maps (relief, surface) that apply on top. A hybrid pattern that lists no
garments (like the built-in Gradient) is offered for every garment, along each one's own gradient map.

### Complete patterns

A picture painted on one garment's UVs that isn't recoloured: graphics, logos, photo prints. Per garment fit:

- **Albedo**: the picture. Alpha = where it covers. Colour 1 shows everywhere else.
- **Mask** (optional): R = which texels the picture may cover.

A complete pattern is only offered for the garment fits it has an albedo for.

### Limiting patterns

- **Garments**: a pattern that lists garments is only offered for them. A simple pattern can name just the
  garment id (every fit), an id and an avatar, and a major version (`"1"` = 1.x). Hybrid and complete patterns
  name each fit.
- When several packs ship a pattern with the same **id**, it's offered for the garments of all of them. Use your own
  id prefix (`yourname.`) unless you mean to extend someone else's pattern.

### Who ships what

- **A pattern ships with every pack whose garments support it.** If your garments support someone else's pattern,
  copy its `.rpat` and textures into your pack, keep its id, and tick your garments on it. Buyers who have both packs
  get one pattern with both lists, and buyers who have only yours still get it.
- **Hybrid and complete patterns' per-garment maps ship with whoever drew them.** These are painted on one garment's
  UVs (e.g. in Substance Painter), so a garment maker who offers a colour layout for their garment draws its maps and
  ships them with the pattern. Each fit (the garment on one avatar) is its own entry with its own maps, since its UVs
  can differ.
- **ID maps** you paint yourself (in Substance Painter, say) when the generated one isn't enough, for example a cuff
  band that's part of a bigger UV island: see [Painting your own maps](making-a-garment.md#painting-your-own-maps).

## Making looks

A look sets a pattern and colours for the parts of a garment.

**From the window (easiest):**

1. Put the garment on, open **Custom**, and make the look.
2. Click **Save as Preset…**. Ros' Wardrobe writes a `.rpre` for the garment.
3. Move it next to the garment or into the pattern's folder, so it ships with your pack.

**In Setup → Presets** (or the Inspector, with the `.rpre` selected), edit it, or make one from scratch with
**New Preset…**:

- **Name**, **Group** and **Order** arrange the Looks gallery.
- **Look Name** names the look's own colouring ("Navy & White"); each colourway has its own name.
- **Garments**: **Every garment**, which suits looks that only use `*`, or **Only some garments**, ticked.
- **Parts**: a pattern and colours per part. A part names a region (`Body`), several (`Collar, Hem`), or `*` for
  every region no other part names. Using `*` plus a few named trims makes a look work on garments with
  different regions. Each part can also set **Scale** and **Rotate** for tiling patterns.
- **Colourways**: **Add Colourway** adds the same look in other colours. A colourway only changes colours: the
  patterns stay the look's own. **Recolour a Part** adds a part to it.

Colours are `#RRGGBB`, or a gradient as `#RRGGBB@0, #RRGGBB@1` (colour at position).

### What makes a good set of looks

- **Lead with the classics.** The first colouring of each look is what buyers see first: navy, white, black,
  heather grey, the colours the real garment comes in.
- **Use trims.** A patterned body with a solid collar and hem in the darkest colour of the pattern reads as a real
  garment.
- **Three to eight colourways** per look gives choice without a wall of swatches.
- **Group them** (Plain, Stripes, Checks…) so the gallery is easy to browse.

See the [file reference](file-reference.md#rpat) for every field.
