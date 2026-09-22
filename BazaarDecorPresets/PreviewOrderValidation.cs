namespace BazaarDecorPresets;

internal static class PreviewOrderValidation
{
    internal static T[] Validate<T>(IReadOnlyList<T[]> rows)
    {
        if (rows == null || rows.Count == 0 || rows[0] == null || rows[0].Length != rows.Count)
            throw new InvalidOperationException("Preview row count and category count do not match.");
        var order = rows[0];
        if (order.Distinct().Count() != order.Length || rows.Any(row => row == null || !row.SequenceEqual(order)))
            throw new InvalidOperationException("Preview rows disagree on their category order.");
        return order.ToArray();
    }
}
