using CalorieTracker.Domain.Calculations;
using CalorieTracker.Domain.Models;
using CalorieTracker.Domain.Repositories;

namespace CalorieTracker.Services;

public interface IBarcodeScannerService
{
    Task StartAsync(string containerElementId, Func<string, Task> onBarcodeDetected);
    Task StopAsync();
}

public interface IFileDownloadService
{
    Task DownloadBytesAsync(string fileName, string contentType, byte[] bytes);
}

public sealed class DiaryService
{
    private readonly IFoodLogRepository _logRepo;
    private readonly IBodyMeasurementRepository _bodyRepo;
    private readonly IUserProfileRepository _profileRepo;

    public DiaryService(
        IFoodLogRepository logRepo,
        IBodyMeasurementRepository bodyRepo,
        IUserProfileRepository profileRepo)
    {
        _logRepo = logRepo;
        _bodyRepo = bodyRepo;
        _profileRepo = profileRepo;
    }

    public static NutrientProfile SumNutrients(IEnumerable<FoodLogEntry> entries)
    {
        var total = NutrientProfile.Zero;
        foreach (var entry in entries)
        {
            total = total.Add(entry.Nutrients);
        }
        return total;
    }

    public async Task<FoodLogEntry> LogFoodItemAsync(
        DateOnly date,
        Guid mealSlotId,
        FoodItem item,
        double grams,
        string? servingLabel = null,
        CancellationToken ct = default)
    {
        var nutrients = item.NutrientsForGrams(grams);
        var entry = new FoodLogEntry
        {
            Id = Guid.NewGuid(),
            Date = date,
            MealSlotId = mealSlotId,
            FoodItemId = item.Id,
            NameSnapshot = item.Name,
            BrandSnapshot = item.Brand,
            QuantityGrams = Math.Round(grams, 1),
            Servings = item.ServingSizeG > 0 ? Math.Round(grams / item.ServingSizeG, 2) : 1,
            ServingLabel = servingLabel ?? $"{grams:0.#}g",
            CaloriesKcal = nutrients.CaloriesKcal,
            FatG = nutrients.FatG,
            SaturatedFatG = nutrients.SaturatedFatG,
            CarbsG = nutrients.CarbsG,
            SugarsG = nutrients.SugarsG,
            FibreG = nutrients.FibreG,
            ProteinG = nutrients.ProteinG,
            SaltG = nutrients.SaltG,
            LoggedAtUtc = DateTime.UtcNow
        };

        await _logRepo.SaveAsync(entry, ct);
        return entry;
    }

    public async Task<FoodLogEntry> LogMealAsync(
        DateOnly date,
        Guid mealSlotId,
        Meal meal,
        double servings = 1,
        CancellationToken ct = default)
    {
        var perServing = meal.PerServingNutrients;
        var nutrients = perServing.Scale(servings);
        var totalGrams = meal.Ingredients.Sum(i => i.QuantityGrams) / (meal.DefaultServings > 0 ? meal.DefaultServings : 1) * servings;

        var entry = new FoodLogEntry
        {
            Id = Guid.NewGuid(),
            Date = date,
            MealSlotId = mealSlotId,
            MealId = meal.Id,
            NameSnapshot = meal.Name,
            BrandSnapshot = "Saved Meal",
            QuantityGrams = Math.Round(totalGrams, 1),
            Servings = Math.Round(servings, 2),
            ServingLabel = servings == 1 ? "1 serving" : $"{servings:0.##} servings",
            CaloriesKcal = nutrients.CaloriesKcal,
            FatG = nutrients.FatG,
            SaturatedFatG = nutrients.SaturatedFatG,
            CarbsG = nutrients.CarbsG,
            SugarsG = nutrients.SugarsG,
            FibreG = nutrients.FibreG,
            ProteinG = nutrients.ProteinG,
            SaltG = nutrients.SaltG,
            LoggedAtUtc = DateTime.UtcNow
        };

        await _logRepo.SaveAsync(entry, ct);
        return entry;
    }

    public async Task<BodyMeasurement> SaveBodyMeasurementAsync(BodyMeasurement measurement, CancellationToken ct = default)
    {
        var profile = await _profileRepo.GetOrCreateAsync(ct);

        var effectiveHeight = measurement.HeightCm ?? profile.HeightCm;
        measurement.HeightCm = effectiveHeight;
        measurement.ComputedBmi = BodyMetricsCalculator.CalculateBmi(measurement.WeightKg, effectiveHeight);
        measurement.ComputedBodyFatPercent = BodyMetricsCalculator.CalculateUsNavyBodyFat(
            profile.BiologicalSex,
            effectiveHeight,
            measurement.NeckCm,
            measurement.WaistCm,
            measurement.HipCm);

        if (measurement.HeightCm.HasValue && measurement.HeightCm != profile.HeightCm)
        {
            profile.HeightCm = measurement.HeightCm;
            await _profileRepo.SaveAsync(profile, ct);
        }

        await _bodyRepo.SaveAsync(measurement, ct);
        return measurement;
    }
}
