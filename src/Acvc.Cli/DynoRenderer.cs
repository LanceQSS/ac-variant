using Acvc.Core.UiMeta;
using ScottPlot;

namespace Acvc.Cli;

/// <summary>
/// PNG dyno chart via ScottPlot (settled: PNG, not ASCII). Torque and power on a
/// shared axis (Nm and bhp live in the same numeric range for road cars); when a
/// tuned curve set is given it overlays the stock one.
/// </summary>
public static class DynoRenderer
{
    public static void Render(
        string title,
        IReadOnlyList<CurvePoint> stock,
        IReadOnlyList<CurvePoint>? tuned,
        string outputPath)
    {
        var plot = new Plot();

        AddPair(plot, stock, tuned is null ? "torque (Nm)" : "stock torque (Nm)",
            tuned is null ? "power (bhp)" : "stock power (bhp)", dashed: tuned is not null);
        if (tuned is not null)
            AddPair(plot, tuned, "tuned torque (Nm)", "tuned power (bhp)", dashed: false);

        plot.Title(title);
        plot.XLabel("rpm");
        plot.YLabel("torque (Nm) / power (bhp)");
        plot.ShowLegend(Alignment.UpperLeft);
        plot.SavePng(outputPath, 1100, 700);
    }

    private static void AddPair(Plot plot, IReadOnlyList<CurvePoint> points, string torqueLabel, string powerLabel, bool dashed)
    {
        var rpm = points.Select(p => p.Rpm).ToArray();

        var torque = plot.Add.Scatter(rpm, points.Select(p => p.TorqueNm).ToArray());
        torque.LegendText = torqueLabel;
        torque.MarkerSize = 0;
        torque.LineWidth = 2;

        var power = plot.Add.Scatter(rpm, points.Select(p => p.PowerBhp).ToArray());
        power.LegendText = powerLabel;
        power.MarkerSize = 0;
        power.LineWidth = 2;

        if (dashed)
        {
            torque.LinePattern = LinePattern.Dashed;
            power.LinePattern = LinePattern.Dashed;
        }
    }
}
