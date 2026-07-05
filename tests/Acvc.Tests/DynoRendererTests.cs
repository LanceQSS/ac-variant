using Acvc.Cli;
using Acvc.Core.UiMeta;

namespace Acvc.Tests;

public class DynoRendererTests
{
    [Fact]
    public void Render_produces_a_real_png()
    {
        var stock = new List<CurvePoint>();
        var tuned = new List<CurvePoint>();
        for (var rpm = 0; rpm <= 7000; rpm += 250)
        {
            var t = rpm == 0 ? 0 : 150 + 60 * Math.Sin(rpm / 2500.0);
            stock.Add(new CurvePoint(rpm, t, t * rpm * 2 * Math.PI / 60 / 745.7));
            tuned.Add(new CurvePoint(rpm, t * 1.35, t * 1.35 * rpm * 2 * Math.PI / 60 / 745.7));
        }

        var path = Path.Combine(Path.GetTempPath(), $"acvc-dyno-{Guid.NewGuid():N}.png");
        try
        {
            DynoRenderer.Render("smoke test", stock, tuned, path);

            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 1000, $"PNG suspiciously small: {bytes.Length} bytes");
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes.Take(8).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
