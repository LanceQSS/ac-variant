# AC Variant

A tuning compiler for Assetto Corsa: describe a tune in a small TOML file (or move
sliders in the GUI), and it builds a **new, non-destructive car variant** — a
complete folder next to the original with transformed physics, regenerated UI
specs, working audio, and linked skins. The original car is never touched.

![Main window](docs/img/main-window.png)
*(screenshot placeholder)*

![Spec panel in Content Manager](docs/img/cm-specs.png)
*(screenshot placeholder)*

## What it does

- **Ten transforms**: power scale, per-range power shaping, rev limiter, turbo
  boost, final drive, gear ratios, mass, tyre grip scale, brake torque scale,
  differential locks.
- **Live preview** in the GUI: dyno plot and bhp/Nm/kg/pwratio recomputed through
  the same pipeline a build uses — what you preview is byte-for-byte what Content
  Manager will show.
- **Specs are the shareable unit.** A tune is a tiny `.toml` you can post anywhere;
  anyone who owns the same car rebuilds the variant locally. The tool never
  packages or redistributes car data.
- **Validation that informs**: warnings inform, never block; the tool only
  refuses files that would break the sim. Realism departures are your call.

## Honest numbers

Variant spec panels are regenerated **from the physics data** (the torque LUT and
boost), not from the original car's marketing figures. Some stock Kunos cars ship
brochure numbers up to ~12% above what their physics actually deliver — so a
variant can honestly show *lower* figures than the original's panel while driving
identically. The LUT is truth.

## Support posture

- **Kunos cars: tested.** The compiler is developed and gated against original
  content.
- **Mod cars: best effort.** Loose-data mods and standard-cipher packed mods build
  fine (the full-install survey tool exists exactly to keep this honest).
- **Encrypted mods: never.** CSP/x4fab-era protected content is detected and
  refused up front, with the reason shown. This is permanent — the tool will not
  attempt to break protection.

## Tune spec example

```toml
[meta]
source_car = "abarth500"
tune_name  = "street_600"

[power]
scale = 1.35

[engine]
limiter = 7400
boost = { max = 1.4, wastegate = 1.4 }

[mass]
total = 1420

[tyres]
grip_scale = 1.10
```

Build it with the GUI (`AC Variant.exe`) or the CLI:

```
acvc build street_600.toml
acvc dyno  street_600.toml      # stock-vs-tuned dyno chart PNG
acvc survey                     # classify + health-check every installed car
```

## Building from source

Requires the .NET 8 SDK (released binaries are self-contained and need nothing).

```
git clone https://github.com/LanceQSS/ac-variant.git
cd ac-variant
dotnet build
dotnet test        # fixture-based tests generate their data from YOUR install:
                   #   .\scripts\make-fixtures.ps1
dotnet run --project src/Acvc.Gui        # GUI
dotnet run --project src/Acvc.Cli -- --help
```

Game data never ships with this repo: test fixtures are generated locally from
your own installation and are gitignored.

## License

MIT — see [LICENSE](LICENSE). Open source, PRs welcome, maintained on a slow cycle.
