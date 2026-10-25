namespace PokeWidget.Core;

/// <summary>État de l'icône réduite : se balance si un nouveau Pokémon est apparu, brille si c'est un chromatique.</summary>
public readonly record struct MiniIconState(bool IsNew, bool Shine, string Label);

public static class MiniIcon
{
    /// <summary>Se déduit de la seed seule : pas besoin de PokéAPI, donc disponible immédiatement.</summary>
    public static MiniIconState Compute(GameState state, string seed, long slot)
    {
        bool caught = state.IsCaught(slot);
        bool shine = !caught && SpawnService.For(seed, slot).Shiny;
        bool isNew = !caught && state.SeenSlot != slot;
        string label = shine ? "Un Pokémon chromatique est là !"
            : isNew ? "Un nouveau Pokémon est apparu"
            : "Afficher le widget Pokémon";
        return new MiniIconState(isNew, shine, label);
    }
}
