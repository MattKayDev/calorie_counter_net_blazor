using CalorieTracker.Domain.Enums;

namespace CalorieTracker.Domain.Calculations;

public sealed record GoalProjectionResult(
    bool AlreadyReached,
    bool IsMovingTowardGoal,
    double DailyDeficitOrSurplusKcal,
    double ProjectedWeeklyChangeKg,
    int? DaysToGoal,
    DateOnly? EstimatedDate);

public static class GoalWeightCalculator
{
    public const double KcalPerKgBodyMass = 7700.0;

    public static double GetActivityMultiplier(ActivityLevel level) => level switch
    {
        ActivityLevel.Sedentary => 1.2,
        ActivityLevel.Light => 1.375,
        ActivityLevel.Moderate => 1.55,
        ActivityLevel.Active => 1.725,
        ActivityLevel.VeryActive => 1.9,
        _ => 1.55
    };

    /// <summary>
    /// Mifflin-St Jeor Basal Metabolic Rate (kcal/day).
    /// </summary>
    public static double? CalculateBmr(double? weightKg, double? heightCm, int? ageYears, BiologicalSex sex)
    {
        if (weightKg is null or <= 0 || heightCm is null or <= 0 || ageYears is null or <= 0)
            return null;

        var baseBmr = 10.0 * weightKg.Value + 6.25 * heightCm.Value - 5.0 * ageYears.Value;
        var offset = sex switch
        {
            BiologicalSex.Male => 5.0,
            BiologicalSex.Female => -161.0,
            _ => -78.0
        };
        return Math.Round(baseBmr + offset, 0);
    }

    public static double? CalculateTdee(double? weightKg, double? heightCm, int? ageYears, BiologicalSex sex, ActivityLevel level)
    {
        var bmr = CalculateBmr(weightKg, heightCm, ageYears, sex);
        if (bmr is null) return null;
        return Math.Round(bmr.Value * GetActivityMultiplier(level), 0);
    }

    /// <summary>
    /// Recommended daily calorie intake based on TDEE and desired weekly weight change (negative for loss, positive for gain).
    /// Clamped to a safe minimum of 1200 kcal/day.
    /// </summary>
    public static double CalculateRecommendedDailyCalories(double tdee, double weeklyWeightChangeKg)
    {
        var dailyDelta = weeklyWeightChangeKg * KcalPerKgBodyMass / 7.0;
        return Math.Round(Math.Max(1200.0, tdee + dailyDelta), 0);
    }

    /// <summary>
    /// Aligns the weekly weight-change pace sign with the target direction (losing, gaining, or maintaining).
    /// Returns a value matching one of the UI weekly pace presets.
    /// </summary>
    public static double AlignWeeklyPaceToGoal(double currentWeightKg, double goalWeightKg, double currentPaceKgPerWeek)
    {
        var diff = goalWeightKg - currentWeightKg;
        if (Math.Abs(diff) < 0.05) return 0.0;

        if (diff < 0)
        {
            if (currentPaceKgPerWeek < 0) return currentPaceKgPerWeek;
            var mag = Math.Abs(currentPaceKgPerWeek);
            return mag > 0 ? -mag : -0.5;
        }

        if (currentPaceKgPerWeek > 0) return currentPaceKgPerWeek;
        var gainMag = Math.Min(0.5, Math.Abs(currentPaceKgPerWeek));
        return gainMag > 0 ? gainMag : 0.25;
    }

    /// <summary>
    /// Projects when a user will reach their goal weight given their current weight, TDEE, and daily calorie intake.
    /// </summary>
    public static GoalProjectionResult ProjectGoalDate(
        DateOnly fromDate,
        double currentWeightKg,
        double goalWeightKg,
        double tdee,
        double dailyCalorieIntake)
    {
        var kgRemaining = goalWeightKg - currentWeightKg; // negative if losing, positive if gaining
        var dailyDeltaKcal = dailyCalorieIntake - tdee;   // negative if deficit, positive if surplus
        var weeklyChangeKg = Math.Round(dailyDeltaKcal * 7.0 / KcalPerKgBodyMass, 2);

        if (Math.Abs(kgRemaining) < 0.05)
        {
            return new GoalProjectionResult(
                AlreadyReached: true,
                IsMovingTowardGoal: true,
                DailyDeficitOrSurplusKcal: Math.Round(dailyDeltaKcal, 0),
                ProjectedWeeklyChangeKg: weeklyChangeKg,
                DaysToGoal: 0,
                EstimatedDate: fromDate);
        }

        var wantsLoss = kgRemaining < 0;
        var movingToward = (wantsLoss && dailyDeltaKcal < -10) || (!wantsLoss && dailyDeltaKcal > 10);
        if (!movingToward)
        {
            return new GoalProjectionResult(
                AlreadyReached: false,
                IsMovingTowardGoal: false,
                DailyDeficitOrSurplusKcal: Math.Round(dailyDeltaKcal, 0),
                ProjectedWeeklyChangeKg: weeklyChangeKg,
                DaysToGoal: null,
                EstimatedDate: null);
        }

        var totalKcalNeeded = Math.Abs(kgRemaining) * KcalPerKgBodyMass;
        var days = (int)Math.Ceiling(totalKcalNeeded / Math.Abs(dailyDeltaKcal));
        days = Math.Clamp(days, 1, 3650); // cap at 10 years

        return new GoalProjectionResult(
            AlreadyReached: false,
            IsMovingTowardGoal: true,
            DailyDeficitOrSurplusKcal: Math.Round(dailyDeltaKcal, 0),
            ProjectedWeeklyChangeKg: weeklyChangeKg,
            DaysToGoal: days,
            EstimatedDate: fromDate.AddDays(days));
    }
}
