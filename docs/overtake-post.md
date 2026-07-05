# [RELEASE] AC Variant — build tuned variants of your cars without touching the originals

*(Overtake forum post draft — screenshots to be added before posting)*

**AC Variant** turns a small text file (or a few sliders) into a complete new car
variant: pick a car, set power / limiter / boost / gearing / mass / grip / brakes /
diff, and it emits a new folder in `content/cars` with transformed physics,
correct spec panels, working engine audio and linked skins. **The original car is
never modified** — variants sit beside it, and deleting a variant folder removes
every trace.

Free and open source (MIT). GUI and command line included; the released exes are
self-contained (no .NET install needed).

## Three things to know up front

**1. Honest numbers.** The variant's spec panel is regenerated from the car's
actual physics data, not from the original's ui figures. Some stock cars ship
brochure numbers well above what their physics deliver (one famous Kunos car is
~12% optimistic). Your variant may honestly show *lower* numbers than the stock
panel while being exactly as fast. The physics data is the truth; the panel
follows it.

**2. Encrypted mods are refused — permanently.** Cars using CSP/x4fab-era
paid-mod encryption are shown grayed-out with the reason. This tool will never
attempt to break protection, full stop. Standard cars and ordinary mods
(loose-data or normally packed) work.

**3. Support posture.** Kunos cars: tested — the tool is developed and gated
against original content. Mod cars: best effort — they build and drive, and a
built-in `survey` command health-checks every car in your install so problems are
visible instead of mysterious. If a mod breaks in an interesting way, the log
folder button gives you everything needed for a bug report.

## Sharing tunes

The shareable artifact is the **tune spec** — a tiny `.toml` like this, visible
and exportable straight from the GUI:

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
```

Post the spec text; anyone who owns the same car loads it and rebuilds the
identical variant locally in seconds. **No car data is ever packaged,
redistributed or re-uploaded by this tool** — that's by design and in line with
both Kunos ToS and forum rules: personal-use modification, not redistribution of
modified data.

## What it deliberately is not

Not an advanced editor, not a raw-file view, not a mod manager, and not a
suspension/aero workshop — ten well-validated transforms, a live dyno preview
that matches the built output exactly, and nothing else. Advanced users get a
loose `data/` folder in every variant to take further by hand.

## Download & source

- GitHub (source + releases): *(link)*
- Current version: **2.0.0-beta.1** — Windows 10/11, x64.

Open source, PRs welcome, maintained on a slow cycle. Report issues on GitHub
with the log file (Logs button in the app).
