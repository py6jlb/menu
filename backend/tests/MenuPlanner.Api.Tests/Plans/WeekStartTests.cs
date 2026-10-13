using Xunit;
using MenuPlanner.Domain.Plans;

namespace MenuPlanner.Api.Tests.Plans;

public sealed class WeekStartTests
{
    [Fact]
    public void Start_NormalizesMidWeekDateToMonday()
    {
        // Четверг 2026-01-08 → понедельник той же недели.
        Assert.Equal(new DateOnly(2026, 1, 5), WeekStart.Start(new DateOnly(2026, 1, 8)));
        Assert.Equal(new DateOnly(2026, 1, 5), WeekStart.Start(new DateOnly(2026, 1, 5)));
        // Воскресенье принадлежит неделе, начавшейся в понедельник.
        Assert.Equal(new DateOnly(2026, 1, 5), WeekStart.Start(new DateOnly(2026, 1, 11)));
    }

    [Fact]
    public void Window_EndsAtSelectedWeek_Inclusive_NotServerCurrentWeek()
    {
        var selected = new DateOnly(2026, 1, 26);

        var (start, end) = WeekStart.Window(selected, 3);

        Assert.Equal(selected, end);
        Assert.Equal(new DateOnly(2026, 1, 12), start);
    }

    [Fact]
    public void Window_SingleWeek_IsJustTheSelectedWeek()
    {
        var selected = new DateOnly(2026, 1, 26);

        var (start, end) = WeekStart.Window(selected, 1);

        Assert.Equal(selected, start);
        Assert.Equal(selected, end);
    }

    [Fact]
    public void TryParse_AcceptsMondayInStrictFormat()
    {
        Assert.True(WeekStart.TryParse("2026-01-05", out var monday));
        Assert.Equal(new DateOnly(2026, 1, 5), monday);
    }

    [Fact]
    public void TryParse_RejectsNonMonday()
    {
        // Дата в верном формате, но это вторник: неделя отклоняется. out может нести
        // разобранную дату — значащим он становится только при true.
        Assert.False(WeekStart.TryParse("2026-01-06", out _));
    }

    [Theory]
    [InlineData("2026-1-5")]   // не строгий формат
    [InlineData("05.01.2026")] // другой формат
    [InlineData("не дата")]
    [InlineData("")]
    public void TryParse_RejectsWrongFormat_WithDefault(string value)
    {
        Assert.False(WeekStart.TryParse(value, out var monday));
        Assert.Equal(default, monday);
    }

    [Fact]
    public void Format_UsesStrictIsoDate()
    {
        Assert.Equal("2026-01-05", WeekStart.Format(new DateOnly(2026, 1, 5)));
    }
}
