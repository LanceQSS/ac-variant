using Acvc.Core.Acd;
using Acvc.Core.Model;
using Acvc.Core.Spec;
using Acvc.Core.Transforms;
using Acvc.Core.UiMeta;

namespace Acvc.Core.Emit;

/// <summary>
/// Outcome of a build attempt. When validation failed, <see cref="Emit"/> is null
/// and nothing was written. <see cref="UiPatch"/> carries the exact spec strings
/// written into ui_car.json — the GUI preview displays these same objects, which is
/// what makes preview-vs-build divergence impossible by construction.
/// </summary>
public sealed record BuildOutcome(ValidationResult Validation, EmitResult? Emit, UiSpecsPatch? UiPatch);

/// <summary>
/// The one canonical load → transform → validate → emit flow, shared by the CLI and
/// the GUI (rule 5: every build goes through Core; two orchestrations would drift).
/// </summary>
public static class VariantBuilder
{
    public static BuildOutcome Build(
        string sourceCarFolder,
        TunePlan plan,
        string outRoot,
        bool force,
        SkinsMode skinsMode,
        string specText,
        string? specFileName)
    {
        var files = CarDataLoader.Load(sourceCarFolder).Files;
        var models = CarModelSet.FromFiles(files);

        var validation = TunePipeline.Apply(plan, models);
        if (validation.HasFailures)
            return new BuildOutcome(validation, null, null);

        var uiPatch = UiCarPatcher.BuildPatch(models);
        var result = VariantEmitter.Emit(
            sourceCarFolder,
            $"{plan.SourceCar}_{plan.TuneName}",
            models.MergedInto(files),
            new EmitOptions
            {
                OutRoot = outRoot,
                Force = force,
                SkinsMode = skinsMode,
                UiNameSuffix = $" — {plan.TuneName}",
                UiPatch = uiPatch,
                SpecText = specText,
                SpecFileName = specFileName,
            });
        return new BuildOutcome(validation, result, uiPatch);
    }
}
