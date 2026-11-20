using CalorieTracker.Domain.Models;
using CalorieTracker.Domain.Repositories;

namespace CalorieTracker.Services;

public sealed record BarcodeLookupResult(FoodItem? Item, bool FoundLocally);

public interface IFoodLookupService
{
    /// <summary>
    /// Checks local SQLite first; only queries Open Food Facts if missing, then persists locally.
    /// </summary>
    Task<BarcodeLookupResult> LookupByBarcodeAsync(string barcode, CancellationToken ct = default);

    /// <summary>
    /// Immediate local SQLite search.
    /// </summary>
    Task<List<FoodItem>> SearchLocalAsync(string query, int limit = 40, CancellationToken ct = default);

    /// <summary>
    /// Queries Open Food Facts in addition to local search, persists all returned items into SQLite,
    /// and returns a deduplicated merged list.
    /// </summary>
    Task<List<FoodItem>> EnrichFromApiAndMergeAsync(string query, IReadOnlyList<FoodItem> localResults, CancellationToken ct = default);
}

public sealed class FoodLookupService : IFoodLookupService
{
    private readonly IFoodItemRepository _foodRepo;
    private readonly IOpenFoodFactsClient _offClient;

    public FoodLookupService(IFoodItemRepository foodRepo, IOpenFoodFactsClient offClient)
    {
        _foodRepo = foodRepo;
        _offClient = offClient;
    }

    public async Task<BarcodeLookupResult> LookupByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var clean = barcode.Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return new BarcodeLookupResult(null, false);

        var local = await _foodRepo.GetByBarcodeAsync(clean, ct);
        if (local is not null)
            return new BarcodeLookupResult(local, FoundLocally: true);

        var remote = await _offClient.GetByBarcodeAsync(clean, ct);
        if (remote is null)
            return new BarcodeLookupResult(null, FoundLocally: false);

        var saved = await _foodRepo.UpsertAsync(remote, ct);
        return new BarcodeLookupResult(saved, FoundLocally: false);
    }

    public Task<List<FoodItem>> SearchLocalAsync(string query, int limit = 40, CancellationToken ct = default) =>
        _foodRepo.SearchAsync(query, limit, ct);

    public async Task<List<FoodItem>> EnrichFromApiAndMergeAsync(
        string query,
        IReadOnlyList<FoodItem> localResults,
        CancellationToken ct = default)
    {
        var apiResults = await _offClient.SearchByNameAsync(query, pageSize: 20, ct);
        if (apiResults.Count == 0)
            return localResults.ToList();

        // Persist everything returned from API into local SQLite so future searches rely less on the API
        await _foodRepo.UpsertManyAsync(apiResults, CancellationToken.None);

        // Merge local + newly cached API items without duplicates
        var seenIds = new HashSet<Guid>();
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<FoodItem>();

        foreach (var item in localResults)
        {
            if (seenIds.Add(item.Id))
            {
                if (!string.IsNullOrWhiteSpace(item.Barcode))
                    seenBarcodes.Add(item.Barcode);
                merged.Add(item);
            }
        }

        foreach (var item in apiResults)
        {
            if (!string.IsNullOrWhiteSpace(item.Barcode) && !seenBarcodes.Add(item.Barcode))
                continue;
            if (seenIds.Add(item.Id))
                merged.Add(item);
        }

        return merged;
    }
}
