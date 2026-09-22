// MapChecks.cs — StarForge ▸ Debug ▸ Check Map Blocking: samples the NavMesh under
// every object on the map and reports any that a ground unit could walk through.
//
// Run it in play mode (the match's map and NavMesh are built at load) or in edit
// mode on the saved map. A point counts as walkable when the NavMesh for
// ground units (GameWorld.GroundAreas: everything but Rubble) has a polygon
// within 0.25 m of it horizontally. Scenery must block everyone; trees and
// boulders must block everyone but the Mauler (their ground is Rubble, which
// only a Mauler's area mask includes).
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using StarForge.World;

namespace StarForge.EditorTools
{
    public static class MapChecks
    {
        [MenuItem("StarForge/Debug/Check Map Blocking")]
        public static void CheckBlocking() => Debug.Log(Report());

        public static string Report()
        {
            var map = Object.FindAnyObjectByType<MapInfo>();
            if (map == null) return "[MapChecks] no MapInfo in the open scene";
            int ground = GameWorld.GroundAreas;
            var sb = new StringBuilder("[MapChecks] ");

            bool Walkable(Vector3 p, int mask)
            {
                if (!NavMesh.SamplePosition(p, out var hit, 2.5f, mask)) return false;
                var d = hit.position - p;
                return new Vector2(d.x, d.z).magnitude < 0.25f;
            }

            // Scenery: a grid over the inner 60% of each footprint volume.
            int sceneryPieces = 0, sceneryLeaks = 0;
            var leaks = new StringBuilder();
            if (map.sceneryRoot != null)
                foreach (Transform t in map.sceneryRoot)
                {
                    if (!t.gameObject.activeInHierarchy) continue;
                    var v = t.GetComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
                    sceneryPieces++;
                    if (v == null) { sceneryLeaks++; leaks.Append($" {t.name}(no volume)"); continue; }
                    int bad = 0;
                    for (int ix = -2; ix <= 2; ix++)
                        for (int iz = -2; iz <= 2; iz++)
                        {
                            var local = v.center + new Vector3(v.size.x * 0.3f * ix / 2f, 0f, v.size.z * 0.3f * iz / 2f);
                            var w = t.TransformPoint(local);
                            if (Walkable(map.Ground(new Vector2(w.x, w.z)), ground)) bad++;
                        }
                    if (bad > 0) { sceneryLeaks++; leaks.Append($" {t.name}@{t.position.x:0},{t.position.z:0}({bad}/25)"); }
                }
            sb.Append($"scenery {sceneryPieces - sceneryLeaks}/{sceneryPieces} blocked");
            if (leaks.Length > 0) sb.Append(" — walkable:").Append(leaks);

            // Boulders and trunks: the centre must be closed to ground units.
            int boulders = 0, boulderLeaks = 0;
            if (map.boulderRoot != null)
                foreach (Transform t in map.boulderRoot)
                {
                    if (!t.gameObject.activeInHierarchy) continue;
                    boulders++;
                    if (Walkable(map.Ground(new Vector2(t.position.x, t.position.z)), ground)) boulderLeaks++;
                }
            sb.Append($"; boulders {boulders - boulderLeaks}/{boulders} blocked");

            // Trees: the trunk and a ring 0.4 m outside the bark (a unit centred there
            // would be half inside the trunk) must be closed.
            int trees = 0, treeLeaks = 0, open = 0;
            var veg = map.vegetation;
            if (veg != null)
                foreach (var p in veg.plants)
                {
                    var k = veg.kinds[p.kind];
                    if (p.volume < 0) { if (!k.bush) open++; continue; }
                    trees++;
                    var c = new Vector2(p.pos.x, p.pos.z);
                    float ring = k.trunkRadius * p.scale + 0.4f;   // a unit centred here would have its body in the bark
                    bool leak = Walkable(map.Ground(c), ground);
                    for (int i = 0; i < 8 && !leak; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        leak = Walkable(map.Ground(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring), ground);
                    }
                    if (leak) treeLeaks++;
                }
            sb.Append($"; trees {trees - treeLeaks}/{trees} blocked round the trunk ({open} more stand where no unit goes)");

            // And the map is still one piece for ground units.
            var path = new NavMeshPath();
            bool linked = NavMesh.SamplePosition(map.Ground(map.StartPos(0)), out var a0, 12f, ground)
                       && NavMesh.SamplePosition(map.Ground(map.StartPos(1)), out var a1, 12f, ground)
                       && NavMesh.CalculatePath(a0.position, a1.position, ground, path)
                       && path.status == NavMeshPathStatus.PathComplete;
            sb.Append(linked ? "; bases connected for ground units" : "; BASES NOT CONNECTED for ground units");

            // Ore and structures carve the NavMesh as obstacles, at runtime only.
            if (Application.isPlaying)
            {
                var world = Object.FindAnyObjectByType<GameWorld>();
                int solid = 0, solidLeaks = 0;
                if (world != null)
                    foreach (var u in world.units)
                    {
                        if (u == null || u.dying || u.def.IsMobile) continue;
                        solid++;
                        if (Walkable(map.Ground(u.pos), ground)) solidLeaks++;
                    }
                sb.Append($"; ore and structures {solid - solidLeaks}/{solid} blocked");
            }
            return sb.ToString();
        }
    }
}
