// BannerClothCheck.cs — a snapshot of every Mech's cloth banner, for the check in
// View/CLAUDE.md (play mode): Tools/editor.sh call StarForge.EditorTools.BannerClothCheck.Report
// at 1x, then again after `Time.timeScale = 8` has run a few seconds (rigid on, cloth off).
using System.Text;
using UnityEngine;
using StarForge.View;

namespace StarForge.EditorTools
{
    public static class BannerClothCheck
    {
        public static string Report()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return "not playing";
            var sb = new StringBuilder($"timeScale {Time.timeScale}");
            foreach (var v in Object.FindObjectsByType<MechView>(FindObjectsSortMode.None))
            {
                var c = v.BannerCloth;
                Transform rigid = null, root = null;
                foreach (var t in v.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Banner") rigid = t;
                    else if (t.name == "BannerCloth") root = t;
                }
                sb.Append($"\nteam {v.Unit.team}: cloth {(c != null && c.Alive)}, tip {(c != null ? c.TipDistance() : -1f):0.00}, resets {(c != null ? c.Resets : -1)}, cloth root {(root != null && root.gameObject.activeSelf)}, rigid {(rigid != null && rigid.gameObject.activeSelf)}");
            }
            return sb.ToString();
        }
    }
}
