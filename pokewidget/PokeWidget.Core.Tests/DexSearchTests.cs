using PokeWidget.Core;
using Xunit;

namespace PokeWidget.Core.Tests;

public class DexSearchTests
{
    // ── Cas de base (hasShiny = false) ─────────────────────────────────────
    [Theory]
    [InlineData(133, "Évoli", false, "evoli", true)]
    [InlineData(133, "Évoli", false, "ÉVO", true)]
    [InlineData(25,  "Pikachu", false, "pika", true)]
    [InlineData(25,  "Pikachu", false, "  PIKA ", true)]
    [InlineData(25,  "Pikachu", false, "sala", false)]
    [InlineData(25,  "Pikachu", false, "", true)]
    [InlineData(25,  "Pikachu", false, "#", true)]
    [InlineData(25,  "Pikachu", false, "25", true)]
    [InlineData(250, "Suicune", false, "25", true)]   // sans #, préfixe de numéro
    [InlineData(250, "Suicune", false, "#25", false)] // avec #, numéro exact
    [InlineData(25,  "Pikachu", false, "#25", true)]
    [InlineData(25,  "Pikachu", false, "5", false)]
    // ── Shiny / chromatique ────────────────────────────────────────────────
    [InlineData(25,  "Pikachu", true,  "shiny", true)]
    [InlineData(25,  "Pikachu", false, "shiny", false)]
    [InlineData(25,  "Pikachu", true,  "chromatique", true)]
    [InlineData(25,  "Pikachu", false, "chromatique", false)]
    // ── Virgules (OR) ──────────────────────────────────────────────────────
    [InlineData(4,   "Salamèche", false, "sala,pika,cara", true)]
    [InlineData(7,   "Carapuce",  false, "sala,pika,cara", true)]
    [InlineData(1,   "Bulbizarre", false, "sala,pika,cara", false)]
    // ── Plages numériques ──────────────────────────────────────────────────
    [InlineData(1,   "Bulbizarre", false, "<30", true)]
    [InlineData(29,  "Nidoran♀",  false, "<30", true)]
    [InlineData(30,  "Nidorina",  false, "<30", false)]
    [InlineData(30,  "Nidorina",  false, ">=30 & <50", true)]
    [InlineData(49,  "Venomoth",  false, ">=30 & <50", true)]
    [InlineData(50,  "Taupiqueur", false, ">=30 & <50", false)]
    // ── Génération ────────────────────────────────────────────────────────
    [InlineData(1,   "Bulbizarre", false, "gen1", true)]
    [InlineData(151, "Mew",        false, "gen1", true)]
    [InlineData(152, "Germignon",  false, "gen1", false)]
    [InlineData(152, "Germignon",  false, "gen2", true)]
    [InlineData(906, "Poussacha",  false, "gen9", true)]
    [InlineData(810, "Ouistempo",  false, "gen9", false)]
    public void Matches_AllCases(int id, string name, bool hasShiny, string query, bool expected) =>
        Assert.Equal(expected, DexSearch.Matches(id, name, hasShiny, query));
}
