# Using Ros' Wardrobe

This guide is for anyone dressing an avatar with clothing that supports Ros' Wardrobe. To make clothing for it,
see [Making a garment](making-a-garment.md).

## Before you start

1. Install Ros' Wardrobe through VCC (see [Installing](../README.md#installing)).
2. Install what the garment's own instructions ask for. That's usually **VRCFury**, which puts the clothing on
   the avatar when you upload.
3. Import the garments **after** Ros' Wardrobe, so their files are read as Ros' Wardrobe files. If you imported
   them first and the window doesn't list them, right-click their folder and choose *Reimport*.

## The window

Open **Tools → Ros' Wardrobe** and pick your avatar in the **Avatar** menu at the top. It lists every avatar in
the open scenes.

The window works out which avatar base yours is built on, from the base's `.ravt` file, which garment packs ship.
It then lists the clothing fitted to that base. Tick *Show garments for other avatars* to see the rest too.

### Putting clothing on

On the left is the clothing for your avatar, by category. Click a garment to put it on. It replaces whatever the
avatar wears in the same slot (a new top takes off the old top), and one Ctrl+Z takes it back off.

**Take Off** removes a worn garment. To change a worn garment's look later, select it (in the window, or select it
on the avatar in the Hierarchy: the same editor shows in the Inspector).

### Picking a look

1. If the garment can be drawn in more than one way, pick a **Material** at the top, e.g. *Toon* (Poiyomi) or
   *PBR* (Unity's Standard lighting). It switches right away, and your look is redone in that material.
2. Click one of the **Looks**. Its colourways show big underneath; click one to wear it.
3. Each look also has a **Custom** colourway. It keeps the look's patterns and lets you change their colours.

### Making your own look

The **Custom** look at the start of the gallery starts from scratch. For each part of the garment (Body, Sleeves,
Collar…):

- pick a **pattern**: Solid, Gradient, and whatever stripes, checks, weaves, knits and prints the garment comes
  with,
- set its **colours**. Colours that allow it can be a **Gradient** along the garment instead of one colour; **⇄**
  flips it,
- for tiling patterns (stripes, checks, weaves…), change its **Scale** or **Turn 90°**.

Every change shows on the garment right away. Ctrl+Z undoes a change.

### Saving a look

**Save as Preset…** writes the look as a `.rpre` file. It then shows up under Looks for every garment it fits,
next time too.

## Where your looks are saved

There's no Apply button. The look is baked into a texture and a material when you move on: picking another garment,
leaving or closing the window, saving the scene, entering play mode, or uploading. They're saved in

```
Assets/Wardrobe Recolors/<Avatar>/<Garment>/
```

outside the garment's own folder, so updating the garment never overwrites your looks. The avatar uses them like
any other material, and you upload as usual.

> **PBR looks:** the PBR material uses a small shader that comes with Ros' Wardrobe (Unity's Standard lighting,
> with both sides of the cloth drawn). Keep Ros' Wardrobe installed while your avatar wears PBR looks. Every other
> material type uses its own shader, and nothing of Ros' Wardrobe ends up in your upload.

## The Avatar section

For avatar bases whose `.ravt` describes them, the **Avatar** section (first on the left) shapes the avatar's body.

- The base's **sliders** by group (Body, Head, Tail…), its **toggles** and **choices** (e.g. ears), and every other
  shape key of the body under *Other shape keys*.
- Worn garments follow the body's shape keys of the same names, so a shirt stays fitted when you make the avatar
  buff or chonky.

These change the avatar in the editor only. In-game, the avatar's own menu can still change what it animates.

## Troubleshooting

**The window lists no clothing for my avatar.**
Make sure the garment pack is imported (its `.rgarm` and `.ravt` files are in the project) and that you picked the
right avatar at the top. If your avatar is a renamed copy of the base, tick *Show garments for other avatars*.

**It warns that a garment needs a tool I haven't installed.**
Usually VRCFury. Install it through VCC: without it, the garment wouldn't be put on the avatar in-game.

**My look didn't upload.**
Looks are baked before VRChat builds the avatar. If a look still shows only as a preview, close the window or save
the scene before uploading, and check `Assets/Wardrobe Recolors`.
