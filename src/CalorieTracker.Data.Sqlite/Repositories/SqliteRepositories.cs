using System.Text.Json;
using CalorieTracker.Domain.Enums;
using CalorieTracker.Domain.Models;
using CalorieTracker.Domain.Repositories;

namespace CalorieTracker.Data.Sqlite.Repositories;

public sealed class SqliteUserProfileRepository : IUserProfileRepository
{
    private readonly IPersonalSqliteEngine _engine;
    private readonly PersonalSqliteSchemaInitializer _init;

    public SqliteUserProfileRepository(IPersonalSqliteEngine engine, PersonalSqliteSchemaInitializer init)
    {
        _engine = engine;
        _init = init;
    }

    public async Task<UserProfile> GetOrCreateAsync(CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var rows = await _engine.QueryAsync("SELECT * FROM UserProfiles LIMIT 1;", null, ct);
        if (rows.Count > 0)
        {
            var r = rows[0];
            return new UserProfile
            {
                Id = RowReader.GetGuid(r, "Id"),
                DailyCalorieGoal = RowReader.GetDouble(r, "DailyCalorieGoal", 2000),
                ProteinGoalG = RowReader.GetDouble(r, "ProteinGoalG", 120),
                CarbsGoalG = RowReader.GetDouble(r, "CarbsGoalG", 225),
                FatGoalG = RowReader.GetDouble(r, "FatGoalG", 65),
                SaturatedFatGoalG = RowReader.GetDouble(r, "SaturatedFatGoalG", 20),
                SugarGoalG = RowReader.GetDouble(r, "SugarGoalG", 45),
                SaltGoalG = RowReader.GetDouble(r, "SaltGoalG", 6),
                FibreGoalG = RowReader.GetDouble(r, "FibreGoalG", 30),
                HeightCm = RowReader.GetNullableDouble(r, "HeightCm"),
                AgeYears = RowReader.GetInt(r, "AgeYears", 30),
                BiologicalSex = (BiologicalSex)RowReader.GetInt(r, "BiologicalSex"),
                ActivityLevel = (ActivityLevel)RowReader.GetInt(r, "ActivityLevel", (int)ActivityLevel.Moderate),
                GoalWeightKg = RowReader.GetNullableDouble(r, "GoalWeightKg"),
                WeeklyWeightChangeKg = RowReader.GetDouble(r, "WeeklyWeightChangeKg", -0.5),
                DailyWaterGoalMl = RowReader.GetInt(r, "DailyWaterGoalMl", 2000),
                ThemeMode = (ThemeMode)RowReader.GetInt(r, "ThemeMode"),
                UnitSystem = (UnitSystem)RowReader.GetInt(r, "UnitSystem"),
                UpdatedAtUtc = RowReader.GetDateTime(r, "UpdatedAtUtc")
            };
        }

        var profile = new UserProfile();
        await SaveAsync(profile, ct);
        return profile;
    }

    public async Task SaveAsync(UserProfile profile, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        profile.UpdatedAtUtc = DateTime.UtcNow;
        await _engine.ExecuteAsync("""
            INSERT INTO UserProfiles (
                Id, DailyCalorieGoal, ProteinGoalG, CarbsGoalG, FatGoalG, SaturatedFatGoalG,
                SugarGoalG, SaltGoalG, FibreGoalG, HeightCm, AgeYears, BiologicalSex, ActivityLevel,
                GoalWeightKg, WeeklyWeightChangeKg, DailyWaterGoalMl, ThemeMode, UnitSystem, UpdatedAtUtc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(Id) DO UPDATE SET
                DailyCalorieGoal = excluded.DailyCalorieGoal,
                ProteinGoalG = excluded.ProteinGoalG,
                CarbsGoalG = excluded.CarbsGoalG,
                FatGoalG = excluded.FatGoalG,
                SaturatedFatGoalG = excluded.SaturatedFatGoalG,
                SugarGoalG = excluded.SugarGoalG,
                SaltGoalG = excluded.SaltGoalG,
                FibreGoalG = excluded.FibreGoalG,
                HeightCm = excluded.HeightCm,
                AgeYears = excluded.AgeYears,
                BiologicalSex = excluded.BiologicalSex,
                ActivityLevel = excluded.ActivityLevel,
                GoalWeightKg = excluded.GoalWeightKg,
                WeeklyWeightChangeKg = excluded.WeeklyWeightChangeKg,
                DailyWaterGoalMl = excluded.DailyWaterGoalMl,
                ThemeMode = excluded.ThemeMode,
                UnitSystem = excluded.UnitSystem,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """,
            new object?[]
            {
                profile.Id.ToString(), profile.DailyCalorieGoal, profile.ProteinGoalG, profile.CarbsGoalG,
                profile.FatGoalG, profile.SaturatedFatGoalG, profile.SugarGoalG, profile.SaltGoalG,
                profile.FibreGoalG, profile.HeightCm, profile.AgeYears, (int)profile.BiologicalSex,
                (int)profile.ActivityLevel, profile.GoalWeightKg, profile.WeeklyWeightChangeKg,
                profile.DailyWaterGoalMl, (int)profile.ThemeMode, (int)profile.UnitSystem,
                profile.UpdatedAtUtc.ToString("O")
            },
            ct);
    }
}

public sealed class SqliteMealSlotRepository : IMealSlotRepository
{
    private readonly IPersonalSqliteEngine _engine;
    private readonly PersonalSqliteSchemaInitializer _init;

    public SqliteMealSlotRepository(IPersonalSqliteEngine engine, PersonalSqliteSchemaInitializer init)
    {
        _engine = engine;
        _init = init;
    }

    public async Task<List<MealSlot>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var sql = includeArchived
            ? "SELECT * FROM MealSlots ORDER BY SortOrder ASC, Name ASC;"
            : "SELECT * FROM MealSlots WHERE IsArchived = 0 ORDER BY SortOrder ASC, Name ASC;";
        var rows = await _engine.QueryAsync(sql, null, ct);
        return rows.Select(r => new MealSlot
        {
            Id = RowReader.GetGuid(r, "Id"),
            Name = RowReader.GetString(r, "Name"),
            Icon = RowReader.GetString(r, "Icon", "🍽️"),
            SortOrder = RowReader.GetInt(r, "SortOrder"),
            IsArchived = RowReader.GetBool(r, "IsArchived"),
            UpdatedAtUtc = RowReader.GetDateTime(r, "UpdatedAtUtc")
        }).ToList();
    }

    public async Task SaveAsync(MealSlot slot, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        slot.UpdatedAtUtc = DateTime.UtcNow;
        await _engine.ExecuteAsync("""
            INSERT INTO MealSlots (Id, Name, Icon, SortOrder, IsArchived, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Icon = excluded.Icon,
                SortOrder = excluded.SortOrder,
                IsArchived = excluded.IsArchived,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """,
            new object?[]
            {
                slot.Id.ToString(), slot.Name, slot.Icon, slot.SortOrder,
                slot.IsArchived ? 1 : 0, slot.UpdatedAtUtc.ToString("O")
            },
            ct);
    }

    public async Task DeleteOrArchiveAsync(Guid id, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        await _engine.ExecuteAsync(
            "UPDATE MealSlots SET IsArchived = 1, UpdatedAtUtc = ? WHERE Id = ?;",
            new object?[] { DateTime.UtcNow.ToString("O"), id.ToString() },
            ct);
    }
}

public sealed class SqliteFoodItemRepository : IFoodItemRepository
{
    private readonly IFoodsSqliteEngine _engine;
    private readonly FoodsSqliteSchemaInitializer _init;

    public SqliteFoodItemRepository(IFoodsSqliteEngine engine, FoodsSqliteSchemaInitializer init)
    {
        _engine = engine;
        _init = init;
    }

    public async Task<FoodItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var rows = await _engine.QueryAsync(
            "SELECT * FROM FoodItems WHERE Id = ? AND IsDeleted = 0 LIMIT 1;",
            new object?[] { id.ToString() },
            ct);
        return rows.Count > 0 ? Map(rows[0]) : null;
    }

    public async Task<FoodItem?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var trimmed = barcode.Trim();
        var rows = await _engine.QueryAsync(
            "SELECT * FROM FoodItems WHERE Barcode = ? AND IsDeleted = 0 LIMIT 1;",
            new object?[] { trimmed },
            ct);
        return rows.Count > 0 ? Map(rows[0]) : null;
    }

    public async Task<List<FoodItem>> SearchAsync(string query, int limit = 40, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return await GetAllAsync(limit, ct);

        var pattern = $"%{trimmed}%";
        var rows = await _engine.QueryAsync("""
            SELECT * FROM FoodItems
            WHERE IsDeleted = 0
              AND (Name LIKE ? COLLATE NOCASE OR Brand LIKE ? COLLATE NOCASE OR Barcode = ?)
            ORDER BY CASE WHEN Source = 0 THEN 0 ELSE 1 END, Name ASC
            LIMIT ?;
            """,
            new object?[] { pattern, pattern, trimmed, limit },
            ct);
        return rows.Select(Map).ToList();
    }

    public async Task<List<FoodItem>> GetAllAsync(int limit = 100, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var rows = await _engine.QueryAsync(
            "SELECT * FROM FoodItems WHERE IsDeleted = 0 ORDER BY UpdatedAtUtc DESC LIMIT ?;",
            new object?[] { limit },
            ct);
        return rows.Select(Map).ToList();
    }

    public async Task<FoodItem> UpsertAsync(FoodItem item, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);

        // Deduplicate by Barcode or ExternalId for external/API foods so repeated searches don't create duplicates
        if (!string.IsNullOrWhiteSpace(item.Barcode))
        {
            var existingByBarcode = await _engine.QueryAsync(
                "SELECT Id FROM FoodItems WHERE Barcode = ? AND IsDeleted = 0 LIMIT 1;",
                new object?[] { item.Barcode.Trim() },
                ct);
            if (existingByBarcode.Count > 0)
            {
                item.Id = RowReader.GetGuid(existingByBarcode[0], "Id");
            }
        }
        else if (!string.IsNullOrWhiteSpace(item.ExternalId))
        {
            var existingByExt = await _engine.QueryAsync(
                "SELECT Id FROM FoodItems WHERE ExternalId = ? AND IsDeleted = 0 LIMIT 1;",
                new object?[] { item.ExternalId.Trim() },
                ct);
            if (existingByExt.Count > 0)
            {
                item.Id = RowReader.GetGuid(existingByExt[0], "Id");
            }
        }

        item.UpdatedAtUtc = DateTime.UtcNow;
        await _engine.ExecuteAsync("""
            INSERT INTO FoodItems (
                Id, Name, Brand, Barcode, ExternalId, Source, ServingSizeG, ServingDescription,
                CaloriesPer100G, FatPer100G, SaturatedFatPer100G, CarbsPer100G, SugarsPer100G,
                FibrePer100G, ProteinPer100G, SaltPer100G, IsDeleted, UpdatedAtUtc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Brand = excluded.Brand,
                Barcode = excluded.Barcode,
                ExternalId = excluded.ExternalId,
                Source = excluded.Source,
                ServingSizeG = excluded.ServingSizeG,
                ServingDescription = excluded.ServingDescription,
                CaloriesPer100G = excluded.CaloriesPer100G,
                FatPer100G = excluded.FatPer100G,
                SaturatedFatPer100G = excluded.SaturatedFatPer100G,
                CarbsPer100G = excluded.CarbsPer100G,
                SugarsPer100G = excluded.SugarsPer100G,
                FibrePer100G = excluded.FibrePer100G,
                ProteinPer100G = excluded.ProteinPer100G,
                SaltPer100G = excluded.SaltPer100G,
                IsDeleted = excluded.IsDeleted,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """,
            new object?[]
            {
                item.Id.ToString(), item.Name.Trim(), item.Brand?.Trim(), item.Barcode?.Trim(),
                item.ExternalId?.Trim(), (int)item.Source, item.ServingSizeG, item.ServingDescription,
                item.CaloriesPer100G, item.FatPer100G, item.SaturatedFatPer100G, item.CarbsPer100G,
                item.SugarsPer100G, item.FibrePer100G, item.ProteinPer100G, item.SaltPer100G,
                item.IsDeleted ? 1 : 0, item.UpdatedAtUtc.ToString("O")
            },
            ct);

        return item;
    }

    public async Task UpsertManyAsync(IEnumerable<FoodItem> items, CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            await UpsertAsync(item, ct);
        }
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        await _engine.ExecuteAsync(
            "UPDATE FoodItems SET IsDeleted = 1, UpdatedAtUtc = ? WHERE Id = ?;",
            new object?[] { DateTime.UtcNow.ToString("O"), id.ToString() },
            ct);
    }

    private static FoodItem Map(Dictionary<string, JsonElement> r) => new()
    {
        Id = RowReader.GetGuid(r, "Id"),
        Name = RowReader.GetString(r, "Name"),
        Brand = RowReader.GetNullableString(r, "Brand"),
        Barcode = RowReader.GetNullableString(r, "Barcode"),
        ExternalId = RowReader.GetNullableString(r, "ExternalId"),
        Source = (FoodSource)RowReader.GetInt(r, "Source"),
        ServingSizeG = RowReader.GetDouble(r, "ServingSizeG", 100),
        ServingDescription = RowReader.GetString(r, "ServingDescription", "100g"),
        CaloriesPer100G = RowReader.GetDouble(r, "CaloriesPer100G"),
        FatPer100G = RowReader.GetDouble(r, "FatPer100G"),
        SaturatedFatPer100G = RowReader.GetDouble(r, "SaturatedFatPer100G"),
        CarbsPer100G = RowReader.GetDouble(r, "CarbsPer100G"),
        SugarsPer100G = RowReader.GetDouble(r, "SugarsPer100G"),
        FibrePer100G = RowReader.GetDouble(r, "FibrePer100G"),
        ProteinPer100G = RowReader.GetDouble(r, "ProteinPer100G"),
        SaltPer100G = RowReader.GetDouble(r, "SaltPer100G"),
        IsDeleted = RowReader.GetBool(r, "IsDeleted"),
        UpdatedAtUtc = RowReader.GetDateTime(r, "UpdatedAtUtc")
    };
}

public sealed class SqliteMealRepository : IMealRepository
{
    private readonly IPersonalSqliteEngine _engine;
    private readonly PersonalSqliteSchemaInitializer _init;

    public SqliteMealRepository(IPersonalSqliteEngine engine, PersonalSqliteSchemaInitializer init)
    {
        _engine = engine;
        _init = init;
    }

    public async Task<List<Meal>> GetAllAsync(CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var mealRows = await _engine.QueryAsync(
            "SELECT * FROM Meals WHERE IsDeleted = 0 ORDER BY Name ASC;",
            null, ct);

        var meals = mealRows.Select(MapMeal).ToList();
        if (meals.Count == 0) return meals;

        var ingRows = await _engine.QueryAsync("SELECT * FROM MealIngredients;", null, ct);
        var grouped = ingRows.Select(MapIngredient).GroupBy(i => i.MealId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var meal in meals)
        {
            if (grouped.TryGetValue(meal.Id, out var list))
                meal.Ingredients = list;
        }
        return meals;
    }

    public async Task<Meal?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var mealRows = await _engine.QueryAsync(
            "SELECT * FROM Meals WHERE Id = ? AND IsDeleted = 0 LIMIT 1;",
            new object?[] { id.ToString() }, ct);
        if (mealRows.Count == 0) return null;

        var meal = MapMeal(mealRows[0]);
        var ingRows = await _engine.QueryAsync(
            "SELECT * FROM MealIngredients WHERE MealId = ?;",
            new object?[] { id.ToString() }, ct);
        meal.Ingredients = ingRows.Select(MapIngredient).ToList();
        return meal;
    }

    public async Task SaveAsync(Meal meal, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        meal.UpdatedAtUtc = DateTime.UtcNow;

        await _engine.ExecuteAsync("""
            INSERT INTO Meals (Id, Name, Notes, DefaultServings, IsDeleted, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Notes = excluded.Notes,
                DefaultServings = excluded.DefaultServings,
                IsDeleted = excluded.IsDeleted,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """,
            new object?[]
            {
                meal.Id.ToString(), meal.Name.Trim(), meal.Notes, meal.DefaultServings,
                meal.IsDeleted ? 1 : 0, meal.UpdatedAtUtc.ToString("O")
            },
            ct);

        await _engine.ExecuteAsync("DELETE FROM MealIngredients WHERE MealId = ?;", new object?[] { meal.Id.ToString() }, ct);

        foreach (var ing in meal.Ingredients)
        {
            ing.MealId = meal.Id;
            await _engine.ExecuteAsync("""
                INSERT INTO MealIngredients (
                    Id, MealId, FoodItemId, FoodNameSnapshot, QuantityGrams,
                    CaloriesKcal, FatG, SaturatedFatG, CarbsG, SugarsG, FibreG, ProteinG, SaltG
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                new object?[]
                {
                    ing.Id.ToString(), ing.MealId.ToString(), ing.FoodItemId.ToString(),
                    ing.FoodNameSnapshot, ing.QuantityGrams, ing.CaloriesKcal, ing.FatG,
                    ing.SaturatedFatG, ing.CarbsG, ing.SugarsG, ing.FibreG, ing.ProteinG, ing.SaltG
                },
                ct);
        }
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        await _engine.ExecuteAsync(
            "UPDATE Meals SET IsDeleted = 1, UpdatedAtUtc = ? WHERE Id = ?;",
            new object?[] { DateTime.UtcNow.ToString("O"), id.ToString() },
            ct);
    }

    private static Meal MapMeal(Dictionary<string, JsonElement> r) => new()
    {
        Id = RowReader.GetGuid(r, "Id"),
        Name = RowReader.GetString(r, "Name"),
        Notes = RowReader.GetNullableString(r, "Notes"),
        DefaultServings = RowReader.GetDouble(r, "DefaultServings", 1),
        IsDeleted = RowReader.GetBool(r, "IsDeleted"),
        UpdatedAtUtc = RowReader.GetDateTime(r, "UpdatedAtUtc")
    };

    private static MealIngredient MapIngredient(Dictionary<string, JsonElement> r) => new()
    {
        Id = RowReader.GetGuid(r, "Id"),
        MealId = RowReader.GetGuid(r, "MealId"),
        FoodItemId = RowReader.GetGuid(r, "FoodItemId"),
        FoodNameSnapshot = RowReader.GetString(r, "FoodNameSnapshot"),
        QuantityGrams = RowReader.GetDouble(r, "QuantityGrams"),
        CaloriesKcal = RowReader.GetDouble(r, "CaloriesKcal"),
        FatG = RowReader.GetDouble(r, "FatG"),
        SaturatedFatG = RowReader.GetDouble(r, "SaturatedFatG"),
        CarbsG = RowReader.GetDouble(r, "CarbsG"),
        SugarsG = RowReader.GetDouble(r, "SugarsG"),
        FibreG = RowReader.GetDouble(r, "FibreG"),
        ProteinG = RowReader.GetDouble(r, "ProteinG"),
        SaltG = RowReader.GetDouble(r, "SaltG")
    };
}

public sealed class SqliteFoodLogRepository : IFoodLogRepository
{
    private readonly IPersonalSqliteEngine _engine;
    private readonly PersonalSqliteSchemaInitializer _init;

    public SqliteFoodLogRepository(IPersonalSqliteEngine engine, PersonalSqliteSchemaInitializer init)
    {
        _engine = engine;
        _init = init;
    }

    public async Task<List<FoodLogEntry>> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var rows = await _engine.QueryAsync(
            "SELECT * FROM FoodLogEntries WHERE Date = ? AND IsDeleted = 0 ORDER BY LoggedAtUtc ASC;",
            new object?[] { date.ToString("yyyy-MM-dd") },
            ct);
        return rows.Select(Map).ToList();
    }

    public async Task<List<FoodLogEntry>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var rows = await _engine.QueryAsync(
            "SELECT * FROM FoodLogEntries WHERE Date >= ? AND Date <= ? AND IsDeleted = 0 ORDER BY Date ASC, LoggedAtUtc ASC;",
            new object?[] { from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd") },
            ct);
        return rows.Select(Map).ToList();
    }

    public async Task<Dictionary<DateOnly, double>> GetDailyCalorieTotalsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var rows = await _engine.QueryAsync("""
            SELECT Date, SUM(CaloriesKcal) AS TotalKcal
            FROM FoodLogEntries
            WHERE Date >= ? AND Date <= ? AND IsDeleted = 0
            GROUP BY Date;
            """,
            new object?[] { from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd") },
            ct);

        var result = new Dictionary<DateOnly, double>();
        foreach (var r in rows)
        {
            result[RowReader.GetDateOnly(r, "Date")] = Math.Round(RowReader.GetDouble(r, "TotalKcal"), 1);
        }
        return result;
    }

    public async Task SaveAsync(FoodLogEntry entry, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await _engine.ExecuteAsync("""
            INSERT INTO FoodLogEntries (
                Id, Date, MealSlotId, FoodItemId, MealId, NameSnapshot, BrandSnapshot,
                QuantityGrams, Servings, ServingLabel, CaloriesKcal, FatG, SaturatedFatG,
                CarbsG, SugarsG, FibreG, ProteinG, SaltG, IsDeleted, LoggedAtUtc, UpdatedAtUtc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(Id) DO UPDATE SET
                Date = excluded.Date,
                MealSlotId = excluded.MealSlotId,
                QuantityGrams = excluded.QuantityGrams,
                Servings = excluded.Servings,
                ServingLabel = excluded.ServingLabel,
                CaloriesKcal = excluded.CaloriesKcal,
                FatG = excluded.FatG,
                SaturatedFatG = excluded.SaturatedFatG,
                CarbsG = excluded.CarbsG,
                SugarsG = excluded.SugarsG,
                FibreG = excluded.FibreG,
                ProteinG = excluded.ProteinG,
                SaltG = excluded.SaltG,
                IsDeleted = excluded.IsDeleted,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """,
            new object?[]
            {
                entry.Id.ToString(), entry.Date.ToString("yyyy-MM-dd"), entry.MealSlotId.ToString(),
                entry.FoodItemId?.ToString(), entry.MealId?.ToString(), entry.NameSnapshot, entry.BrandSnapshot,
                entry.QuantityGrams, entry.Servings, entry.ServingLabel, entry.CaloriesKcal, entry.FatG,
                entry.SaturatedFatG, entry.CarbsG, entry.SugarsG, entry.FibreG, entry.ProteinG, entry.SaltG,
                entry.IsDeleted ? 1 : 0, entry.LoggedAtUtc.ToString("O"), entry.UpdatedAtUtc.ToString("O")
            },
            ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        await _engine.ExecuteAsync(
            "UPDATE FoodLogEntries SET IsDeleted = 1, UpdatedAtUtc = ? WHERE Id = ?;",
            new object?[] { DateTime.UtcNow.ToString("O"), id.ToString() },
            ct);
    }

    private static FoodLogEntry Map(Dictionary<string, JsonElement> r) => new()
    {
        Id = RowReader.GetGuid(r, "Id"),
        Date = RowReader.GetDateOnly(r, "Date"),
        MealSlotId = RowReader.GetGuid(r, "MealSlotId"),
        FoodItemId = RowReader.GetNullableGuid(r, "FoodItemId"),
        MealId = RowReader.GetNullableGuid(r, "MealId"),
        NameSnapshot = RowReader.GetString(r, "NameSnapshot"),
        BrandSnapshot = RowReader.GetNullableString(r, "BrandSnapshot"),
        QuantityGrams = RowReader.GetDouble(r, "QuantityGrams"),
        Servings = RowReader.GetDouble(r, "Servings", 1),
        ServingLabel = RowReader.GetString(r, "ServingLabel", "100g"),
        CaloriesKcal = RowReader.GetDouble(r, "CaloriesKcal"),
        FatG = RowReader.GetDouble(r, "FatG"),
        SaturatedFatG = RowReader.GetDouble(r, "SaturatedFatG"),
        CarbsG = RowReader.GetDouble(r, "CarbsG"),
        SugarsG = RowReader.GetDouble(r, "SugarsG"),
        FibreG = RowReader.GetDouble(r, "FibreG"),
        ProteinG = RowReader.GetDouble(r, "ProteinG"),
        SaltG = RowReader.GetDouble(r, "SaltG"),
        IsDeleted = RowReader.GetBool(r, "IsDeleted"),
        LoggedAtUtc = RowReader.GetDateTime(r, "LoggedAtUtc"),
        UpdatedAtUtc = RowReader.GetDateTime(r, "UpdatedAtUtc")
    };
}

public sealed class SqliteBodyMeasurementRepository : IBodyMeasurementRepository
{
    private readonly IPersonalSqliteEngine _engine;
    private readonly PersonalSqliteSchemaInitializer _init;

    public SqliteBodyMeasurementRepository(IPersonalSqliteEngine engine, PersonalSqliteSchemaInitializer init)
    {
        _engine = engine;
        _init = init;
    }

    public async Task<List<BodyMeasurement>> GetAllAsync(CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var rows = await _engine.QueryAsync(
            "SELECT * FROM BodyMeasurements WHERE IsDeleted = 0 ORDER BY Date DESC, UpdatedAtUtc DESC;",
            null, ct);
        return rows.Select(Map).ToList();
    }

    public async Task<BodyMeasurement?> GetLatestAsync(CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var rows = await _engine.QueryAsync(
            "SELECT * FROM BodyMeasurements WHERE IsDeleted = 0 ORDER BY Date DESC, UpdatedAtUtc DESC LIMIT 1;",
            null, ct);
        return rows.Count > 0 ? Map(rows[0]) : null;
    }

    public async Task SaveAsync(BodyMeasurement m, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        m.UpdatedAtUtc = DateTime.UtcNow;
        await _engine.ExecuteAsync("""
            INSERT INTO BodyMeasurements (
                Id, Date, WeightKg, HeightCm, NeckCm, WaistCm, HipCm,
                ManualBodyFatPercent, ComputedBmi, ComputedBodyFatPercent, Notes, IsDeleted, UpdatedAtUtc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(Id) DO UPDATE SET
                Date = excluded.Date,
                WeightKg = excluded.WeightKg,
                HeightCm = excluded.HeightCm,
                NeckCm = excluded.NeckCm,
                WaistCm = excluded.WaistCm,
                HipCm = excluded.HipCm,
                ManualBodyFatPercent = excluded.ManualBodyFatPercent,
                ComputedBmi = excluded.ComputedBmi,
                ComputedBodyFatPercent = excluded.ComputedBodyFatPercent,
                Notes = excluded.Notes,
                IsDeleted = excluded.IsDeleted,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """,
            new object?[]
            {
                m.Id.ToString(), m.Date.ToString("yyyy-MM-dd"), m.WeightKg, m.HeightCm,
                m.NeckCm, m.WaistCm, m.HipCm, m.ManualBodyFatPercent, m.ComputedBmi,
                m.ComputedBodyFatPercent, m.Notes, m.IsDeleted ? 1 : 0, m.UpdatedAtUtc.ToString("O")
            },
            ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        await _engine.ExecuteAsync(
            "UPDATE BodyMeasurements SET IsDeleted = 1, UpdatedAtUtc = ? WHERE Id = ?;",
            new object?[] { DateTime.UtcNow.ToString("O"), id.ToString() },
            ct);
    }

    private static BodyMeasurement Map(Dictionary<string, JsonElement> r) => new()
    {
        Id = RowReader.GetGuid(r, "Id"),
        Date = RowReader.GetDateOnly(r, "Date"),
        WeightKg = RowReader.GetNullableDouble(r, "WeightKg"),
        HeightCm = RowReader.GetNullableDouble(r, "HeightCm"),
        NeckCm = RowReader.GetNullableDouble(r, "NeckCm"),
        WaistCm = RowReader.GetNullableDouble(r, "WaistCm"),
        HipCm = RowReader.GetNullableDouble(r, "HipCm"),
        ManualBodyFatPercent = RowReader.GetNullableDouble(r, "ManualBodyFatPercent"),
        ComputedBmi = RowReader.GetNullableDouble(r, "ComputedBmi"),
        ComputedBodyFatPercent = RowReader.GetNullableDouble(r, "ComputedBodyFatPercent"),
        Notes = RowReader.GetNullableString(r, "Notes"),
        IsDeleted = RowReader.GetBool(r, "IsDeleted"),
        UpdatedAtUtc = RowReader.GetDateTime(r, "UpdatedAtUtc")
    };
}

public sealed class SqliteWaterLogRepository : IWaterLogRepository
{
    private readonly IPersonalSqliteEngine _engine;
    private readonly PersonalSqliteSchemaInitializer _init;

    public SqliteWaterLogRepository(IPersonalSqliteEngine engine, PersonalSqliteSchemaInitializer init)
    {
        _engine = engine;
        _init = init;
    }

    public async Task<WaterLog> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        var rows = await _engine.QueryAsync(
            "SELECT * FROM WaterLogs WHERE Date = ? LIMIT 1;",
            new object?[] { date.ToString("yyyy-MM-dd") }, ct);
        if (rows.Count > 0)
        {
            return new WaterLog
            {
                Date = RowReader.GetDateOnly(rows[0], "Date"),
                AmountMl = RowReader.GetInt(rows[0], "AmountMl"),
                UpdatedAtUtc = RowReader.GetDateTime(rows[0], "UpdatedAtUtc")
            };
        }
        return new WaterLog { Date = date, AmountMl = 0 };
    }

    public async Task SaveAsync(WaterLog log, CancellationToken ct = default)
    {
        await _init.EnsureCreatedAsync(ct);
        log.AmountMl = Math.Max(0, log.AmountMl);
        log.UpdatedAtUtc = DateTime.UtcNow;
        await _engine.ExecuteAsync("""
            INSERT INTO WaterLogs (Date, AmountMl, UpdatedAtUtc)
            VALUES (?, ?, ?)
            ON CONFLICT(Date) DO UPDATE SET
                AmountMl = excluded.AmountMl,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """,
            new object?[] { log.Date.ToString("yyyy-MM-dd"), log.AmountMl, log.UpdatedAtUtc.ToString("O") },
            ct);
    }
}

public sealed class SqliteBackupService : IDatabaseBackupService
{
    private readonly IPersonalSqliteEngine _personalEngine;
    private readonly PersonalSqliteSchemaInitializer _personalInit;
    private readonly IFoodsSqliteEngine _foodsEngine;
    private readonly FoodsSqliteSchemaInitializer _foodsInit;

    public SqliteBackupService(
        IPersonalSqliteEngine personalEngine,
        PersonalSqliteSchemaInitializer personalInit,
        IFoodsSqliteEngine foodsEngine,
        FoodsSqliteSchemaInitializer foodsInit)
    {
        _personalEngine = personalEngine;
        _personalInit = personalInit;
        _foodsEngine = foodsEngine;
        _foodsInit = foodsInit;
    }

    public async Task<byte[]> ExportPersonalSqliteBytesAsync(CancellationToken ct = default)
    {
        await _personalInit.EnsureCreatedAsync(ct);
        return await _personalEngine.ExportBytesAsync(ct);
    }

    public async Task ImportPersonalSqliteBytesAsync(byte[] sqliteBytes, CancellationToken ct = default)
    {
        await _personalEngine.ImportBytesAsync(sqliteBytes, ct);
        _personalInit.ResetInitializationState();
        await _personalInit.EnsureCreatedAsync(ct);
    }

    public async Task<byte[]> ExportFoodsSqliteBytesAsync(CancellationToken ct = default)
    {
        await _foodsInit.EnsureCreatedAsync(ct);
        return await _foodsEngine.ExportBytesAsync(ct);
    }

    public async Task ImportFoodsSqliteBytesAsync(byte[] sqliteBytes, CancellationToken ct = default)
    {
        await _foodsEngine.ImportBytesAsync(sqliteBytes, ct);
        _foodsInit.ResetInitializationState();
        await _foodsInit.EnsureCreatedAsync(ct);
    }
}
