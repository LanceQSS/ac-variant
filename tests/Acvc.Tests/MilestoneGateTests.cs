using System.Text;
using Acvc.Core.Acd;

namespace Acvc.Tests;

/// <summary>
/// Milestone 1 gate (CLAUDE.md build order): decrypting a stock Kunos data.acd must
/// yield files that all parse as text and include the known car data files.
/// Skipped when tests/fixtures/ has not been populated — run scripts/make-fixtures.ps1.
/// </summary>
public class MilestoneGateTests
{
    private const string SkipReason =
        "tests/fixtures/ has no car with data.acd — run scripts/make-fixtures.ps1 against a local AC install";

    private static readonly string[] RequiredFiles =
        { "engine.ini", "car.ini", "drivetrain.ini", "power.lut" };

    [SkippableFact]
    public void Every_fixture_unpacks_to_parseable_text_with_required_files()
    {
        var cars = Fixtures.CarFolders();
        Skip.If(cars.Count == 0, SkipReason);

        foreach (var carFolder in cars)
        {
            var data = AcdUnpacker.Load(carFolder);

            foreach (var required in RequiredFiles)
                Assert.True(data.Files.ContainsKey(required),
                    $"{data.CarFolderName}: expected '{required}' in data.acd, found: " +
                    string.Join(", ", data.Files.Keys.OrderBy(k => k)));

            foreach (var (name, content) in data.Files)
            {
                var printable = content.Count(IsTextByte);
                var ratio = content.Length == 0 ? 1.0 : (double)printable / content.Length;
                Assert.True(ratio >= 0.85,
                    $"{data.CarFolderName}/{name}: only {ratio:P1} of {content.Length} bytes are text — decryption is wrong.");
            }

            var carIni = Encoding.ASCII.GetString(data.Files["car.ini"]);
            Assert.Contains("[", carIni);
            Assert.Contains("=", carIni);
        }
    }

    [Fact]
    public void Protected_style_content_raises_ProtectedDataException()
    {
        // CSP/x4fab-protected mods parse structurally but their content is not
        // Kunos-ROT ciphertext, so decryption yields uniform garbage. (Note that a
        // *wrong Kunos key* would NOT be caught here: key strings are all ASCII
        // digits/dashes, so wrong-key output differs by at most ±12 per byte and
        // stays printable. Rule 2 detection is Kunos-vs-foreign, not key identity.)
        var noise = new byte[2048];
        for (var i = 0; i < noise.Length; i++)
            noise[i] = (byte)(i * 197 + 31);
        var acd = SyntheticAcd.Build(true, ("car.ini", noise), ("engine.ini", noise));

        var carFolder = Path.Combine(Path.GetTempPath(), "acvc-tests", Guid.NewGuid().ToString("N"), "some_protected_mod");
        Directory.CreateDirectory(carFolder);
        try
        {
            File.WriteAllBytes(Path.Combine(carFolder, "data.acd"), acd);
            var ex = Assert.Throws<ProtectedDataException>(() => AcdUnpacker.Load(carFolder));
            Assert.Contains("CSP/x4fab", ex.Message);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(carFolder)!, recursive: true);
        }
    }

    private static bool IsTextByte(byte b) =>
        b is >= 0x20 and <= 0x7E or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
