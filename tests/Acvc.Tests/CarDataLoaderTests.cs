using System.Text;
using Acvc.Core.Acd;
using Acvc.Core.Model;

namespace Acvc.Tests;

/// <summary>M6: loose-data cars are first-class; data.acd wins when both exist.</summary>
public class CarDataLoaderTests : IDisposable
{
    private readonly string _root;

    public CarDataLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "acvc-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Synthetic loose-data car derived from the unpacked fixture (per M6 item 1).</summary>
    private string CreateLooseCar(string name)
    {
        var fixture = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals("abarth500", StringComparison.OrdinalIgnoreCase));
        Skip.If(fixture is null, ModelTestUtil.FixtureSkipReason);

        var car = Path.Combine(_root, name);
        var dataDir = Path.Combine(car, "data");
        Directory.CreateDirectory(dataDir);
        foreach (var (fileName, bytes) in AcdUnpacker.Load(fixture!).Files)
            File.WriteAllBytes(Path.Combine(dataDir, fileName), bytes);
        return car;
    }

    [SkippableFact]
    public void Loose_data_car_loads_with_loose_origin_and_full_content()
    {
        var car = CreateLooseCar("loose_mod_car");

        var data = CarDataLoader.Load(car);

        Assert.Equal(CarDataOrigin.LooseData, data.Origin);
        Assert.Equal("loose_mod_car", data.CarFolderName);
        Assert.Equal(1100.0, CarModelSet.FromFiles(data.Files).Car.TotalMass);
        Assert.True(data.Files.Count >= 40);
    }

    [SkippableFact]
    public void When_both_exist_data_acd_wins_over_loose()
    {
        var fixture = Fixtures.CarFolders()
            .FirstOrDefault(d => Path.GetFileName(d).Equals("abarth500", StringComparison.OrdinalIgnoreCase));
        Skip.If(fixture is null, ModelTestUtil.FixtureSkipReason);

        // Folder named abarth500 so the acd decrypts; loose data/ holds a decoy car.ini.
        var car = Path.Combine(_root, "abarth500");
        Directory.CreateDirectory(Path.Combine(car, "data"));
        File.Copy(Path.Combine(fixture!, "data.acd"), Path.Combine(car, "data.acd"));
        File.WriteAllBytes(Path.Combine(car, "data", "car.ini"),
            Encoding.Latin1.GetBytes("[BASIC]\nTOTALMASS=1\n"));

        var data = CarDataLoader.Load(car);

        Assert.Equal(CarDataOrigin.PackedAcd, data.Origin);
        Assert.Equal(1100.0, CarModelSet.FromFiles(data.Files).Car.TotalMass); // acd value, not the decoy 1
    }

    [Fact]
    public void Neither_source_fails_loudly()
    {
        var car = Path.Combine(_root, "empty_car");
        Directory.CreateDirectory(car);
        Assert.Throws<FileNotFoundException>(() => CarDataLoader.Load(car));
    }

    [SkippableFact]
    public void Unpack_to_directory_works_from_a_loose_data_car()
    {
        var car = CreateLooseCar("loose_unpack_me");
        var outDir = Path.Combine(_root, "out");

        var data = AcdUnpacker.UnpackToDirectory(car, outDir);

        Assert.True(data.Files.Count >= 40);
        Assert.True(File.Exists(Path.Combine(outDir, "engine.ini")));
        // Source untouched: still no data.acd, loose files intact.
        Assert.False(File.Exists(Path.Combine(car, "data.acd")));
    }
}
