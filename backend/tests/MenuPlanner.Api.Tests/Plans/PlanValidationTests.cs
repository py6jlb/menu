using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Plans;
using Xunit;

namespace MenuPlanner.Api.Tests.Plans;

/// <summary>
/// Прямые тесты чистых правил записей недельного плана — без HTTP. До выноса
/// эти ветки проверялись только сквозь <c>PlanFlowTests</c>.
/// </summary>
public sealed class PlanValidationTests
{
    [Fact]
    public void ValidEntries_ReturnNull()
    {
        var entries = new[]
        {
            new PlanEntryRequest(PlanningCatalog.DayMin, "breakfast", Guid.NewGuid(), PlanningCatalog.PortionsMin),
            new PlanEntryRequest(PlanningCatalog.DayMax, "dinner", Guid.NewGuid(), PlanningCatalog.PortionsMax)
        };

        Assert.Null(PlanValidation.ValidateEntries(entries));
    }

    [Fact]
    public void AllKnownMealTypes_AreAccepted()
    {
        var entries = PlanningCatalog.MealTypeCodes
            .Select(code => new PlanEntryRequest(0, code, Guid.NewGuid(), 1))
            .ToList();

        Assert.Null(PlanValidation.ValidateEntries(entries));
    }

    [Fact]
    public void TooManyEntries_ReturnLimitError()
    {
        // На один больше, чем слотов недели (7 дней × 5 приёмов).
        var entries = Enumerable.Range(0, PlanningCatalog.EntriesMax + 1)
            .Select(_ => new PlanEntryRequest(0, "breakfast", Guid.NewGuid(), 1))
            .ToList();

        var error = PlanValidation.ValidateEntries(entries);

        Assert.NotNull(error);
        Assert.Equal("plan_entries_too_many", error!.Code);
        Assert.Equal("entries", error.Field);
        Assert.Equal($"В плане не может быть больше {PlanningCatalog.EntriesMax} записей.", error.Error);
    }

    [Fact]
    public void TooManyEntries_TakesPrecedenceOverOtherViolations()
    {
        // Порядок проверок зафиксирован: лимит записей отсекается первым, даже
        // когда записи ещё и невалидны.
        var entries = Enumerable.Range(0, PlanningCatalog.EntriesMax + 1)
            .Select(_ => new PlanEntryRequest(-1, "brunch", Guid.NewGuid(), 0))
            .ToList();

        var error = PlanValidation.ValidateEntries(entries);

        Assert.Equal("plan_entries_too_many", error!.Code);
    }

    [Fact]
    public void NullEntry_ReturnsControlledError()
    {
        var entries = new List<PlanEntryRequest> { null! };

        var error = PlanValidation.ValidateEntries(entries);

        Assert.NotNull(error);
        Assert.Equal("plan_entry_null", error!.Code);
        Assert.Equal("entries[0]", error.Field);
        Assert.Equal("Запись плана пуста.", error.Error);
    }

    [Fact]
    public void NullEntry_ReportsItsOwnIndex()
    {
        var entries = new List<PlanEntryRequest>
        {
            new(0, "breakfast", Guid.NewGuid(), 1),
            null!
        };

        var error = PlanValidation.ValidateEntries(entries);

        Assert.Equal("plan_entry_null", error!.Code);
        Assert.Equal("entries[1]", error.Field);
    }

    [Theory]
    [InlineData(PlanningCatalog.DayMin - 1)]
    [InlineData(PlanningCatalog.DayMax + 1)]
    public void DayOutOfRange_ReturnsDayError(int day)
    {
        var entries = new[] { new PlanEntryRequest(day, "breakfast", Guid.NewGuid(), 1) };

        var error = PlanValidation.ValidateEntries(entries);

        Assert.NotNull(error);
        Assert.Equal("plan_day_range", error!.Code);
        Assert.Equal("entries[0].day", error.Field);
        Assert.Equal(
            $"Номер дня недели должен быть от {PlanningCatalog.DayMin} до {PlanningCatalog.DayMax}.",
            error.Error);
    }

    [Fact]
    public void UnknownMealType_ReturnsMealTypeError()
    {
        var entries = new[] { new PlanEntryRequest(0, "brunch", Guid.NewGuid(), 1) };

        var error = PlanValidation.ValidateEntries(entries);

        Assert.NotNull(error);
        Assert.Equal("plan_meal_type_invalid", error!.Code);
        Assert.Equal("entries[0].mealType", error.Field);
        Assert.Equal("Недопустимый приём пищи «brunch».", error.Error);
    }

    [Theory]
    [InlineData(PlanningCatalog.PortionsMin - 1)]
    [InlineData(PlanningCatalog.PortionsMax + 1)]
    public void PortionsOutOfRange_ReturnsPortionsError(int portions)
    {
        var entries = new[] { new PlanEntryRequest(0, "breakfast", Guid.NewGuid(), portions) };

        var error = PlanValidation.ValidateEntries(entries);

        Assert.NotNull(error);
        Assert.Equal("plan_portions_range", error!.Code);
        Assert.Equal("entries[0].portions", error.Field);
        Assert.Equal(
            $"Количество порций должно быть от {PlanningCatalog.PortionsMin} до {PlanningCatalog.PortionsMax}.",
            error.Error);
    }

    [Fact]
    public void DuplicateDayAndMealType_ReturnsSlotError()
    {
        var entries = new[]
        {
            new PlanEntryRequest(0, "breakfast", Guid.NewGuid(), 1),
            new PlanEntryRequest(0, "breakfast", Guid.NewGuid(), 2)
        };

        var error = PlanValidation.ValidateEntries(entries);

        Assert.NotNull(error);
        Assert.Equal("plan_duplicate_slot", error!.Code);
        Assert.Equal("entries[1]", error.Field);
        Assert.Equal("В плане не может быть двух записей для одного дня и приёма пищи.", error.Error);
    }

    [Fact]
    public void DifferentMealTypeOnSameDay_IsNotDuplicate()
    {
        var entries = new[]
        {
            new PlanEntryRequest(0, "breakfast", Guid.NewGuid(), 1),
            new PlanEntryRequest(0, "lunch", Guid.NewGuid(), 1)
        };

        Assert.Null(PlanValidation.ValidateEntries(entries));
    }
}
