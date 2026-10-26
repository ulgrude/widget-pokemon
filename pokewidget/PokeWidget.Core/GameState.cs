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

    public bool AreAllAtCap(int cap = GameConfig.BallCap) => Poke >= cap && Super >= cap && Hyper >= cap;
}

public sealed class DexEntry
{
    public string Name { get; set; } = "";
    public int Rate { get; set; }
    public int Count { get; set; }
    public int Shiny { get; set; }
    public int Mystery { get; set; }
}

public sealed class ShopState
{
    public int BallCapUpgrades { get; set; }       // 0 à 5 (+10 par amélioration)
    public int MissCatchRateUpgrades { get; set; } // 0 ou 1 (+0.02 au taux par lancer raté)
    public int ShinyBoostUpgrades { get; set; }    // 0 ou 1 (+25 % de chance de chromatique)
    public int SpentDoublons { get; set; }
    public int SpentShinyDoublons { get; set; }
}

public enum ThrowOutcome { Unavailable, Missed, Caught }

/// <summary>
/// État de la partie. Même forme JSON que le userscript (balls, lastTick, lastCaptureSlot, dex, shop),
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
    public ShopState Shop { get; set; } = new();

    public int MissedCount { get; set; }
    public long? MissedSlot { get; set; }

    public int MaxBallCap => GameConfig.BallCap + (Shop.BallCapUpgrades * 10);

    public static GameState Fresh(long nowMs) => new() { LastTick = nowMs };

    public bool IsCaught(long slot) => LastCaptureSlot == slot;

    /// <summary>Marque le slot comme vu. Retourne true si l'état a changé (donc à sauvegarder).</summary>
    public bool MarkSeen(long slot)
    {
        if (SeenSlot == slot) return false;
        SeenSlot = slot;
        return true;
    }

    public void AddBall(BallType t, int n = 1) => Balls[t] = Math.Min(MaxBallCap, Balls[t] + n);

    /// <summary>
    /// Calcule le total de doublons obtenus (normaux et chromatiques).
    /// Les doublons normaux viennent des exemplaires normaux au-delà du 1er,
    /// et les doublons chromatiques des exemplaires chromatiques au-delà du 1er.
    /// </summary>
    public (int NormalDoublons, int ShinyDoublons) GetEarnedDoublons()
    {
        int normalDupes = 0;
        int shinyDupes = 0;
        foreach (var e in Dex.Values)
        {
            int normalCount = Math.Max(0, e.Count - e.Shiny);
            if (normalCount > 1) normalDupes += (normalCount - 1);
            if (e.Shiny > 1) shinyDupes += (e.Shiny - 1);
        }
        return (normalDupes, shinyDupes);
    }

    public int AvailableDoublons => Math.Max(0, GetEarnedDoublons().NormalDoublons - Shop.SpentDoublons);
    public int AvailableShinyDoublons => Math.Max(0, GetEarnedDoublons().ShinyDoublons - Shop.SpentShinyDoublons);

    public double GetEffectiveRate(int baseRate, long slot)
    {
        if (Shop.MissCatchRateUpgrades > 0 && MissedSlot == slot)
            return baseRate + (MissedCount * 0.02);
        return baseRate;
    }

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

        int cap = MaxBallCap;
        for (long h = 0; h < hours; h++)
        {
            AddBall(BallType.Poke);
            for (int t = 0; t < 3; t++)
                for (int i = 0; i < trials[t]; i++)
                    if (rng.NextDouble() < GameConfig.BonusChance) AddBall((BallType)t);
            if (Balls.AreAllAtCap(cap)) break;
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

        if (MissedSlot != spawn.Slot)
        {
            MissedSlot = spawn.Slot;
            MissedCount = 0;
        }

        double effRate = GetEffectiveRate(species.Rate, spawn.Slot);
        if (rng.NextDouble() >= CatchMath.Probability(effRate, BallInfo.Bonus(type)))
        {
            MissedCount++;
            return ThrowOutcome.Missed;
        }

        MissedCount = 0;
        if (!Dex.TryGetValue(spawn.Id, out var e))
            Dex[spawn.Id] = e = new DexEntry { Name = species.Name, Rate = species.Rate };
        e.Count++;
        if (spawn.Shiny) e.Shiny++;
        LastCaptureSlot = spawn.Slot;
        return ThrowOutcome.Caught;
    }

    public bool BuyMystery(int id, string name, int rate, bool shiny)
    {
        if (AvailableDoublons < 10) return false;
        Shop.SpentDoublons += 10;
        if (!Dex.TryGetValue(id, out var e))
            Dex[id] = e = new DexEntry { Name = name, Rate = rate };
        e.Count++;
        if (shiny) e.Shiny++;
        e.Mystery++;
        return true;
    }

    public bool BuyBallCap()
    {
        if (Shop.BallCapUpgrades >= 5 || AvailableDoublons < 5) return false;
        Shop.SpentDoublons += 5;
        Shop.BallCapUpgrades++;
        return true;
    }

    public bool BuyMissCatchRate()
    {
        if (Shop.MissCatchRateUpgrades >= 1 || AvailableDoublons < 100) return false;
        Shop.SpentDoublons += 100;
        Shop.MissCatchRateUpgrades++;
        return true;
    }

    public bool BuyShinyBoost()
    {
        if (Shop.ShinyBoostUpgrades >= 1 || AvailableShinyDoublons < 1) return false;
        Shop.SpentShinyDoublons += 1;
        Shop.ShinyBoostUpgrades++;
        return true;
    }
}
