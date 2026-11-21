namespace CalorieTracker.Domain.Enums;

public enum UnitSystem
{
    Metric = 0,
    UkImperial = 1,
    UsImperial = 2
}

public enum BiologicalSex
{
    Unspecified = 0,
    Male = 1,
    Female = 2
}

public enum FoodSource
{
    Custom = 0,
    OpenFoodFacts = 1
}

public enum ActivityLevel
{
    Sedentary = 0,     // 1.2
    Light = 1,         // 1.375
    Moderate = 2,      // 1.55
    Active = 3,        // 1.725
    VeryActive = 4     // 1.9
}

public enum ThemeMode
{
    Light = 0,
    Dark = 1
}

public enum ColorScheme
{
    RoyalBlue = 0,
    Green = 1,
    Purple = 2,
    Orange = 3,
    Red = 4,
    Teal = 5,
    Rose = 6,
    Indigo = 7
}

public enum CalorieSummaryStyle
{
    Standard = 0,
    Compact = 1,
    Minimal = 2
}
