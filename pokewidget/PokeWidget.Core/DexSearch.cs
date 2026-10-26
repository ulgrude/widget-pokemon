using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PokeWidget.Core;

public static class DexSearch
{
    // ── Générations : plages d'IDs officielles ──────────────────────────────
    private static readonly (int From, int To)[] Generations =
    [
        (1,  151),   // Gen 1
        (152, 251),  // Gen 2
        (252, 386),  // Gen 3
        (387, 493),  // Gen 4
        (494, 649),  // Gen 5
        (650, 721),  // Gen 6
        (722, 809),  // Gen 7
        (810, 905),  // Gen 8
        (906, 1025), // Gen 9
    ];

    /// <summary>Minuscules, sans accents, sans espaces autour : "Évoli" == "evoli".</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().ToLowerInvariant().Trim();
    }

    /// <summary>
    /// Teste si un Pokémon du Pokédex correspond à la requête.
    /// Syntaxes supportées :
    ///   - "pika"          → contient dans le nom
    ///   - "25"            → numéros commençant par 25
    ///   - "#25"           → numéro exact
    ///   - "chromatique" / "shiny" → entrées avec au moins 1 shiny
    ///   - "mystere" / "mystère"   → entrées obtenues via pokémon mystère
    ///   - "doublon" / "doublons"  → entrées ayant au moins 1 doublon
    ///   - "gen1" … "gen9" → génération
    ///   - "sal,cara,pika" → plusieurs termes séparés par des virgules (OR)
    ///   - "&lt;30"        → IDs 1–29
    ///   - "&gt;=30 &amp; &lt;50" → IDs 30–49 (opérateurs : &lt; &lt;= &gt; &gt;= avec &amp; entre eux)
    /// </summary>
    public static bool Matches(int id, string name, bool hasShiny, string query) =>
        Matches(id, name, hasShiny, false, false, query);

    public static bool Matches(int id, string name, bool hasShiny, bool hasMystery, bool hasDuplicate, string query)
    {
        string raw = Normalize(query);
        if (raw.Length == 0) return true;

        // Virgule → OR sur plusieurs termes
        if (raw.Contains(','))
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Any(t => MatchesSingle(id, name, hasShiny, hasMystery, hasDuplicate, t));

        // Plage numérique (contient '<', '>', '&' ou '=')
        if (raw.IndexOfAny(['<', '>', '&']) >= 0)
            return MatchesRange(id, raw);

        return MatchesSingle(id, name, hasShiny, hasMystery, hasDuplicate, raw);
    }

    private static bool MatchesSingle(int id, string name, bool hasShiny, bool hasMystery, bool hasDuplicate, string q)
    {
        if (q.Length == 0) return true;

        // Shiny / chromatique
        if (q == "shiny" || q == "chromatique")
            return hasShiny;

        // Mystère
        if (q == "mystere" || q == "mystère")
            return hasMystery;

        // Doublon
        if (q == "doublon" || q == "doublons")
            return hasDuplicate;

        // Génération (gen1 … gen9)
        var genMatch = Regex.Match(q, @"^gen(\d)$");
        if (genMatch.Success && int.TryParse(genMatch.Groups[1].Value, out int gen))
        {
            if (gen < 1 || gen > Generations.Length) return false;
            var (from, to) = Generations[gen - 1];
            return id >= from && id <= to;
        }

        // Numéro exact (#25) ou commence par (25) ; '#' seul = tout afficher
        bool exact = q.StartsWith('#');
        string qn = exact ? q[1..] : q;
        if (qn.Length == 0) return true; // '#' seul
        if (qn.All(char.IsAsciiDigit))
            return exact ? id.ToString() == qn : id.ToString().StartsWith(qn, StringComparison.Ordinal);

        // Nom (contient)
        return Normalize(name).Contains(q, StringComparison.Ordinal);
    }

    // ── Plages numériques ─────────────────────────────────────────────────
    // Exemples : "< 30", ">= 30 & < 50", "> 100 & <= 200"
    private static bool MatchesRange(int id, string raw)
    {
        // Divise sur '&' pour obtenir plusieurs conditions
        var parts = raw.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var m = Regex.Match(part, @"^(<=|>=|<|>)\s*(\d+)$");
            if (!m.Success || !int.TryParse(m.Groups[2].Value, out int threshold))
                return false; // syntaxe non reconnue → pas de résultat

            bool cond = m.Groups[1].Value switch
            {
                "<"  => id <  threshold,
                "<=" => id <= threshold,
                ">"  => id >  threshold,
                ">=" => id >= threshold,
                _    => false,
            };
            if (!cond) return false;
        }
        return true;
    }
}
