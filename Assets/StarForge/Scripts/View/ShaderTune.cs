// ShaderTune.cs — set global shader values from the player's command line.
//
//   -sftune _SF_TuneA=0.1,0,0,0  -sftune _SF_GrassPixelShadow=1
//
// A look that is judged by pictures from the built player (the gallery) could only
// be tuned by rebuilding for every number. The vegetation shaders read a few global
// offsets that are zero in any normal run (SF_Meadow.hlsl: _SF_TuneA, _SF_TuneB) and
// A/B switches (_SF_GrassPixelShadow), and this sets them for one run. One value
// sets a float and the same value in x of a vector; several, a vector. Nothing here
// runs without the flag, and nothing is saved.
using System;
using System.Globalization;
using UnityEngine;

namespace StarForge.View
{
    public static class ShaderTune
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "-sftune") continue;
                int eq = args[i + 1].IndexOf('=');
                if (eq <= 0) continue;
                string name = args[i + 1].Substring(0, eq);
                var parts = args[i + 1].Substring(eq + 1).Split(',');
                var v = Vector4.zero;
                bool ok = parts.Length >= 1 && parts.Length <= 4;
                for (int k = 0; ok && k < parts.Length; k++)
                {
                    ok = float.TryParse(parts[k], NumberStyles.Float, CultureInfo.InvariantCulture, out float f);
                    v[k] = f;
                }
                if (!ok) { Debug.LogWarning($"[StarForge] -sftune {args[i + 1]}: not name=number[,number,number,number]"); continue; }
                if (parts.Length == 1) Shader.SetGlobalFloat(name, v.x);
                Shader.SetGlobalVector(name, v);
                Debug.Log($"[StarForge] -sftune {name} = {v}");
            }
        }
    }
}
