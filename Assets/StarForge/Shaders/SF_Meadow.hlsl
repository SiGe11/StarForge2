// SF_Meadow.hlsl — what the meadow's ground and its grass agree on.
//
// The grass is tufts of opaque blades (SF_Grass) on a scanned ground of sand and
// stones (SF_Terrain's meadow layer), and between the tufts that ground showed
// through as bare brown: from the RTS camera a meadow read as dark spikes on dirt,
// and from the overview, where the tufts have sunk away, as no meadow at all. So
// the terrain lays a turf of the grass's own colour under the tufts, and both take
// their colour from the same broad patches -- lusher and deeper green here, drier
// and yellower there -- so a field is drifts of colour, not one flat green.
//
// Included by SF_Terrain (fragment), SF_Grass (vertex) and SF_Tree. No keywords.
#ifndef SF_MEADOW_INCLUDED
#define SF_MEADOW_INCLUDED

// Tuning offsets for a player run (-sftune _SF_TuneA=x,y,z,w; Game/ShaderTune.cs):
// zero in any normal run, so the numbers in the shaders are what ships.
//   _SF_TuneA  x turf strength, y grass brightness, z far thinning, w flowers
//   _SF_TuneB  x leaf wrap, y leaf self-shadow floor, z light through leaves, w leaf ambient
//   _SF_TuneC  x turf photograph's strength, y its tile size (share), z burnt photograph's
//              strength, w how much of the scan's light and dark the turf keeps
float4 _SF_TuneA;
float4 _SF_TuneB;
//   _SF_TuneD  x -1: steep faces keep their splat (SF_Terrain's slope-to-rock rule off)
//              y added to SF_Terrain's _CliffStretch (0.55; +0.45 = the cliff scan as tall as it is wide)
float4 _SF_TuneC;
float4 _SF_TuneD;

// 0 a dry, yellower patch .. 1 a lush, deeper one, from the terrain's variation
// channel (MapGenerator.Variation, in the AO texture's G: fBm about 18 m across).
// The terrain has it in hand already; SF_Grass reads the same texture per clump
// (_SF_TerrainAO, bound by GroundScatter). It was a value noise of its own here,
// worked out for every terrain pixel: that build read 44.7, 44.3 fps against the one
// before it at 45.4, 45.4 (420 s, scale held 0.75); from the texture 44.6, 45.0
// against 44.9 (2026-11-20), for drifts the terrain already carried.
half SFMeadowPatch(half variation)
{
    return saturate((variation - 0.3) * 2.5);
}

// The turf's colour (linear) for a patch value: the shaded base of the grass, a
// little under the blades' own mid-tone so the tufts still stand out of it.
half3 SFTurfColor(half patch)
{
    return lerp(half3(0.092, 0.098, 0.034), half3(0.040, 0.082, 0.021), patch);
}

// What the blades' colour is multiplied by in a patch: dry patches paler and
// yellower, lush ones as the material has them.
half3 SFGrassPatchTint(half patch)
{
    return lerp(half3(1.42, 1.16, 0.80), half3(0.96, 1.04, 0.98), patch);
}
#endif
