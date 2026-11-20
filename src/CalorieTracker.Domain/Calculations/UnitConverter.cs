using CalorieTracker.Domain.Enums;

namespace CalorieTracker.Domain.Calculations;

public static class UnitConverter
{
    private const double LbsPerKg = 2.20462262185;
    private const double CmPerInch = 2.54;

    public static double KgToLbs(double kg) => Math.Round(kg * LbsPerKg, 1);
    public static double LbsToKg(double lbs) => Math.Round(lbs / LbsPerKg, 2);

    public static (int Stones, double Pounds) KgToStonesAndPounds(double kg)
    {
        var totalLbs = kg * LbsPerKg;
        var stones = (int)Math.Floor(totalLbs / 14.0);
        var pounds = Math.Round(totalLbs - stones * 14.0, 1);
        if (pounds >= 14.0)
        {
            stones += 1;
            pounds = 0;
        }
        return (stones, pounds);
    }

    public static double StonesAndPoundsToKg(int stones, double pounds) =>
        LbsToKg(stones * 14.0 + pounds);

    public static double CmToInches(double cm) => Math.Round(cm / CmPerInch, 1);
    public static double InchesToCm(double inches) => Math.Round(inches * CmPerInch, 1);

    public static (int Feet, double Inches) CmToFeetAndInches(double cm)
    {
        var totalInches = cm / CmPerInch;
        var feet = (int)Math.Floor(totalInches / 12.0);
        var inches = Math.Round(totalInches - feet * 12.0, 1);
        if (inches >= 12.0)
        {
            feet += 1;
            inches = 0;
        }
        return (feet, inches);
    }

    public static double FeetAndInchesToCm(int feet, double inches) =>
        InchesToCm(feet * 12.0 + inches);

    public static string FormatWeight(double? kg, UnitSystem unitSystem)
    {
        if (kg is null) return "—";
        return unitSystem switch
        {
            UnitSystem.Metric => $"{kg.Value:0.0} kg",
            UnitSystem.UkImperial => FormatUkWeight(kg.Value),
            UnitSystem.UsImperial => $"{KgToLbs(kg.Value):0.0} lbs",
            _ => $"{kg.Value:0.0} kg"
        };
    }

    private static string FormatUkWeight(double kg)
    {
        var (st, lbs) = KgToStonesAndPounds(kg);
        return $"{st} st {lbs:0.0} lb";
    }

    public static string FormatHeight(double? cm, UnitSystem unitSystem)
    {
        if (cm is null) return "—";
        if (unitSystem == UnitSystem.Metric)
            return $"{cm.Value:0.0} cm";

        var (ft, inches) = CmToFeetAndInches(cm.Value);
        return $"{ft}' {inches:0.0}\"";
    }

    public static string FormatLength(double? cm, UnitSystem unitSystem)
    {
        if (cm is null) return "—";
        return unitSystem == UnitSystem.Metric
            ? $"{cm.Value:0.0} cm"
            : $"{CmToInches(cm.Value):0.0} in";
    }

    public static string WeightUnitLabel(UnitSystem unitSystem) => unitSystem switch
    {
        UnitSystem.Metric => "kg",
        UnitSystem.UkImperial => "lbs",
        UnitSystem.UsImperial => "lbs",
        _ => "kg"
    };

    public static string LengthUnitLabel(UnitSystem unitSystem) =>
        unitSystem == UnitSystem.Metric ? "cm" : "in";
}
