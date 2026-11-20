# Calorie Tracker (Blazor WebAssembly PWA)

A free, local-first, mobile-friendly calorie, nutrient, and body-metrics tracker inspired by **Nutracheck**, built with **.NET 10 Blazor WebAssembly (PWA)** and **local SQLite**.

## Features
- **Daily Calorie & Nutrient Diary:** Track calories, protein, carbs, fat, saturated fat, sugars, fibre, and salt with a Nutracheck-inspired calorie ring and macro bars.
- **Full History Calendar:** Jump to any past day or month to review daily calorie totals and logged foods.
- **Custom Meal Times / Slots:** Start with Breakfast, Lunch, Dinner, and Snacks — add, customize, or archive your own slots anytime.
- **Custom Foods & Saved Meals:** Create custom foods (per 100g) and multi-ingredient reusable meals.
- **Local-First Food Search & Barcode Scanner:**
  - Barcode scans check your local SQLite database first and only query **Open Food Facts** if missing, saving the product locally.
  - Name searches return local SQLite matches immediately while automatically querying **Open Food Facts** in addition and saving all returned items into SQLite so your local database grows richer over time.
- **Body Metrics, BMI & Body Fat %:** Track weight, height, and tape measurements (neck, waist, hip) with automatic **BMI** and **US Navy Body Fat %** estimates (plus optional manual smart-scale Body Fat % entry).
- **Metric & Imperial Support:** Stores everything canonically in metric while letting you switch display units anytime (Metric / UK Imperial / US Imperial).
- **Local SQLite Backup & Restore:** Export and restore your genuine `.sqlite` database file anytime.

## Architecture (Future-Proofed)
- `src/CalorieTracker.Domain` — Pure C# models, repository interfaces, unit conversions, BMI & Body Fat % math.
- `src/CalorieTracker.Data.Sqlite` — SQLite repositories behind `ISqliteEngine` (sync-ready `Guid` IDs, `UpdatedAtUtc`, soft deletes).
- `src/CalorieTracker.Services` — Open Food Facts API client, local-first `FoodLookupService`, `DiaryService`, platform service interfaces.
- `src/CalorieTracker.UI` — Shared Razor Class Library (ready to drop into a future **.NET MAUI Blazor Hybrid** app).
- `src/CalorieTracker.Web` — Thin Blazor WebAssembly PWA host (`sql.js` + IndexedDB persistence, `html5-qrcode` camera scanner, Hostinger `.htaccess`).
- `tests/CalorieTracker.Tests` — Unit & SQLite integration tests.

## Running Locally
```bash
dotnet run --project src/CalorieTracker.Web
```
Then open the displayed `http://localhost:...` URL in your browser (or mobile emulator).

## Running Tests
```bash
dotnet test
```

## Deploying to Hostinger (Free Static Hosting)
1. Publish the static Blazor WebAssembly bundle:
   ```bash
   dotnet publish src/CalorieTracker.Web -c Release -o publish
   ```
2. Upload the contents of `publish/wwwroot/` (including the `.htaccess` file) to your Hostinger `public_html` directory.
3. Ensure HTTPS is enabled on your Hostinger domain (required by mobile browsers for PWA installation and camera barcode scanning).
