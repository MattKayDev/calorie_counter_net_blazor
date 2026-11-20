namespace CalorieTracker.Domain.Models;

public sealed record NutrientProfile
{
    public double CaloriesKcal { get; init; }
    public double FatG { get; init; }
    public double SaturatedFatG { get; init; }
    public double CarbsG { get; init; }
    public double SugarsG { get; init; }
    public double FibreG { get; init; }
    public double ProteinG { get; init; }
    public double SaltG { get; init; }

    public static NutrientProfile Zero => new();

    public NutrientProfile Scale(double factor) => new()
    {
        CaloriesKcal = Math.Round(CaloriesKcal * factor, 1),
        FatG = Math.Round(FatG * factor, 2),
        SaturatedFatG = Math.Round(SaturatedFatG * factor, 2),
        CarbsG = Math.Round(CarbsG * factor, 2),
        SugarsG = Math.Round(SugarsG * factor, 2),
        FibreG = Math.Round(FibreG * factor, 2),
        ProteinG = Math.Round(ProteinG * factor, 2),
        SaltG = Math.Round(SaltG * factor, 3)
    };

    public NutrientProfile Add(NutrientProfile other) => new()
    {
        CaloriesKcal = Math.Round(CaloriesKcal + other.CaloriesKcal, 1),
        FatG = Math.Round(FatG + other.FatG, 2),
        SaturatedFatG = Math.Round(SaturatedFatG + other.SaturatedFatG, 2),
        CarbsG = Math.Round(CarbsG + other.CarbsG, 2),
        SugarsG = Math.Round(SugarsG + other.SugarsG, 2),
        FibreG = Math.Round(FibreG + other.FibreG, 2),
        ProteinG = Math.Round(ProteinG + other.ProteinG, 2),
        SaltG = Math.Round(SaltG + other.SaltG, 3)
    };
}
