using CalorieTracker.Data.Sqlite.Repositories;
using CalorieTracker.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CalorieTracker.Data.Sqlite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqliteRepositories(this IServiceCollection services)
    {
        services.AddScoped<SqliteSchemaInitializer>();
        services.AddScoped<IUserProfileRepository, SqliteUserProfileRepository>();
        services.AddScoped<IMealSlotRepository, SqliteMealSlotRepository>();
        services.AddScoped<IFoodItemRepository, SqliteFoodItemRepository>();
        services.AddScoped<IMealRepository, SqliteMealRepository>();
        services.AddScoped<IFoodLogRepository, SqliteFoodLogRepository>();
        services.AddScoped<IBodyMeasurementRepository, SqliteBodyMeasurementRepository>();
        services.AddScoped<IWaterLogRepository, SqliteWaterLogRepository>();
        services.AddScoped<IDatabaseBackupService, SqliteBackupService>();
        return services;
    }
}
