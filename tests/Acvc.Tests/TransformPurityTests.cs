namespace Acvc.Tests;

/// <summary>
/// CLAUDE.md design rule: transforms never touch the filesystem. This guards the
/// whole Transforms/ folder against I/O creeping in — implicit usings make
/// System.IO available without any using line, so we scan for usage tokens.
/// </summary>
public class TransformPurityTests
{
    private static readonly string[] ForbiddenTokens =
        { "File.", "Directory.", "Stream", "StreamWriter", "StreamReader", "Console.", "Environment." };

    [Fact]
    public void Transforms_folder_contains_no_io()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "acvc.slnx")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        var transformsDir = Path.Combine(dir!, "src", "Acvc.Core", "Transforms");
        var files = Directory.GetFiles(transformsDir, "*.cs");
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in ForbiddenTokens)
                if (text.Contains(token, StringComparison.Ordinal))
                    violations.Add($"{Path.GetFileName(file)}: {token}");
        }

        Assert.True(violations.Count == 0,
            "I/O tokens found in Transforms/: " + string.Join("; ", violations));
    }
}
