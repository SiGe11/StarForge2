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
            if (!assetPath.StartsWith(TextureDir)) return;
            var ti = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);

            ti.maxTextureSize = 1024;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.anisoLevel = 4;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;

            if (file.EndsWith("_n"))
            {
                // Grayscale detail photographs make valid height maps, so the
                // normal map is derived on import (the original did the same
                // Sobel pass on the CPU). Water swell has tiny per-texel
                // gradients and needs far more gain than gravel or rock.
                ti.textureType = TextureImporterType.NormalMap;
                ti.convertToNormalmap = true;
                ti.normalmapFilter = TextureImporterNormalFilter.Sobel;
                ti.heightmapScale = file.StartsWith("water") ? 0.25f : 0.08f;
            }
            else if (file == "smoke_puffs")
            {
                // Data, not colour: normal xy, occlusion, coverage (Tools/make_smoke_puffs.py).
                ti.sRGBTexture = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
            }
            else if (file == "explosion" || file == "smoke" || file == "particles" || file == "scorch")
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
        }

        void OnPreprocessModel()
        {
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
            mi.isReadable = false;
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
