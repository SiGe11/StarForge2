// UxmlLint.cs -- the bug hunt's F8: an XML comment may not contain "--" (a USS class like m-portrait--0 in one fails the
// whole UXML's import, and the menus then fail on their first missing element). Checked whenever the project changes, for the
// UXML files under UI/, so the next one is named by file and line when it is saved, not found as a null reference at play.
// No importer hook (a postprocessor with preprocess methods would reimport every texture): only file times, two files.
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace StarForge.EditorTools
{
    [InitializeOnLoad]
    public static class UxmlLint
    {
        const string Dir = "Assets/StarForge/UI";
        static readonly Dictionary<string, System.DateTime> seen = new Dictionary<string, System.DateTime>();

        static UxmlLint()
        {
            EditorApplication.projectChanged += CheckAll;
            EditorApplication.delayCall += CheckAll;
        }

        static void CheckAll()
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var path in Directory.GetFiles(Dir, "*.uxml"))
            {
                var t = File.GetLastWriteTimeUtc(path);
                if (seen.TryGetValue(path, out var was) && was == t) continue;
                seen[path] = t;
                foreach (var line in Scan(File.ReadAllText(path)))
                    Debug.LogError($"[UxmlLint] {path}({line}): an XML comment may not contain '--' (it breaks the whole file's import; a USS class name like m-portrait--0 does). Reword it.");
            }
        }

        /// <summary>The line of every comment that holds a double hyphen, or is never closed.</summary>
        public static List<int> Scan(string text)
        {
            var bad = new List<int>();
            int pos = 0;
            while (true)
            {
                int open = text.IndexOf("<!--", pos, System.StringComparison.Ordinal);
                if (open < 0) break;
                int close = text.IndexOf("-->", open + 4, System.StringComparison.Ordinal);
                int line = 1 + CountNewlines(text, open);
                if (close < 0) { bad.Add(line); break; }
                string body = text.Substring(open + 4, close - open - 4);
                if (body.Contains("--") || body.EndsWith("-")) bad.Add(line);
                pos = close + 3;
            }
            return bad;
        }

        static int CountNewlines(string s, int upTo)
        {
            int n = 0;
            for (int i = 0; i < upTo; i++) if (s[i] == '\n') n++;
            return n;
        }
    }
}
