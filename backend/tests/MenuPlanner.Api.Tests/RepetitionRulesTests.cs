using MenuPlanner.Api.Recipes.Repetition;
using Xunit;

namespace MenuPlanner.Api.Tests;

public sealed class RepetitionRulesTests
{
    [Fact]
    public void WeekStart_NormalizesMidWeekDateToMonday()
    {
        // Четверг 2026-01-08 → понедельник той же недели.
        Assert.Equal(new DateOnly(2026, 1, 5), RepetitionRules.WeekStart(new DateOnly(2026, 1, 8)));
        Assert.Equal(new DateOnly(2026, 1, 5), RepetitionRules.WeekStart(new DateOnly(2026, 1, 5)));
        // Воскресенье принадлежит неделе, начавшейся в понедельник.
        Assert.Equal(new DateOnly(2026, 1, 5), RepetitionRules.WeekStart(new DateOnly(2026, 1, 11)));
    }

    [Fact]
    public void Window_EndsAtSelectedWeek_Inclusive_NotServerCurrentWeek()
    {
        var selected = new DateOnly(2026, 1, 26);

        var (start, end) = RepetitionRules.Window(selected, 3);

        Assert.Equal(selected, end);
        Assert.Equal(new DateOnly(2026, 1, 12), start);
    }

    [Fact]
    public void Window_SingleWeek_IsJustTheSelectedWeek()
    {
        var selected = new DateOnly(2026, 1, 26);

        var (start, end) = RepetitionRules.Window(selected, 1);

        Assert.Equal(selected, start);
        Assert.Equal(selected, end);
    }
}
