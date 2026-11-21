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

    [Fact]
    public void UnitConverter_FormatsStorageSizesAcrossBytesKbMbAndGb()
    {
        Assert.Equal("—", UnitConverter.FormatStorageSize(null));
        Assert.Equal("512 B", UnitConverter.FormatStorageSize(512));
        Assert.Equal("36.0 KB", UnitConverter.FormatStorageSize(36 * 1024L));
        Assert.Equal("1.50 MB", UnitConverter.FormatStorageSize((long)(1.5 * 1024 * 1024)));
        Assert.Equal("2.50 GB", UnitConverter.FormatStorageSize((long)(2.5 * 1024 * 1024 * 1024)));
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
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);
        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);
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
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);
        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);
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
    public async Task DiaryLogging_AndHistoricalDateTotals_WorkEndToEnd_WithStrictDbSeparation()
    {
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();
        var personalInit = new PersonalSqliteSchemaInitializer(personalEngine);
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);

        var profileRepo = new SqliteUserProfileRepository(personalEngine, personalInit);
        var slotRepo = new SqliteMealSlotRepository(personalEngine, personalInit);
        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);
        var logRepo = new SqliteFoodLogRepository(personalEngine, personalInit);
        var bodyRepo = new SqliteBodyMeasurementRepository(personalEngine, personalInit);
        var waterRepo = new SqliteWaterLogRepository(personalEngine, personalInit);
        var diary = new DiaryService(logRepo, bodyRepo, profileRepo);

        var slots = await slotRepo.GetAllAsync();
        Assert.Equal(5, slots.Count); // Water + Breakfast, Lunch, Dinner, Snacks
        Assert.True(slots[0].IsWaterTracker);
        var foodSlots = slots.Where(s => !s.IsWaterTracker).ToList();
        Assert.Equal(4, foodSlots.Count);

        var chicken = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Chicken Breast",
            CaloriesPer100G = 165,
            ProteinPer100G = 31,
            FatPer100G = 3.6
        });

        var pastDate = new DateOnly(2026, 10, 15);
        await diary.LogFoodItemAsync(pastDate, foodSlots[0].Id, chicken, grams: 200);

        var entries = await logRepo.GetByDateAsync(pastDate);
        Assert.Single(entries);
        Assert.Equal(330, entries[0].CaloriesKcal);
        Assert.Equal(62, entries[0].ProteinG);

        var totals = await logRepo.GetDailyCalorieTotalsAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal(330, totals[pastDate]);

        // Edit quantity in place (200g -> 100g) and verify nutrient rescaling
        await diary.UpdateEntryQuantityAsync(entries[0], newGrams: 100, foodSlots[1].Id);
        var updated = await logRepo.GetByDateAsync(pastDate);
        Assert.Equal(165, updated[0].CaloriesKcal);
        Assert.Equal(foodSlots[1].Id, updated[0].MealSlotId);

        // Copy day entries to next day
        var nextDay = pastDate.AddDays(1);
        var copiedCount = await diary.CopyEntriesFromDateAsync(pastDate, nextDay);
        Assert.Equal(1, copiedCount);
        var nextEntries = await logRepo.GetByDateAsync(nextDay);
        Assert.Single(nextEntries);
        Assert.Equal(165, nextEntries[0].CaloriesKcal);

        // Water log persistence
        await waterRepo.SaveAsync(new WaterLog { Date = pastDate, AmountMl = 1750 });
        var loadedWater = await waterRepo.GetByDateAsync(pastDate);
        Assert.Equal(1750, loadedWater.AmountMl);

        // Water slot can be reordered below all food slots, disabled, stay disabled across re-init, and re-enabled
        var waterSlot = slots.First(s => s.IsWaterTracker);
        waterSlot.SortOrder = 99;
        await slotRepo.SaveAsync(waterSlot);
        var reordered = await slotRepo.GetAllAsync();
        Assert.True(reordered[^1].IsWaterTracker);

        await slotRepo.DeleteOrArchiveAsync(MealSlot.WaterSlotId);
        var afterDisable = await slotRepo.GetAllAsync();
        Assert.Equal(4, afterDisable.Count);
        Assert.DoesNotContain(afterDisable, s => s.IsWaterTracker);

        // Re-running schema initialization must keep Water disabled when archived
        personalInit.ResetInitializationState();
        await personalInit.EnsureCreatedAsync();
        var afterReinit = await slotRepo.GetAllAsync();
        Assert.DoesNotContain(afterReinit, s => s.IsWaterTracker);

        // Re-enable Water slot and verify past water logs stay intact
        var archivedWater = (await slotRepo.GetAllAsync(includeArchived: true)).First(s => s.IsWaterTracker);
        archivedWater.IsArchived = false;
        await slotRepo.SaveAsync(archivedWater);
        Assert.Contains(await slotRepo.GetAllAsync(), s => s.IsWaterTracker);
        Assert.Equal(1750, (await waterRepo.GetByDateAsync(pastDate)).AmountMl);

        // Verify strict separation: foods.sqlite has NO personal tables, personal.sqlite has NO FoodItems table
        var foodDbTables = await foodsEngine.QueryAsync("SELECT name FROM sqlite_master WHERE type='table';");
        var foodTableNames = foodDbTables.Select(r => r["name"].GetString()).ToList();
        Assert.Contains("FoodItems", foodTableNames);
        Assert.DoesNotContain("FoodLogEntries", foodTableNames);
        Assert.DoesNotContain("UserProfiles", foodTableNames);
        Assert.DoesNotContain("BodyMeasurements", foodTableNames);

        var personalDbTables = await personalEngine.QueryAsync("SELECT name FROM sqlite_master WHERE type='table';");
        var personalTableNames = personalDbTables.Select(r => r["name"].GetString()).ToList();
        Assert.Contains("FoodLogEntries", personalTableNames);
        Assert.Contains("UserProfiles", personalTableNames);
        Assert.DoesNotContain("FoodItems", personalTableNames);

        // Verify PinCalorieSummary, SummaryStyle (Standard/Compact/Minimal), and ShowFoodsInBottomNav defaults and persistence
        var profile = await profileRepo.GetOrCreateAsync();
        Assert.True(profile.PinCalorieSummary);
        Assert.Equal(CalorieSummaryStyle.Standard, profile.SummaryStyle);
        Assert.True(profile.ShowFoodsInBottomNav);

        profile.PinCalorieSummary = false;
        profile.SummaryStyle = CalorieSummaryStyle.Compact;
        profile.ShowFoodsInBottomNav = false;
        await profileRepo.SaveAsync(profile);

        var reloadedCompact = await profileRepo.GetOrCreateAsync();
        Assert.False(reloadedCompact.PinCalorieSummary);
        Assert.Equal(CalorieSummaryStyle.Compact, reloadedCompact.SummaryStyle);
        Assert.False(reloadedCompact.ShowFoodsInBottomNav);

        profile.SummaryStyle = CalorieSummaryStyle.Minimal;
        await profileRepo.SaveAsync(profile);
        Assert.Equal(CalorieSummaryStyle.Minimal, (await profileRepo.GetOrCreateAsync()).SummaryStyle);
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

    [Fact]
    public void GoalWeightCalculator_AlignsWeeklyPaceDirectionToGoalWeight()
    {
        // Losing goal flips positive/zero pace to loss, keeps existing loss pace
        Assert.Equal(-0.25, GoalWeightCalculator.AlignWeeklyPaceToGoal(85, 78, 0.25));
        Assert.Equal(-0.5, GoalWeightCalculator.AlignWeeklyPaceToGoal(85, 78, 0.0));
        Assert.Equal(-0.75, GoalWeightCalculator.AlignWeeklyPaceToGoal(85, 78, -0.75));

        // Gaining goal flips negative/zero pace to gain (capped at +0.5), keeps existing gain pace
        Assert.Equal(0.5, GoalWeightCalculator.AlignWeeklyPaceToGoal(70, 78, -1.0));
        Assert.Equal(0.25, GoalWeightCalculator.AlignWeeklyPaceToGoal(70, 78, -0.25));
        Assert.Equal(0.25, GoalWeightCalculator.AlignWeeklyPaceToGoal(70, 78, 0.0));
        Assert.Equal(0.5, GoalWeightCalculator.AlignWeeklyPaceToGoal(70, 78, 0.5));

        // Equal goal resets pace to maintain (0.0)
        Assert.Equal(0.0, GoalWeightCalculator.AlignWeeklyPaceToGoal(80, 80, -0.5));
    }

    [Fact]
    public void UsdaParser_ExtractsNutrientsAndConvertsSodiumToSalt()
    {
        const string json = """
            {
              "foods": [
                {
                  "fdcId": 2021609,
                  "description": "SUGAR",
                  "brandName": "WEIS",
                  "gtinUpc": "041493647402",
                  "servingSize": 4.0,
                  "householdServingFullText": "1 Tbsp",
                  "foodNutrients": [
                    { "nutrientId": 1008, "nutrientNumber": "208", "unitName": "KCAL", "value": 375 },
                    { "nutrientId": 1005, "nutrientNumber": "205", "unitName": "G", "value": 100 },
                    { "nutrientId": 2000, "nutrientNumber": "269", "unitName": "G", "value": 100 },
                    { "nutrientId": 1093, "nutrientNumber": "307", "unitName": "MG", "value": 400 }
                  ]
                }
              ]
            }
            """;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var items = OpenFoodFactsClient.ParseUsdaResponse(doc.RootElement);

        Assert.Single(items);
        Assert.Equal("Sugar", items[0].Name);
        Assert.Equal("WEIS", items[0].Brand);
        Assert.Equal("041493647402", items[0].Barcode);
        Assert.Equal(375, items[0].CaloriesPer100G);
        Assert.Equal(100, items[0].CarbsPer100G);
        Assert.Equal(100, items[0].SugarsPer100G);
        Assert.Equal(1.0, items[0].SaltPer100G); // 400mg sodium = 0.4g * 2.5 = 1.0g salt
    }

    [Fact]
    public async Task BackupService_ReportsPositiveDatabaseSizesForBothDatabases()
    {
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();
        var personalInit = new PersonalSqliteSchemaInitializer(personalEngine);
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);
        var backupService = new SqliteBackupService(personalEngine, personalInit, foodsEngine, foodsInit);
        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);

        var initialPersonalBytes = await backupService.GetPersonalDatabaseSizeBytesAsync();
        var initialFoodsBytes = await backupService.GetFoodsDatabaseSizeBytesAsync();

        Assert.True(initialPersonalBytes > 0);
        Assert.True(initialFoodsBytes > 0);

        for (var i = 0; i < 40; i++)
        {
            await foodRepo.UpsertAsync(new FoodItem
            {
                Name = $"Sample Food Item {i} with long description text",
                Brand = "Brand Name",
                Barcode = $"0000000000{i:000}",
                Source = FoodSource.Custom,
                CaloriesPer100G = 250 + i
            });
        }

        var expandedFoodsBytes = await backupService.GetFoodsDatabaseSizeBytesAsync();
        Assert.True(expandedFoodsBytes >= initialFoodsBytes);
    }

    [Fact]
    public async Task DuplicateEntry_CreatesIndependentEntryOnSameOrDifferentMealAndDate()
    {
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();
        var personalInit = new PersonalSqliteSchemaInitializer(personalEngine);
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);

        var profileRepo = new SqliteUserProfileRepository(personalEngine, personalInit);
        var slotRepo = new SqliteMealSlotRepository(personalEngine, personalInit);
        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);
        var logRepo = new SqliteFoodLogRepository(personalEngine, personalInit);
        var bodyRepo = new SqliteBodyMeasurementRepository(personalEngine, personalInit);
        var diary = new DiaryService(logRepo, bodyRepo, profileRepo);

        var foodSlots = (await slotRepo.GetAllAsync()).Where(s => !s.IsWaterTracker).ToList();
        var chicken = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Chicken Breast",
            CaloriesPer100G = 165,
            ProteinPer100G = 31,
            FatPer100G = 3.6
        });

        var day1 = new DateOnly(2026, 11, 20);
        var day2 = day1.AddDays(1);
        var original = await diary.LogFoodItemAsync(day1, foodSlots[0].Id, chicken, grams: 200, servingLabel: "1 large fillet");

        // Duplicate to same day & same slot with unchanged quantity (preserves label, creates distinct Id)
        var sameSlotDup = await diary.DuplicateEntryAsync(original, day1, foodSlots[0].Id, 200);
        Assert.NotEqual(original.Id, sameSlotDup.Id);
        Assert.Equal("1 large fillet", sameSlotDup.ServingLabel);
        Assert.Equal(330, sameSlotDup.CaloriesKcal);

        var day1Entries = await logRepo.GetByDateAsync(day1);
        Assert.Equal(2, day1Entries.Count);

        // Duplicate to a different date, meal slot, and adjusted quantity (200g -> 150g)
        var otherDayDup = await diary.DuplicateEntryAsync(original, day2, foodSlots[2].Id, 150);
        Assert.NotEqual(original.Id, otherDayDup.Id);
        Assert.Equal(day2, otherDayDup.Date);
        Assert.Equal(foodSlots[2].Id, otherDayDup.MealSlotId);
        Assert.Equal(150, otherDayDup.QuantityGrams);
        Assert.Equal("150g", otherDayDup.ServingLabel);
        Assert.Equal(247.5, otherDayDup.CaloriesKcal);
        Assert.Equal(46.5, otherDayDup.ProteinG);

        // Original remains untouched
        var reloadedOriginal = (await logRepo.GetByDateAsync(day1)).Single(e => e.Id == original.Id);
        Assert.Equal(200, reloadedOriginal.QuantityGrams);
        Assert.Equal("1 large fillet", reloadedOriginal.ServingLabel);

        // Edit in place can move an entry to another day while preserving its ServingLabel when grams unchanged
        await diary.UpdateEntryQuantityAsync(sameSlotDup, newGrams: 200, foodSlots[1].Id, newDate: day2);
        Assert.Single(await logRepo.GetByDateAsync(day1));
        var day2Entries = await logRepo.GetByDateAsync(day2);
        Assert.Equal(2, day2Entries.Count);
        var moved = day2Entries.Single(e => e.Id == sameSlotDup.Id);
        Assert.Equal(foodSlots[1].Id, moved.MealSlotId);
        Assert.Equal("1 large fillet", moved.ServingLabel);
    }

    [Fact]
    public async Task FoodLogging_RemembersCustomPortionsAndSupportsGramsAndMlUnits()
    {
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();
        var personalInit = new PersonalSqliteSchemaInitializer(personalEngine);
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);

        var profileRepo = new SqliteUserProfileRepository(personalEngine, personalInit);
        var slotRepo = new SqliteMealSlotRepository(personalEngine, personalInit);
        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);
        var logRepo = new SqliteFoodLogRepository(personalEngine, personalInit);
        var bodyRepo = new SqliteBodyMeasurementRepository(personalEngine, personalInit);
        var diary = new DiaryService(logRepo, bodyRepo, profileRepo);

        var foodSlots = (await slotRepo.GetAllAsync()).Where(s => !s.IsWaterTracker).ToList();
        var coffee = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Black Coffee",
            MeasurementUnit = "ml",
            ServingSizeG = 237,
            ServingDescription = "1 cup (237ml)",
            CaloriesPer100G = 2
        });

        var loadedCoffee = await foodRepo.GetByIdAsync(coffee.Id);
        Assert.NotNull(loadedCoffee);
        Assert.Equal("ml", loadedCoffee!.UnitLabel);

        var day = new DateOnly(2026, 11, 21);

        // Log custom 237ml serving
        var first = await diary.LogFoodItemAsync(day, foodSlots[0].Id, coffee, grams: 237, unit: "ml");
        Assert.Equal("ml", first.MeasurementUnit);
        Assert.Equal("237ml", first.ServingLabel);

        var portions1 = await logRepo.GetRecentPortionsForFoodAsync(coffee.Id);
        Assert.Single(portions1);
        Assert.Equal(new SavedFoodPortion(237, "ml"), portions1[0]);

        // Log 350ml, then 237ml again -> deduplicated, most recently used first
        await Task.Delay(5);
        await diary.LogFoodItemAsync(day, foodSlots[0].Id, coffee, grams: 350, unit: "ml");
        await Task.Delay(5);
        await diary.LogFoodItemAsync(day, foodSlots[0].Id, coffee, grams: 237, unit: "ml");

        var portions2 = await logRepo.GetRecentPortionsForFoodAsync(coffee.Id);
        Assert.Equal(2, portions2.Count);
        Assert.Equal(new SavedFoodPortion(237, "ml"), portions2[0]);
        Assert.Equal(new SavedFoodPortion(350, "ml"), portions2[1]);

        // Editing unit from ml -> g updates ServingLabel and MeasurementUnit
        await diary.UpdateEntryQuantityAsync(first, newGrams: 250, foodSlots[0].Id, newUnit: "g");
        var updatedFirst = (await logRepo.GetByDateAsync(day)).Single(e => e.Id == first.Id);
        Assert.Equal("g", updatedFirst.MeasurementUnit);
        Assert.Equal("250g", updatedFirst.ServingLabel);

        // Duplicating with custom unit preserves target unit
        var duplicatedMl = await diary.DuplicateEntryAsync(updatedFirst, day, foodSlots[1].Id, targetGrams: 200, targetUnit: "ml");
        Assert.Equal("ml", duplicatedMl.MeasurementUnit);
        Assert.Equal("200ml", duplicatedMl.ServingLabel);

        // Logging multiple servings (e.g. 5 × 48g bar) stores explicit servings, total grams, descriptive label, and 5x nutrients
        var bar = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Oat Bar",
            ServingSizeG = 48,
            CaloriesPer100G = 400,
            ProteinPer100G = 10
        });
        var fiveBars = await diary.LogFoodItemAsync(
            day,
            foodSlots[2].Id,
            bar,
            grams: 240,
            servingLabel: "5 × 48g (240g)",
            unit: "g",
            servings: 5);
        Assert.Equal(240, fiveBars.QuantityGrams);
        Assert.Equal(5, fiveBars.Servings);
        Assert.Equal("5 × 48g (240g)", fiveBars.ServingLabel);
        Assert.Equal(960, fiveBars.CaloriesKcal);
        Assert.Equal(24, fiveBars.ProteinG);

        // USDA parser detects ml vs g from servingSizeUnit
        const string usdaJson = """
            {
              "foods": [
                {
                  "fdcId": 1001,
                  "description": "Brewed Coffee",
                  "servingSize": 237.0,
                  "servingSizeUnit": "ml",
                  "foodNutrients": [
                    { "nutrientId": 1008, "nutrientNumber": "208", "unitName": "KCAL", "value": 1 }
                  ]
                },
                {
                  "fdcId": 1002,
                  "description": "Apple",
                  "servingSize": 182.0,
                  "servingSizeUnit": "g",
                  "foodNutrients": [
                    { "nutrientId": 1008, "nutrientNumber": "208", "unitName": "KCAL", "value": 52 }
                  ]
                }
              ]
            }
            """;
        using var doc = System.Text.Json.JsonDocument.Parse(usdaJson);
        var parsed = OpenFoodFactsClient.ParseUsdaResponse(doc.RootElement);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("ml", parsed[0].MeasurementUnit);
        Assert.Equal("g", parsed[1].MeasurementUnit);
    }

    [Fact]
    public async Task SchemaInitializers_HandleConcurrentUpgradeMigrationsWithoutDuplicateColumnErrors()
    {
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();

        // Simulate legacy schemas before PinCalorieSummary, ColorScheme, MeasurementUnit columns existed
        await personalEngine.ExecuteAsync("""
            CREATE TABLE UserProfiles (
                Id TEXT PRIMARY KEY,
                DailyCalorieGoal REAL NOT NULL DEFAULT 2000,
                ProteinGoalG REAL NOT NULL DEFAULT 120,
                CarbsGoalG REAL NOT NULL DEFAULT 225,
                FatGoalG REAL NOT NULL DEFAULT 65,
                SaturatedFatGoalG REAL NOT NULL DEFAULT 20,
                SugarGoalG REAL NOT NULL DEFAULT 45,
                SaltGoalG REAL NOT NULL DEFAULT 6,
                FibreGoalG REAL NOT NULL DEFAULT 30,
                HeightCm REAL NULL,
                BiologicalSex INTEGER NOT NULL DEFAULT 0,
                UnitSystem INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);

        await personalEngine.ExecuteAsync("""
            CREATE TABLE FoodLogEntries (
                Id TEXT PRIMARY KEY,
                Date TEXT NOT NULL,
                MealSlotId TEXT NOT NULL,
                FoodItemId TEXT NULL,
                MealId TEXT NULL,
                NameSnapshot TEXT NOT NULL,
                BrandSnapshot TEXT NULL,
                QuantityGrams REAL NOT NULL,
                Servings REAL NOT NULL DEFAULT 1,
                ServingLabel TEXT NOT NULL DEFAULT '100g',
                CaloriesKcal REAL NOT NULL,
                FatG REAL NOT NULL,
                SaturatedFatG REAL NOT NULL,
                CarbsG REAL NOT NULL,
                SugarsG REAL NOT NULL,
                FibreG REAL NOT NULL,
                ProteinG REAL NOT NULL,
                SaltG REAL NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                LoggedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);

        await foodsEngine.ExecuteAsync("""
            CREATE TABLE FoodItems (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Brand TEXT NULL,
                Barcode TEXT NULL,
                ExternalId TEXT NULL,
                Source INTEGER NOT NULL DEFAULT 0,
                ServingSizeG REAL NOT NULL DEFAULT 100,
                ServingDescription TEXT NOT NULL DEFAULT '100g',
                CaloriesPer100G REAL NOT NULL DEFAULT 0,
                FatPer100G REAL NOT NULL DEFAULT 0,
                SaturatedFatPer100G REAL NOT NULL DEFAULT 0,
                CarbsPer100G REAL NOT NULL DEFAULT 0,
                SugarsPer100G REAL NOT NULL DEFAULT 0,
                FibrePer100G REAL NOT NULL DEFAULT 0,
                ProteinPer100G REAL NOT NULL DEFAULT 0,
                SaltPer100G REAL NOT NULL DEFAULT 0,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);

        var personalInit = new PersonalSqliteSchemaInitializer(personalEngine);
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);

        // Concurrent EnsureCreatedAsync calls (as occurs between AppLayout & DiaryPage after an app update)
        var tasks = new List<Task>();
        for (var i = 0; i < 6; i++)
        {
            tasks.Add(personalInit.EnsureCreatedAsync());
            tasks.Add(foodsInit.EnsureCreatedAsync());
        }
        await Task.WhenAll(tasks);

        var profileRepo = new SqliteUserProfileRepository(personalEngine, personalInit);
        var profile = await profileRepo.GetOrCreateAsync();
        Assert.True(profile.PinCalorieSummary);
        Assert.Equal(CalorieSummaryStyle.Standard, profile.SummaryStyle);
        Assert.True(profile.ShowFoodsInBottomNav);

        // Also verify upgrading an existing DB that had CompactCalorieSummary = 1 migrates cleanly to Minimal
        using var upgradePersonalEngine = new InMemoryPersonalSqliteEngine();
        using var upgradeFoodsEngine = new InMemoryFoodsSqliteEngine();
        await upgradePersonalEngine.ExecuteAsync("""
            CREATE TABLE UserProfiles (
                Id TEXT PRIMARY KEY,
                DailyCalorieGoal REAL NOT NULL DEFAULT 2000,
                ProteinGoalG REAL NOT NULL DEFAULT 120,
                CarbsGoalG REAL NOT NULL DEFAULT 225,
                FatGoalG REAL NOT NULL DEFAULT 65,
                SaturatedFatGoalG REAL NOT NULL DEFAULT 20,
                SugarGoalG REAL NOT NULL DEFAULT 45,
                SaltGoalG REAL NOT NULL DEFAULT 6,
                FibreGoalG REAL NOT NULL DEFAULT 30,
                CompactCalorieSummary INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL
            );
            INSERT INTO UserProfiles (Id, CompactCalorieSummary, UpdatedAtUtc) VALUES ('11111111-1111-1111-1111-111111111199', 1, '2026-11-21T07:00:00Z');
            """);
        var upgradedInit = new PersonalSqliteSchemaInitializer(upgradePersonalEngine);
        var upgradedProfile = await new SqliteUserProfileRepository(upgradePersonalEngine, upgradedInit).GetOrCreateAsync();
        Assert.Equal(CalorieSummaryStyle.Minimal, upgradedProfile.SummaryStyle);

        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);
        var item = await foodRepo.UpsertAsync(new FoodItem { Name = "Milk", MeasurementUnit = "ml" });
        Assert.Equal("ml", (await foodRepo.GetByIdAsync(item.Id))!.MeasurementUnit);
    }

    [Fact]
    public async Task CustomFood_CanBeCreatedPartiallyAndEditedLaterWithoutDuplication()
    {
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);
        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);

        // Create a minimal partial custom food
        var partial = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Homemade Protein Shake",
            Source = FoodSource.Custom,
            CaloriesPer100G = 95
        });

        // Later edit that same custom food to fill out all remaining details
        var edited = new FoodItem
        {
            Id = partial.Id,
            Name = "Homemade Protein Shake (Updated)",
            Brand = "Kitchen",
            Barcode = "5099999000111",
            Source = FoodSource.Custom,
            MeasurementUnit = "ml",
            ServingSizeG = 250,
            ServingDescription = "1 bottle (250ml)",
            CaloriesPer100G = 102,
            ProteinPer100G = 18.5,
            CarbsPer100G = 4.2,
            SugarsPer100G = 2.1,
            FatPer100G = 1.8,
            SaturatedFatPer100G = 0.7,
            FibrePer100G = 0.9,
            SaltPer100G = 0.25
        };
        await foodRepo.UpsertAsync(edited);

        var all = await foodRepo.GetAllAsync();
        Assert.Single(all);
        var saved = all[0];
        Assert.Equal(partial.Id, saved.Id);
        Assert.Equal("Homemade Protein Shake (Updated)", saved.Name);
        Assert.Equal("Kitchen", saved.Brand);
        Assert.Equal("5099999000111", saved.Barcode);
        Assert.Equal("ml", saved.MeasurementUnit);
        Assert.Equal(250, saved.ServingSizeG);
        Assert.Equal("1 bottle (250ml)", saved.ServingDescription);
        Assert.Equal(102, saved.CaloriesPer100G);
        Assert.Equal(18.5, saved.ProteinPer100G);
        Assert.Equal(4.2, saved.CarbsPer100G);
        Assert.Equal(2.1, saved.SugarsPer100G);
        Assert.Equal(1.8, saved.FatPer100G);
        Assert.Equal(0.7, saved.SaturatedFatPer100G);
        Assert.Equal(0.9, saved.FibrePer100G);
        Assert.Equal(0.25, saved.SaltPer100G);
    }

    [Fact]
    public async Task FoodRepository_GetBySourceAsync_ReturnsOnlyMatchingSourceAndFiltersLocally()
    {
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);
        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);

        var customOats = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Overnight Oats",
            Brand = "Homemade Kitchen",
            Barcode = "5099900012345",
            Source = FoodSource.Custom,
            CaloriesPer100G = 320
        });
        await Task.Delay(5);
        var customShake = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Berry Smoothie",
            Brand = "BlendCo",
            Source = FoodSource.Custom,
            MeasurementUnit = "ml",
            CaloriesPer100G = 85
        });
        var offOats = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Rolled Oats",
            Brand = "Quaker",
            Barcode = "5000108001234",
            ExternalId = "off:5000108001234",
            Source = FoodSource.OpenFoodFacts,
            CaloriesPer100G = 374
        });

        // Returns only FoodSource.Custom items when query is null/blank
        var allCustom = await foodRepo.GetBySourceAsync(FoodSource.Custom);
        Assert.Equal(2, allCustom.Count);
        Assert.DoesNotContain(allCustom, f => f.Id == offOats.Id);

        // Filters custom items case-insensitively by Name, Brand, and partial Barcode, excluding OpenFoodFacts items even when Name matches
        var byName = await foodRepo.GetBySourceAsync(FoodSource.Custom, "oats");
        Assert.Single(byName);
        Assert.Equal(customOats.Id, byName[0].Id);

        var byBrand = await foodRepo.GetBySourceAsync(FoodSource.Custom, "blendco");
        Assert.Single(byBrand);
        Assert.Equal(customShake.Id, byBrand[0].Id);

        var byBarcode = await foodRepo.GetBySourceAsync(FoodSource.Custom, "99000123");
        Assert.Single(byBarcode);
        Assert.Equal(customOats.Id, byBarcode[0].Id);

        // Soft-deleted custom items are excluded
        await foodRepo.SoftDeleteAsync(customShake.Id);
        var remainingCustom = await foodRepo.GetBySourceAsync(FoodSource.Custom);
        Assert.Single(remainingCustom);
        Assert.Equal(customOats.Id, remainingCustom[0].Id);
    }

    [Fact]
    public async Task EditAndDuplicateEntry_SupportsServingsMultiplierAndFormatsDisplay()
    {
        using var personalEngine = new InMemoryPersonalSqliteEngine();
        using var foodsEngine = new InMemoryFoodsSqliteEngine();
        var personalInit = new PersonalSqliteSchemaInitializer(personalEngine);
        var foodsInit = new FoodsSqliteSchemaInitializer(foodsEngine, personalEngine);

        var profileRepo = new SqliteUserProfileRepository(personalEngine, personalInit);
        var slotRepo = new SqliteMealSlotRepository(personalEngine, personalInit);
        var foodRepo = new SqliteFoodItemRepository(foodsEngine, foodsInit);
        var logRepo = new SqliteFoodLogRepository(personalEngine, personalInit);
        var bodyRepo = new SqliteBodyMeasurementRepository(personalEngine, personalInit);
        var diary = new DiaryService(logRepo, bodyRepo, profileRepo);

        var foodSlots = (await slotRepo.GetAllAsync()).Where(s => !s.IsWaterTracker).ToList();
        var bar = await foodRepo.UpsertAsync(new FoodItem
        {
            Name = "Protein Flapjack",
            ServingSizeG = 48,
            CaloriesPer100G = 500,
            ProteinPer100G = 10
        });

        var day = new DateOnly(2026, 11, 21);
        var entry = await diary.LogFoodItemAsync(day, foodSlots[0].Id, bar, grams: 48, servings: 1);
        Assert.Equal("1 serving (48g)", DiaryService.FormatEntryServing(entry));

        // Edit entry to 3 servings (3 x 48g = 144g)
        await diary.UpdateEntryQuantityAsync(
            entry,
            newGrams: 144,
            newMealSlotId: foodSlots[0].Id,
            newUnit: "g",
            newServings: 3,
            newServingLabel: "3 × 48g (144g)");

        var edited = (await logRepo.GetByDateAsync(day)).Single(e => e.Id == entry.Id);
        Assert.Equal(144, edited.QuantityGrams);
        Assert.Equal(3, edited.Servings);
        Assert.Equal("3 × 48g (144g)", edited.ServingLabel);
        Assert.Equal(720, edited.CaloriesKcal);
        Assert.Equal(14.4, edited.ProteinG);
        Assert.Equal("3 servings (144g)", DiaryService.FormatEntryServing(edited));

        // Edit entry keeping total 144g while changing serving count to 2 x 72g
        await diary.UpdateEntryQuantityAsync(
            edited,
            newGrams: 144,
            newMealSlotId: foodSlots[0].Id,
            newUnit: "g",
            newServings: 2,
            newServingLabel: "2 × 72g (144g)");

        var rebucketed = (await logRepo.GetByDateAsync(day)).Single(e => e.Id == entry.Id);
        Assert.Equal(2, rebucketed.Servings);
        Assert.Equal("2 × 72g (144g)", rebucketed.ServingLabel);
        Assert.Equal(720, rebucketed.CaloriesKcal);
        Assert.Equal("2 servings (144g)", DiaryService.FormatEntryServing(rebucketed));

        // Duplicate entry with 5 servings (5 x 48g = 240g)
        var duplicated = await diary.DuplicateEntryAsync(
            rebucketed,
            targetDate: day,
            targetMealSlotId: foodSlots[1].Id,
            targetGrams: 240,
            targetUnit: "g",
            targetServings: 5,
            targetServingLabel: "5 × 48g (240g)");

        Assert.Equal(5, duplicated.Servings);
        Assert.Equal(240, duplicated.QuantityGrams);
        Assert.Equal(1200, duplicated.CaloriesKcal);
        Assert.Equal("5 × 48g (240g)", duplicated.ServingLabel);
        Assert.Equal("5 servings (240g)", DiaryService.FormatEntryServing(duplicated));
    }
}
