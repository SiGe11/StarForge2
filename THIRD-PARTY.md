# Third-party assets

This repository is public (`github.com/SiGe11/StarForge2`), so everything
committed to it has to be free to **redistribute**, not merely free to use. That
rules out the Unity Asset Store EULA, CC-BY-NC and "free for personal use",
however generous they sound; in practice the art and audio here are all CC0,
and the two font families carry the only attribution obligations we have.

The project's own code and art are MIT ([LICENSE](LICENSE), © Simon Gergely).
That covers what we wrote; everything below keeps the terms of whoever made it,
which is what this register is for. Every entry was checked against its source
page — `python3 Tools/check_licences.py` re-checks them against the live sources,
and fails if a committed asset is not on this list.

| Licence | What it obliges us to do | Text |
|---|---|---|
| CC0 1.0 | Nothing. Credit is given here anyway, because the people who made these deserve it. | [LICENSES/CC0-1.0.txt](LICENSES/CC0-1.0.txt) |
| SIL OFL 1.1 | Ship the licence text with the font; don't sell the font on its own; don't release a modified copy under the original name. We do none of those. | [LICENSES/OFL-1.1-Inter.txt](LICENSES/OFL-1.1-Inter.txt) |
| Apache 2.0 | Ship the licence text and keep the copyright notice. | [LICENSES/Apache-2.0.txt](LICENSES/Apache-2.0.txt) |

`python3 Tools/fetch_assets.py` downloads the sources into `Art/Source/`, which
git ignores; the packers listed in each table turn them into the files committed
under `Assets/`. The fonts are the exception — they are committed as they were
downloaded.

## Poly Haven — CC0 1.0 (<https://polyhaven.com/license>)

Scanned ground and rock, packed by `Tools/blender/pack_textures.py` and
`Tools/blender/build_rocks.py`. `Art/Source/polyhaven/manifest.json` records each
asset's authors, licence and real-world size; `Art/Models/rocks.json` records
which scan each rock came from.

| Asset | Author(s) | Becomes |
|---|---|---|
| [Coast Sand Rocks 02](https://polyhaven.com/a/coast_sand_rocks_02) | Rob Tuytel | `Art/Textures/Terrain/meadow_ch.png`, `meadow_nrm.jpg` (the layer grass grows on) |
| [Forest Ground 04](https://polyhaven.com/a/forest_ground_04) | Rico Cilliers, Rob Tuytel | `Art/Textures/Terrain/dirt_ch.png`, `dirt_nrm.jpg` |
| [Aerial Rocks 02](https://polyhaven.com/a/aerial_rocks_02) | Rob Tuytel | `Art/Textures/Terrain/cliff_ch.png`, `cliff_nrm.jpg` |
| [Coast Sand 01](https://polyhaven.com/a/coast_sand_01) | Rob Tuytel | `Art/Textures/Terrain/sand_ch.png`, `sand_nrm.jpg` |
| [Rock Moss Set 01](https://polyhaven.com/a/rock_moss_set_01) | Kless Gyzen | `Art/Models/SF_SCAN_BOULDER_E/F.fbx`, `SF_SCAN_CRAG_A.fbx`, `SF_SCAN_SHELF_A.fbx` + `Art/Textures/Rocks/rock_moss_set_01_*.jpg` |
| [Rock Moss Set 02](https://polyhaven.com/a/rock_moss_set_02) | Kless Gyzen | `Art/Models/SF_SCAN_BOULDER_A–D.fbx` + `Art/Textures/Rocks/rock_moss_set_02_*.jpg` |
| [Boulder 01](https://polyhaven.com/a/boulder_01) | Rico Cilliers | `Art/Models/SF_SCAN_CRAG_B.fbx` + `Art/Textures/Rocks/boulder_01_*.jpg` |
| [Rocky Trail](https://polyhaven.com/a/rocky_trail), [Cliff Side](https://polyhaven.com/a/cliff_side) | Amal Kumar; Dario Barresi, James Ray Cock, Jenelle van Heerden | fetched as spares, nothing committed |

## ambientCG — CC0 1.0 (<https://ambientcg.com/license>)

Leaf atlases, cut into the crowns' leaf sprays by `Tools/blender/make_leaf_cards.py`.

| Asset | Becomes |
|---|---|
| [Leaf Set 016](https://ambientcg.com/view?id=LeafSet016) | `Art/Textures/Leaves/oak_col.png`, `oak_nrm.png` |
| [Leaf Set 014](https://ambientcg.com/view?id=LeafSet014) | `Art/Textures/Leaves/birch_col.png`, `birch_nrm.png` |
| [Leaf Set 024](https://ambientcg.com/view?id=LeafSet024) | `Art/Textures/Leaves/beech_col.png`, `beech_nrm.png` |
| [Leaf Set 019](https://ambientcg.com/view?id=LeafSet019) | `Art/Textures/Leaves/conifer_col.png`, `conifer_nrm.png` |

## Kenney — CC0 1.0 (<https://kenney.nl>)

Sound packs; `Tools/make_audio.py` copies the effects it uses under game names
and mixes two of them into the energy weapons.

| Pack | Becomes |
|---|---|
| [Sci-Fi Sounds](https://kenney.nl/assets/sci-fi-sounds) | explosions, mining, shields, the laser layer under `bolt_*.wav` / `pulse_*.wav`, and the sources `make_audio.py` builds `blast_*.wav`, `blastbig_*.wav` and `engine_heavy.wav` from |
| [Impact Sounds](https://kenney.nl/assets/impact-sounds) | metal hits, rock, thuds, the wood thump in `treefall_*.wav` |
| [Interface Sounds](https://kenney.nl/assets/interface-sounds) | `Audio/Sfx/ui_*.ogg` |

## Quaternius — CC0 1.0 (<https://quaternius.com>)

Animated low-poly animals, via OpenGameArt. `Tools/pack_fauna.py` copies the
FBXs; `Tools/blender/build_songbird.py` re-poses and re-animates the perched
bird into a flying one and paints it with a palette texture.

| Pack | Becomes |
|---|---|
| [Animals Pack](https://opengameart.org/content/5-low-poly-animals) | `Art/Fauna/Fox.fbx`, `Songbird.fbx`, `Songbird_palette.png` |
| [Animal Pack Vol. 2](https://opengameart.org/content/animated-animales-low-poly) | `Art/Fauna/Wolf.fbx`, `Eagle.fbx` |

## OpenGameArt — CC0 1.0: music

Copied under the mood they play in by `Tools/make_audio.py`, which also measures
each one's loudness into `Audio/Music/music.json`. WAV sources are Vorbis-encoded
(`Tools/blender/encode_audio.py`) rather than committed raw.

| Track | Author | Becomes |
|---|---|---|
| [At Home (orchestral)](https://opengameart.org/content/at-home-orchestral) | wolfgang | `music_calm_0.ogg` |
| [First Light Particles](https://opengameart.org/content/first-light-particles-–-cc0-atmospheric-pianoambient-track) | yoiyami | `music_calm_1.ogg` |
| [Contemplation](https://opengameart.org/content/contemplation-0) | Joth | `music_calm_2.mp3` |
| [Insistent: background loop](https://opengameart.org/content/insistent-background-loop) | yd | `music_tension_0.ogg` |
| [Battle Theme A](https://opengameart.org/content/battle-theme-a) | cynicmusic | `music_combat_0.mp3` |

## OpenGameArt — CC0 1.0: recorded sound

What the guns and the felled trees are built from, in `Tools/make_audio.py`.

| Source | Author(s) | Becomes |
|---|---|---|
| [The Free Firearm Sound Library](https://opengameart.org/content/the-free-firearm-sound-library) | Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney (uploaded by bart) | `rifle_0–3.wav`, `cannon_0–2.wav`, and the muzzle crack under `bolt_*` / `pulse_*` — every gun recorded from beside the shooter and again at a distance, and a shot mixes both |
| [tree chop fall thud](https://opengameart.org/content/tree-chop-fall-thud) | kheetor | the fall and the crash in `treefall_*.wav` / `treecrash_*.wav` |
| [Tree Creaking](https://opengameart.org/content/tree-creaking) | AntumDeluge, from a sample by Department64 | the creak in `treefall_*.wav` |
| [100 CC0 metal and wood SFX](https://opengameart.org/content/100-cc0-metal-and-wood-sfx) | rubberduck | wood breaks in `treecrash_*.wav`, `crush_*.wav`; the metal hits `engine_tracks.wav` is built from |
| [75 CC0 breaking / falling / hit sfx](https://opengameart.org/content/75-cc0-breaking-falling-hit-sfx) | rubberduck | the same |

## Fonts

The only assets here that are not CC0, and the only ones with conditions. Both
licences allow redistribution in a public repository; both require the licence
text to travel with the font, which is what `LICENSES/` is for. Neither font is
modified, renamed or sold.

| Font | Author | Licence | Files |
|---|---|---|---|
| [Inter](https://rsms.me/inter/) 3.009/3.010 | The Inter Project Authors (Rasmus Andersson) | SIL Open Font License 1.1 | `UI/Fonts/Inter-Regular.ttf`, `Inter-SemiBold.ttf` |
| [Roboto Mono](https://fonts.google.com/specimen/Roboto+Mono) 3.000 | The Roboto Mono Project Authors (Christian Robertson, Google) | Apache License 2.0, as stated in the file we ship — upstream has since moved to OFL 1.1 | `UI/Fonts/RobotoMono-Bold.ttf` |

## Unity

Unity, the Universal Render Pipeline and the other packages in
`Packages/manifest.json` are referenced by version and fetched by the package
manager; none of their files are committed here. They are covered by the Unity
Companion License and the Unity terms the editor is used under.

Unity's URP project template also left a "URP Empty Template" readme behind —
`Assets/TutorialInfo/` (a Readme script and editor, a layout, stylesheets and
`URP.png`) and the `Assets/Readme.asset` that used them. Redistributable under the
Unity Companion License, but not ours and referenced by nothing, so they were
deleted. `Tools/check_licences.py` warns if a template upgrade puts them back.

## Everything else is made for this project

Not third party, and not on the list above:

- **Models** — every `SF_*.fbx` except the `SF_SCAN_*` rocks: built by
  `Tools/blender/build_models.py`, `build_models_ext.py`, `build_env.py` and
  `build_flora.py`.
- **Shaders, scripts, UI** — written for this project.
- **Generated textures** — `armor.png`/`armor_n.png`, `flames.png`,
  `smoke_puffs.png`, `foliage_*.png`, the icons, the cloud cookie and the lens
  dirt: made by the `Tools/make_*.py` generators.
- **Synthesised audio** — `Audio/Ambience/*.wav` (wind, water, fire, birds),
  written by `Tools/make_audio.py` out of filtered noise.
- **Textures carried over from the original StarForge** — `clouds.jpg`,
  `crystal.jpg`, `explosion.jpg`, `particles.jpg`, `scorch.jpg`, `smoke.jpg`,
  `terrain-macro.jpg`, `water-height.jpg`, `water_n.jpg`, `trak.png`,
  `windows.png`, `ground*.png`, `cliff*.png`, `Assets/icon.png`,
  `Assets/cursor.png`. These come from the author's own C++ StarForge
  (`~/repositories/StarForge`, MIT, © Simon Gergely), where they were authored
  for the project with an image generator rather than taken from a stock library.

## Checking

```bash
python3 Tools/check_licences.py          # re-checks every source page
python3 Tools/check_licences.py --offline # structure only, no network
```

It verifies that each source still states the licence claimed here, that every
file this register points at exists, that everything `Tools/fetch_assets.py`
downloads is registered, and — the check that matters most — that no image,
model, font or sound under `Assets/` is missing from this document.
