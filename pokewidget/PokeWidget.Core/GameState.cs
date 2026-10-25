namespace PokeWidget.Core;

public sealed class BallStock
{
    public int Poke { get; set; }
    public int Super { get; set; }
    public int Hyper { get; set; }

    public int this[BallType t]
    {
        get => t switch { BallType.Poke => Poke, BallType.Super => Super, _ => Hyper };
        set { switch (t) { case BallType.Poke: Poke = value; break; case BallType.Super: Super = value; break; default: Hyper = value; break; } }
    }

    public bool AreAllAtCap() => Poke >= GameConfig.BallCap && Super >= GameConfig.BallCap && Hyper >= GameConfig.BallCap;
}

public sealed class DexEntry
{
    public string Name { get; set; } = "";
    public int Rate { get; set; }
    public int Count { get; set; }
    public int Shiny { get; set; }
}

public enum ThrowOutcome { Unavailable, Missed, Caught }

/// <summary>
/// État de la partie. Même forme JSON que le userscript (balls, lastTick, lastCaptureSlot, dex),
/// ce qui permet de réimporter l'ancienne progression telle quelle.
/// </summary>
public sealed class GameState
{
    public BallStock Balls { get; set; } = new() { Poke = GameConfig.StartBalls };
    public long LastTick { get; set; }
    public long? LastCaptureSlot { get; set; }
    /// <summary>Dernier slot "vu" à l'écran : sert à l'animation de l'icône réduite.</summary>
    public long? SeenSlot { get; set; }
    public Dictionary<int, DexEntry> Dex { get; set; } = new();

    public static GameState Fresh(long nowMs) => new() { LastTick = nowMs };

    public bool IsCaught(long slot) => LastCaptureSlot == slot;

    /// <summary>Marque le slot comme vu. Retourne true si l'état a changé (donc à sauvegarder).</summary>
    public bool MarkSeen(long slot)
    {
        if (SeenSlot == slot) return false;
        SeenSlot = slot;
        return true;
    }

    public void AddBall(BallType t, int n = 1) => Balls[t] = Math.Min(GameConfig.BallCap, Balls[t] + n);

    /// <summary>
    /// Distribue les balls dues depuis LastTick (rattrape les heures manquées).
    /// Retourne true si quelque chose a changé (donc à sauvegarder).
    /// </summary>
    public bool TickBalls(long nowMs, Random rng)
    {
        long hours = (nowMs - LastTick) / GameConfig.BallMs;
        if (hours <= 0) return false;
        LastTick += hours * GameConfig.BallMs;

        var trials = new int[3];
        foreach (var e in Dex.Values) trials[(int)BallInfo.TierFor(e.Rate)] += e.Count;

        for (long h = 0; h < hours; h++)
        {
            AddBall(BallType.Poke);
            for (int t = 0; t < 3; t++)
                for (int i = 0; i < trials[t]; i++)
                    if (rng.NextDouble() < GameConfig.BonusChance) AddBall((BallType)t);
            if (Balls.AreAllAtCap()) break;
        }
        return true;
    }

    /// <summary>
    /// Lance une ball : la consomme (réussite ou non) et met à jour le Pokédex en cas de capture.
    /// Le résultat est immédiat ; c'est à l'interface de retarder l'affichage pour l'animation.
    /// </summary>
    public ThrowOutcome Throw(BallType type, Spawn spawn, SpeciesInfo species, Random rng)
    {
        if (IsCaught(spawn.Slot) || Balls[type] <= 0) return ThrowOutcome.Unavailable;
        Balls[type]--;
        if (rng.NextDouble() >= CatchMath.Probability(species.Rate, BallInfo.Bonus(type)))
            return ThrowOutcome.Missed;

        if (!Dex.TryGetValue(spawn.Id, out var e))
            Dex[spawn.Id] = e = new DexEntry { Name = species.Name, Rate = species.Rate };
        e.Count++;
        if (spawn.Shiny) e.Shiny++;
        LastCaptureSlot = spawn.Slot;
        return ThrowOutcome.Caught;
    }
}
