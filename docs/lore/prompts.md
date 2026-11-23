# StarForge: prompts for the pictures and the intro clip

Written 2026-11-22 for Google Gemini, against `starforge-lore.md` beside this file.

**How to use.** Every prompt below is complete: copy the whole block, attach the file it names, send.
Nothing has to be put together from pieces. Save what Gemini returns under the file name given, in
`Art/Source/llm/lore/` (the folder exists and is not committed). The game's copies are made from
there, and each one is recorded in `THIRD-PARTY.md` with the tool and the date.

| # | What | Needed? | Attach | Save as |
|---|---|---|---|---|
| 1 | Key art with the Brightest Star | yes | `Assets/StarForge/UI/Art/keyart.jpg` | `keyart_star.png` |
| 2 | Plate: the Quenching | optional | the result of prompt 1 | `plate_quenching.png` |
| 3 | Plate: the two hands | optional | the result of prompt 1 | `plate_two_hands.png` |
| 4 | Plate: vespers in two camps | optional | the result of prompt 1 | `plate_vespers.png` |
| 5 | Plate: the gantry | optional | the result of prompt 1 | `plate_gantry.png` |
| 6 | Intro clip, ten seconds | optional | the result of prompt 1 | `intro.mp4` |

The key art is the title screen's background. The four plates are for the Field Manual's Chronicle
page. The intro clip plays once before the title and can be skipped.

Before a result is committed: the repository is public, so check that your Gemini plan lets you
redistribute what it makes.

---

## 1. Key art with the Brightest Star (needed)

Attach `Assets/StarForge/UI/Art/keyart.jpg`. Save the result as `Art/Source/llm/lore/keyart_star.png`.

```text
Edit the attached painting. Keep everything exactly as it is: the composition, the colours, the
brushwork, the image size and the wide 16:9 shape. Make only these three additions.

1. Add one brilliant blue-white star low in the clear band of sky just above the horizon, a little
left of centre. It is far brighter than anything else in the sky, with a faint four-point glint and
a soft halo. Add a faint reflection of it in each of the two lakes.

2. On the near grassy slope at the left, add a tumbled dry-stone field wall and the roofless corner
of a small stone farmhouse with a young birch tree growing inside it. They are old, mossy and half
sunk into the grass, small in the picture, and painted in the same style as the rocks around them.

3. On the walking war machine that stands on the ridge at the right, add a cloth standard hanging
from a crossbar on its back, as a dark silhouette like the machine itself.

Do not add any text, letters, logos or watermark. Do not change the two small armies, the glowing
crystals, the lakes, the road, the cliffs, the fire or the clouds. Do not brighten the picture: the
dark edges and the calm dark sky at the top must stay as they are, because a title is laid over them.
```

---

## 2. Plate: the Quenching (optional)

Attach the finished key art from prompt 1. Save as `Art/Source/llm/lore/plate_quenching.png`.

```text
Paint a new picture in exactly the style of the attached painting: the same painterly digital
matte-painting brushwork, the same muted palette of slate blue, moss green and weathered grey stone
with small accents of ember orange, the same soft haze and the same dark vignette toward every edge.
Use the attached painting only for its style. The new picture shows a different scene. Wide 16:9.

The scene: night on a bare hilltop. In the middle stands a small stone hearth, no larger than a
village smithy's, open to the sky. Its fire is white-gold like sunlight, steady and without smoke,
and it is failing: only the last of it is left, low in the hearth, and the stone is already going
dark. Nine armoured guards stand in a ring round the hearth with their backs to us, each with a
small oil lamp at his feet. Eight of the lamps have burnt out. The ninth is still alight. In the
valley far below, the lights of a city are going out one district at a time. Overhead there is one
brilliant blue-white star with a faint four-point glint, far brighter than the others. The mood is
silent, still and grave: gold draining away into black.

The lower third of the picture is darker and quiet, so that text can be laid over it. No text, no
letters, no logos, no watermark. No modern objects. No magic effects, no beams, no spells: this is a
craftsman's hearth going out. No faces are visible.
```

---

## 3. Plate: the two hands (optional)

Attach the finished key art from prompt 1. Save as `Art/Source/llm/lore/plate_two_hands.png`.

```text
Paint a new picture in exactly the style of the attached painting: the same painterly digital
matte-painting brushwork, the same muted palette of slate blue and weathered grey stone with small
accents of warm lamplight, the same soft haze and the same dark vignette toward every edge. Use the
attached painting only for its style. The new picture shows a different scene. Wide 16:9.

The scene: a vast, plain stone hall at night, open to the sky through one round opening in the roof.
Under the opening two figures stand apart, facing each other across an empty stone table. On the
left is a tall woman veiled in white cloth from the crown of her head to the floor, so that her face
cannot be seen at all; she holds a tall staff topped with a small lamp. On the right is a weathered
war-leader in dark riding armour and a heavy fur-collared cloak, bare-headed, with no crown, his
face in shadow. On the table between them lie two unbroken wax seals and one blank unsigned sheet.
Through the round opening above them shines one brilliant blue-white star with a faint four-point
glint. Neither figure is looking at the other. Cold lamplight, long shadows, great stillness.

The lower third of the picture is darker and quiet, so that text can be laid over it. No text, no
letters or writing on the sheet, no logos, no watermark. No modern objects. No magic effects.
```

---

## 4. Plate: vespers in two camps (optional)

Attach the finished key art from prompt 1. Save as `Art/Source/llm/lore/plate_vespers.png`.

```text
Paint a new picture in exactly the style of the attached painting: the same painterly digital
matte-painting brushwork, the same muted twilight palette of slate blue, moss green and weathered
grey stone, the same soft haze and the same dark vignette toward every edge. Use the attached
painting only for its style. The new picture shows a different scene. Wide 16:9.

The scene: dusk on a northern moorland. In the foreground is an army camp whose lamps and markings
are clear blue. Armoured soldiers kneel bare-headed in rows with their helmets on the ground beside
them, each wearing an ankle-length blue cloth cape; we see them from behind. In front of them a
celebrant in a long white robe under a stiff gold mantle raises an oil lamp toward the sky, and a
thin thread of incense rises beside him. Low heavy tanks are parked at the edge of the camp with
their guns lowered. Across a shallow valley, small and far away, a second camp whose lamps are
orange-red is doing exactly the same thing at the same moment: the same kneeling rows, the same
raised lamp. Between the two camps lies dark ground with a few clusters of softly glowing pale teal
crystals. Above both camps is the first star of the evening: one brilliant blue-white star with a
faint four-point glint.

The two armies have identical equipment; only the colour differs. The lower third of the picture is
darker and quiet, so that text can be laid over it. No text, no letters, no logos, no watermark. No
modern objects. No faces turned to the viewer.
```

---

## 5. Plate: the gantry (optional)

Attach the finished key art from prompt 1. Save as `Art/Source/llm/lore/plate_gantry.png`.

```text
Paint a new picture in exactly the style of the attached painting: the same painterly digital
matte-painting brushwork, the same muted twilight palette of slate blue and weathered steel grey
with small accents of ember orange, the same soft haze and the same dark vignette toward every edge.
Use the attached painting only for its style. The new picture shows a different scene. Wide 16:9.

The scene: dusk at the edge of a military base. A tall steel gantry stands on a landing pad under
floodlights. In the gantry stands a walking war machine nine metres tall, angular and worn, with the
armour plates of its chest swung open. Deep inside the open chest, behind a metal grille, sits an
old dark-metal object about the size of a barrel with a faint, steady light inside it, like a banked
ember; it does not glow brightly. On a service platform level with the chest stands a knight in
plain dark armour with the helmet held under one arm, turned away from us so that the face is not
seen, resting one bare hand on the grille. A cloth standard hangs from a crossbar on the machine's
back. Far overhead are the small running lights of ships in orbit, and one brilliant blue-white
star with a faint four-point glint.

The lower third of the picture is darker and quiet, so that text can be laid over it. No text, no
letters, no logos, no watermark. No screens or holograms. No magic effects.
```

---

## 6. Intro clip, ten seconds (optional)

Two complete versions. Use A unless you want cuts: it starts from the key art and the camera never
moves, so its last frame is the title screen's background and the clip can end straight into the
menu. If your plan's clips are shorter than ten seconds, send the prompt as it is.

### 6A. The key art comes alive (recommended)

Attach the finished key art from prompt 1 as the starting image. Save as `Art/Source/llm/lore/intro.mp4`.

```text
Make a 10-second video from the attached painting, in the same painterly style. Wide 16:9. The
camera is locked off and does not move, zoom or cut at any time: the first frame and the last frame
have exactly the framing of the painting. Only things inside the scene move.

Seconds 0 to 3: dusk, very still. Clouds drift slowly. The single blue-white star above the horizon
brightens and gives a soft four-point glint. The pale teal crystals in the foreground rocks grow
brighter and dimmer slowly, like breathing. Mist moves over the two lakes.

Seconds 3 to 7: between the two small armies in the valley, thin lines of tracer fire begin to
flicker, blue from the left army and orange-red from the right. A few small muzzle flashes. The
smoke from the burning wood on the right leans with the wind.

Seconds 7 to 10: the walking war machine on the ridge at the right turns its head toward the valley
and takes one slow, heavy step; the cloth standard on its back stirs. The star glints once more.
Hold still on the last frame.

Sound: wind over open ground; far away, a low choir chanting without instruments and without words;
distant gunfire starting at second 3; one deep footfall at second 8. A calm, dry, low narrator
speaks once, slowly, between seconds 1 and 7: "One army. One oath. And a fire no one living has seen."
No other speech. No music with drums.

No text, no titles, no subtitles, no logos, no watermark anywhere in the video. No magic effects,
no beams, no explosions in the foreground. Keep the picture as dark and muted as the painting.
```

### 6B. Three cuts, from text alone

Nothing to attach. Save as `Art/Source/llm/lore/intro.mp4`.

```text
Make a 10-second cinematic video in the style of animated painterly matte paintings, like high-end
strategy-game key art brought to life: visible brushwork, a muted twilight palette of slate blue,
moss green and weathered grey with small accents of ember orange and pale teal light, soft haze,
dark vignette at the edges. Not photorealistic, not cartoon. Wide 16:9. Three shots.

Shot 1, seconds 0 to 3: a black night sky. One brilliant blue-white star appears and steadies, with
a faint four-point glint. The camera tilts slowly down to a northern moorland valley at dusk: spruce
woods, grey cliffs, two small lakes, clusters of softly glowing pale teal crystals in the dark
rocks. In the valley two small army camps face each other, one lit with blue lamps and one with
orange-red lamps, with identical low heavy tanks and armoured infantry in ankle-length capes.

Shot 2, seconds 3 to 7: a walking war machine nine metres tall, angular and worn, falls out of the
dusk clouds on retro-rockets and lands beside a floodlit steel gantry in a blast of dust. It
straightens. A cloth standard unfurls from a crossbar on its back. Soldiers in blue capes look up at
it from the height of its knees.

Shot 3, seconds 7 to 10: wide over the whole valley. Thin lines of blue and orange-red tracer fire
flicker between the two camps. A spruce wood is burning on the right, the smoke leaning with the
wind. The war machine stands as a dark silhouette on a ridge at the right. The blue-white star
shines above the horizon. Hold on this view.

Sound: wind; a low choir chanting without instruments and without words; the roar of the rockets and
a heavy landing in shot 2; distant gunfire in shot 3. A calm, dry, low narrator speaks once, slowly,
over shots 1 and 2: "One army. One oath. And a fire no one living has seen." No other speech.

No text, no titles, no subtitles, no logos, no watermark anywhere in the video. No magic effects,
no energy beams, no modern Earth vehicles or buildings. The two armies have identical equipment;
only their colour differs.
```

If the narrator's voice comes out badly, delete the two sentences about the narrator and send the
prompt again; the line can be added later, or left out.

---

## Results, 2026-11-22 (Google Gemini)

All five pictures came back at 2752 x 1536 as JPG and the clip at 1280 x 720, 24 frames a second,
10 seconds, with sound. Kept in `Art/Source/llm/lore/`.

| File | Verdict |
|---|---|
| `keyart_star.jpg` | In the game since 2026-11-22 as `Assets/StarForge/UI/Art/keyart.jpg`. Used as it is: the star with its reflections, the farmhouse and wall, the standard. The dark areas the title sits on are unchanged (mean luminance within 0.007 of the old picture). |
| `plate_two_hands.jpg` | Good as it is. (All four plates are scaled to 1024 x 572 for the game.) |
| `plate_vespers.jpg` | Good as it is. |
| `plate_gantry.jpg` | First version: a hand gripped the grille from inside the chest (touch-up 7). Second version, 23:04: the hand is gone, and the core behind the grille went with it, so the chest is empty. Touch-up 9 below puts the core back. Third version, 23:08: the core is back behind the grille and there is no hand. Good as it is. |
| `plate_quenching.jpg` | Usable. A second small star sits beside the Brightest Star, the hearth's fire is orange, and every lantern is lit. Touch-up 8 below, optional. |
| `plate_quenching_v2.jpg` | 2026-11-23, from touch-up 8. The hearth's fire is white-gold and the smoke is gone, which was the point; the nine guards and the city are unchanged. The small second star is fainter but still there, and several lanterns are still lit: neither shows at the size the Chronicle page draws the plate. This is the version prepared for the game. |
| `order_standards.jpg` | 2026-11-23, from prompt 10. Eight banners in the order asked, on plain green, seven of them right: ring, unlit lantern, nine stars with the last one solid (eight-pointed, not four), plain black, broken crown, horizon line, hammer. The Emberguard's ember came out as a flame with a heart shape in it (the prompt's own word "heart" did that), and the Broken Crown's banner has a darker band down its whole height: touch-up 11 below. The banners are about 5:4, not twice as tall as wide; each standard is rebuilt at the Mech banner's proportions from its device and field, so that does not matter. |
| `order_standards_v2.jpg` | 2026-11-23, from touch-up 11. The Emberguard's ember is a glowing coal now and the Broken Crown's field is one even brown, as asked. The edit also scattered five crosses and a star over the Wardens' horizon. So the game's texture takes seven banners from this sheet and the Wardens' from the first one (`Tools/pack_standards.py`, `KEEP_FIRST`); all eight are right in it. |
| `intro.MP4` | Prepared for the game as a 1280 x 712 copy (the pale rows at the bottom cropped off; nothing else changed). The camera holds still as asked. By the end a second, larger war machine has walked in at the right edge, so the last frame is not the key art. A thin pale line runs along the bottom edge (cropped when the clip is prepared for the game). |

## 7. Touch-up: the hand in the gantry plate

Attach `Art/Source/llm/lore/plate_gantry.jpg`. Save the result over it, as `plate_gantry.jpg`.

```text
Edit the attached painting. Keep everything exactly as it is except for one thing. Inside the open
chest of the war machine, at the right-hand side of the opening, a human hand is gripping the bars of
the grille from the inside. Remove that hand completely and paint the grille bars and the dark
interior where it was. Nobody is inside the machine.

The knight standing on the platform at the left stays exactly as he is. Do not change anything
else: the same image size, the same wide 16:9 shape, the same colours and brushwork. No text, no
letters, no logos, no watermark.
```

## 8. Touch-up: the Quenching plate (optional)

Attach `Art/Source/llm/lore/plate_quenching.jpg`. Save the result beside it, as
`plate_quenching_v2.jpg`: the current plate is usable and stays until the new one has been checked
(the gantry plate lost its core to an edit saved over the original).

```text
Edit the attached painting. Keep everything exactly as it is except for these three changes.

1. Remove the small second star just to the lower right of the large blue-white star, so that the
large star is the only bright star in the sky.

2. Change the small fire in the stone hearth from an orange wood fire to a white-gold light like
sunlight: steady, low, without flames licking upward and without smoke. Remove the wisp of smoke
rising beside the hearth.

3. Small lanterns stand on the ground around the ring of guards. Leave exactly one of them alight,
the one nearest the bottom of the picture. Make all the others dark and unlit, with no glow.

Do not change anything else: the same nine guards, the same city in the valley, the same image
size, the same wide 16:9 shape, the same colours and brushwork. No text, no letters, no logos, no
watermark.
```

## 9. Touch-up: put the core back in the gantry plate

Attach `Art/Source/llm/lore/plate_gantry.jpg` (the version without the hand). Save the result over it.

```text
Edit the attached painting. Keep everything exactly as it is except for one thing. Behind the bars
of the grille in the open chest of the war machine, the interior is empty and dark. Put back what
belongs there: in the middle of the dark interior, behind the bars, an old dark-metal object about
the size of a barrel, rounded like a lidded urn, with a small window in its side through which a
faint, steady, warm orange light shows, like a banked ember. The light is dim: it touches only the
nearest bars and does not glow brightly. No hand, no person and no face inside the machine.

The knight on the platform, the gantry, the standard, the sky and everything else stay exactly as
they are: the same image size, the same wide 16:9 shape, the same colours and brushwork. No text,
no letters, no logos, no watermark.
```

## 10. Sheet: the eight orders' standards (for the Mech's banner)

Attach nothing. Save as `Art/Source/llm/lore/order_standards.jpg`. The banners are cut out of the
sheet by their place in it, so the order matters: top row Ashen Covenant, Iron Lantern, Ninth
Vigil, Sable Oath; bottom row Broken Crown, Emberguard, Wardens of the Long Night, Brazen Host.
The lore names no field colour for the Ninth Vigil and the Broken Crown: dark plum and dark brown
are chosen here to keep clear of the wings' blue and red.

```text
Paint one flat reference sheet showing eight cloth banners, for use as game textures. Wide 16:9.

Layout: the eight banners stand in two rows of four, evenly spaced, none touching, all exactly the
same size and shape. Each banner is a tall plain rectangle, about twice as tall as it is wide, with
straight edges and square corners. Every banner is seen exactly from the front, perfectly flat, as
if pinned to a board: no folds, no ripples, no perspective, no pole, no cords, no tassels, no
fringe. The light is even and shadowless. Behind the banners the whole background is one flat,
solid, bright green with nothing else on it and no shadows.

Style: heavy woven wool with the device sewn on as applique and thick embroidery thread. Flat,
bold, simple heraldry that reads from far away: one device per banner, centred, about half the
banner's width, in one or two flat colours. The cloth is muted and a little worn and faded, never
shiny. Each banner has the same narrow stitched hem all round, in a slightly darker tone of its own
field colour.

Top row, left to right:
First banner: ash-grey field. A plain white ring: a thick open circle, nothing inside it.
Second banner: pale bone-white field. A black hand lantern in silhouette, with a ring handle on
top. It is unlit: no flame and no glow, and its panes show the pale field through them.
Third banner: very dark plum-violet field. Nine small four-pointed stars in bone white, in three
rows of three. Eight of them are thin outlines only, hollow. The ninth, at the bottom right, is
solid.
Fourth banner: plain black field with no device at all, only the woven black cloth and its hem.

Bottom row, left to right:
Fifth banner: dark brown field. A simple crown in tarnished gold, split cleanly down the middle
into two halves that stand a little apart, with a clear gap of brown between them.
Sixth banner: black field. One small glowing ember in orange: a single rounded coal shape, solid
orange with a paler yellow-orange heart. No flames, no sparks, no rays.
Seventh banner: dark slate-grey field. One thin pale horizontal line across the full width of the
banner, a little below the middle, like a far horizon. Nothing else.
Eighth banner: charcoal field. A smith's hammer in brass yellow, upright, its head at the top and
its straight handle below.

No blue and no red anywhere. No text, no letters, no numbers, no labels, no logos, no watermark. No
people, no hands, no objects other than the eight banners.
```

## 11. Touch-up: the ember and the brown banner in the orders' sheet

Attach `Art/Source/llm/lore/order_standards.jpg`. Save the result beside it, as
`order_standards_v2.jpg`.

```text
Edit the attached picture of eight cloth banners. Keep everything exactly as it is except for these
two changes.

1. The banner in the bottom row, second from the left, is black with a small orange flame that has
a heart shape inside it. Replace that flame with a single ember: one small glowing lump of coal, a
rounded, slightly irregular oval lying on its side, about as wide as the flame was. It is solid
ember orange, brighter and more yellow toward its middle and darker orange at its edge. It is not a
flame: no pointed tip, no tongues, no heart shape, no sparks, no rays. It is sewn on as applique
like the devices on the other banners. The black field and its hem stay as they are.

2. The banner in the bottom row at the far left is dark brown with a gold crown in two halves. A
darker vertical band runs down the whole height of this banner through its middle. Remove that
band, so that the field is one even dark brown from edge to edge. The two halves of the crown stay
exactly where they are, with the gap of plain brown between them.

Do not change anything else: the other six banners, the green background, the layout, the image
size and the wide 16:9 shape all stay the same. No text, no letters, no numbers, no logos, no
watermark.
```

---

## What to check before keeping a result

- The orders' sheet: eight banners in two rows of four, in the order given; nine stars with only
  the last one solid; the lantern unlit; the crown in two halves with a gap; the fourth banner
  empty; a plain green background; no letters or numbers.

- One star only, blue-white, brighter than everything else in the sky.
- The two armies look alike; only blue and orange-red differ.
- The Holy Mother's face is never seen; the war-leader wears no crown.
- The old fire is white-gold and steady, a hearth and never a spell. Nobody alive has seen it, so it
  appears only in the Quenching plate.
- The core in the gantry plate is dark metal with a faint steady light. It does not shine, show a
  face or speak.
- No text of any kind in a picture or in the clip.

The ten-minute script written first, with its narration, is kept in `intro-long-script.md`.
