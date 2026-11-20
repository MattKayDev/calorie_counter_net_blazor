namespace CalorieTracker.Data.Sqlite;

public sealed class PersonalSqliteSchemaInitializer
{
    private readonly IPersonalSqliteEngine _engine;
    private bool _initialized;

    public PersonalSqliteSchemaInitializer(IPersonalSqliteEngine engine)
    {
        _engine = engine;
    }

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _engine.InitializeAsync(ct);

        await _engine.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS UserProfiles (
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
                AgeYears INTEGER NULL DEFAULT 30,
                BiologicalSex INTEGER NOT NULL DEFAULT 0,
                ActivityLevel INTEGER NOT NULL DEFAULT 2,
                GoalWeightKg REAL NULL,
                WeeklyWeightChangeKg REAL NOT NULL DEFAULT -0.5,
                DailyWaterGoalMl INTEGER NOT NULL DEFAULT 2000,
                ThemeMode INTEGER NOT NULL DEFAULT 0,
                ColorScheme INTEGER NOT NULL DEFAULT 0,
                UnitSystem INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL
            );
            """, null, ct);

        await _engine.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS MealSlots (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Icon TEXT NOT NULL DEFAULT '🍽️',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsArchived INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL
            );
            """, null, ct);

        await _engine.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS Meals (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Notes TEXT NULL,
                DefaultServings REAL NOT NULL DEFAULT 1,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL
            );
            """, null, ct);

        await _engine.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS MealIngredients (
                Id TEXT PRIMARY KEY,
                MealId TEXT NOT NULL,
                FoodItemId TEXT NOT NULL,
                FoodNameSnapshot TEXT NOT NULL,
                QuantityGrams REAL NOT NULL,
                CaloriesKcal REAL NOT NULL,
                FatG REAL NOT NULL,
                SaturatedFatG REAL NOT NULL,
                CarbsG REAL NOT NULL,
                SugarsG REAL NOT NULL,
                FibreG REAL NOT NULL,
                ProteinG REAL NOT NULL,
                SaltG REAL NOT NULL
            );
            """, null, ct);

        await _engine.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_MealIngredients_MealId ON MealIngredients(MealId);", null, ct);

        await _engine.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS FoodLogEntries (
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
            """, null, ct);

        await _engine.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_FoodLogEntries_Date ON FoodLogEntries(Date);", null, ct);

        await _engine.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS BodyMeasurements (
                Id TEXT PRIMARY KEY,
                Date TEXT NOT NULL,
                WeightKg REAL NULL,
                HeightCm REAL NULL,
                NeckCm REAL NULL,
                WaistCm REAL NULL,
                HipCm REAL NULL,
                ManualBodyFatPercent REAL NULL,
                ComputedBmi REAL NULL,
                ComputedBodyFatPercent REAL NULL,
                Notes TEXT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL
            );
            """, null, ct);

        await _engine.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_BodyMeasurements_Date ON BodyMeasurements(Date);", null, ct);

        await _engine.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS WaterLogs (
                Date TEXT PRIMARY KEY,
                AmountMl INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL
            );
            """, null, ct);

        // Safe additive migrations for existing UserProfiles tables
        await EnsureColumnAsync("UserProfiles", "AgeYears", "INTEGER NULL DEFAULT 30", ct);
        await EnsureColumnAsync("UserProfiles", "ActivityLevel", "INTEGER NOT NULL DEFAULT 2", ct);
        await EnsureColumnAsync("UserProfiles", "GoalWeightKg", "REAL NULL", ct);
        await EnsureColumnAsync("UserProfiles", "WeeklyWeightChangeKg", "REAL NOT NULL DEFAULT -0.5", ct);
        await EnsureColumnAsync("UserProfiles", "DailyWaterGoalMl", "INTEGER NOT NULL DEFAULT 2000", ct);
        await EnsureColumnAsync("UserProfiles", "ThemeMode", "INTEGER NOT NULL DEFAULT 0", ct);
        await EnsureColumnAsync("UserProfiles", "ColorScheme", "INTEGER NOT NULL DEFAULT 0", ct);

        // Seed default meal slots if none exist
        var existingSlots = await _engine.QueryAsync("SELECT COUNT(*) AS Cnt FROM MealSlots;", null, ct);
        if (existingSlots.Count == 0 || RowReader.GetInt(existingSlots[0], "Cnt") == 0)
        {
            var now = DateTime.UtcNow.ToString("O");
            var defaults = new[]
            {
                (Guid.Parse("11111111-1111-1111-1111-111111111101"), "Breakfast", "🌅", 0),
                (Guid.Parse("11111111-1111-1111-1111-111111111102"), "Lunch", "🥗", 1),
                (Guid.Parse("11111111-1111-1111-1111-111111111103"), "Dinner", "🍽️", 2),
                (Guid.Parse("11111111-1111-1111-1111-111111111104"), "Snacks", "🍎", 3),
            };
            foreach (var (id, name, icon, order) in defaults)
            {
                await _engine.ExecuteAsync(
                    "INSERT INTO MealSlots (Id, Name, Icon, SortOrder, IsArchived, UpdatedAtUtc) VALUES (?, ?, ?, ?, 0, ?);",
                    new object?[] { id.ToString(), name, icon, order, now },
                    ct);
            }
        }

        _initialized = true;
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var cols = await _engine.QueryAsync($"PRAGMA table_info({table});", null, ct);
        var exists = cols.Any(c => string.Equals(RowReader.GetString(c, "name"), column, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            await _engine.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition};", null, ct);
        }
    }

    public void ResetInitializationState() => _initialized = false;
}

public sealed class FoodsSqliteSchemaInitializer
{
    private readonly IFoodsSqliteEngine _foodsEngine;
    private readonly IPersonalSqliteEngine _personalEngine;
    private bool _initialized;

    public FoodsSqliteSchemaInitializer(IFoodsSqliteEngine foodsEngine, IPersonalSqliteEngine personalEngine)
    {
        _foodsEngine = foodsEngine;
        _personalEngine = personalEngine;
    }

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _foodsEngine.InitializeAsync(ct);

        await _foodsEngine.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS FoodItems (
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
            """, null, ct);

        await _foodsEngine.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_FoodItems_Barcode ON FoodItems(Barcode);", null, ct);
        await _foodsEngine.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_FoodItems_Name ON FoodItems(Name);", null, ct);
        await _foodsEngine.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_FoodItems_ExternalId ON FoodItems(ExternalId);", null, ct);

        // One-time migration: if a legacy single-DB personal.sqlite still has a FoodItems table,
        // copy those food items cleanly into foods.sqlite and drop the legacy table from personal.sqlite.
        await MigrateLegacyFoodItemsFromPersonalDbAsync(ct);

        _initialized = true;
    }

    private async Task MigrateLegacyFoodItemsFromPersonalDbAsync(CancellationToken ct)
    {
        try
        {
            await _personalEngine.InitializeAsync(ct);
            var legacyCheck = await _personalEngine.QueryAsync(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='FoodItems';", null, ct);
            if (legacyCheck.Count == 0) return;

            var legacyRows = await _personalEngine.QueryAsync("SELECT * FROM FoodItems;", null, ct);
            foreach (var r in legacyRows)
            {
                await _foodsEngine.ExecuteAsync("""
                    INSERT OR IGNORE INTO FoodItems (
                        Id, Name, Brand, Barcode, ExternalId, Source, ServingSizeG, ServingDescription,
                        CaloriesPer100G, FatPer100G, SaturatedFatPer100G, CarbsPer100G, SugarsPer100G,
                        FibrePer100G, ProteinPer100G, SaltPer100G, IsDeleted, UpdatedAtUtc
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                    """,
                    new object?[]
                    {
                        RowReader.GetString(r, "Id"),
                        RowReader.GetString(r, "Name"),
                        RowReader.GetNullableString(r, "Brand"),
                        RowReader.GetNullableString(r, "Barcode"),
                        RowReader.GetNullableString(r, "ExternalId"),
                        RowReader.GetInt(r, "Source"),
                        RowReader.GetDouble(r, "ServingSizeG", 100),
                        RowReader.GetString(r, "ServingDescription", "100g"),
                        RowReader.GetDouble(r, "CaloriesPer100G"),
                        RowReader.GetDouble(r, "FatPer100G"),
                        RowReader.GetDouble(r, "SaturatedFatPer100G"),
                        RowReader.GetDouble(r, "CarbsPer100G"),
                        RowReader.GetDouble(r, "SugarsPer100G"),
                        RowReader.GetDouble(r, "FibrePer100G"),
                        RowReader.GetDouble(r, "ProteinPer100G"),
                        RowReader.GetDouble(r, "SaltPer100G"),
                        RowReader.GetInt(r, "IsDeleted"),
                        RowReader.GetString(r, "UpdatedAtUtc", DateTime.UtcNow.ToString("O"))
                    },
                    ct);
            }

            await _personalEngine.ExecuteAsync("DROP TABLE IF EXISTS FoodItems;", null, ct);
        }
        catch
        {
            // Non-fatal if personal DB doesn't exist or has no legacy table
        }
    }

    public void ResetInitializationState() => _initialized = false;
}
