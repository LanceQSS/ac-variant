namespace Acvc.Gui.ViewModels;

/// <summary>The selected car's stock values — the no-op baseline every control resets to.</summary>
public sealed record StockState(
    int Limiter,
    double? BoostMax,
    double? BoostWastegate,
    double FinalDrive,
    double Mass,
    double? DiffPower,
    double? DiffCoast,
    bool HasTurbo,
    bool HasDifferential,
    bool HasTyres,
    bool HasBrakes);
