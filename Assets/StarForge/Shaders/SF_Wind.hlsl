// SF_Wind.hlsl — the air over the battlefield, shared by the vegetation shaders
// (SF_Tree, SF_Grass).
//
// Two things move a plant. The match's wind (World/Wind, uploaded by
// View/Atmosphere) is the steady one: one heading and one strength for the whole
// map, gusting. The other is pressure: a Mauler's muzzle blast and a shell
// bursting throw a wall of air out, and the growth it passes over is pressed
// flat away from it and springs back behind it. Those are queued by
// View/FXDirector and uploaded as a handful of expanding fronts, so a shader
// needs no state and a seed replays identically.
#ifndef SF_WIND_INCLUDED
#define SF_WIND_INCLUDED

// The match's wind: xy heading, z strength, w the gust's travelling phase.
float4 _SF_Wind;

// Live pressure fronts (FXDirector.UploadGusts):
//   _SF_Gusts[i]      xy the origin in world xz, z the front's radius now,
//                     w how far it shoves a plant at the front, in metres
//   _SF_GustShape[i]  xy the heading it favours, z how much it favours it
//                     (0 all round, 1 forward only), w the front's thickness
#define SF_MAX_GUSTS 4
float4 _SF_Gusts[SF_MAX_GUSTS];
float4 _SF_GustShape[SF_MAX_GUSTS];
float _SF_GustCount;

/// <summary>The shove on a plant rooted at <c>rootXZ</c>, in metres, horizontal.
/// Nothing until the front arrives; behind it the pressure falls away over the
/// front's thickness, with a light draw back after it as the air goes home.</summary>
float2 SFGust(float2 rootXZ)
{
    // Nothing is blasting nearly all the time, and this runs on every blade of
    // grass in the field: leave on a scalar the whole wave agrees about.
    if (_SF_GustCount < 0.5) return 0.0;
    int n = (int)_SF_GustCount;
    float2 push = 0.0;
    for (int i = 0; i < n; i++)
    {
        float4 g = _SF_Gusts[i];
        float4 s = _SF_GustShape[i];
        float2 rel = rootXZ - g.xy;
        float d = length(rel);
        float2 away = d > 1e-3 ? rel / d : float2(0.0, 1.0);
        float behind = g.z - d;
        float k = behind / max(s.w, 0.01);
        float wave = behind < 0.0 ? 0.0 : exp2(-k * 1.6) * cos(k * 1.7);
        // A muzzle blast is thrown out in front of the gun; a burst goes all round.
        float cone = lerp(1.0, saturate(dot(away, s.xy) * 0.5 + 0.5), s.z);
        push += away * (g.w * wave * cone);
    }
    return push;
}

#endif
