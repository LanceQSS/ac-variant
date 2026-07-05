# CLAUDE.md — AC Variant Compiler (`acvc`)

## What this is
A C#/.NET 8 CLI that compiles a declarative tune spec (TOML) into a new, non-destructive Assetto Corsa car variant: unpacks the source car's `data.acd`, applies physics-aware transforms, regenerates UI metadata so Content Manager shows correct specs, and emits a complete new car folder with a loose `data/` directory.

v2 adds a WPF GUI shell (`Acvc.Gui`) over the unchanged compiler core, and a public release target (GitHub + Overtake).

**IS:** a friendly GUI + CLI over the same compiler — pick car, set values, see live dyno/spec preview, build a non-destructive variant. A public tool.
**IS NOT:** an advanced editor, a raw-file view, a mod manager, or an app with any logic outside `Acvc.Core`. Advanced users get the loose `data/` folder.

## Non-negotiable rules
1. **Never modify the original car folder.** All output goes to a new folder (`<car>_<tunename>`). Any code path that writes inside the source folder is a highest-severity bug.
2. **Never attempt CSP/x4fab-encrypted mods.** The standard Kunos ACD cipher is keyed on the folder name; CSP-era paid-mod encryption is separate and not breakable. Detect it (decrypt output is non-ASCII garbage / known INI files fail to parse), refuse with a clear message, exit non-zero.
3. **Tune specs are the shareable unit, never tuned data.** Nothing in this tool may package, zip, or export modified Kunos/mod data files for distribution. Output stays in the local `content/cars/` tree. (Kunos ToS + Overtake rules: personal-use modification is fine; redistributing modified official data is not.)
4. **Fail loudly.** No silent fallbacks. If a transform can't apply (missing `[TURBO]` section on an NA car, malformed LUT), report exactly what and why.
5. **The GUI is a dumb shell.** Every read, transform, preview computation, and build goes through `Acvc.Core`. If the GUI needs logic, it moves to Core with tests first. The 162+ Core tests remain the authority.
6. **No network calls at runtime.** No telemetry, no update checks, no remote anything. Zero-maintenance by construction.

## Verified format facts (do not re-derive; do not contradict)
- `data.acd` is a sequence of files where each original byte occupies a 32-bit field (packed size ≈ 4× content). Encryption is a simple ROT cipher.
- The key is generated **from the car's folder name** by 8 small algorithms; the 8 byte values are joined as `"%d-%d-%d-%d-%d-%d-%d-%d"`. Consequence: renaming a folder breaks decryption of its `.acd`, and a variant folder with a new name cannot reuse the original `.acd`.
- **v1 sidesteps repacking entirely:** variant folders ship a loose `data/` directory and no `data.acd`. AC reads the loose folder when no `.acd` is present. Repack-with-new-key is v2, not v1.
- Exact key-generation algorithms: **port, don't invent.** Two references:
  - `CarTuner/` in this repo root contains the 2017 "Assetto Corsa Car Tuner" download, which bundles `quickbms.exe` and (if present) the `assetto_corsa_acd.bms` script — the BMS script is plain text and encodes the cipher exactly. Read it first.
  - https://github.com/0danny/AssettoTools — open-source C# implementation of CreateKey/decrypt/encrypt. **Check its license before copying code verbatim**; if missing or restrictive, reimplement from the BMS script/format description (the algorithm is trivial; tests prove correctness either way).
- Correctness gate: decrypt a stock Kunos `data.acd` and confirm every extracted file parses as INI/LUT text.

## Reference material in this repo
- `CarTuner/` — 2017 ACCT tool (compiled exe = black-box behavioral reference only; the bundled QuickBMS `.bms` script is the readable artifact of value). Do not ship, link, or vendor any of it into `acvc` output or releases.
- Treat `CarTuner/` as read-only reference. It is not a dependency.

## Architecture
```
src/
  Acvc.Core/
    Acd/          # cipher (key gen, decrypt, [v2: encrypt]), container parse
    Model/        # typed INI/LUT readers-writers: engine.ini, drivetrain.ini,
                  # car.ini, power.lut (preserve unknown keys/sections verbatim)
    Transforms/   # one class per transform; pure functions data-in/data-out
    UiMeta/       # ui_car.json spec + curve regeneration
    Emit/         # variant folder assembly, skin handling, ui renaming
  Acvc.Cli/       # System.CommandLine entry: `acvc build <spec.toml>`,
                  # `acvc unpack <car>`, `acvc dyno <car|spec>`
tests/
  Acvc.Tests/     # xUnit
```
Design rule: transforms never touch the filesystem. Pipeline is `load → transform (pure) → validate → emit`. Unknown INI keys and comments pass through untouched — the model layer must be lossless for sections it doesn't understand.

## Transforms (v2 complete set = v1 seven + three handling transforms — do not add more without explicit instruction)
- `power.scale` — multiply power.lut torque values by factor (LUT is torque-at-crank vs RPM).
- `power.curve` — optional per-range shaping (list of rpm-range → factor).
- `engine.limiter` — set rev limiter in engine.ini.
- `engine.boost` — set MAX_BOOST / WASTEGATE in `[TURBO_n]` sections; error if no turbo section exists.
- `drivetrain.final` / `drivetrain.gears` — final drive and per-gear ratios.
- `mass.total` — TOTALMASS in car.ini.
- `tyres.grip_scale` — scales lateral/longitudinal reference grip in **every compound section** of tyres.ini. Tyre-model versions differ in key names across AC's history: derive the per-VERSION key map from the M6 survey results, never from assumption. Unknown VERSION = transform error naming the version found; never a silent skip.
- `brakes.torque_scale` — scales MAX_TORQUE in brakes.ini.
- `diff.power` / `diff.coast` — set `[DIFFERENTIAL]` POWER / COAST (0–1 lock fractions); error if the section is absent.

Explicitly still out: springs, ARBs, dampers, geometry, aero, preload, front share, drivetrain conversion (RWD↔AWD — requires sections the source car may not have; not a one-key change).

Validation after transform: mass > 0 and within ±60% of source; LUT monotonic in RPM; no NaN; limiter above peak-power RPM; warn (not fail) past sanity thresholds like >3× power.

ValidationLimits.cs additions (named constants, like the originals):
- grip_scale: warn outside ±15%, fail outside ±40%.
- brakes.torque_scale: warn >1.5 or <0.5, fail ≤ 0.
- diff.power / diff.coast: fail outside [0, 1].
Repricing note applies to these as to the originals.

## UI metadata regeneration
After transforms, regenerate in the variant's `ui/ui_car.json`:
- `name` (append tune name), `specs` (bhp, torque, weight, pwratio — recomputed from transformed data),
- `torqueCurve` / `powerCurve` arrays.

**Before trusting any curve-format assumption, read a stock Kunos car's `ui_car.json` and match its exact array shape.** This is the most likely place for silent format drift — validate by loading the variant in CM and eyeballing the spec panel.

Settled facts (M5, verified against stock abarth500 + bmw_m3_e30):
- Kunos ui files are **not strict JSON** (raw control chars inside strings) — regeneration is done by byte-splicing values, never a parser round-trip.
- Curves are arrays of `["rpm","value"]` **string pairs**, grid 0 → LIMITER in 500-rpm steps with the limiter as the final point and a forced `["0","0"]` origin.
- Spec string formats: `"213bhp"`, `"305Nm"`, `"1345kg"`, `"6.31kg/hp"` (pwratio truncated, not rounded); ui `weight` = TOTALMASS − 75 kg (driver).
- Effective torque = power.lut × (1 + Σ MAX_BOOST over `[TURBO_n]`); power[bhp] = T·rpm·2π/60 ÷ 745.7 (exact identity between stock torqueCurve/powerCurve arrays).
- Stock Kunos specs can be marketing numbers: bmw_m3_e30 ships brochure curves ~12% above its LUT. acvc regenerates from the LUT — variants may honestly show lower figures than the stock car's panel.

## Tune spec (TOML)
```toml
[meta]
source_car = "ks_toyota_supra_mkiv"   # folder name under content/cars
tune_name  = "street_600"

[power]
scale = 1.35

[engine]
limiter = 7400
boost = { max = 1.4, wastegate = 1.4 }

[drivetrain]
final = 3.90

[mass]
total = 1420

[tyres]
grip_scale = 1.10

[brakes]
torque_scale = 1.15

[diff]
power = 0.45
coast = 0.25
```
Every table optional except `[meta]`. Unknown keys = hard error (catch typos, no silent ignores).

Additional keys: `power.curve = [ { from = 3000, to = 5000, factor = 1.1 }, ... ]` and `drivetrain.gears = [3.2, 2.1, 1.5, 1.1, 0.9]` (must match the car's gear COUNT). power.curve semantics (implemented, M3): `from`/`to` are both **inclusive**; rows outside every listed range — including gaps between ranges — are left untouched; overlapping ranges are a hard error, not a precedence rule.

## Mod-car support (Core)
- Cars with a loose `data/` and no `data.acd` are first-class inputs. When both exist, `data.acd` wins (matches the game's read behavior).
- Encrypted data: hard refusal, unchanged (rule 2).
- UI-metadata degradation: a car whose ui_car.json is absent, unparseable, or missing specs/curves still **builds**. Regenerate what is computable, skip the rest, warn naming exactly which fields were skipped. Never block a build on ui cosmetics.
- Unknown or extra files in `data/` pass through verbatim (lossless model guarantee now stated as a requirement, not an accident).

## Skins
Do not copy all skin folders (ACCT duplicated gigabytes; its top complaint). **Settled in M5 (manual verdict on the real install): AC and CM both follow NTFS junctions in `skins/` — all 10 abarth500 skins listed and rendered through junctions, in CM and in-game.** Default is therefore `--skins junction` (one junction per source skin, created via `mklink /J`; no elevation needed); `--skins copy` copies only the first (alphabetical) skin as the fallback. The variant readme names the skin mode and the source skins path.

## GUI (Acvc.Gui)
- WPF on .NET 8, WPF-UI (Fluent) theme. Windows-only by decision.
- Single main window: car picker (search box; Kunos/mod badge; encrypted cars visible but grayed with the reason as tooltip — never a late error), two transform groups (**Power & Drivetrain**, **Handling**), tune-name field, live preview pane (dyno plot + bhp/Nm/kg/pwratio with deltas vs stock, recomputed through Core on every change), Build button with result toast and an "open variant folder" action.
- AC path: autodetect Steam install (registry SteamPath + libraryfolders.vdf parse for secondary libraries), manual folder picker fallback, persisted to acvc.config.toml. Never hardcoded (existing rule).
- Error surfaces show Core's messages **verbatim** — Core already writes user-grade errors; the GUI must not paraphrase them.
- Rolling log at %LOCALAPPDATA%\acvc\logs (spec + outcome per build). The bug-report affordance is a button that opens the log folder.
- Skins: junction default (settled M4); automatic fallback to copy-first-skin when junction creation fails (permissions/non-NTFS), with a notice.

## Release engineering
- License: MIT. LICENSE at root. Public GitHub repo.
- **History hygiene gate before the repo goes public:** `CarTuner/` (third-party compiled tool + QuickBMS) must never appear in public history — redistributing it violates the same rules we honor. If it was ever committed, rewrite history (git filter-repo) before publishing. Fixtures must also be absent from history (should already hold — verify, don't assume).
- No installer for beta: single-file exe. Version in title bar and readme.
- docs/overtake-post.md: what it does, honest-numbers policy stated upfront, encrypted-mods limitation stated upfront, maintenance note ("open source, PRs welcome, maintained on a slow cycle").
- Public name: OPEN decision — "acvc" is a placeholder until chosen; rename touches exe, title bar, repo, post.
- Beta path: GitHub pre-release tested on a clean second machine (no dev tools) before the Overtake post.

## Build order (v1 milestones 1–5: done, tagged v1.0)
6. **Mod-car hardening:** loose-data support, `acvc survey` over the full install, triage + fix core bugs, ui degradation policy. Gate: survey clean of core bugs; one loose-data mod car builds and drives.
7. **Handling transforms:** the +3 set, tyre-version key map from survey data, validation limits. Gate: tests + manual drive check (grip/brake/diff changes each perceptible and sane).
8. **GUI:** per the GUI section. Gate: build a variant end-to-end through the GUI on my machine, including one mod car and one encrypted car shown grayed.
9. **Release:** history hygiene gate, MIT, README, post draft, clean-machine beta test, GitHub pre-release. Gate: exe runs on a machine that has never seen the .NET SDK.

## Environment
- Windows 11, .NET 8 LTS, single-file publish (`dotnet publish -r win-x64 -p:PublishSingleFile=true`).
- AC install (Steam default): `C:\Program Files (x86)\Steam\steamapps\common\assettocorsa` — but always take the path from `acvc.config.toml` or `--ac-path`; never hardcode.
- Test cars: one NA Kunos car + one turbo Kunos car to cover both engine.ini shapes.
- Unit tests never run against the live install. Fixtures live in `tests/fixtures/`, generated locally by a setup script that reads the user's own install, and are **gitignored — Kunos data never goes in the repo.**

## Commands
```
dotnet build
dotnet test
dotnet run --project src/Acvc.Cli -- build specs/street_600.toml
dotnet run --project src/Acvc.Cli -- build specs/street_600.toml --force --skins copy
dotnet run --project src/Acvc.Cli -- unpack ks_toyota_supra_mkiv
dotnet run --project src/Acvc.Cli -- dyno specs/street_600.toml
dotnet run --project src/Acvc.Cli -- survey --report survey.json
```

## Out of scope (v2 — reject scope creep, cite this section)
Raw/advanced editing tab, suspension/aero/geometry transforms, drivetrain conversion, batch multi-car application, .acd repacking, auto-updates, telemetry, localization, cross-platform, anything touching x4fab encryption (permanent).
