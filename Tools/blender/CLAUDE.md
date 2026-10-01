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

**Nested groups do not survive FBX export.** With `bake_space_transform` the writer mangles a grandchild's transform (the Digger's `Arm/Cutter` came in turned 270° and metres out of place). `export_fbx.export_groups` writes every group as a direct child of the root (`Arm/Cutter` → `Cutter` beside `Arm`), and the game chains them again, each keeping where it stands (`UnitView.Bind` for the cutter, `MechView.RigLegs` for thigh > shin > foot).

## Scanned plants (`bake_scanned_flora.py -- <KIND>`)
Cycles on the CPU, 1-6 min a kind; fetch first (`fetch_assets.py island_tree_01 ...`). Poly Haven scans baked to: the trunk decimated with its own UVs and bark photograph (`Leaves/scan_<KIND>_bark.jpg`, SF_Tree `_BarkStyle` 2, `PlantKind.barkTex`); leaves and twigs k-means-clustered, each cluster rendered orthographically from out of the crown and up into a tile of `scan_<KIND>_col.png`/`_nrm.png` (normals in the card's frame). Layout → `Tools/blender/scanned/<KIND>.json` (committed, game space); `scanned_flora.py` builds the model from it inside `build_flora`'s kind builders; the procedural builders remain the fallback for a kind with no bake.
- **A card shows *all* the foliage within a sphere round its cluster** (`SPHERE`), with a ragged rim (each leaf island its own reach), so neighbouring cards overlap like sprays. Showing only its own cluster, a card covered 15-19% of its tile and the crowns read as burnt. Leaf coverage is grown a texel (`GROW`) against the 0.45 cut-off.
- Cards bring their own occlusion by crown depth (`sf_ao`), which `export_fbx.bake_ao` keeps (ray-cast against a hundred overlapping quads it blackened the crowns).
- Wind and blast pressure need nothing new: the vertex-colour contract is the procedural one (B sway weight, A 1 on cards, G random per card). Foliage is two-sided (`_Cull` 0); far copies (`LOD_BUILDERS`) keep every card and thin the trunk.
- `AgentPlay.TreeGallery` photographs the most isolated plant of each kind at closest zoom, and in a held pressure front (`Temp/tree_<KIND>[_pushed].png`, 3x).
