using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Acvc.Core.Acd;
using Acvc.Core.Emit;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Survey;
using Acvc.Core.Transforms;
using Acvc.Core.UiMeta;

namespace Acvc.Gui.ViewModels;

public sealed record SpecRow(string Label, string Stock, string Tuned, string Delta);
public sealed record ValidationRow(bool IsFailure, string Text);

/// <summary>
/// The window's whole state. Dumb-shell rule: everything computational here is a
/// straight call into Acvc.Core — the preview runs the REAL pipeline on the cached
/// in-memory car and displays the very UiSpecsPatch strings a build would write,
/// so preview and built output cannot diverge. WPF-free by design (testable).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const double Epsilon = 1e-9;

    // ---- car & cached data ----
    private IReadOnlyDictionary<string, byte[]>? _files;
    private StockState? _stock;
    private UiSpecsPatch? _stockPatch;
    private (double TorqueNm, double PowerBhp) _stockPeaks;
    private IReadOnlyList<PowerCurveRange>? _loadedCurve;  // from a loaded spec; no dedicated controls
    private IReadOnlyList<double>? _loadedGears;

    [ObservableProperty] private string _acPath = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private List<CarListItem> _cars = new();
    [ObservableProperty] private CarListItem? _selectedCar;
    [ObservableProperty] private bool _isBusy;

    // ---- controls (default = stock = no-op) ----
    [ObservableProperty] private string _tuneName = "";
    [ObservableProperty] private string _tuneNameError = "";
    [ObservableProperty] private double _powerScale = 1.0;
    [ObservableProperty] private string _limiterText = "";
    [ObservableProperty] private string _boostMaxText = "";
    [ObservableProperty] private string _boostWastegateText = "";
    [ObservableProperty] private string _finalDriveText = "";
    [ObservableProperty] private string _massText = "";
    [ObservableProperty] private double _gripScale = 1.0;
    [ObservableProperty] private double _brakeScale = 1.0;
    [ObservableProperty] private double _diffPower;
    [ObservableProperty] private double _diffCoast;
    [ObservableProperty] private bool _hasTurbo;
    [ObservableProperty] private bool _hasDifferential;
    [ObservableProperty] private string _loadedExtrasNote = "";

    // ---- stock/delta captions ----
    [ObservableProperty] private string _limiterStockText = "";
    [ObservableProperty] private string _boostStockText = "";
    [ObservableProperty] private string _finalStockText = "";
    [ObservableProperty] private string _massStockText = "";
    [ObservableProperty] private string _diffStockText = "";

    // ---- preview outputs ----
    [ObservableProperty] private List<SpecRow> _specRows = new();
    [ObservableProperty] private List<ValidationRow> _validationRows = new();
    [ObservableProperty] private IReadOnlyList<CurvePoint> _stockPoints = Array.Empty<CurvePoint>();
    [ObservableProperty] private IReadOnlyList<CurvePoint> _tunedPoints = Array.Empty<CurvePoint>();
    [ObservableProperty] private string _specText = "";
    [ObservableProperty] private bool _canBuild;

    /// <summary>Raised after a preview recompute so the view can redraw the plot.</summary>
    public event EventHandler? PreviewUpdated;

    public IEnumerable<CarListItem> FilteredCars
        => string.IsNullOrWhiteSpace(SearchText)
            ? Cars
            : Cars.Where(c => c.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredCars));
    partial void OnCarsChanged(List<CarListItem> value) => OnPropertyChanged(nameof(FilteredCars));

    // ---- car catalog ------------------------------------------------------------

    public async Task LoadCarsAsync()
    {
        if (string.IsNullOrWhiteSpace(AcPath))
            return;
        IsBusy = true;
        StatusText = "Scanning cars…";
        try
        {
            var acPath = AcPath;
            var list = await Task.Run(() => CarCatalog.List(acPath));
            Cars = list.Select(c => new CarListItem(c)).ToList();
            StatusText = $"{list.Count} cars ({list.Count(c => c.IsBuildable)} buildable)";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message; // Core message, verbatim
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectCarAsync(CarListItem item)
    {
        if (!item.IsSelectable)
            return;
        IsBusy = true;
        StatusText = $"Loading {item.Name}…";
        try
        {
            var loaded = await Task.Run(() =>
            {
                var files = CarDataLoader.Load(item.Car.Folder).Files;
                var models = CarModelSet.FromFiles(files);
                var patch = UiCarPatcher.BuildPatch(models);
                var points = PowerCurves.SampleGrid(models.Engine, models.PowerLut, 100);
                var peaks = PowerCurves.Peaks(points);
                var turbo = models.Engine.Turbos.FirstOrDefault();
                var stock = new StockState(
                    models.Engine.Limiter,
                    turbo?.MaxBoost,
                    turbo?.Wastegate,
                    models.Drivetrain.FinalRatio,
                    models.Car.TotalMass,
                    models.Drivetrain.HasDifferential ? models.Drivetrain.DiffPower : null,
                    models.Drivetrain.HasDifferential ? models.Drivetrain.DiffCoast : null,
                    models.Engine.HasTurbo,
                    models.Drivetrain.HasDifferential,
                    models.Tyres is not null,
                    models.Brakes is not null);
                return (files, patch, points, peaks, stock);
            });

            _files = loaded.files;
            _stockPatch = loaded.patch;
            _stockPeaks = (loaded.peaks.TorqueNm, loaded.peaks.PowerBhp);
            StockPoints = loaded.points;
            SelectedCar = item;
            ApplyStock(loaded.stock);
            _loadedCurve = null;
            _loadedGears = null;
            LoadedExtrasNote = "";
            StatusText = $"{item.Name} loaded ({item.Badge})";
            await RefreshPreviewAsync();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            _files = null;
            _stock = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Resets every control to the car's stock (no-op) values. Public for tests.</summary>
    public void ApplyStock(StockState stock)
    {
        _stock = stock;
        PowerScale = 1.0;
        LimiterText = stock.Limiter.ToString(CultureInfo.InvariantCulture);
        BoostMaxText = stock.BoostMax?.ToString(CultureInfo.InvariantCulture) ?? "";
        BoostWastegateText = stock.BoostWastegate?.ToString(CultureInfo.InvariantCulture) ?? "";
        FinalDriveText = stock.FinalDrive.ToString(CultureInfo.InvariantCulture);
        MassText = stock.Mass.ToString(CultureInfo.InvariantCulture);
        GripScale = 1.0;
        BrakeScale = 1.0;
        DiffPower = stock.DiffPower ?? 0;
        DiffCoast = stock.DiffCoast ?? 0;
        HasTurbo = stock.HasTurbo;
        HasDifferential = stock.HasDifferential;

        LimiterStockText = $"stock {stock.Limiter}";
        BoostStockText = stock.HasTurbo
            ? $"stock {stock.BoostMax?.ToString(CultureInfo.InvariantCulture)} / {stock.BoostWastegate?.ToString(CultureInfo.InvariantCulture)}"
            : "naturally aspirated — no [TURBO_n] section";
        FinalStockText = $"stock {stock.FinalDrive.ToString(CultureInfo.InvariantCulture)}";
        MassStockText = $"stock {stock.Mass.ToString(CultureInfo.InvariantCulture)} kg";
        DiffStockText = stock.HasDifferential
            ? $"stock {stock.DiffPower?.ToString(CultureInfo.InvariantCulture)} / {stock.DiffCoast?.ToString(CultureInfo.InvariantCulture)}"
            : "no [DIFFERENTIAL] section";
    }

    // ---- plan construction (pure; unit-tested) -------------------------------------

    /// <summary>
    /// Maps control state to a TunePlan: a control at its stock value contributes
    /// nothing, so untouched controls keep the plan a no-op. Input parse problems
    /// come back as errors, never exceptions.
    /// </summary>
    public TunePlan? TryBuildPlan(out List<string> inputErrors)
    {
        var errors = inputErrors = new List<string>();
        if (_stock is not { } stock || SelectedCar is null)
        {
            errors.Add("No car selected.");
            return null;
        }

        double? ParseChanged(string text, double stockValue, string label)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                errors.Add($"{label}: '{text}' is not a number.");
                return null;
            }
            return Math.Abs(value - stockValue) > Epsilon ? value : null;
        }

        int? limiter = null;
        if (!string.IsNullOrWhiteSpace(LimiterText))
        {
            if (!int.TryParse(LimiterText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                errors.Add($"Limiter: '{LimiterText}' is not an integer.");
            else if (value != stock.Limiter)
                limiter = value;
        }

        BoostSpec? boost = null;
        if (stock.HasTurbo)
        {
            var max = ParseChanged(BoostMaxText, stock.BoostMax ?? 0, "Boost max");
            var wastegate = ParseChanged(BoostWastegateText, stock.BoostWastegate ?? 0, "Wastegate");
            if (max is not null || wastegate is not null)
            {
                // Both values are explicit in the spec — no silent defaults.
                var maxValue = max ?? stock.BoostMax ?? 0;
                var wgValue = wastegate ?? stock.BoostWastegate ?? 0;
                boost = new BoostSpec(maxValue, wgValue);
            }
        }

        double? diffPower = null, diffCoast = null;
        if (stock.HasDifferential)
        {
            if (Math.Abs(DiffPower - (stock.DiffPower ?? 0)) > Epsilon)
                diffPower = Math.Round(DiffPower, 2);
            if (Math.Abs(DiffCoast - (stock.DiffCoast ?? 0)) > Epsilon)
                diffCoast = Math.Round(DiffCoast, 2);
        }

        return new TunePlan
        {
            SourceCar = SelectedCar.Name,
            TuneName = string.IsNullOrWhiteSpace(TuneName) ? "unnamed" : TuneName,
            PowerScale = Math.Abs(PowerScale - 1.0) > Epsilon ? Math.Round(PowerScale, 3) : null,
            PowerCurve = _loadedCurve,
            Limiter = limiter,
            Boost = boost,
            FinalDrive = ParseChanged(FinalDriveText, stock.FinalDrive, "Final drive"),
            Gears = _loadedGears,
            MassTotal = ParseChanged(MassText, stock.Mass, "Mass"),
            GripScale = Math.Abs(GripScale - 1.0) > Epsilon ? Math.Round(GripScale, 3) : null,
            BrakeTorqueScale = Math.Abs(BrakeScale - 1.0) > Epsilon ? Math.Round(BrakeScale, 3) : null,
            DiffPower = diffPower,
            DiffCoast = diffCoast,
        };
    }

    // ---- live preview: the real pipeline ------------------------------------------

    private CancellationTokenSource? _debounce;

    /// <summary>Debounced entry point wired to every control change.</summary>
    public async void SchedulePreview(int delayMs = 250)
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(delayMs, cts.Token);
            await RefreshPreviewAsync();
        }
        catch (TaskCanceledException)
        {
            // superseded by a newer change
        }
    }

    public async Task RefreshPreviewAsync()
    {
        if (_files is null || _stock is null)
            return;

        TuneNameError = TuneSpecParser.IsValidTuneName(TuneName)
            ? ""
            : "letters, digits, '_' or '-' only";

        var plan = TryBuildPlan(out var inputErrors);
        var rows = new List<ValidationRow>(inputErrors.Select(e => new ValidationRow(true, e)));

        if (plan is null)
        {
            ValidationRows = rows;
            CanBuild = false;
            return;
        }

        SpecText = TuneSpecWriter.Write(plan);

        var files = _files;
        var result = await Task.Run(() =>
        {
            var models = CarModelSet.FromFiles(files);
            try
            {
                var validation = TunePipeline.Apply(plan, models);
                var patch = UiCarPatcher.BuildPatch(models);
                var points = PowerCurves.SampleGrid(models.Engine, models.PowerLut, 100);
                var peaks = PowerCurves.Peaks(points);
                return (validation, patch, (IReadOnlyList<CurvePoint>?)points, peaks.TorqueNm, peaks.PowerBhp, (string?)null);
            }
            catch (TransformException ex)
            {
                // Core's message, verbatim — shown as a failure, never a crash.
                return (null, null, null, 0, 0, ex.Message)!;
            }
        });

        if (result.Item6 is { } transformError)
        {
            rows.Add(new ValidationRow(true, transformError));
            ValidationRows = rows;
            CanBuild = false;
            PreviewUpdated?.Invoke(this, EventArgs.Empty);
            return;
        }

        var validationResult = result.Item1!;
        var tunedPatch = result.Item2!;
        TunedPoints = result.Item3!;

        rows.AddRange(validationResult.Failures.Select(f =>
            new ValidationRow(true, $"FAIL [{f.Rule}]: {f.Message} (value {f.Value:0.###}, limit {f.Limit:0.###})")));
        rows.AddRange(validationResult.Warnings.Select(w =>
            new ValidationRow(false, $"warning [{w.Rule}]: {w.Message} (value {w.Value:0.###}, limit {w.Limit:0.###})")));
        ValidationRows = rows;

        // The displayed strings ARE the UiSpecsPatch a build writes into ui_car.json.
        var stockPatch = _stockPatch!;
        SpecRows = new List<SpecRow>
        {
            new("Power", stockPatch.Bhp, tunedPatch.Bhp, Delta(result.Item5, _stockPeaks.PowerBhp, "bhp")),
            new("Torque", stockPatch.Torque, tunedPatch.Torque, Delta(result.Item4, _stockPeaks.TorqueNm, "Nm")),
            new("Weight", stockPatch.Weight, tunedPatch.Weight, ""),
            new("Pw ratio", stockPatch.PwRatio, tunedPatch.PwRatio, ""),
        };

        CanBuild = !validationResult.HasFailures
                   && inputErrors.Count == 0
                   && TuneSpecParser.IsValidTuneName(TuneName);
        PreviewUpdated?.Invoke(this, EventArgs.Empty);
    }

    private static string Delta(double tuned, double stock, string unit)
    {
        var diff = tuned - stock;
        return Math.Abs(diff) < 0.5 ? "" : $"{(diff > 0 ? "+" : "")}{diff:0} {unit}";
    }

    // ---- spec save/load --------------------------------------------------------------

    public string? SaveSpec(string path)
    {
        var plan = TryBuildPlan(out var errors);
        if (plan is null || errors.Count > 0)
            return "Fix input errors before saving: " + string.Join(" ", errors);
        if (!TuneSpecParser.IsValidTuneName(TuneName))
            return "Set a valid tune name before saving.";
        File.WriteAllText(path, TuneSpecWriter.Write(plan));
        StatusText = $"Spec saved: {path}";
        return null;
    }

    /// <summary>Parses a spec and maps it onto the controls. Returns an error string or null.</summary>
    public async Task<string?> LoadSpecAsync(string path)
    {
        TunePlan plan;
        try
        {
            plan = TuneSpecParser.Parse(File.ReadAllText(path));
        }
        catch (TuneSpecException ex)
        {
            return ex.Message; // Core message verbatim
        }

        if (SelectedCar?.Name != plan.SourceCar)
        {
            var car = Cars.FirstOrDefault(c => c.Name.Equals(plan.SourceCar, StringComparison.OrdinalIgnoreCase));
            if (car is null || !car.IsSelectable)
                return $"Spec targets '{plan.SourceCar}', which is not a buildable car in this install.";
            await SelectCarAsync(car);
        }
        if (_stock is not { } stock)
            return "Car failed to load.";

        TuneName = plan.TuneName;
        PowerScale = plan.PowerScale ?? 1.0;
        LimiterText = (plan.Limiter ?? stock.Limiter).ToString(CultureInfo.InvariantCulture);
        BoostMaxText = (plan.Boost?.Max ?? stock.BoostMax)?.ToString(CultureInfo.InvariantCulture) ?? "";
        BoostWastegateText = (plan.Boost?.Wastegate ?? stock.BoostWastegate)?.ToString(CultureInfo.InvariantCulture) ?? "";
        FinalDriveText = (plan.FinalDrive ?? stock.FinalDrive).ToString(CultureInfo.InvariantCulture);
        MassText = (plan.MassTotal ?? stock.Mass).ToString(CultureInfo.InvariantCulture);
        GripScale = plan.GripScale ?? 1.0;
        BrakeScale = plan.BrakeTorqueScale ?? 1.0;
        DiffPower = plan.DiffPower ?? stock.DiffPower ?? 0;
        DiffCoast = plan.DiffCoast ?? stock.DiffCoast ?? 0;
        _loadedCurve = plan.PowerCurve;
        _loadedGears = plan.Gears;
        LoadedExtrasNote = (plan.PowerCurve, plan.Gears) switch
        {
            (null, null) => "",
            ({ } c, null) => $"Loaded spec carries power.curve ({c.Count} range(s)) — preserved in builds.",
            (null, { } g) => $"Loaded spec carries custom gears ({g.Count}) — preserved in builds.",
            ({ } c, { } g) => $"Loaded spec carries power.curve ({c.Count}) and gears ({g.Count}) — preserved in builds.",
        };

        StatusText = $"Spec loaded: {Path.GetFileName(path)}";
        await RefreshPreviewAsync();
        return null;
    }

    // ---- build ----------------------------------------------------------------------

    public sealed record GuiBuildResult(bool Collision, string? Error, EmitResult? Emit, string SpecText);

    public async Task<GuiBuildResult> BuildAsync(bool force)
    {
        var plan = TryBuildPlan(out var errors);
        if (plan is null || errors.Count > 0)
            return new GuiBuildResult(false, string.Join(" ", errors.DefaultIfEmpty("No plan.")), null, "");

        var specText = TuneSpecWriter.Write(plan);
        var sourceFolder = SelectedCar!.Car.Folder;
        var outRoot = Path.Combine(AcPath, "content", "cars");
        var target = Path.Combine(outRoot, $"{plan.SourceCar}_{plan.TuneName}");
        if (!force && Directory.Exists(target))
            return new GuiBuildResult(true, null, null, specText);

        IsBusy = true;
        StatusText = "Building…";
        try
        {
            var outcome = await Task.Run(() => VariantBuilder.Build(
                sourceFolder, plan, outRoot, force, SkinsMode.Junction, specText, TuneName + ".toml"));
            if (outcome.Validation.HasFailures)
            {
                var text = string.Join("\n", outcome.Validation.Failures.Select(f => $"FAIL [{f.Rule}]: {f.Message}"));
                StatusText = "Validation failed — nothing was written.";
                return new GuiBuildResult(false, text, null, specText);
            }
            StatusText = $"Built {outcome.Emit!.VariantName}";
            return new GuiBuildResult(false, null, outcome.Emit, specText);
        }
        catch (Exception ex) when (ex is EmitException or TransformException or IOException or InvalidOperationException)
        {
            StatusText = ex.Message;
            return new GuiBuildResult(false, ex.Message, null, specText);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
