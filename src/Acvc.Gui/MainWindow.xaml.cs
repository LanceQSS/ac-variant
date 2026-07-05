using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ScottPlot;
using Acvc.Gui.Services;
using Acvc.Gui.ViewModels;

namespace Acvc.Gui;

/// <summary>
/// View wiring only (rule 5: the GUI is a dumb shell). Every handler forwards to
/// MainViewModel, which talks to Acvc.Core; this file renders results and opens
/// dialogs, nothing else.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private string? _lastVariantPath;
    private bool _syncing;

    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        Title = $"AC Variant {version}";

        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.PreviewUpdated += (_, _) => Dispatcher.Invoke(RenderPreview);
        Loaded += async (_, _) => await StartupAsync();
    }

    private async Task StartupAsync()
    {
        var acPath = AcPathService.LoadConfigured() ?? AcPathService.Autodetect();
        if (acPath is null)
        {
            _vm.StatusText = "Assetto Corsa install not found — pick it with the … button.";
            return;
        }
        AcPathService.Persist(acPath);
        _vm.AcPath = acPath;
        AcPathText.Text = acPath;
        await _vm.LoadCarsAsync();
    }

    // ---- VM → view -----------------------------------------------------------------

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.StatusText):
                    StatusTextBlock.Text = _vm.StatusText;
                    break;
                case nameof(MainViewModel.Cars):
                case nameof(MainViewModel.FilteredCars):
                    CarList.ItemsSource = _vm.FilteredCars.ToList();
                    break;
                case nameof(MainViewModel.IsBusy):
                    BusyBar.Visibility = _vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case nameof(MainViewModel.CanBuild):
                    BuildButton.IsEnabled = _vm.CanBuild;
                    break;
                case nameof(MainViewModel.SpecText):
                    SpecPreview.Text = _vm.SpecText;
                    break;
                case nameof(MainViewModel.ValidationRows):
                    ValidationList.ItemsSource = _vm.ValidationRows;
                    break;
                case nameof(MainViewModel.TuneNameError):
                    TuneNameErrorText.Text = _vm.TuneNameError;
                    break;
                case nameof(MainViewModel.LoadedExtrasNote):
                    LoadedExtrasText.Text = _vm.LoadedExtrasNote;
                    break;
            }
        });
    }

    /// <summary>Pushes stock captions + control values from the VM after a car load/spec load.</summary>
    private void SyncControlsFromVm()
    {
        _syncing = true;
        try
        {
            TuneNameBox.Text = _vm.TuneName;
            PowerScaleSlider.Value = _vm.PowerScale;
            LimiterBox.Text = _vm.LimiterText;
            BoostMaxBox.Text = _vm.BoostMaxText;
            BoostWastegateBox.Text = _vm.BoostWastegateText;
            BoostMaxBox.IsEnabled = BoostWastegateBox.IsEnabled = _vm.HasTurbo;
            FinalBox.Text = _vm.FinalDriveText;
            MassBox.Text = _vm.MassText;
            GripSlider.Value = _vm.GripScale;
            BrakeSlider.Value = _vm.BrakeScale;
            DiffPowerSlider.Value = _vm.DiffPower;
            DiffCoastSlider.Value = _vm.DiffCoast;
            DiffPowerSlider.IsEnabled = DiffCoastSlider.IsEnabled = _vm.HasDifferential;

            LimiterStock.Text = _vm.LimiterStockText;
            BoostStock.Text = _vm.BoostStockText;
            FinalStock.Text = _vm.FinalStockText;
            MassStock.Text = _vm.MassStockText;
            DiffStock.Text = _vm.DiffStockText;
        }
        finally
        {
            _syncing = false;
        }
        UpdateSliderCaptions();
    }

    private void UpdateSliderCaptions()
    {
        PowerScaleValue.Text = $"× {PowerScaleSlider.Value.ToString("0.00", CultureInfo.InvariantCulture)}";
        GripValue.Text = $"× {GripSlider.Value.ToString("0.00", CultureInfo.InvariantCulture)}";
        BrakeValue.Text = $"× {BrakeSlider.Value.ToString("0.00", CultureInfo.InvariantCulture)}";
        DiffPowerValue.Text = DiffPowerSlider.Value.ToString("0.00", CultureInfo.InvariantCulture)
                              + " / " + DiffCoastSlider.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void RenderPreview()
    {
        // Specs grid — these strings are the exact UiSpecsPatch values a build writes.
        var rows = _vm.SpecRows;
        if (rows.Count == 4)
        {
            BhpStock.Text = rows[0].Stock; BhpTuned.Text = rows[0].Tuned; BhpDelta.Text = rows[0].Delta;
            TorqueStock.Text = rows[1].Stock; TorqueTuned.Text = rows[1].Tuned; TorqueDelta.Text = rows[1].Delta;
            WeightStock.Text = rows[2].Stock; WeightTuned.Text = rows[2].Tuned;
            PwStock.Text = rows[3].Stock; PwTuned.Text = rows[3].Tuned;
        }

        var plot = DynoPlot.Plot;
        plot.Clear();
        if (_vm.StockPoints.Count > 0)
        {
            AddPair(plot, _vm.StockPoints, "stock", dashed: true);
            if (_vm.TunedPoints.Count > 0)
                AddPair(plot, _vm.TunedPoints, "tuned", dashed: false);
            plot.XLabel("rpm");
            plot.YLabel("Nm / bhp");
            plot.ShowLegend(Alignment.UpperLeft);
        }
        DynoPlot.Refresh();
    }

    private static void AddPair(Plot plot, IReadOnlyList<Acvc.Core.UiMeta.CurvePoint> points, string label, bool dashed)
    {
        var rpm = points.Select(p => p.Rpm).ToArray();
        var torque = plot.Add.Scatter(rpm, points.Select(p => p.TorqueNm).ToArray());
        torque.LegendText = $"{label} torque";
        torque.MarkerSize = 0;
        torque.LineWidth = 2;
        var power = plot.Add.Scatter(rpm, points.Select(p => p.PowerBhp).ToArray());
        power.LegendText = $"{label} power";
        power.MarkerSize = 0;
        power.LineWidth = 2;
        if (dashed)
        {
            torque.LinePattern = LinePattern.Dashed;
            power.LinePattern = LinePattern.Dashed;
        }
    }

    // ---- view → VM -----------------------------------------------------------------

    private void PushControlsToVm()
    {
        _vm.TuneName = TuneNameBox.Text;
        _vm.PowerScale = PowerScaleSlider.Value;
        _vm.LimiterText = LimiterBox.Text;
        _vm.BoostMaxText = BoostMaxBox.Text;
        _vm.BoostWastegateText = BoostWastegateBox.Text;
        _vm.FinalDriveText = FinalBox.Text;
        _vm.MassText = MassBox.Text;
        _vm.GripScale = GripSlider.Value;
        _vm.BrakeScale = BrakeSlider.Value;
        _vm.DiffPower = DiffPowerSlider.Value;
        _vm.DiffCoast = DiffCoastSlider.Value;
    }

    private void OnAnyControlChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsLoaded)
            return;
        UpdateSliderCaptions();
        PushControlsToVm();
        _vm.SchedulePreview();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
        => _vm.SearchText = SearchBox.Text;

    private async void OnCarSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CarList.SelectedItem is not CarListItem item || item == _vm.SelectedCar)
            return;
        await _vm.SelectCarAsync(item);
        SyncControlsFromVm();
        RenderPreview();
    }

    private async void OnPickAcPath(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Pick the Assetto Corsa install folder" };
        if (dialog.ShowDialog(this) != true)
            return;
        _vm.AcPath = dialog.FolderName;
        AcPathText.Text = dialog.FolderName;
        AcPathService.Persist(dialog.FolderName);
        await _vm.LoadCarsAsync();
    }

    private void OnSaveSpec(object sender, RoutedEventArgs e)
    {
        PushControlsToVm();
        var dialog = new SaveFileDialog
        {
            Filter = "Tune spec (*.toml)|*.toml",
            FileName = $"{_vm.TuneName}.toml",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (_vm.SaveSpec(dialog.FileName) is { } error)
            MessageBox.Show(this, error, "Save spec", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void OnLoadSpec(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Tune spec (*.toml)|*.toml" };
        if (dialog.ShowDialog(this) != true)
            return;
        var error = await _vm.LoadSpecAsync(dialog.FileName);
        if (error is not null)
        {
            MessageBox.Show(this, error, "Load spec", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        CarList.SelectedItem = _vm.SelectedCar;
        SyncControlsFromVm();
        RenderPreview();
    }

    private async void OnBuild(object sender, RoutedEventArgs e)
    {
        PushControlsToVm();
        var result = await _vm.BuildAsync(force: false);
        if (result.Collision)
        {
            var answer = MessageBox.Show(this,
                "The variant folder already exists. Replace it? (Whole-folder swap, never a merge.)",
                "Variant exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
            result = await _vm.BuildAsync(force: true);
        }

        var car = _vm.SelectedCar?.Name ?? "?";
        if (result.Error is not null)
        {
            BuildLog.Write(car, result.SpecText, $"FAILED: {result.Error}");
            MessageBox.Show(this, result.Error, "Build failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (result.Emit is not { } emit)
            return;

        BuildLog.Write(car, result.SpecText,
            $"OK: {emit.VariantPath} | sfx: {emit.AudioNote} | skins: {emit.SkinsNote}" +
            (emit.UiNotes.Count > 0 ? $" | ui: {string.Join("; ", emit.UiNotes)}" : ""));

        _lastVariantPath = emit.VariantPath;
        ToastText.Text = $"Built {emit.VariantName} — skins: {emit.SkinsNote}";
        Toast.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; timer.Stop(); };
        timer.Start();
    }

    private void OnOpenVariant(object sender, RoutedEventArgs e)
    {
        if (_lastVariantPath is not null && System.IO.Directory.Exists(_lastVariantPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastVariantPath}\"") { UseShellExecute = true });
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(BuildLog.LogFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BuildLog.LogFolder}\"") { UseShellExecute = true });
    }
}
