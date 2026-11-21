using CalorieTracker.Domain.Enums;

namespace CalorieTracker.Domain.Models;

public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double DailyCalorieGoal { get; set; } = 2000;
    public double ProteinGoalG { get; set; } = 120;
    public double CarbsGoalG { get; set; } = 225;
    public double FatGoalG { get; set; } = 65;
    public double SaturatedFatGoalG { get; set; } = 20;
    public double SugarGoalG { get; set; } = 45;
    public double SaltGoalG { get; set; } = 6;
    public double FibreGoalG { get; set; } = 30;
    public double? HeightCm { get; set; }
    public int? AgeYears { get; set; } = 30;
    public BiologicalSex BiologicalSex { get; set; } = BiologicalSex.Unspecified;
    public ActivityLevel ActivityLevel { get; set; } = ActivityLevel.Moderate;
    public double? GoalWeightKg { get; set; }
    public double WeeklyWeightChangeKg { get; set; } = -0.5;
    public int DailyWaterGoalMl { get; set; } = 2000;
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Light;
    public ColorScheme ColorScheme { get; set; } = ColorScheme.RoyalBlue;
    public UnitSystem UnitSystem { get; set; } = UnitSystem.Metric;
    public bool PinCalorieSummary { get; set; } = true;
    public CalorieSummaryStyle SummaryStyle { get; set; } = CalorieSummaryStyle.Standard;
    public bool ShowFoodsInBottomNav { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WaterLog
{
    public DateOnly Date { get; set; }
    public int AmountMl { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MealSlot
{
    public static readonly Guid WaterSlotId = Guid.Parse("11111111-1111-1111-1111-111111111105");

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🍽️";
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsWaterTracker => Id == WaterSlotId;
}

public sealed class FoodItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Barcode { get; set; }
    public string? ExternalId { get; set; }
    public FoodSource Source { get; set; } = FoodSource.Custom;
    public double ServingSizeG { get; set; } = 100;
    public string ServingDescription { get; set; } = "100g";
    public string MeasurementUnit { get; set; } = "g";

    public string UnitLabel => NormalizeUnit(MeasurementUnit);

    public static string NormalizeUnit(string? unit) =>
        string.Equals(unit?.Trim(), "ml", StringComparison.OrdinalIgnoreCase) ? "ml" : "g";

    // Nutrients per 100g
    public double CaloriesPer100G { get; set; }
    public double FatPer100G { get; set; }
    public double SaturatedFatPer100G { get; set; }
    public double CarbsPer100G { get; set; }
    public double SugarsPer100G { get; set; }
    public double FibrePer100G { get; set; }
    public double ProteinPer100G { get; set; }
    public double SaltPer100G { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public NutrientProfile Per100G => new()
    {
        CaloriesKcal = CaloriesPer100G,
        FatG = FatPer100G,
        SaturatedFatG = SaturatedFatPer100G,
        CarbsG = CarbsPer100G,
        SugarsG = SugarsPer100G,
        FibreG = FibrePer100G,
        ProteinG = ProteinPer100G,
        SaltG = SaltPer100G
    };

    public NutrientProfile NutrientsForGrams(double grams) => Per100G.Scale(grams / 100.0);
}

public sealed class Meal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public double DefaultServings { get; set; } = 1;
    public List<MealIngredient> Ingredients { get; set; } = new();
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public NutrientProfile TotalNutrients
    {
        get
        {
            var total = NutrientProfile.Zero;
            foreach (var ingredient in Ingredients)
            {
                total = total.Add(ingredient.Nutrients);
            }
            return total;
        }
    }

    public NutrientProfile PerServingNutrients =>
        DefaultServings > 0 ? TotalNutrients.Scale(1.0 / DefaultServings) : TotalNutrients;
}

public sealed class MealIngredient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MealId { get; set; }
    public Guid FoodItemId { get; set; }
    public string FoodNameSnapshot { get; set; } = string.Empty;
    public double QuantityGrams { get; set; }

    public double CaloriesKcal { get; set; }
    public double FatG { get; set; }
    public double SaturatedFatG { get; set; }
    public double CarbsG { get; set; }
    public double SugarsG { get; set; }
    public double FibreG { get; set; }
    public double ProteinG { get; set; }
    public double SaltG { get; set; }

    public NutrientProfile Nutrients => new()
    {
        CaloriesKcal = CaloriesKcal,
        FatG = FatG,
        SaturatedFatG = SaturatedFatG,
        CarbsG = CarbsG,
        SugarsG = SugarsG,
        FibreG = FibreG,
        ProteinG = ProteinG,
        SaltG = SaltG
    };
}

public sealed class FoodLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public Guid MealSlotId { get; set; }
    public Guid? FoodItemId { get; set; }
    public Guid? MealId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public string? BrandSnapshot { get; set; }
    public double QuantityGrams { get; set; }
    public double Servings { get; set; } = 1;
    public string ServingLabel { get; set; } = "100g";
    public string MeasurementUnit { get; set; } = "g";

    public string UnitLabel => FoodItem.NormalizeUnit(MeasurementUnit);

    public double CaloriesKcal { get; set; }
    public double FatG { get; set; }
    public double SaturatedFatG { get; set; }
    public double CarbsG { get; set; }
    public double SugarsG { get; set; }
    public double FibreG { get; set; }
    public double ProteinG { get; set; }
    public double SaltG { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime LoggedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public NutrientProfile Nutrients => new()
    {
        CaloriesKcal = CaloriesKcal,
        FatG = FatG,
        SaturatedFatG = SaturatedFatG,
        CarbsG = CarbsG,
        SugarsG = SugarsG,
        FibreG = FibreG,
        ProteinG = ProteinG,
        SaltG = SaltG
    };
}

public sealed class BodyMeasurement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public double? WeightKg { get; set; }
    public double? HeightCm { get; set; }
    public double? NeckCm { get; set; }
    public double? WaistCm { get; set; }
    public double? HipCm { get; set; }
    public double? ManualBodyFatPercent { get; set; }
    public double? ComputedBmi { get; set; }
    public double? ComputedBodyFatPercent { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public double? EffectiveBodyFatPercent => ManualBodyFatPercent ?? ComputedBodyFatPercent;
}

public sealed record SavedFoodPortion(double Quantity, string Unit);
