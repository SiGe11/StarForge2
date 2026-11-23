// StandardsTrials.cs -- the check for the knightly orders' standards (View/OrderStandards): the panel on a
// Mech's cloth banner and the device on the HUD's Mech strip. From a fresh play at the menu:
//   Tools/editor.sh call StarForge.EditorTools.StandardsTrials.Look
//   Tools/editor.sh wait StarForge.EditorTools.StandardsTrials.Report "done|failed" 120
// Eight Mechs, one of each order, four a side (spectated, so both sides are shown; the groups stand out of each
// other's reach) and a Mech-Hunter, whose flag is the same cloth with no order. Temp/standards_banners.png has each
// banner face on from both sides, by a camera of its own (meshes, so a camera render catches them);
// Temp/standards_hud.png is the screen with the strip. Then 8x and back: the cloth gives way to the rigid banner.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using StarForge.Game;
using StarForge.Sim;
using StarForge.View;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class StandardsTrials
    {
        public static string Look()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            new GameObject("StandardsLook").AddComponent<StandardsLook>();
            return "staging; wait StarForge.EditorTools.StandardsTrials.Report \"done|failed\" (about a minute)";
        }

        public static string Report()
        {
            var s = Object.FindAnyObjectByType<StandardsLook>();
            return s == null ? "none" : s.report;
        }

        /// <summary>Edit mode, once after the atlas arrives or is packed again: its import settings live in its
        /// .meta (it sits in Resources, outside the folders SFAssetPostprocessor rules). Clamped, so a cell at
        /// the sheet's edge never wraps round to the far side; no rescale; mipmaps for the game's distance.</summary>
        public static string ImportAtlas()
        {
            const string path = "Assets/StarForge/Resources/MechStandards.png";
            if (!(UnityEditor.AssetImporter.GetAtPath(path) is UnityEditor.TextureImporter ti)) return "no texture at " + path;
            ti.textureType = UnityEditor.TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = UnityEditor.TextureImporterAlphaSource.None;
            ti.npotScale = UnityEditor.TextureImporterNPOTScale.None;
            ti.maxTextureSize = 1024;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = UnityEditor.TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return t == null ? "did not load" : $"{t.width}x{t.height} {t.format}, mips {t.mipmapCount}, wrap {t.wrapMode}, filter {t.filterMode}";
        }
    }

    // Not named after its file: Unity refuses to add a MonoBehaviour that has a script asset in an Editor folder.
    public sealed class StandardsLook : MonoBehaviour
    {
        public string report = "staging";
        bool memoryWas, spectateWas, saved;

        // The match statics outlive the play session: put back what this set, also when play stops part-way.
        void OnDestroy()
        {
            if (!saved) return;
            MatchSettings.aiMemory = memoryWas;
            MatchSettings.spectate = spectateWas;
            Time.timeScale = 1f;
        }

        IEnumerator Start()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) { report = "failed: no GameBootstrap"; yield break; }
            if (OrderStandards.Atlas == null) { report = "failed: no atlas at Resources/MechStandards"; yield break; }
            memoryWas = MatchSettings.aiMemory;
            spectateWas = MatchSettings.spectate;
            saved = true;
            if (GameWorld.Instance == null || !GameWorld.Instance.running)
            {
                MatchSettings.aiMemory = false;
                MatchSettings.spectate = true;   // both sides' Mechs are drawn
                boot.StartMatch();
                yield return new WaitForSecondsRealtime(1.5f);
            }
            var world = GameWorld.Instance;
            var rig = FindAnyObjectByType<RTSCamera>();
            Time.timeScale = 1f;
            var sb = new StringBuilder();
            var mechs = new List<Unit>();
            Vector2 mid = new Vector2(world.MapSize * 0.5f, world.MapSize * 0.5f);
            Vector2[] spots = { Spot(world, 0, mid), Spot(world, 1, mid) };
            sb.AppendLine($"groups {(spots[0] - spots[1]).magnitude:0} m apart; atlas {OrderStandards.Atlas.width}x{OrderStandards.Atlas.height} {OrderStandards.Atlas.format}, mips {OrderStandards.Atlas.mipmapCount}");
            for (int i = 0; i < OrderStandards.Count; i++)
            {
                int team = i < 4 ? 0 : 1;
                var d = new MechDesign
                {
                    locomotion = MechLocomotion.Biped, frame = MechFrame.Medium, utility = MechUtility.Shield,
                    className = "Medium-Biped", pilot = "Trial", callsign = "TRIAL " + i, order = OrderStandards.NameOf(i),
                };
                d.weapons.Add(MechWeapon.Autocannon);
                d.weapons.Add(MechWeapon.Laser);
                var at = world.NearestWalkable(spots[team] + new Vector2((i % 4 - 1.5f) * 11f, 0f));
                mechs.Add(world.TrialMech(team, d, at, 0f, false, i == 0));   // the first is its side's Mech: the HUD's strip shows it
            }
            var hunter = world.Spawn(UnitType.MechHunter, 0, world.NearestWalkable(spots[0] + new Vector2(0f, -12f)));
            if (rig != null) { rig.scripted = true; rig.JumpTo(spots[0]); rig.ZoomTo(34f); }
            yield return new WaitForSecondsRealtime(6f);   // the cloths switch on at the first Tick and settle

            // Each banner face on, from both sides.
            const int W = 256, H = 448;
            var views = new List<Transform>();
            // A Mech is drawn by its MechView, which is no child of a UnitView: find each by its unit.
            var mechViews = FindObjectsByType<MechView>(FindObjectsSortMode.None);
            foreach (var m in mechs)
            {
                Transform t = null;
                foreach (var mv in mechViews) if (mv.Unit == m) t = mv.transform;
                views.Add(t);
            }
            views.Add(hunter != null && hunter.view != null ? hunter.view.transform : null);
            var sheet = new Texture2D(W * views.Count, H * 2, TextureFormat.RGB24, false);
            var rt = new RenderTexture(W, H, 24);
            var camGo = new GameObject("StandardsCam");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = rt;
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            for (int i = 0; i < views.Count; i++)
            {
                string who = i < mechs.Count ? $"team {mechs[i].team} {OrderStandards.NameOf(i)}"
                    : $"Mech-Hunter (an order of its own by its id: {OrderStandards.NameOf(OrderStandards.ForUnit(hunter.id))})";
                Transform root = null, rigid = null;
                if (views[i] != null)
                    foreach (var t in views[i].GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "BannerCloth") root = t;
                        else if (t.name == "Banner") rigid = t;
                    }
                if (root == null) { sb.AppendLine($"{who}: NO cloth banner under its view"); continue; }
                var cloth = root.GetComponentInChildren<Cloth>(true);
                var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var ownView = views[i].GetComponentInChildren<MechView>(true);
                var bc = ownView != null ? ownView.BannerCloth : null;
                float lowest = 0f, far = 0f;
                int particles = 0;
                if (cloth != null)
                {
                    var v = cloth.vertices;
                    particles = v.Length;
                    foreach (var p in v) { lowest = Mathf.Min(lowest, p.y); far = Mathf.Max(far, p.magnitude); }
                }
                var rigidPanel = rigid != null ? rigid.Find("Standard") : null;
                sb.AppendLine($"{who}: cloth root on {root.gameObject.activeSelf}, rigid on {(rigid != null && rigid.gameObject.activeSelf)}, " +
                              $"rigid panel {(rigidPanel != null ? rigidPanel.GetComponent<MeshFilter>().sharedMesh.name : "NONE")}, " +
                              $"carries order {(bc != null ? bc.Order.ToString() : "n/a")}, tip {(bc != null ? bc.TipDistance() : far):0.00}, resets {(bc != null ? bc.Resets : 0)}, " +
                              $"particles {particles} for {(smr != null && smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0)} vertices, " +
                              $"material {(smr != null && smr.sharedMaterial != null ? smr.sharedMaterial.name : "none")}, lowest particle {lowest:0.00} at scale {root.lossyScale.y:0.00} (the hem rests at -1.12 x the scale: a Mech's banner is drawn 1.2x; a panel come loose reads about -2 x it)");
                // The cloth hangs by gravity under the crossbar; look at its middle along the crossbar's own facing.
                Vector3 scale = root.lossyScale;
                Vector3 centre = root.position + Vector3.down * 0.56f * scale.y;
                Vector3 dir = root.forward; dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) dir = views[i].forward;
                dir.Normalize();
                for (int side = 0; side < 2; side++)
                {
                    cam.transform.position = centre + dir * (side == 0 ? 2.6f : -2.6f) * scale.y;
                    cam.transform.LookAt(centre, Vector3.up);
                    cam.Render();
                    var was = RenderTexture.active;
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, W, H), i * W, side == 0 ? H : 0);
                    RenderTexture.active = was;
                }
            }
            sheet.Apply();
            File.WriteAllBytes("Temp/standards_banners.png", sheet.EncodeToPNG());
            cam.targetTexture = null;
            Destroy(camGo);
            rt.Release();
            Destroy(rt);
            Destroy(sheet);
            sb.AppendLine($"Temp/standards_banners.png: {views.Count} columns (orders 0-7, then the Mech-Hunter), top row from the crossbar's +z side, bottom row from behind it");

            // The HUD's strip: the device beside the name of the side's Mech.
            yield return null;
            var doc = FindAnyObjectByType<UIDocument>();
            var dev = doc != null ? UQueryExtensions.Q(doc.rootVisualElement, "mechStripDevice") : null;
            var title = doc != null ? UQueryExtensions.Q<Label>(doc.rootVisualElement, "mechStripTitle") : null;
            if (dev == null) sb.AppendLine("HUD: no mechStripDevice in the document");
            else
            {
                var bg = dev.resolvedStyle.backgroundImage;
                sb.AppendLine($"HUD strip: \"{(title != null ? title.text : "?")}\", device shown {dev.resolvedStyle.display == DisplayStyle.Flex}, " +
                              $"{dev.resolvedStyle.width:0}x{dev.resolvedStyle.height:0} px, sprite {(bg.sprite != null ? bg.sprite.name + " " + bg.sprite.rect : "none")}");
            }
            ScreenCapture.CaptureScreenshot("Temp/standards_hud.png");
            yield return new WaitForSecondsRealtime(0.5f);

            // Above 3.5x the cloth cannot run: the rigid banner shows, and the cloth comes back at 1x.
            Time.timeScale = 8f;
            yield return new WaitForSecondsRealtime(1f);
            sb.AppendLine("at 8x: " + CountOn(views));
            Shoot(views, "Temp/standards_rigid.png");
            sb.AppendLine("Temp/standards_rigid.png: the rigid banners at 8x, the same columns and rows");
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(1.5f);
            sb.AppendLine("back at 1x: " + CountOn(views));
            report = "done\n" + sb;
        }

        // Flat, dry, open ground between a side's base and the middle, as MechShowcase picks it.
        static Vector2 Spot(GameWorld world, int team, Vector2 mid)
        {
            var map = world.Map;
            Vector2 home = map.StartPos(team), best = home;
            float bestScore = -1e9f;
            var rng = new System.Random(1234 + team);
            for (int i = 0; i < 400; i++)
            {
                double a = rng.NextDouble() * 6.2832, r = rng.NextDouble() * 26.0;
                var p = Vector2.Lerp(home, mid, 0.2f + 0.2f * (float)rng.NextDouble()) + new Vector2((float)(System.Math.Cos(a) * r), (float)(System.Math.Sin(a) * r));
                if (!map.InBounds(p, 20f) || map.WaterDepth(p) > -1f || !world.Walkable(p)) continue;
                float lo = 1e9f, hi = -1e9f;
                for (int k = 0; k < 12; k++)
                {
                    float h = map.HeightAt(p + new Vector2(Mathf.Cos(k * 0.52f), Mathf.Sin(k * 0.52f)) * 18f);
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
                float score = -(hi - lo) * 3f - world.UnitsNear(p, 20f).Count * 5f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        // Each view's banner on show (the cloth, or the rigid one when the cloth cannot run), face on from both sides.
        static void Shoot(List<Transform> views, string file)
        {
            const int W = 256, H = 448;
            var sheet = new Texture2D(W * views.Count, H * 2, TextureFormat.RGB24, false);
            var rt = new RenderTexture(W, H, 24);
            var camGo = new GameObject("StandardsCam");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = rt;
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i] == null) continue;
                Transform root = null, rigid = null;
                foreach (var t in views[i].GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "BannerCloth") root = t;
                    else if (t.name == "Banner") rigid = t;
                }
                var shown = root != null && root.gameObject.activeSelf ? root : rigid;
                if (shown == null) continue;
                Vector3 scale = shown.lossyScale;
                Vector3 centre = shown.position + Vector3.down * 0.56f * scale.y;
                Vector3 dir = shown.forward; dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) dir = views[i].forward;
                dir.Normalize();
                for (int side = 0; side < 2; side++)
                {
                    cam.transform.position = centre + dir * (side == 0 ? 2.6f : -2.6f) * scale.y;
                    cam.transform.LookAt(centre, Vector3.up);
                    cam.Render();
                    var was = RenderTexture.active;
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, W, H), i * W, side == 0 ? H : 0);
                    RenderTexture.active = was;
                }
            }
            sheet.Apply();
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            cam.targetTexture = null;
            Destroy(camGo);
            rt.Release();
            Destroy(rt);
            Destroy(sheet);
        }

        static string CountOn(List<Transform> views)
        {
            int cloth = 0, rigid = 0, n = 0;
            foreach (var v in views)
            {
                if (v == null) continue;
                n++;
                foreach (var t in v.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "BannerCloth" && t.gameObject.activeSelf) cloth++;
                    else if (t.name == "Banner" && t.gameObject.activeSelf) rigid++;
                }
            }
            return $"cloth on {cloth} of {n}, rigid banner on {rigid} of {n}";
        }
    }
}
