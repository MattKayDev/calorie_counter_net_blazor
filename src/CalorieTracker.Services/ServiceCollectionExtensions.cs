using Microsoft.Extensions.DependencyInjection;

namespace CalorieTracker.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCalorieTrackerServices(this IServiceCollection services)
    {
        services.AddScoped<IOpenFoodFactsClient, OpenFoodFactsClient>();
        services.AddScoped<IFoodLookupService, FoodLookupService>();
        services.AddScoped<DiaryService>();
        services.AddScoped<AppLayoutState>();
        return services;
    }
}
