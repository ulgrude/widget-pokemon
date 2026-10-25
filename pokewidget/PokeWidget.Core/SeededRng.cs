namespace PokeWidget.Core;

/// <summary>
/// Port exact de hashStr + mulberry32 du userscript. Tout est en uint "unchecked" :
/// la multiplication 32 bits de uint donne les mêmes bits que Math.imul, et >> sur uint
/// équivaut à >>> en JavaScript. Ne pas "simplifier" sous peine de changer les seeds.
/// </summary>
public sealed class SeededRng
{
    private uint _a;

    public SeededRng(uint seed) => _a = seed;

    public static SeededRng FromString(string s) => new(HashString(s));

    public static uint HashString(string s)
    {
        unchecked
        {
            uint h1 = 1779033703u, h2 = 3144134277u, h3 = 1013904242u, h4 = 2773480762u;
            foreach (char c in s) // char = unité UTF-16, comme charCodeAt
            {
                uint k = c;
                h1 = h2 ^ ((h1 ^ k) * 597399067u);
                h2 = h3 ^ ((h2 ^ k) * 2869860233u);
                h3 = h4 ^ ((h3 ^ k) * 951274213u);
                h4 = h1 ^ ((h4 ^ k) * 2716044179u);
            }
            h1 = (h3 ^ (h1 >> 18)) * 597399067u;
            h2 = (h4 ^ (h2 >> 22)) * 2869860233u;
            h3 = (h1 ^ (h3 >> 17)) * 951274213u;
            h4 = (h2 ^ (h4 >> 19)) * 2716044179u;
            return h1 ^ h2 ^ h3 ^ h4;
        }
    }

    /// <summary>Double dans [0, 1).</summary>
    public double NextDouble()
    {
        unchecked
        {
            _a += 0x6D2B79F5u;
            uint t = (_a ^ (_a >> 15)) * (1u | _a);
            t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
}
