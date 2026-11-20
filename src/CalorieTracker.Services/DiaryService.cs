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

public interface IThemeService
{
    Task ApplyThemeAsync(CalorieTracker.Domain.Enums.ThemeMode theme, CalorieTracker.Domain.Enums.ColorScheme scheme);
}

public sealed record AppUpdateCheckResult(bool UpdateAvailable, bool IsSupported, string Message);

public interface IAppUpdateService
{
    event Action? UpdateAvailableChanged;
    bool IsUpdateAvailable { get; }
    Task StartWatchingAsync();
    Task<AppUpdateCheckResult> CheckForUpdateAsync();
    Task ApplyUpdateAsync();
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

    public async Task<FoodLogEntry> UpdateEntryQuantityAsync(
        FoodLogEntry entry,
        double newGrams,
        Guid newMealSlotId,
        CancellationToken ct = default)
    {
        if (newGrams <= 0) return entry;

        var ratio = entry.QuantityGrams > 0 ? newGrams / entry.QuantityGrams : 1.0;
        var scaled = entry.Nutrients.Scale(ratio);

        entry.MealSlotId = newMealSlotId;
        entry.QuantityGrams = Math.Round(newGrams, 1);
        entry.Servings = Math.Round(entry.Servings * ratio, 2);
        entry.ServingLabel = $"{entry.QuantityGrams:0.#}g";
        entry.CaloriesKcal = scaled.CaloriesKcal;
        entry.FatG = scaled.FatG;
        entry.SaturatedFatG = scaled.SaturatedFatG;
        entry.CarbsG = scaled.CarbsG;
        entry.SugarsG = scaled.SugarsG;
        entry.FibreG = scaled.FibreG;
        entry.ProteinG = scaled.ProteinG;
        entry.SaltG = scaled.SaltG;

        await _logRepo.SaveAsync(entry, ct);
        return entry;
    }

    public async Task<int> CopyEntriesFromDateAsync(
        DateOnly sourceDate,
        DateOnly targetDate,
        Guid? onlySlotId = null,
        CancellationToken ct = default)
    {
        var sourceEntries = await _logRepo.GetByDateAsync(sourceDate, ct);
        if (onlySlotId.HasValue)
            sourceEntries = sourceEntries.Where(e => e.MealSlotId == onlySlotId.Value).ToList();

        foreach (var src in sourceEntries)
        {
            var copy = new FoodLogEntry
            {
                Id = Guid.NewGuid(),
                Date = targetDate,
                MealSlotId = src.MealSlotId,
                FoodItemId = src.FoodItemId,
                MealId = src.MealId,
                NameSnapshot = src.NameSnapshot,
                BrandSnapshot = src.BrandSnapshot,
                QuantityGrams = src.QuantityGrams,
                Servings = src.Servings,
                ServingLabel = src.ServingLabel,
                CaloriesKcal = src.CaloriesKcal,
                FatG = src.FatG,
                SaturatedFatG = src.SaturatedFatG,
                CarbsG = src.CarbsG,
                SugarsG = src.SugarsG,
                FibreG = src.FibreG,
                ProteinG = src.ProteinG,
                SaltG = src.SaltG,
                LoggedAtUtc = DateTime.UtcNow
            };
            await _logRepo.SaveAsync(copy, ct);
        }

        return sourceEntries.Count;
    }
}
