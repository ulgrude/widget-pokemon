namespace PokeWidget.Core;

public enum BallType { Poke = 0, Super = 1, Hyper = 2 }

public static class BallInfo
{
    public static double Bonus(BallType t) => t switch { BallType.Poke => 1, BallType.Super => 1.5, _ => 2 };
    public static string Label(BallType t) => t switch { BallType.Poke => "Poké Ball", BallType.Super => "Super Ball", _ => "Hyper Ball" };

    /// <summary>Type de ball offert en bonus selon le taux de capture de l'espèce.</summary>
    public static BallType TierFor(int rate) => rate >= 120 ? BallType.Poke : rate > 45 ? BallType.Super : BallType.Hyper;
}

public static class CatchMath
{
    public static double Probability(int rate, double bonus)
    {
        double a = Math.Min(255, rate * bonus / 3);
        if (a >= 255) return 1;
        double b = 1048560 / Math.Sqrt(Math.Sqrt(16711680 / a));
        return Math.Pow(b / 65536, 4);
    }
}
