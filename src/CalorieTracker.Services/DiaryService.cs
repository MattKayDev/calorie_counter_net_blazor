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

public sealed class AppLayoutState
{
    public bool ShowFoodsInBottomNav { get; private set; } = true;
    public event Action? Changed;

    public void SetShowFoodsInBottomNav(bool show)
    {
        if (ShowFoodsInBottomNav == show) return;
        ShowFoodsInBottomNav = show;
        Changed?.Invoke();
    }
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
        string? unit = null,
        double? servings = null,
        CancellationToken ct = default)
    {
        var normalizedUnit = FoodItem.NormalizeUnit(unit ?? item.MeasurementUnit);
        var roundedGrams = Math.Round(grams, 1);
        var nutrients = item.NutrientsForGrams(grams);
        var entry = new FoodLogEntry
        {
            Id = Guid.NewGuid(),
            Date = date,
            MealSlotId = mealSlotId,
            FoodItemId = item.Id,
            NameSnapshot = item.Name,
            BrandSnapshot = item.Brand,
            QuantityGrams = roundedGrams,
            Servings = servings is > 0 ? Math.Round(servings.Value, 2) : (item.ServingSizeG > 0 ? Math.Round(grams / item.ServingSizeG, 2) : 1),
            ServingLabel = servingLabel ?? $"{roundedGrams:0.#}{normalizedUnit}",
            MeasurementUnit = normalizedUnit,
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
        DateOnly? newDate = null,
        string? newUnit = null,
        CancellationToken ct = default)
    {
        if (newGrams <= 0) return entry;

        if (newDate.HasValue)
            entry.Date = newDate.Value;
        entry.MealSlotId = newMealSlotId;

        ApplyScaledQuantity(entry, entry, newGrams, newUnit);

        await _logRepo.SaveAsync(entry, ct);
        return entry;
    }

    public async Task<FoodLogEntry> DuplicateEntryAsync(
        FoodLogEntry source,
        DateOnly targetDate,
        Guid targetMealSlotId,
        double targetGrams,
        string? targetUnit = null,
        CancellationToken ct = default)
    {
        var effectiveGrams = targetGrams > 0 ? targetGrams : (source.QuantityGrams > 0 ? source.QuantityGrams : 100);
        var copy = new FoodLogEntry
        {
            Id = Guid.NewGuid(),
            Date = targetDate,
            MealSlotId = targetMealSlotId,
            FoodItemId = source.FoodItemId,
            MealId = source.MealId,
            NameSnapshot = source.NameSnapshot,
            BrandSnapshot = source.BrandSnapshot,
            LoggedAtUtc = DateTime.UtcNow
        };

        ApplyScaledQuantity(copy, source, effectiveGrams, targetUnit);

        await _logRepo.SaveAsync(copy, ct);
        return copy;
    }

    private static void ApplyScaledQuantity(FoodLogEntry target, FoodLogEntry source, double targetGrams, string? targetUnit)
    {
        var normalizedUnit = FoodItem.NormalizeUnit(targetUnit ?? source.MeasurementUnit);
        var unitChanged = !string.Equals(source.UnitLabel, normalizedUnit, StringComparison.OrdinalIgnoreCase);
        var quantityChanged = source.QuantityGrams > 0 && Math.Abs(targetGrams - source.QuantityGrams) >= 0.05;
        var ratio = quantityChanged ? targetGrams / source.QuantityGrams : 1.0;
        var scaled = quantityChanged ? source.Nutrients.Scale(ratio) : source.Nutrients;
        var roundedGrams = Math.Round(targetGrams, 1);

        target.QuantityGrams = roundedGrams;
        target.Servings = quantityChanged ? Math.Round(source.Servings * ratio, 2) : source.Servings;
        target.MeasurementUnit = normalizedUnit;
        target.ServingLabel = (quantityChanged || unitChanged) ? $"{roundedGrams:0.#}{normalizedUnit}" : source.ServingLabel;
        target.CaloriesKcal = scaled.CaloriesKcal;
        target.FatG = scaled.FatG;
        target.SaturatedFatG = scaled.SaturatedFatG;
        target.CarbsG = scaled.CarbsG;
        target.SugarsG = scaled.SugarsG;
        target.FibreG = scaled.FibreG;
        target.ProteinG = scaled.ProteinG;
        target.SaltG = scaled.SaltG;
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
                MeasurementUnit = src.UnitLabel,
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
