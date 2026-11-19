// TreeTrials.cs — play-mode looks at the trees as the game's camera sees them.
//
//   Tools/editor.sh play
//   Tools/editor.sh call StarForge.EditorTools.TreeTrials.Graze
//   Tools/editor.sh wait StarForge.EditorTools.TreeTrials.GrazeReport
//
// Graze: the three thickest stands of trees on the map, each from the game's close and
// middle zoom, with SF_Tree's edge-on card fade off and on (Temp/treelook_<n>_<zoom>_<off|on>.png,
// 2x), the wind stilled so the two differ only by the fade. Reports, for each pair, how
// many pixels changed and by how much.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class TreeTrials
    {
        /// <summary>The last run's report (kept here: the watcher removes itself when done).</summary>
        public static string last = "not run";

        public static string Graze()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var old = Object.FindAnyObjectByType<TreeGrazeLooker>();
            if (old != null) Object.Destroy(old.gameObject);
            last = "looking";
            new GameObject("TreeGraze").AddComponent<TreeGrazeLooker>();
            return "looking; call GrazeReport in about half a minute";
        }

        public static string GrazeReport()
        {
            return last;
        }
    }

    public sealed class TreeGrazeLooker : MonoBehaviour
    {
        string report { get => TreeTrials.last; set => TreeTrials.last = value; }
        static readonly int GrazeId = Shader.PropertyToID("_CardGraze");
        static readonly int WindId = Shader.PropertyToID("_SF_Wind");

        // A copy of the foliage material to switch the fade on: the real one is a generated
        // asset, and a value set on it in play is written into the .mat at the next save.
        Material foliage, foliageWas;
        StarForge.View.VegetationRenderer renderer_;
        Vector4 grazeWas;
        float savedScale = 1f;
        StarForge.View.RTSCamera rig;
        bool wasScripted;
        bool still, blowing;
        float windPhase;
        readonly List<UIDocument> hidden = new List<UIDocument>();

        void OnDestroy()
        {
            if (renderer_ != null && foliageWas != null) renderer_.foliageMaterial = foliageWas;
            if (foliage != null) Destroy(foliage);
            Time.timeScale = savedScale;
            if (rig != null) rig.scripted = wasScripted;
            foreach (var d in hidden) if (d != null && d.rootVisualElement != null) d.rootVisualElement.style.display = DisplayStyle.Flex;
        }

        // After Atmosphere's Update has uploaded the match's wind: a strength just over 0,
        // so the shader's own fallback breeze stays off too.
        void LateUpdate()
        {
            if (blowing) Shader.SetGlobalVector(WindId, new Vector4(0.82f, 0.57f, 0.8f, windPhase));
            else if (still) Shader.SetGlobalVector(WindId, new Vector4(0.82f, 0.57f, 0.0011f, 0f));
        }

        IEnumerator Start()
        {
            var world = GameWorld.Instance;
            var vr = Object.FindAnyObjectByType<StarForge.View.VegetationRenderer>();
            rig = Object.FindAnyObjectByType<StarForge.View.RTSCamera>();
            var veg = world != null ? world.Plants : null;
            if (world == null || vr == null || rig == null || veg == null || vr.foliageMaterial == null) { report = "no world/vegetation/camera"; yield break; }
            if (world.running) { report = "a match is running: run it from the menu (nothing fogged, nothing moving)"; yield break; }
            renderer_ = vr;
            foliageWas = vr.foliageMaterial;
            foliage = new Material(foliageWas);
            vr.foliageMaterial = foliage;
            grazeWas = foliage.GetVector(GrazeId);
            savedScale = Time.timeScale;
            Time.timeScale = 1f;
            wasScripted = rig.scripted;
            rig.scripted = true;
            foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (d.rootVisualElement != null && d.rootVisualElement.style.display != DisplayStyle.None)
                {
                    d.rootVisualElement.style.display = DisplayStyle.None;
                    hidden.Add(d);
                }
            // Still air, so a pair differs only by the fade (LateUpdate, below).
            still = true;

            // The three thickest stands: the trees with the most trees within 12 m, 40 m apart.
            var spots = new List<Vector2>();
            var counts = new int[veg.plants.Length];
            for (int i = 0; i < veg.plants.Length; i++)
            {
                if (veg.kinds[veg.plants[i].kind].bush || veg.kinds[veg.plants[i].kind].foliageSubmesh < 0) { counts[i] = -1; continue; }
                var c = veg.Pos2(i);
                for (int j = 0; j < veg.plants.Length; j++)
                    if (!veg.kinds[veg.plants[j].kind].bush && veg.kinds[veg.plants[j].kind].foliageSubmesh >= 0 && (veg.Pos2(j) - c).sqrMagnitude < 12f * 12f) counts[i]++;
            }
            for (int n = 0; n < 3; n++)
            {
                int best = -1;
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] < 0 || (best >= 0 && counts[i] <= counts[best])) continue;
                    bool far = true;
                    foreach (var s in spots) if ((veg.Pos2(i) - s).sqrMagnitude < 40f * 40f) far = false;
                    if (far) best = i;
                }
                if (best < 0) break;
                spots.Add(veg.Pos2(best));
            }
            var sb = new StringBuilder();
            var off = new Vector4(0f, 0f, 0f, 0f);
            for (int n = 0; n < spots.Count; n++)
                foreach (float zoom in new[] { 26f, 55f })
                {
                    rig.JumpTo(spots[n]);
                    rig.ZoomTo(zoom);
                    yield return new WaitForSecondsRealtime(1.5f);
                    Texture2D a = null, b = null;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        foliage.SetVector(GrazeId, pass == 0 ? off : grazeWas);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        var tex = ScreenCapture.CaptureScreenshotAsTexture(2);
                        System.IO.File.WriteAllBytes($"Temp/treelook_{n}_{zoom:0}_{(pass == 0 ? "off" : "on")}.png", tex.EncodeToPNG());
                        if (pass == 0) a = tex; else b = tex;
                    }
                    // How much of the picture the fade changed.
                    var pa = a.GetPixels32();
                    var pb = b.GetPixels32();
                    int changed = 0;
                    long sum = 0;
                    for (int i = 0; i < pa.Length && i < pb.Length; i++)
                    {
                        int d = Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
                        if (d > 24) { changed++; sum += d; }
                    }
                    sb.Append($"stand {n} at ({spots[n].x:0},{spots[n].y:0}) zoom {zoom:0}: {100f * changed / Mathf.Max(1, pa.Length):0.00}% of pixels changed" +
                              $"{(changed > 0 ? $", mean {sum / (3f * changed):0} of 255" : "")}; ");
                    Destroy(a);
                    Destroy(b);
                }
            // In the wind: a card's own shiver turns its plane a little each frame, so one near
            // edge-on could come and go with the fade. The same 40 frames of the same wind (the
            // clock stopped, the gust phase stepped by hand), fade off and on: pixels whose
            // brightness reverses direction six times or more by over 6 of 255.
            if (spots.Count > 0)
            {
                Time.timeScale = 0f;
                foreach (float zoom in new[] { 26f, 55f })
                {
                    rig.JumpTo(spots[0]);
                    rig.ZoomTo(zoom);
                    yield return new WaitForSecondsRealtime(1.5f);
                    sb.Append($"| in the wind, stand 0 zoom {zoom:0}, pixels vibrating: ");
                    for (int pass = 0; pass < 2; pass++)
                    {
                        foliage.SetVector(GrazeId, pass == 0 ? off : grazeWas);
                        blowing = true;
                        int[] last = null, reversals = null;
                        sbyte[] dir = null;
                        for (int f = 0; f < 40; f++)
                        {
                            windPhase = 100f + f / 60f;
                            yield return null;
                            yield return new WaitForEndOfFrame();
                            var tex = ScreenCapture.CaptureScreenshotAsTexture();
                            var px = tex.GetPixels32();
                            Destroy(tex);
                            if (last == null) { last = new int[px.Length]; reversals = new int[px.Length]; dir = new sbyte[px.Length]; }
                            for (int i = 0; i < px.Length && i < last.Length; i++)
                            {
                                int luma = (px[i].r * 2 + px[i].g * 5 + px[i].b) / 8;
                                if (f > 0)
                                {
                                    int d = luma - last[i];
                                    sbyte s = d > 6 ? (sbyte)1 : d < -6 ? (sbyte)(-1) : (sbyte)0;
                                    if (s != 0) { if (dir[i] != 0 && s != dir[i]) reversals[i]++; dir[i] = s; }
                                }
                                last[i] = luma;
                            }
                        }
                        int vibrating = 0;
                        foreach (int r in reversals) if (r >= 6) vibrating++;
                        sb.Append($"{(pass == 0 ? "fade off" : "fade on")} {100f * vibrating / Mathf.Max(1, reversals.Length):0.000}% ({vibrating}){(pass == 0 ? ", " : " ")}");
                        blowing = false;
                    }
                }
                Time.timeScale = 1f;
            }
            report = sb.Length > 0 ? sb.ToString() : "no stand of trees found";
            Destroy(gameObject, 0.5f);
        }
    }
}
