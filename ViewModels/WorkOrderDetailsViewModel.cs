using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.ViewModels;

public sealed class WorkOrderDetailsViewModel
{
    public required WorkOrder Order { get; init; }

    public IReadOnlyList<MaterialReservation> Reservations { get; init; } =
        Array.Empty<MaterialReservation>();

    public ProductionCostAnalysisViewModel CostAnalysis { get; init; } = new();
}

public sealed class ProductionCostAnalysisViewModel
{
    public CostComparisonViewModel MaterialCost { get; init; } = new(0m, 0m);

    public CostComparisonViewModel LaborCost { get; init; } = new(0m, 0m);

    public CostComparisonViewModel MachineCost { get; init; } = new(0m, 0m);

    public CostComparisonViewModel TotalCost { get; init; } = new(0m, 0m);

    public CostComparisonViewModel UnitCost { get; init; } = new(0m, 0m);
}

public sealed record CostComparisonViewModel
{
    public CostComparisonViewModel(decimal target, decimal actual)
    {
        Target = RoundCurrency(target);
        Actual = RoundCurrency(actual);
        Variance = RoundCurrency(Actual - Target);
    }

    public decimal Target { get; }

    public decimal Actual { get; }

    public decimal Variance { get; }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
