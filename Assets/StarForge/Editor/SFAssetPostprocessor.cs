// SFAssetPostprocessor.cs — import settings for StarForge art, so dropping a
// re-exported FBX or a new texture into the folder needs no manual setup.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StarForge.EditorTools
{
    public class SFAssetPostprocessor : AssetPostprocessor
    {
        public const string TextureDir = "Assets/StarForge/Art/Textures/";
        public const string ModelDir = "Assets/StarForge/Art/Models/";

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(FaunaDir) && Path.GetFileNameWithoutExtension(assetPath).EndsWith("_palette"))
            {
                // An animal's colours, one texel per part (Tools/blender/build_songbird.py):
                // every face's UVs sit on a texel centre, so no filtering, no mipmaps.
                var pi = (TextureImporter)assetImporter;
                pi.sRGBTexture = true;
                pi.mipmapEnabled = false;
                pi.filterMode = FilterMode.Point;
                pi.wrapMode = TextureWrapMode.Clamp;
                pi.textureCompression = TextureImporterCompression.Uncompressed;
                pi.npotScale = TextureImporterNPOTScale.None;
                return;
            }
            if (!assetPath.StartsWith(TextureDir)) return;
            var ti = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);

            ti.maxTextureSize = 1024;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.anisoLevel = 4;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;

            if (assetPath.Contains("/Leaves/") && file.EndsWith("_col"))
            {
                // Leaf-spray cards (Tools/blender/make_leaf_cards.py) and the scanned
                // trees' baked clusters (bake_scanned_flora.py): alpha is coverage, and
                // the mipmaps keep it, or distant crowns thin out to nothing.
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.45f;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = 1024;
            }
            else if (file.EndsWith("_nrm"))
            {
                // Scanned normal maps (Tools/fetch_assets.py): already OpenGL
                // tangent space, imported as they are. The leaf-card atlases' tiles
                // must not wrap into each other.
                ti.textureType = TextureImporterType.NormalMap;
                ti.convertToNormalmap = false;
                if (assetPath.Contains("/Leaves/")) ti.wrapMode = TextureWrapMode.Clamp;
            }
            else if (file.EndsWith("_ch"))
            {
                // Colour in RGB, height in A (Tools/blender/pack_textures.py): the
                // alpha is data, never transparency.
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
            }
            else if (file.EndsWith("_n"))
            {
                // A grayscale height map (the hull plating's, Tools/make_panel_texture.py,
                // or the original's water photograph) turned into a normal map on import,
                // as the original's Sobel pass did on the CPU. Water swell has tiny
                // per-texel gradients and needs far more gain.
                ti.textureType = TextureImporterType.NormalMap;
                ti.convertToNormalmap = true;
                ti.normalmapFilter = TextureImporterNormalFilter.Sobel;
                ti.heightmapScale = file.StartsWith("water") ? 0.25f : 0.08f;
            }
            else if (file.StartsWith("foliage_"))
            {
                // Data (Tools/make_leaf_textures.py): leaf normal xy, occlusion, shade.
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.maxTextureSize = 512;
            }
            else if (file == "smoke_puffs")
            {
                // Data, not colour: normal xy, occlusion, coverage (Tools/make_smoke_puffs.py).
                ti.sRGBTexture = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
            }
            else if (file.StartsWith("fx_"))
            {
                // Simulated flipbooks (Tools/blender/render_flipbooks.py): 8x8 cells (256, or
                // 384 for the explosion's 3072 sheet), four data channels premultiplied by
                // coverage -- sun-lit smoke, fire, sky-lit smoke, coverage (SF_Flipbook).
                ti.sRGBTexture = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.maxTextureSize = 4096;
                // Scaled up to the next power of two: kept at 3072 the sheet is never
                // block-compressed (RGBA32, 50 MB with mips, even with BC7 asked for by name);
                // at 4096 it is BC7, 22 MB.
                ti.npotScale = TextureImporterNPOTScale.ToLarger;
            }
            else if (file == "explosion" || file == "flames" || file == "particles" || file == "scorch")
            {
                // Bright-on-black sheets: coverage comes from luminance.
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                ti.alphaIsTransparency = false;
            }
            else if (file.StartsWith("sf_ui_") || file.StartsWith("sf_decal_"))
            {
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.maxTextureSize = 512;
            }
            else if (file == "cape_fabric")
            {
                // The capes' weave and embroidered band (InfantryCape): the band runs on along
                // each trim past the texture's width, mirrored so it never shows a seam.
                ti.wrapModeU = TextureWrapMode.Mirror;
                ti.wrapModeV = TextureWrapMode.Repeat;
            }
        }

        public const string AudioDir = "Assets/StarForge/Audio/";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioDir)) return;
            var ai = (AudioImporter)assetImporter;
            var set = ai.defaultSampleSettings;
            set.compressionFormat = AudioCompressionFormat.Vorbis;
            if (assetPath.Contains("/Music/"))
            {
                // Three 48 s stems: streamed, not held decoded in memory.
                set.loadType = AudioClipLoadType.Streaming;
                set.quality = 0.55f;
            }
            else if (assetPath.Contains("/Ambience/") && !Path.GetFileName(assetPath).StartsWith("bird"))
            {
                set.loadType = AudioClipLoadType.CompressedInMemory;
                set.quality = 0.5f;
            }
            else
            {
                // Short effects, played many times a second in a fight.
                set.loadType = AudioClipLoadType.DecompressOnLoad;
                set.quality = 0.7f;
            }
            ai.defaultSampleSettings = set;
            ai.forceToMono = false;
            ai.loadInBackground = assetPath.Contains("/Music/");
        }

        public const string FaunaDir = "Assets/StarForge/Art/Fauna/";

        void OnPreprocessModel()
        {
            if (assetPath.StartsWith(FaunaDir))
            {
                // Quaternius's animals (Tools/pack_fauna.py): rigged, with their cycles
                // as legacy clips View/Fauna.cs plays directly. Their materials are
                // plain grey, so none are imported; SceneAssembler colours them.
                var fm = (ModelImporter)assetImporter;
                fm.globalScale = 1f;
                fm.useFileScale = true;
                fm.bakeAxisConversion = true;
                fm.importCameras = false;
                fm.importLights = false;
                fm.importBlendShapes = false;
                fm.animationType = ModelImporterAnimationType.Legacy;
                fm.importAnimation = true;
                fm.animationWrapMode = WrapMode.Loop;
                // The Biolab's creatures (Tools/blender/build_creatures.py) keep their materials'
                // names, which PrefabBuilder colours them by: the submesh order is not the file's
                // slot order (the wasp's wings took its stripes' colour by index).
                fm.materialImportMode = Path.GetFileName(assetPath).StartsWith("Creature_")
                    ? ModelImporterMaterialImportMode.ImportStandard : ModelImporterMaterialImportMode.None;
                fm.isReadable = false;
                return;
            }
            if (!assetPath.StartsWith(ModelDir)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            // The ore stays readable: a runtime NavMesh rebuild now and then took an ore node's
            // meshes in spite of its ignore modifier, which the Editor reads and a player cannot
            // ("does not allow read access"), so the two built different tiles (BugTrials.OreSourceRead).
            mi.isReadable = assetPath.EndsWith("/SF_ORE.fbx");
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.optimizeMeshPolygons = true;
            mi.optimizeMeshVertices = true;
            // Keep the Blender slot names on embedded materials; the prefab
            // builder remaps each slot to a URP material by that name.
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }
    }
}
