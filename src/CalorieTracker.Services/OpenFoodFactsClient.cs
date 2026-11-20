using System.Globalization;
using System.Text.Json;
using CalorieTracker.Domain.Enums;
using CalorieTracker.Domain.Models;

namespace CalorieTracker.Services;

public interface IOpenFoodFactsClient
{
    Task<FoodItem?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<List<FoodItem>> SearchByNameAsync(string query, int pageSize = 20, CancellationToken ct = default);
}

public sealed class OpenFoodFactsClient : IOpenFoodFactsClient
{
    private readonly HttpClient _http;
    private const string Fields = "code,product_name,brands,serving_size,serving_quantity,nutriments";

    public OpenFoodFactsClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<FoodItem?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var clean = barcode.Trim();
        if (string.IsNullOrWhiteSpace(clean)) return null;

        try
        {
            var url = $"https://world.openfoodfacts.org/api/v2/product/{Uri.EscapeDataString(clean)}.json?fields={Fields}";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.Number && statusEl.GetInt32() == 0)
                return null;

            if (!root.TryGetProperty("product", out var product))
                return null;

            return ParseProduct(product, clean);
        }
        catch
        {
            // Offline or API temporarily unreachable
            return null;
        }
    }

    public async Task<List<FoodItem>> SearchByNameAsync(string query, int pageSize = 20, CancellationToken ct = default)
    {
        var clean = query.Trim();
        if (clean.Length < 2) return new List<FoodItem>();

        try
        {
            var url = $"https://world.openfoodfacts.org/cgi/search.pl?search_terms={Uri.EscapeDataString(clean)}&search_simple=1&action=process&json=1&page_size={pageSize}&fields={Fields}";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return new List<FoodItem>();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
                return new List<FoodItem>();

            var list = new List<FoodItem>();
            foreach (var prod in products.EnumerateArray())
            {
                var parsed = ParseProduct(prod, null);
                if (parsed is not null)
                    list.Add(parsed);
            }
            return list;
        }
        catch
        {
            return new List<FoodItem>();
        }
    }

    private static FoodItem? ParseProduct(JsonElement prod, string? fallbackBarcode)
    {
        var name = GetString(prod, "product_name");
        if (string.IsNullOrWhiteSpace(name)) return null;

        var code = GetString(prod, "code") ?? fallbackBarcode;
        var brand = GetString(prod, "brands");
        if (!string.IsNullOrWhiteSpace(brand) && brand.Contains(','))
            brand = brand.Split(',')[0].Trim();

        var servingQty = GetDouble(prod, "serving_quantity");
        var servingDesc = GetString(prod, "serving_size");
        if (servingQty <= 0) servingQty = 100;
        if (string.IsNullOrWhiteSpace(servingDesc)) servingDesc = $"{servingQty:0}g";

        double kcal = 0, fat = 0, satFat = 0, carbs = 0, sugars = 0, fibre = 0, protein = 0, salt = 0;

        if (prod.TryGetProperty("nutriments", out var nut) && nut.ValueKind == JsonValueKind.Object)
        {
            kcal = GetDouble(nut, "energy-kcal_100g");
            if (kcal <= 0)
            {
                var kj = GetDouble(nut, "energy-kj_100g");
                if (kj <= 0) kj = GetDouble(nut, "energy_100g");
                if (kj > 0) kcal = Math.Round(kj / 4.184, 1);
            }

            fat = GetDouble(nut, "fat_100g");
            satFat = GetDouble(nut, "saturated-fat_100g");
            carbs = GetDouble(nut, "carbohydrates_100g");
            sugars = GetDouble(nut, "sugars_100g");
            fibre = GetDouble(nut, "fiber_100g");
            protein = GetDouble(nut, "proteins_100g");
            salt = GetDouble(nut, "salt_100g");
            if (salt <= 0)
            {
                var sodium = GetDouble(nut, "sodium_100g");
                if (sodium > 0) salt = Math.Round(sodium * 2.5, 3);
            }
        }

        // Skip entries that have no calorie and no macro data at all
        if (kcal <= 0 && fat <= 0 && carbs <= 0 && protein <= 0)
            return null;

        return new FoodItem
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Brand = string.IsNullOrWhiteSpace(brand) ? null : brand.Trim(),
            Barcode = string.IsNullOrWhiteSpace(code) ? null : code.Trim(),
            ExternalId = string.IsNullOrWhiteSpace(code) ? $"off:{name.Trim().ToLowerInvariant()}" : $"off:{code.Trim()}",
            Source = FoodSource.OpenFoodFacts,
            ServingSizeG = Math.Round(servingQty, 1),
            ServingDescription = servingDesc.Trim(),
            CaloriesPer100G = Math.Round(kcal, 1),
            FatPer100G = Math.Round(fat, 2),
            SaturatedFatPer100G = Math.Round(satFat, 2),
            CarbsPer100G = Math.Round(carbs, 2),
            SugarsPer100G = Math.Round(sugars, 2),
            FibrePer100G = Math.Round(fibre, 2),
            ProteinPer100G = Math.Round(protein, 2),
            SaltPer100G = Math.Round(salt, 3)
        };
    }

    private static string? GetString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    private static double GetDouble(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
            return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }
}
