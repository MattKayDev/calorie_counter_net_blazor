using CalorieTracker.Domain.Enums;

namespace CalorieTracker.Domain.Calculations;

public static class BodyMetricsCalculator
{
    public static double? CalculateBmi(double? weightKg, double? heightCm)
    {
        if (weightKg is null or <= 0 || heightCm is null or <= 0)
            return null;

        var heightM = heightCm.Value / 100.0;
        var bmi = weightKg.Value / (heightM * heightM);
        return Math.Round(bmi, 1);
    }

    public static string GetBmiCategory(double? bmi) => bmi switch
    {
        null => "—",
        < 18.5 => "Underweight",
        < 25.0 => "Healthy",
        < 30.0 => "Overweight",
        _ => "Obese"
    };

    /// <summary>
    /// Calculates body fat percentage using the US Navy circumference method (metric cm inputs).
    /// Male requires height, neck, waist. Female requires height, neck, waist, hip.
    /// </summary>
    public static double? CalculateUsNavyBodyFat(
        BiologicalSex sex,
        double? heightCm,
        double? neckCm,
        double? waistCm,
        double? hipCm = null)
    {
        if (heightCm is null or <= 0 || neckCm is null or <= 0 || waistCm is null or <= 0)
            return null;

        if (sex == BiologicalSex.Male)
        {
            var diff = waistCm.Value - neckCm.Value;
            if (diff <= 0) return null;

            var bf = 495.0 / (1.0324 - 0.19077 * Math.Log10(diff) + 0.15456 * Math.Log10(heightCm.Value)) - 450.0;
            return ClampBodyFat(bf);
        }

        if (sex == BiologicalSex.Female)
        {
            if (hipCm is null or <= 0) return null;
            var sumDiff = waistCm.Value + hipCm.Value - neckCm.Value;
            if (sumDiff <= 0) return null;

            var bf = 495.0 / (1.29579 - 0.35004 * Math.Log10(sumDiff) + 0.22100 * Math.Log10(heightCm.Value)) - 450.0;
            return ClampBodyFat(bf);
        }

        return null;
    }

    private static double? ClampBodyFat(double bf)
    {
        if (double.IsNaN(bf) || double.IsInfinity(bf))
            return null;
        return Math.Round(Math.Clamp(bf, 2.0, 70.0), 1);
    }
}
