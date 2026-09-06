using System.Globalization;

namespace MenuPlanner.Api.ShoppingList;

public enum UnitGroup
{
    Weight,
    Volume,
    Pieces,
    Household
}

public sealed record IngredientLine(string Name, decimal Amount, string Unit);

public sealed record ShoppingListItem(string Name, decimal Amount, string Unit, string Display);

public static class ShoppingListBuilder
{
    private const decimal BaseUnitThreshold = 1000m;

    public static IReadOnlyList<ShoppingListItem> Build(IReadOnlyList<IngredientLine> lines)
    {
        var buckets = new Dictionary<(string Normalized, string Key), Bucket>();

        foreach (var line in lines)
        {
            var name = line.Name.Trim();
            var normalized = name.ToLowerInvariant();
            var group = UnitGroupOf(line.Unit);
            var bucketKey = group == UnitGroup.Household ? line.Unit : group.ToString();

            if (!buckets.TryGetValue((normalized, bucketKey), out var bucket))
            {
                bucket = new Bucket(name, normalized, group, line.Unit);
                buckets.Add((normalized, bucketKey), bucket);
            }

            bucket.Amount += ToBase(line.Amount, line.Unit, group);
        }

        return buckets.Values
            .OrderBy(b => (int)b.Group)
            .ThenBy(b => b.Normalized, StringComparer.Ordinal)
            .ThenBy(b => b.Unit, StringComparer.Ordinal)
            .Select(ToItem)
            .ToList();
    }

    public static decimal Scale(decimal amount, int portions, int servings)
    {
        if (servings <= 0)
            throw new ArgumentOutOfRangeException(nameof(servings), "Количество порций рецепта должно быть больше нуля.");
        return amount * portions / servings;
    }

    public static UnitGroup UnitGroupOf(string unit) => unit switch
    {
        "g" or "kg" => UnitGroup.Weight,
        "ml" or "l" => UnitGroup.Volume,
        "pcs" => UnitGroup.Pieces,
        _ => UnitGroup.Household
    };

    private static decimal ToBase(decimal amount, string unit, UnitGroup group) => group switch
    {
        UnitGroup.Weight => unit == "kg" ? amount * BaseUnitThreshold : amount,
        UnitGroup.Volume => unit == "l" ? amount * BaseUnitThreshold : amount,
        _ => amount
    };

    private static ShoppingListItem ToItem(Bucket bucket) => bucket.Group switch
    {
        UnitGroup.Weight when bucket.Amount >= BaseUnitThreshold =>
            Item(bucket, bucket.Amount / BaseUnitThreshold, "kg", $"{Format(bucket.Amount / BaseUnitThreshold)} кг"),
        UnitGroup.Weight =>
            Item(bucket, bucket.Amount, "g", $"{Format(bucket.Amount)} г"),
        UnitGroup.Volume when bucket.Amount >= BaseUnitThreshold =>
            Item(bucket, bucket.Amount / BaseUnitThreshold, "l", $"{Format(bucket.Amount / BaseUnitThreshold)} л"),
        UnitGroup.Volume =>
            Item(bucket, bucket.Amount, "ml", $"{Format(bucket.Amount)} мл"),
        UnitGroup.Pieces =>
            Item(bucket, bucket.Amount, "pcs", $"{Format(bucket.Amount)} шт"),
        _ =>
            Item(bucket, bucket.Amount, bucket.Unit, $"{Format(bucket.Amount)} {RussianLabel(bucket.Unit, bucket.Amount)}")
    };

    private static ShoppingListItem Item(Bucket bucket, decimal amount, string unit, string display) =>
        new(bucket.Name, amount, unit, display);

    private static string Format(decimal value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string RussianLabel(string unit, decimal amount) => unit switch
    {
        "glass" => RussianPlural(amount, "стакан", "стакана", "стаканов"),
        "tbsp" => RussianPlural(amount, "ст. ложка", "ст. ложки", "ст. ложек"),
        "tsp" => RussianPlural(amount, "ч. ложка", "ч. ложки", "ч. ложек"),
        "pinch" => RussianPlural(amount, "щепотка", "щепотки", "щепоток"),
        _ => unit
    };

    private static string RussianPlural(decimal value, string one, string few, string many)
    {
        var absolute = Math.Abs(value);
        if (absolute != Math.Truncate(absolute))
            return few;

        var lastTwo = (long)(absolute % 100);
        var lastOne = lastTwo % 10;

        if (lastTwo is >= 11 and <= 14)
            return many;
        if (lastOne == 1)
            return one;
        if (lastOne is >= 2 and <= 4)
            return few;
        return many;
    }

    private sealed class Bucket
    {
        public Bucket(string name, string normalized, UnitGroup group, string unit)
        {
            Name = name;
            Normalized = normalized;
            Group = group;
            Unit = unit;
        }

        public string Name { get; }
        public string Normalized { get; }
        public UnitGroup Group { get; }
        public string Unit { get; }
        public decimal Amount { get; set; }
    }
}