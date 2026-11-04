# Blender → Unity contract — area notes

Regenerate models (Blender 5.x, headless), then re-run Build step 2:

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python Tools/blender/export_fbx.py -- Assets/StarForge/Art/Models Tools/blender/starforge_models.blend
```

`sf_model.py` and `build_models.py` come from the original; `build_models_ext.py` (Skimmer, Sentinel), `build_env.py` (scenery) and `build_flora.py` (trees and bushes, bark slot before foliage, crown shape into `models.json`) are new. `build_mechs.py` has its own entry point and exports only `SF_MECH_*` and `mechs.json`, so the other FBXs are not rewritten. **The export rewrites every FBX byte-for-byte differently: restore the ones you did not mean to change** (`git checkout` them).

**The exporter:**
- names material slots after the palette (`armor`, `team`, `glow_cyan`, …), which `SFMaterialLibrary` maps to URP materials;
- bakes AO into vertex colour R, writes G/B from `sf_model.set_vdata` where a part sets them;
- exports `Model.group` parts as child objects that `UnitView` animates **by name** (`LegL`, `LegR`, `Gun`, `Arm`, `Cutter`, `Drum`, `Barrel(s)`, `Head`, `Trolley`, the Digger's `Load`, the ore seam's `Crystals`): renaming a group breaks its animation;
- writes `SF_<NAME>_CHUNKS.fbx` for structures → `Debris_*` prefabs for `DebrisBurst`;
- gives flora `SF_<NAME>_LOD1.fbx` (drawn beyond `VegetationRenderer.lodDistance`); leaf cards and canopy clusters carry custom normals pointing out of the crown (`sf_model.set_normal_hint`, applied in `export_fbx.apply_normal_hints`).

**One original model alone:** `build_roster.py -- Assets/StarForge/Art/Models WORKER` (`ORIGINALS_BY_NAME`) exports the Digger and merges its `models.json` entry, leaving every other FBX as it was.

**Never leave two faces flush**: a part laid on another's face, or ending in its plane, z-fights and shimmers as the camera moves (the Armory's door light sat in the door's face; its ridge stripe ended in the gables' planes; the Digger's idler lay within millimetres of its track band): stand it a centimetre or two proud, or stop it short.

**Nested groups do not survive FBX export.** With `bake_space_transform` the writer mangles a grandchild's transform (the Digger's `Arm/Cutter` came in turned 270° and metres out of place). `export_fbx.export_groups` writes every group as a direct child of the root (`Arm/Cutter` → `Cutter` beside `Arm`), and the game chains them again, each keeping where it stands (`UnitView.Bind` for the cutter, `MechView.RigLegs` for thigh > shin > foot).

## Rigged soldier (`build_soldier_rig.py -- preview <png> [front|side|back]` or `-- export Assets/StarForge/Art/Models`)
Writes only `SF_TROOPER_RIG.fbx`; then `Tools/editor.sh call StarForge.EditorTools.SoldierRigBuilder.Build` makes the Animator and prefabs in `Resources/` (`_RIG.fbx` gets its import rule from `SFAssetPostprocessor`: Generic, optimised hierarchy, one bone a vertex, clips renamed from `armature|action`).
- **Parts are bound whole to one bone**, by where they are modelled (`part_bone`): plates never stretch.
- **One material, so one draw a pass** (`merge_materials`, on export only: the preview sheets keep their slots): every face gets the palette index of its old slot in colour G, per corner since a part's faces can share a vertex and still differ in slot, and the mesh takes the one slot `palette`. The index is `sf_model.MAT_INDEX`, and **`SFMaterialLibrary.PaletteOrder` must list `MAT_NAMES` in the same order**; nothing checks that, so a new palette entry goes at the end of both. Rock, crystal, ore glass and the flora slots have paths of their own and are refused (`NO_PALETTE`). `SFMaterialLibrary.BuildPalette` turns each slot's numbers into a texel of `SF_palette_T<team>.asset`.
- **KayKit's bones keep their direction and roll**, only their heads move, so a clip's local rotations copy over as they are; the hips' travel scales with leg length (`LEG_RATIO`).
- **Take only the legs, hips and trunk from KayKit.** Its mannequin's rifle clips hold its arms out round a huge head: on these shoulders the rifle hung off to the side. A `hold` says where the rifle sits against the chest and points, and the arms are solved to it each frame (a free hand goes to the source hand's place against the chest, `HAND_SCALE`). The rifle is its own bone under the chest, keyed in every clip, so it can change hands.
- **A hold's `follow` must be low when the trunk bends** (`LOW_READY`, 0.3): a crouch's lean at full follow dug the muzzle into the ground.
- **Written with the FBX writer's axes and no space baking the model imports facing -Z** (the same flip `export_fbx.py` records): `SoldierRigBuilder` turns it 180 degrees under the prefab's root. The mesh's own axes stay Z up, so its culling box is a cube.
- **Keep a hand on the rifle under 0.95 of the arm's length, and ease the reach, never cut it** (`SOFT_REACH`): held at the limit the left elbow jumped 0.10 m and 39 degrees on every shot, and in the crawl 0.17 m in a frame when the hand passed the limit. The rifle in a hand's care goes where the hand can really reach (`within_reach`). `-- check` prints each clip's hand misses (target under 2 cm), reach, largest elbow step and loop seam: 2026-11-03 misses 0.5-1.7 cm, reach 0.94 at most on the rifle (Reload's belt trip 0.97, Crawl's free hand 0.99), every loop's seam 0.000, Fire's elbow step 0.018 m. Throw's and Stab's 0.6-0.7 m a frame are the source's fast swing: the hand moves as far. It reads the poses before export; Unity's compression is unmeasured.
- Read the sheet from Blender first (a render takes ~15 s), then `-sfrigposes` on the player for the game's own lighting (`../../Assets/StarForge/Scripts/Game/CLAUDE.md`).

## Scanned plants (`bake_scanned_flora.py -- <KIND>`)
Cycles on the CPU, 1-6 min a kind; fetch first (`fetch_assets.py island_tree_01 ...`). Poly Haven scans baked to: the trunk decimated with its own UVs and bark photograph (`Leaves/scan_<KIND>_bark.jpg`, SF_Tree `_BarkStyle` 2, `PlantKind.barkTex`); leaves and twigs k-means-clustered, each cluster rendered orthographically from out of the crown and up into a tile of `scan_<KIND>_col.png`/`_nrm.png` (normals in the card's frame). Layout → `Tools/blender/scanned/<KIND>.json` (committed, game space); `scanned_flora.py` builds the model from it inside `build_flora`'s kind builders; the procedural builders remain the fallback for a kind with no bake.
- **A card shows *all* the foliage within a sphere round its cluster** (`SPHERE`), with a ragged rim (each leaf island its own reach), so neighbouring cards overlap like sprays. Showing only its own cluster, a card covered 15-19% of its tile and the crowns read as burnt. Leaf coverage is grown a texel (`GROW`) against the 0.45 cut-off.
- Cards bring their own occlusion by crown depth (`sf_ao`), which `export_fbx.bake_ao` keeps (ray-cast against a hundred overlapping quads it blackened the crowns).
- Wind and blast pressure need nothing new: the vertex-colour contract is the procedural one (B sway weight, A 1 on cards, G random per card). Foliage is two-sided (`_Cull` 0); far copies (`LOD_BUILDERS`) keep every card and thin the trunk.
- `AgentPlay.TreeGallery` photographs the most isolated plant of each kind at closest zoom, and in a held pressure front (`Temp/tree_<KIND>[_pushed].png`, 3x).
