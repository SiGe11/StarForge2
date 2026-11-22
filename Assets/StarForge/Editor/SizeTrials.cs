// SizeTrials.cs -- Editor-only measurements for the size round (no play mode).
//   UiArtFormats      which importer setting block-compresses the menu's NPOT JPGs at their own size
//   SnapshotBefore/After + CompareSnapshots   Unity's own import of the 16 staged textures, before and after they are replaced
// Scratch copies live under Assets/_sizetrial and are always deleted again (a leftover keyart copy would be imported).
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StarForge.EditorTools
{
    public static class SizeTrials
    {
        const string Scratch = "Assets/_sizetrial";
        const string Leaves = "Assets/StarForge/Art/Textures/Leaves/";
        const string Staged = "Art/Source/staged/maint/size_pass2/";

        [Serializable] class Entry { public string path; }
        [Serializable] class Man { public Entry[] files; }

        static void Defaults(TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default; ti.spriteImportMode = SpriteImportMode.None;
            ti.npotScale = TextureImporterNPOTScale.None; ti.mipmapEnabled = true; ti.isReadable = true;
            ti.alphaSource = TextureImporterAlphaSource.None; ti.sRGBTexture = true; ti.filterMode = FilterMode.Trilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.maxTextureSize = 4096;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = false });
        }

        /// <summary>The menu art's formats under several importer settings; the row to keep is compressed AND keeps width x height.</summary>
        public static string UiArtFormats()
        {
            var sb = new StringBuilder();
            Directory.CreateDirectory(Scratch);
            try
            {
                foreach (var f in new[] { "keyart.jpg", "opponent_recruit.jpg" })
                {
                    string dst = Scratch + "/" + f;
                    AssetDatabase.CopyAsset("Assets/StarForge/UI/Art/" + f, dst);
                    var ti = (TextureImporter)AssetImporter.GetAtPath(dst);
                    // Reference: uncompressed, no mips, for the error figures.
                    Defaults(ti); ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false; ti.SaveAndReimport();
                    var rf = AssetDatabase.LoadAssetAtPath<Texture2D>(dst); var refPx = rf.GetPixels32();
                    void Try(string name, Action<TextureImporter> cfg)
                    {
                        Defaults(ti); cfg(ti); ti.SaveAndReimport();
                        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
                        double sum = 0; int n = 0;
                        if (t.width == rf.width && t.height == rf.height)
                        {
                            var px = t.GetPixels32();
                            for (int i = 0; i < px.Length; i += 7) { sum += Math.Abs(px[i].r - refPx[i].r) + Math.Abs(px[i].g - refPx[i].g) + Math.Abs(px[i].b - refPx[i].b); n += 3; }
                        }
                        sb.AppendLine($"{f,-20} {name,-24} {t.width}x{t.height} {t.format,-14} mips {t.mipmapCount,2} {UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t) / 1048576.0,6:0.00} MiB  mean err {(n > 0 ? sum / n : -1):0.00}/255");
                    }
                    Try("now (None, mips, HQ)", c => { });
                    Try("no mips", c => c.mipmapEnabled = false);
                    Try("Sprite, mips", c => { c.textureType = TextureImporterType.Sprite; c.spriteImportMode = SpriteImportMode.Single; });
                    Try("Sprite, no mips", c => { c.textureType = TextureImporterType.Sprite; c.spriteImportMode = SpriteImportMode.Single; c.mipmapEnabled = false; });
                    Try("Standalone BC7", c => c.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = 4096, format = TextureImporterFormat.BC7 }));
                    Try("Standalone DXT5", c => c.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = 4096, format = TextureImporterFormat.DXT5 }));
                }
            }
            finally { AssetDatabase.DeleteAsset(Scratch); AssetDatabase.Refresh(); }
            return sb.ToString();
        }

        /// <summary>Reimports the four menu images (after a rule change) and reads their format, size and memory back.</summary>
        public static string ReimportUiArt()
        {
            var sb = new StringBuilder();
            foreach (var f in new[] { "keyart.jpg", "opponent_recruit.jpg", "opponent_veteran.jpg", "opponent_commander.jpg" })
            {
                string p = "Assets/StarForge/UI/Art/" + f;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                sb.AppendLine($"{f,-26} {t.width}x{t.height} {t.format,-14} mips {t.mipmapCount,2} {UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t) / 1048576.0,6:0.00} MiB");
            }
            return sb.ToString();
        }

        static readonly string[] UiPaths = {
            "Assets/StarForge/UI/Art/keyart.jpg", "Assets/StarForge/UI/Art/opponent_recruit.jpg",
            "Assets/StarForge/UI/Art/opponent_veteran.jpg", "Assets/StarForge/UI/Art/opponent_commander.jpg" };
        static readonly float[] UiScales = { 1f, 0.5f, 0.333f };

        /// <summary>The menu art drawn at 1, 1/2 and 1/3 of its size by the GPU (what a smaller on-screen element samples), read back.</summary>
        public static string UiSnapshot(string tag)
        {
            string dir = SnapDir("ui_" + tag); Directory.CreateDirectory(dir); int n = 0;
            foreach (var p in UiPaths)
                foreach (var s in UiScales)
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                    int w = Mathf.Max(1, Mathf.RoundToInt(t.width * s)), h = Mathf.Max(1, Mathf.RoundToInt(t.height * s));
                    var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var prev = RenderTexture.active; Graphics.Blit(t, rt); RenderTexture.active = rt;
                    var r = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                    r.ReadPixels(new Rect(0, 0, w, h), 0, 0); r.Apply(false);
                    File.WriteAllBytes(dir + Path.GetFileName(p) + "@" + s.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ".raw", r.GetRawTextureData());
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(r); n++;
                }
            return $"ui snapshot '{tag}': {n} renders";
        }
        public static string UiSnapshotBefore() => UiSnapshot("before");
        public static string UiSnapshotAfter() => UiSnapshot("after");

        /// <summary>Per image and scale: mean abs RGB difference and the share of texels off by more than 8 levels, against the thresholds fixed before the change.</summary>
        public static string UiCompare()
        {
            var sb = new StringBuilder(); bool all = true;
            sb.AppendLine("render                          mean RGB  max  >8 levels   threshold (mean / >8)   verdict");
            foreach (var f in Directory.GetFiles(SnapDir("ui_before"), "*.raw"))
            {
                string name = Path.GetFileName(f); string g = SnapDir("ui_after") + name;
                if (!File.Exists(g)) { sb.AppendLine(name + ": no 'after'"); all = false; continue; }
                var A = File.ReadAllBytes(f); var B = File.ReadAllBytes(g); double sum = 0; int mx = 0, off = 0, n = A.Length / 4;
                for (int i = 0; i < A.Length; i += 4)
                    for (int c = 0; c < 3; c++) { int d = Math.Abs(A[i + c] - B[i + c]); sum += d; if (d > mx) mx = d; if (d > 8) off++; }
                string sc = name.Substring(name.IndexOf('@') + 1).Replace(".raw", "");
                double tm = sc == "1" ? 1.5 : sc == "0.5" ? 2.0 : 3.0, tf = sc == "1" ? 1.0 : sc == "0.5" ? 2.0 : 4.0;
                double mean = sum / (3.0 * n), frac = 100.0 * off / (3.0 * n); bool ok = mean <= tm && frac <= tf; all &= ok;
                sb.AppendLine($"{name,-30} {mean,8:0.00} {mx,4} {frac,8:0.00}%   {tm:0.0} / {tf:0.0}%   {(ok ? "PASS" : "FAIL")}");
            }
            sb.AppendLine(all ? "ALL PASS" : "AT LEAST ONE FAILED");
            return sb.ToString();
        }

        static string[] StagedPaths()
        {
            var m = JsonUtility.FromJson<Man>(File.ReadAllText(Staged + "manifest.json"));
            var r = new string[m.files.Length];
            for (int i = 0; i < r.Length; i++) r[i] = m.files[i].path;
            return r;
        }

        static string SnapDir(string tag) => "Temp/size_" + tag + "/";

        /// <summary>Writes mip 0 of each staged texture as Unity imported it (read back through a render texture: the real ones are not readable).</summary>
        public static string Snapshot(string tag)
        {
            Directory.CreateDirectory(SnapDir(tag));
            int n = 0;
            foreach (var p in StagedPaths())
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t == null) return "missing " + p;
                bool lin = p.EndsWith("_nrm.png");
                var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, lin ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(t, rt); RenderTexture.active = rt;
                var r = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, lin);
                r.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); r.Apply(false);
                File.WriteAllBytes(SnapDir(tag) + Path.GetFileName(p) + "." + t.width + "x" + t.height + ".raw", r.GetRawTextureData());
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(r);
                n++;
            }
            return $"snapshot '{tag}': {n} textures";
        }
        public static string SnapshotBefore() => Snapshot("before");
        public static string SnapshotAfter() => Snapshot("after");

        /// <summary>Per texture: mean/max abs difference of mip 0 (8-bit levels) and, for colour sheets, how many texels switch side of the 0.45 alpha cut-off.</summary>
        public static string CompareSnapshots()
        {
            var sb = new StringBuilder(); sb.AppendLine("texture                      size        mean RGB  max RGB  mean A  max A  cut-off flips");
            foreach (var p in StagedPaths())
            {
                string name = Path.GetFileName(p);
                string[] a = Directory.GetFiles(SnapDir("before"), name + ".*.raw"), b = Directory.GetFiles(SnapDir("after"), name + ".*.raw");
                if (a.Length != 1 || b.Length != 1) { sb.AppendLine(name + ": snapshot missing"); continue; }
                var A = File.ReadAllBytes(a[0]); var B = File.ReadAllBytes(b[0]);
                if (A.Length != B.Length) { sb.AppendLine($"{name,-28} SIZE CHANGED {Path.GetFileName(a[0])} -> {Path.GetFileName(b[0])}"); continue; }
                double sRgb = 0, sA = 0; int mRgb = 0, mA = 0, flips = 0, cut = (int)(0.45f * 255f);
                for (int i = 0; i < A.Length; i += 4)
                {
                    for (int c = 0; c < 3; c++) { int d = Math.Abs(A[i + c] - B[i + c]); sRgb += d; if (d > mRgb) mRgb = d; }
                    int da = Math.Abs(A[i + 3] - B[i + 3]); sA += da; if (da > mA) mA = da;
                    if ((A[i + 3] > cut) != (B[i + 3] > cut)) flips++;
                }
                int px = A.Length / 4;
                sb.AppendLine($"{name,-28} {Path.GetFileName(a[0]).Split('.')[2]}  {sRgb / (3.0 * px),8:0.00}  {mRgb,7}  {sA / px,6:0.00}  {mA,5}  {100.0 * flips / px,6:0.00}%");
            }
            return sb.ToString();
        }
    }
}
