using CalorieTracker.Data.Sqlite;
using CalorieTracker.Data.Sqlite.Repositories;
using CalorieTracker.Domain.Calculations;
using CalorieTracker.Domain.Enums;
using CalorieTracker.Domain.Models;
using CalorieTracker.Services;

namespace CalorieTracker.Tests;

public class BodyMetricsAndUnitsTests
{
    [Fact]
    public void CalculateBmi_ReturnsExpectedValueAndCategory()
    {
        var bmi = BodyMetricsCalculator.CalculateBmi(weightKg: 75, heightCm: 180);
        Assert.Equal(23.1, bmi);
        Assert.Equal("Healthy", BodyMetricsCalculator.GetBmiCategory(bmi));
    }

    [Fact]
    public void CalculateUsNavyBodyFat_MaleAndFemale_ReturnsRealisticPercentage()
    {
        var maleBf = BodyMetricsCalculator.CalculateUsNavyBodyFat(BiologicalSex.Male, heightCm: 180, neckCm: 38, waistCm: 85);
        Assert.NotNull(maleBf);
        Assert.InRange(maleBf!.Value, 12.0, 20.0);

        var femaleBf = BodyMetricsCalculator.CalculateUsNavyBodyFat(BiologicalSex.Female, heightCm: 165, neckCm: 33, waistCm: 72, hipCm: 96);
        Assert.NotNull(femaleBf);
        Assert.InRange(femaleBf!.Value, 20.0, 32.0);
    }

    [Fact]
    public void BodyMeasurement_PrefersManualBodyFatOverUsNavyEstimate()
    {
        var m = new BodyMeasurement
        {
            ManualBodyFatPercent = 18.4,
            ComputedBodyFatPercent = 16.9
        };
        Assert.Equal(18.4, m.EffectiveBodyFatPercent);
    }

    [Fact]
    public void UnitConverter_ConvertsAndFormatsMetricUkAndUs()
    {
        Assert.Equal("70.0 kg", UnitConverter.FormatWeight(70, UnitSystem.Metric));
        Assert.Equal("11 st 0.3 lb", UnitConverter.FormatWeight(70, UnitSystem.UkImperial));
        Assert.Equal("154.3 lbs", UnitConverter.FormatWeight(70, UnitSystem.UsImperial));
        Assert.Equal("5' 10.9\"", UnitConverter.FormatHeight(180, UnitSystem.UkImperial));
    }
}

public class SqliteRepositoryAndLookupTests
{
    private sealed class FakeOpenFoodFactsClient : IOpenFoodFactsClient
    {
        public int BarcodeCalls { get; private set; }
        public int SearchCalls { get; private set; }

        public Task<FoodItem?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
        {
            BarcodeCalls++;
            if (barcode == "5000159461122")
            {
                return Task.FromResult<FoodItem?>(new FoodItem
                {
                    Name = "Snickers Bar",
                    Brand = "Mars",
                    Barcode = barcode,
                    ExternalId = $"off:{barcode}",
                    Source = FoodSource.OpenFoodFacts,
                    ServingSizeG = 48,
                    ServingDescription = "1 bar (48g)",
                    CaloriesPer100G = 488,
                    ProteinPer100G = 8.6,
                    CarbsPer100G = 60.5,
                    SugarsPer100G = 51.8,
                    FatPer100G = 22.8,
                    SaturatedFatPer100G = 8.3,
                    SaltPer100G = 0.63
                });
            }
            return Task.FromResult<FoodItem?>(null);
        }

        public Task<List<FoodItem>> SearchByNameAsync(string query, int pageSize = 20, CancellationToken ct = default)
        {
            SearchCalls++;
            return Task.FromResult(new List<FoodItem>
            {
                new()
                {
                    Name = "Rolled Oats",
                    Brand = "Quaker",
                    Barcode = "5000108001234",
                    ExternalId = "off:5000108001234",
                    Source = FoodSource.OpenFoodFacts,
                    CaloriesPer100G = 374,
                    ProteinPer100G = 11.0,
                    CarbsPer100G = 60.0,
                    FatPer100G = 8.0
                }
            });
        }
    }

    [Fact]
    public async Task BarcodeLookup_ChecksLocalFirst_CachesApiResult_AndSkipsApiOnSecondScan()
    {
        using var engine = new InMemorySqliteEngine();
        var init = new SqliteSchemaInitializer(engine);
        var foodRepo = new SqliteFoodItemRepository(engine, init);
        var fakeOff = new FakeOpenFoodFactsClient();
        var lookup = new FoodLookupService(foodRepo, fakeOff);

        // First scan: not in local DB -> calls API and saves locally
        var first = await lookup.LookupByBarcodeAsync("5000159461122");
        Assert.NotNull(first.Item);
        Assert.False(first.FoundLocally);
        Assert.Equal(1, fakeOff.BarcodeCalls);

        // Second scan: found in local SQLite -> zero additional API calls!
        var second = await lookup.LookupByBarcodeAsync("5000159461122");
        Assert.NotNull(second.Item);
        Assert.True(second.FoundLocally);
        Assert.Equal(1, fakeOff.BarcodeCalls);
        Assert.Equal(first.Item!.Id, second.Item!.Id);
    }

    [Fact]
    public async Task NameSearch_EnrichesLocalDbFromApi_WithoutDuplicates()
    {
        using var engine = new InMemorySqliteEngine();
        var init = new SqliteSchemaInitializer(engine);
        var foodRepo = new SqliteFoodItemRepository(engine, init);
        var fakeOff = new FakeOpenFoodFactsClient();
        var lookup = new FoodLookupService(foodRepo, fakeOff);

        await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "My Homemade Oats",
            Source = FoodSource.Custom,
            CaloriesPer100G = 350
        });

        var local = await lookup.SearchLocalAsync("Oats");
        Assert.Single(local);

        var merged = await lookup.EnrichFromApiAndMergeAsync("Oats", local);
        Assert.Equal(2, merged.Count);

        // Next local-only search now finds BOTH the custom food and the cached API food!
        var localAfter = await lookup.SearchLocalAsync("Oats");
        Assert.Equal(2, localAfter.Count);
    }

    [Fact]
    public async Task DiaryLogging_AndHistoricalDateTotals_WorkEndToEnd()
    {
        using var engine = new InMemorySqliteEngine();
        var init = new SqliteSchemaInitializer(engine);
        var profileRepo = new SqliteUserProfileRepository(engine, init);
        var slotRepo = new SqliteMealSlotRepository(engine, init);
        var foodRepo = new SqliteFoodItemRepository(engine, init);
        var logRepo = new SqliteFoodLogRepository(engine, init);
        var bodyRepo = new SqliteBodyMeasurementRepository(engine, init);
        var diary = new DiaryService(logRepo, bodyRepo, profileRepo);

        var slots = await slotRepo.GetAllAsync();
        Assert.Equal(4, slots.Count); // Breakfast, Lunch, Dinner, Snacks

        var chicken = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Chicken Breast",
            CaloriesPer100G = 165,
            ProteinPer100G = 31,
            FatPer100G = 3.6
        });

        var pastDate = new DateOnly(2026, 10, 15);
        await diary.LogFoodItemAsync(pastDate, slots[0].Id, chicken, grams: 200);

        var entries = await logRepo.GetByDateAsync(pastDate);
        Assert.Single(entries);
        Assert.Equal(330, entries[0].CaloriesKcal);
        Assert.Equal(62, entries[0].ProteinG);

        var totals = await logRepo.GetDailyCalorieTotalsAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal(330, totals[pastDate]);

        // Edit quantity in place (200g -> 100g) and verify nutrient rescaling
        await diary.UpdateEntryQuantityAsync(entries[0], newGrams: 100, slots[1].Id);
        var updated = await logRepo.GetByDateAsync(pastDate);
        Assert.Equal(165, updated[0].CaloriesKcal);
        Assert.Equal(slots[1].Id, updated[0].MealSlotId);

        // Copy day entries to next day
        var nextDay = pastDate.AddDays(1);
        var copiedCount = await diary.CopyEntriesFromDateAsync(pastDate, nextDay);
        Assert.Equal(1, copiedCount);
        var nextEntries = await logRepo.GetByDateAsync(nextDay);
        Assert.Single(nextEntries);
        Assert.Equal(165, nextEntries[0].CaloriesKcal);

        // Water log persistence
        var waterRepo = new SqliteWaterLogRepository(engine, init);
        await waterRepo.SaveAsync(new WaterLog { Date = pastDate, AmountMl = 1750 });
        var loadedWater = await waterRepo.GetByDateAsync(pastDate);
        Assert.Equal(1750, loadedWater.AmountMl);
    }

    [Fact]
    public void GoalWeightCalculator_ComputesTdeeRecommendedCaloriesAndProjectedDate()
    {
        var bmr = GoalWeightCalculator.CalculateBmr(weightKg: 85, heightCm: 180, ageYears: 30, BiologicalSex.Male);
        Assert.Equal(1830, bmr);

        var tdee = GoalWeightCalculator.CalculateTdee(85, 180, 30, BiologicalSex.Male, ActivityLevel.Moderate);
        Assert.Equal(2836, tdee);

        var rec = GoalWeightCalculator.CalculateRecommendedDailyCalories(tdee!.Value, weeklyWeightChangeKg: -0.5);
        Assert.Equal(2286, rec);

        // Losing 5 kg at a 550 kcal/day deficit = (5 * 7700) / 550 = 70 days
        var start = new DateOnly(2026, 11, 20);
        var proj = GoalWeightCalculator.ProjectGoalDate(start, currentWeightKg: 85, goalWeightKg: 80, tdee: 2836, dailyCalorieIntake: 2286);
        Assert.True(proj.IsMovingTowardGoal);
        Assert.False(proj.AlreadyReached);
        Assert.Equal(70, proj.DaysToGoal);
        Assert.Equal(start.AddDays(70), proj.EstimatedDate);

        // Eating above TDEE when trying to lose -> not moving toward goal
        var stalled = GoalWeightCalculator.ProjectGoalDate(start, 85, 80, tdee: 2836, dailyCalorieIntake: 3000);
        Assert.False(stalled.IsMovingTowardGoal);
        Assert.Null(stalled.EstimatedDate);
    }
}
