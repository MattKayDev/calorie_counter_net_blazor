using CalorieTracker.Domain.Models;

namespace CalorieTracker.Domain.Repositories;

public interface IUserProfileRepository
{
    Task<UserProfile> GetOrCreateAsync(CancellationToken ct = default);
    Task SaveAsync(UserProfile profile, CancellationToken ct = default);
}

public interface IMealSlotRepository
{
    Task<List<MealSlot>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default);
    Task SaveAsync(MealSlot slot, CancellationToken ct = default);
    Task DeleteOrArchiveAsync(Guid id, CancellationToken ct = default);
}

public interface IFoodItemRepository
{
    Task<FoodItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FoodItem?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<List<FoodItem>> SearchAsync(string query, int limit = 40, CancellationToken ct = default);
    Task<List<FoodItem>> GetAllAsync(int limit = 100, CancellationToken ct = default);
    Task<FoodItem> UpsertAsync(FoodItem item, CancellationToken ct = default);
    Task UpsertManyAsync(IEnumerable<FoodItem> items, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IMealRepository
{
    Task<List<Meal>> GetAllAsync(CancellationToken ct = default);
    Task<Meal?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(Meal meal, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IFoodLogRepository
{
    Task<List<FoodLogEntry>> GetByDateAsync(DateOnly date, CancellationToken ct = default);
    Task<List<FoodLogEntry>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<Dictionary<DateOnly, double>> GetDailyCalorieTotalsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task SaveAsync(FoodLogEntry entry, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IBodyMeasurementRepository
{
    Task<List<BodyMeasurement>> GetAllAsync(CancellationToken ct = default);
    Task<BodyMeasurement?> GetLatestAsync(CancellationToken ct = default);
    Task SaveAsync(BodyMeasurement measurement, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IWaterLogRepository
{
    Task<WaterLog> GetByDateAsync(DateOnly date, CancellationToken ct = default);
    Task SaveAsync(WaterLog log, CancellationToken ct = default);
}

public interface IDatabaseBackupService
{
    Task<byte[]> ExportSqliteBytesAsync(CancellationToken ct = default);
    Task ImportSqliteBytesAsync(byte[] sqliteBytes, CancellationToken ct = default);
}
