# Editor generators — area notes

**Generated content.** Materials, prefabs, icons, the map scene and the render pipeline asset are produced by the generators here: change the generator, then re-run that Build step **and every later one** (prefabs need materials, the map needs prefabs, assembly needs the map scene). Step 2 preserves tuned values in `Data/Units/*.asset` (including hotkeys: edit the asset too when changing one). Step 3 regenerates `Scenes/Battlefield.unity` from scratch, discarding hand edits to the map. Scene components (ground cover, atmosphere, quality presets, warmup, HUD) are wired by `SceneAssembler`, never by hand.

**URP 17.6 quirks handled in `RenderSetup.cs`:** `upscalerName` is compiled out (set the obsolete `upscalingFilter` enum; FSR = 3), and SSAO is configured through the renderer feature's `m_Settings` via `SerializedObject` because its volume override is compiled out (behind MODERN_SSAO).

**`TerrainData.SetAlphamaps` does not survive the asset being saved**: splat weights were silently lost and the map rendered as pure moss for a long time. `MapBuilder.PaintSplat` writes the splat texture's pixels directly as the last build step and logs the layer shares — check that line.

Step 5 (shader variants) uses reflection into Unity's internal recorder (`ShaderUtil`), because RunCommand rejects `System.Reflection`: keep reflection-heavy code in project Editor scripts.

`MapChecks.Report` (menu "Check Map Blocking"), `MechTrials`, `BugTrials`, `AIEvalMenu`, `AgentInbox` (file inbox behind `Tools/editor.sh`) live here; what each trial measures is in the area file of the code it tests (`../Scripts/World/CLAUDE.md`, `../Scripts/AI/CLAUDE.md`, `../Scripts/View/CLAUDE.md`).
