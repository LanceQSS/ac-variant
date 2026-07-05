using Acvc.Core.Spec;
using Acvc.Core.Survey;
using Acvc.Gui.ViewModels;

namespace Acvc.Tests;

/// <summary>
/// Viewmodel-level tests (no UI automation): the control-state → TunePlan mapping is
/// the GUI's only piece of judgment, so it is pinned here. Everything downstream of
/// the plan is Core and already covered.
/// </summary>
public class MainViewModelTests
{
    private static readonly StockState AbarthStock = new(
        Limiter: 6500, BoostMax: 1.38, BoostWastegate: 1.18,
        FinalDrive: 3.353, Mass: 1100, DiffPower: 0.04, DiffCoast: 0.04,
        HasTurbo: true, HasDifferential: true, HasTyres: true, HasBrakes: true);

    private static MainViewModel Vm()
    {
        var vm = new MainViewModel
        {
            SelectedCar = new CarListItem(new CatalogCar("abarth500", @"C:\x\abarth500", "kunos-packed", true, null)),
            TuneName = "test_tune",
        };
        vm.ApplyStock(AbarthStock);
        return vm;
    }

    [Fact]
    public void Stock_controls_produce_a_noop_plan()
    {
        var plan = Vm().TryBuildPlan(out var errors);

        Assert.Empty(errors);
        Assert.NotNull(plan);
        Assert.Equal("abarth500", plan!.SourceCar);
        Assert.Equal("test_tune", plan.TuneName);
        Assert.Null(plan.PowerScale);
        Assert.Null(plan.Limiter);
        Assert.Null(plan.Boost);
        Assert.Null(plan.FinalDrive);
        Assert.Null(plan.MassTotal);
        Assert.Null(plan.GripScale);
        Assert.Null(plan.BrakeTorqueScale);
        Assert.Null(plan.DiffPower);
        Assert.Null(plan.DiffCoast);
    }

    [Fact]
    public void Changed_controls_map_to_exactly_those_plan_fields()
    {
        var vm = Vm();
        vm.PowerScale = 1.35;
        vm.LimiterText = "7400";
        vm.MassText = "1420";
        vm.GripScale = 1.25;
        vm.BrakeScale = 0.6;
        vm.DiffPower = 0.9; // stock 0.04

        var plan = vm.TryBuildPlan(out var errors)!;

        Assert.Empty(errors);
        Assert.Equal(1.35, plan.PowerScale);
        Assert.Equal(7400, plan.Limiter);
        Assert.Equal(1420.0, plan.MassTotal);
        Assert.Equal(1.25, plan.GripScale);
        Assert.Equal(0.6, plan.BrakeTorqueScale);
        Assert.Equal(0.9, plan.DiffPower);
        Assert.Null(plan.DiffCoast);         // untouched stays out of the plan
        Assert.Null(plan.FinalDrive);
        Assert.Null(plan.Boost);
    }

    [Fact]
    public void Changing_only_boost_max_sends_an_explicit_pair()
    {
        var vm = Vm();
        vm.BoostMaxText = "1.6";

        var plan = vm.TryBuildPlan(out var errors)!;

        Assert.Empty(errors);
        Assert.Equal(new BoostSpec(1.6, 1.18), plan.Boost); // wastegate pinned at stock, explicitly
    }

    [Fact]
    public void Invalid_numeric_input_is_an_error_not_an_exception()
    {
        var vm = Vm();
        vm.MassText = "not-a-number";

        var plan = vm.TryBuildPlan(out var errors);

        Assert.NotNull(plan);
        Assert.Contains(errors, e => e.Contains("not-a-number"));
        Assert.Null(plan!.MassTotal);
    }

    [Fact]
    public void Save_spec_roundtrips_through_the_core_parser()
    {
        var vm = Vm();
        vm.PowerScale = 1.2;
        vm.DiffPower = 0.5;
        var path = Path.Combine(Path.GetTempPath(), $"acvc-vm-{Guid.NewGuid():N}.toml");
        try
        {
            Assert.Null(vm.SaveSpec(path));
            var plan = TuneSpecParser.Parse(File.ReadAllText(path));
            Assert.Equal("abarth500", plan.SourceCar);
            Assert.Equal(1.2, plan.PowerScale);
            Assert.Equal(0.5, plan.DiffPower);
            Assert.Null(plan.GripScale);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
