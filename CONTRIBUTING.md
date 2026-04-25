# Contributing to FLARE

FLARE is a small, focused NVIDIA-GPU diagnostic tool. Contributions are welcome — keep changes tight and in the spirit of the existing scope.

## Build and test

```
dotnet build FLARE.slnx
dotnet test FLARE.slnx
```

To build the same framework-dependent single-file exe shape used by releases:

```
.\build-exe.ps1
```

This writes `build\FLARE.exe`. Release packaging and GitHub Release uploads
are still handled by GitHub Actions.

## Layout

See [ARCHITECTURE.md](ARCHITECTURE.md) for the 60-second tour. Short version:

- **`FLARE.Core/`** — pure logic. Data collection (event log, registry, nvidia-smi, minidump parsers) and report generation. No WPF references.
- **`FLARE.UI/`** — WPF front-end. Orchestrates `FlareRunner` on a background task, renders results in a log pane.
- **`FLARE.Tests/`** — xUnit. Parser tests, regex classifiers, report rendering, pipeline integration.

## A note on test density

300+ tests against 5,700+ LOC of product code is a high ratio, and deliberate. Three categories earn it:

1. **Security invariants** — the env-variable whitelist in `MinidumpLocator`, the trusted-root check for `cdb.exe`, and the reparse-point re-check in the elevated dump-copy helper all guard threats that break silently. Pinning tests are the only backstop.
2. **Parsers for formats FLARE doesn't control** — nvlddmkm event payloads, PAGEDU64 header offsets, nvidia-smi `-q` layout, the `.0.15.` NVIDIA driver-version segment, setupapi.dev.log. A silent Windows or NVIDIA format shift would otherwise land as an empty report section with no diagnostic.
3. **Pipeline wiring via the `FlareDependencies` test seam** — lets the full collector-to-report path run without live Windows APIs, so ordering, cancellation propagation, and section-rendering regressions surface in CI rather than in the wild.

A softer fourth tier — report-formatting assertions — pins exact phrasing in places. Kept because report readability is the product, but this is the category to trim first if it ever blocks a legitimate wording change. The bias overall is "a comment-plus-test over a bare invariant that future-you (or a contributor) might refactor away."

## Guidelines

- **Keep scope tight.** FLARE is a GPU-fault reporter, not a general diagnostic suite. New features need a clear link to identifying or contextualizing NVIDIA GPU issues.
- **Don't reach for ASCII ligatures.** The report is read in editors with coding fonts — avoid `->`, `<--`, `===`, `=>` etc. in user-facing strings; they render as arrows.
- **Locale-safe formatting.** Use `CultureInfo.InvariantCulture` on any `F0`/`F1`/etc. format specifier that lands in the report. The Swedish-locale test in `ReportGeneratorTests` pins this.
- **Tests before Windows-API refactors.** Collectors that hit live Windows APIs (Event Log, registry, nvidia-smi, cdb) are hard to exercise in CI. Prefer narrow integration tests using `FlareDependencies` over rewriting the static layer.

## Opening a pull request

1. Branch from `dev`.
2. Keep commits small and topical.
3. Add or update tests for any new parsing, report rendering, or correlation logic.
4. Run `dotnet test FLARE.slnx` before pushing — the suite (300+ tests covering Core parsers, pipeline orchestration, UI settings/view-model, and security-critical cdb auto-detection) is fast.
5. Target `dev` (never `main`). Releases are squash-merged from `dev` to `main` and tagged `v*.*.*`.

## Releases

- CI builds on every push/PR against `main`.
- Pushing a tag `v*` triggers the release workflow — it passes the tag version into the app assembly, packages `FLARE-<version>-win-x64.zip`, and publishes a GitHub Release using `HISTORY.log` as the body. Update `HISTORY.log` for the new version before tagging.
- Binaries are **not code-signed** and will not be. See the README "Unsigned binary" section for the reasoning; this is a settled decision, not a roadmap item. First-run SmartScreen warnings are expected. PRs, issues, or review comments proposing code-signing will be closed with a pointer back to the README.
