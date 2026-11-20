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

/// <summary>
/// Free, zero-cost online food lookup client combining:
/// 1. Open Food Facts (v2 product/barcode API + v2 brand search + legacy text search when available)
/// 2. USDA FoodData Central (CORS-enabled free API with whole foods & branded products)
/// All returned items are persisted by FoodLookupService into local foods.sqlite.
/// </summary>
public sealed class OpenFoodFactsClient : IOpenFoodFactsClient
{
    private readonly HttpClient _http;
    private const string OffFields = "code,product_name,brands,serving_size,serving_quantity,nutriments";

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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));
            var url = $"https://world.openfoodfacts.org/api/v2/product/{Uri.EscapeDataString(clean)}.json?fields={OffFields}";
            using var resp = await _http.GetAsync(url, timeoutCts.Token);
            if (!resp.IsSuccessStatusCode || !IsJson(resp)) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.Number && statusEl.GetInt32() == 0)
                return null;

            if (!root.TryGetProperty("product", out var product))
                return null;

            return ParseOffProduct(product, clean);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<FoodItem>> SearchByNameAsync(string query, int pageSize = 20, CancellationToken ct = default)
    {
        var clean = query.Trim();
        if (clean.Length < 2) return new List<FoodItem>();

        // Run Open Food Facts + USDA FoodData Central in parallel so search stays fast and reliable
        // even when Open Food Facts text search returns 503 under load.
        var usdaTask = SearchUsdaAsync(clean, Math.Min(pageSize, 15), ct);
        var offBrandTask = SearchOffBrandAsync(clean, Math.Min(pageSize, 10), ct);
        var offCgiTask = SearchOffCgiAsync(clean, Math.Min(pageSize, 10), ct);

        await Task.WhenAll(usdaTask, offBrandTask, offCgiTask);

        var combined = new List<FoodItem>();
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRangeUnique(IEnumerable<FoodItem> items)
        {
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.Barcode) && !seenBarcodes.Add(item.Barcode))
                    continue;
                var nameKey = $"{item.Name}|{item.Brand}";
                if (!seenNames.Add(nameKey))
                    continue;
                combined.Add(item);
            }
        }

        // Prefer Open Food Facts UK/EU branded items first, then USDA whole & branded foods
        AddRangeUnique(offBrandTask.Result);
        AddRangeUnique(offCgiTask.Result);
        AddRangeUnique(usdaTask.Result);

        return combined.Take(pageSize).ToList();
    }

    private async Task<List<FoodItem>> SearchOffBrandAsync(string query, int pageSize, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
            var slug = query.ToLowerInvariant().Replace(' ', '-');
            var url = $"https://world.openfoodfacts.org/api/v2/search?brands_tags={Uri.EscapeDataString(slug)}&page_size={pageSize}&fields={OffFields}";
            using var resp = await _http.GetAsync(url, timeoutCts.Token);
            if (!resp.IsSuccessStatusCode || !IsJson(resp)) return new List<FoodItem>();

            await using var stream = await resp.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
            return ParseOffProductList(doc.RootElement);
        }
        catch
        {
            return new List<FoodItem>();
        }
    }

    private async Task<List<FoodItem>> SearchOffCgiAsync(string query, int pageSize, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2.5));
            var url = $"https://world.openfoodfacts.org/cgi/search.pl?search_terms={Uri.EscapeDataString(query)}&search_simple=1&action=process&json=1&page_size={pageSize}&fields={OffFields}";
            using var resp = await _http.GetAsync(url, timeoutCts.Token);
            if (!resp.IsSuccessStatusCode || !IsJson(resp)) return new List<FoodItem>();

            await using var stream = await resp.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
            return ParseOffProductList(doc.RootElement);
        }
        catch
        {
            return new List<FoodItem>();
        }
    }

    private async Task<List<FoodItem>> SearchUsdaAsync(string query, int pageSize, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            var url = $"https://api.nal.usda.gov/fdc/v1/foods/search?api_key=DEMO_KEY&query={Uri.EscapeDataString(query)}&pageSize={pageSize}";
            using var resp = await _http.GetAsync(url, timeoutCts.Token);
            if (!resp.IsSuccessStatusCode || !IsJson(resp)) return new List<FoodItem>();

            await using var stream = await resp.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
            return ParseUsdaResponse(doc.RootElement);
        }
        catch
        {
            return new List<FoodItem>();
        }
    }

    public static List<FoodItem> ParseUsdaResponse(JsonElement root)
    {
        if (!root.TryGetProperty("foods", out var foods) || foods.ValueKind != JsonValueKind.Array)
            return new List<FoodItem>();

        var list = new List<FoodItem>();
        foreach (var f in foods.EnumerateArray())
        {
            var desc = GetString(f, "description");
            if (string.IsNullOrWhiteSpace(desc)) continue;

            var fdcId = GetString(f, "fdcId");
            var brand = GetString(f, "brandName") ?? GetString(f, "brandOwner");
            var barcode = GetString(f, "gtinUpc");
            var servingSize = GetDouble(f, "servingSize");
            if (servingSize <= 0) servingSize = 100;
            var servingDesc = GetString(f, "householdServingFullText");
            if (string.IsNullOrWhiteSpace(servingDesc)) servingDesc = $"{servingSize:0}g";

            double kcal = 0, protein = 0, fat = 0, satFat = 0, carbs = 0, sugars = 0, fibre = 0, sodiumMg = 0;

            if (f.TryGetProperty("foodNutrients", out var nutrients) && nutrients.ValueKind == JsonValueKind.Array)
            {
                foreach (var n in nutrients.EnumerateArray())
                {
                    var id = GetInt(n, "nutrientId");
                    var num = GetString(n, "nutrientNumber");
                    var unit = GetString(n, "unitName");
                    var val = GetDouble(n, "value");

                    if (id == 1008 || (num == "208" && !string.Equals(unit, "kJ", StringComparison.OrdinalIgnoreCase)))
                        kcal = val;
                    else if (id == 1003 || num == "203")
                        protein = val;
                    else if (id == 1004 || num == "204")
                        fat = val;
                    else if (id == 1258 || num == "606")
                        satFat = val;
                    else if (id == 1005 || num == "205")
                        carbs = val;
                    else if (id == 2000 || num == "269")
                        sugars = val;
                    else if (id == 1079 || num == "291")
                        fibre = val;
                    else if (id == 1093 || num == "307")
                        sodiumMg = val;
                }
            }

            // Convert sodium (mg/100g) to salt (g/100g): salt = sodium_g * 2.5
            var saltG = sodiumMg > 0 ? Math.Round(sodiumMg / 1000.0 * 2.5, 3) : 0;

            list.Add(new FoodItem
            {
                Id = Guid.NewGuid(),
                Name = CleanTitleCase(desc.Trim()),
                Brand = string.IsNullOrWhiteSpace(brand) ? "USDA" : brand.Trim(),
                Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(),
                ExternalId = !string.IsNullOrWhiteSpace(fdcId) ? $"usda:{fdcId}" : $"usda:{desc.Trim().ToLowerInvariant()}",
                Source = FoodSource.OpenFoodFacts,
                ServingSizeG = Math.Round(servingSize, 1),
                ServingDescription = servingDesc.Trim(),
                CaloriesPer100G = Math.Round(kcal, 1),
                FatPer100G = Math.Round(fat, 2),
                SaturatedFatPer100G = Math.Round(satFat, 2),
                CarbsPer100G = Math.Round(carbs, 2),
                SugarsPer100G = Math.Round(sugars, 2),
                FibrePer100G = Math.Round(fibre, 2),
                ProteinPer100G = Math.Round(protein, 2),
                SaltPer100G = saltG
            });
        }

        return list;
    }

    private static List<FoodItem> ParseOffProductList(JsonElement root)
    {
        if (!root.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            return new List<FoodItem>();

        var list = new List<FoodItem>();
        foreach (var prod in products.EnumerateArray())
        {
            var parsed = ParseOffProduct(prod, null);
            if (parsed is not null)
                list.Add(parsed);
        }
        return list;
    }

    private static FoodItem? ParseOffProduct(JsonElement prod, string? fallbackBarcode)
    {
        var name = GetString(prod, "product_name");
        if (string.IsNullOrWhiteSpace(name)) return null;

        var code = GetString(prod, "code") ?? fallbackBarcode;
        var brand = GetBrandString(prod);

        var servingQty = GetDouble(prod, "serving_quantity");
        var servingDesc = GetString(prod, "serving_size");
        if (servingQty <= 0) servingQty = 100;
        if (string.IsNullOrWhiteSpace(servingDesc)) servingDesc = $"{servingQty:0}g";

        double kcal = 0, fat = 0, satFat = 0, carbs = 0, sugars = 0, fibre = 0, protein = 0, salt = 0;
        var hasNutriments = false;

        if (prod.TryGetProperty("nutriments", out var nut) && nut.ValueKind == JsonValueKind.Object)
        {
            hasNutriments = true;
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

        if (!hasNutriments)
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

    private static bool IsJson(HttpResponseMessage resp)
    {
        var media = resp.Content.Headers.ContentType?.MediaType;
        return media is not null && media.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetBrandString(JsonElement prod)
    {
        if (!prod.TryGetProperty("brands", out var b)) return null;
        if (b.ValueKind == JsonValueKind.String)
        {
            var s = b.GetString();
            if (!string.IsNullOrWhiteSpace(s) && s.Contains(','))
                return s.Split(',')[0].Trim();
            return s;
        }
        if (b.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in b.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    return item.GetString();
            }
        }
        return null;
    }

    private static string CleanTitleCase(string s)
    {
        // Convert ALL-CAPS USDA descriptions like "SUGAR" into cleaner "Sugar" while preserving mixed case
        if (s.Length > 2 && s == s.ToUpperInvariant())
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        }
        return s;
    }

    private static string? GetString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    private static int GetInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
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
