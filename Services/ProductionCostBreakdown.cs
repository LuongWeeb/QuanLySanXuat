namespace WmsMes.Web.Services;

internal readonly record struct ProductionCostBreakdown(
    decimal Material,
    decimal Labor,
    decimal Machine,
    decimal Total)
{
    public static ProductionCostBreakdown FromRaw(
        decimal material,
        decimal labor,
        decimal machine)
    {
        var rounded = new[]
        {
            RoundCurrency(material),
            RoundCurrency(labor),
            RoundCurrency(machine)
        };
        var raw = new[] { material, labor, machine };
        var total = RoundCurrency(material + labor + machine);
        var residual = total - rounded.Sum();
        if (residual != 0m)
        {
            var largestIndex = Array.IndexOf(raw, raw.Max());
            rounded[largestIndex] += residual;
        }

        return new ProductionCostBreakdown(
            rounded[0],
            rounded[1],
            rounded[2],
            total);
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
