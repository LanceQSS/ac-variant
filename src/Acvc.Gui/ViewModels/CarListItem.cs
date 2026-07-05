using Acvc.Core.Survey;

namespace Acvc.Gui.ViewModels;

/// <summary>Picker row: name + badge; encrypted/broken cars stay visible but grayed.</summary>
public sealed class CarListItem
{
    public CarListItem(CatalogCar car) => Car = car;

    public CatalogCar Car { get; }
    public string Name => Car.Name;

    public string Badge => Car.Classification switch
    {
        "encrypted" => "Encrypted",
        "broken-container" => "Broken",
        "no-data" => "No data",
        _ => Car.IsKunos ? "Kunos" : "Mod — best effort",
    };

    public bool IsSelectable => Car.IsBuildable;

    /// <summary>Core's refusal message, verbatim, as the tooltip for grayed cars.</summary>
    public string? Tooltip => Car.Reason;
}
