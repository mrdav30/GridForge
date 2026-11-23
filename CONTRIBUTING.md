# Contributing

Focused fixes can proceed through a pull request. Open an issue or discussion
first when a change affects public API shape, deterministic behavior,
serialization layout, topology semantics, storage strategy, or several
subsystems at once.

Please follow the [Code of Conduct](CODE_OF_CONDUCT.md). See
[Support](.github/SUPPORT.md) for help and [Security](SECURITY.md) for private
vulnerability reports.

## Development setup

Install the SDK selected by `global.json` and the .NET 10 runtime used by the
tests and benchmarks. Then restore and run the normal Debug workflow:

```powershell
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug --no-restore
dotnet test GridForge.slnx --configuration Debug --no-build
```

GridForge targets `netstandard2.1` and `net10.0`. Keep shared runtime code
compatible with both targets and free of game-engine dependencies.

## Pull Request Process

1. Keep the change focused and preserve deterministic, world-scoped behavior.
2. Add or update the closest tests for behavior changes, especially around
   snapping, identity, topology, sparse storage, pooling, and query lifetimes.
3. Update XML documentation, the README, DocFX guides, migration guide, or
   benchmarks whenever the public contract or evidence changes.
4. Run the Standard and Lean release suites before requesting review:

   ```powershell
   dotnet test GridForge.slnx --configuration Release
   dotnet test GridForge.slnx --configuration ReleaseLean
   ```

5. Run focused BenchmarkDotNet cases when changing registration, tracing,
   scanning, pooling, blockers, occupants, diagnostics, or other hot paths.
6. Keep generated `bin`, `obj`, `TestResults`, DocFX, coverage, and benchmark
   artifacts out of the pull request.

## Coverage validation

Require 100% reported line, branch and executable-method coverage of the
`GridForge` assembly in `Release` and `ReleaseLean`. The core test project
collects this scope; sibling assemblies are not included. Use a fresh results
directory for each configuration:

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj -c Release --collect:"XPlat Code Coverage" --settings tests/GridForge.Tests/coverlet.runsettings --results-directory artifacts/coverage/Release
./.assets/scripts/assert-full-coverage.ps1 -CoverageDirectory artifacts/coverage/Release
```

Repeat with `ReleaseLean` in both configuration and output path. Preserve the
source-mode flags described below when validating coordinated sibling sources.
The same raw Cobertura gate runs in every Windows/Linux build/test lane and
before Pages publication. It requires the expected assembly, meaningful coverage
data and zero uncovered lines, branch outcomes or executable methods. The
existing Pages summary gate remains in place.

Complexity exceptions justify complexity only. Add behavioral tests for
reachable paths, or prove and remove unreachable runtime branches; do not waive
reported gaps through the register or new exclusions.

## Documentation

The root README is the concise product introduction. Deeper usage and
architecture guidance belongs under `docs/api/guides`, published alongside the
API reference. Keep links between Markdown pages relative and include their
`.md` extension. The repository is the documentation source of truth; there is
no wiki synchronization workflow. Keep guides evergreen and preserve completed
design and validation records under `docs/feature-work/done` as history.

Build the API site from a Release assembly:

```powershell
dotnet build src/GridForge/GridForge.csproj --configuration Release
dotnet tool restore
dotnet tool run docfx docs/api/docfx.json --warningsAsErrors
```

The generated site is disposable under `docs/api/obj`. Do not edit or commit
generated API metadata or HTML.

### Public XML contracts

Document authored public and protected APIs, including overloads, operators,
indexers, nested types, and declarations selected only on another shipped
target. Give members a concise summary and applicable parameter, type-parameter,
return, and property-value text. Explain coordinate spaces, defaults, failure
outputs, mutation, borrowed storage, invalidation, ordering, and caller-visible
exceptions. Use `inheritdoc` only when the generated page actually resolves the
shared contract, including its parameter names and exceptions. Private-field
inheritance does not populate public event summaries in the current assembly
metadata build; document those events directly.

The site selects only `src/GridForge/bin/Release/net10.0/GridForge.dll` and its
adjacent `GridForge.xml`. It does not build from C# source or automatically
include sibling assemblies, Lean output, or `netstandard2.1`. Build both Release
variants and inspect all four `GridForge.xml` outputs. On develop, retain the
source-mode flags from the coordinated workflow below throughout restore, build,
and test. Do not substitute a stale package-mode assembly for the site input.
Build the site with `--warningsAsErrors` and run the local-link and
required-page checks in `.github/workflows/coverage.yml`.

Reconcile source declarations, emitted XML, assembly symbols, and generated
DocFX members. Count partial shells once per API identity, and account
separately for implicit constructors, record synthesis, and MemoryPack-generated
APIs. Start inventory verification with a fresh `docs/api/obj` directory: move
any old generated output aside before rebuilding. DocFX can otherwise retain
metadata and pages for types removed from the current assembly. Compiler
warnings alone do not detect missing return text or unresolved inherited
comments. Inspect representative HTML for every family and documentation
pattern. The
[XML documentation verification record](docs/feature-work/done/2026-11-22-public-api-xml-documentation.md)
records the checked inputs, inventory, decisions, and known generated
exclusions.

Keep documentation passes behavior-preserving. Record confirmed implementation
defects in the [issue tracker](docs/feature-work/issue-tracker.md) with a
portable reproducer and source revision; put runtime fixes and maintained
regression tests in separate work. Preserve existing CI policy and do not stage
generated output.

## Source layout and cleanup

Give each namespace-level type or delegate its own file. Public/internal nested
types use companion files while preserving containing types, generic
constraints, and CLR identities; private nested helpers may stay with their
owner. Authored C# files under `src` carry the current filename and MIT license
header. Test and benchmark files under `tests` have no license header.

Run cleanup explicitly before review using the SDK from `global.json` and
Node.js/npm for Prettier:

```powershell
$env:TargetFramework='net10.0'
dotnet format GridForge.slnx --severity error
Remove-Item Env:TargetFramework
npx --yes prettier@3.9.5 --write .
```

Select one target framework for formatting to avoid competing fixes in shared
multi-target files. Clear that selection before normal builds and validate both
target frameworks. If workspace loading reports unmatched project/metadata
references, stop semantic fixes and clean/rebuild before retrying. The formatter
follows `.editorconfig`; it does not reproduce a Visual Studio cleanup profile.
Prettier is pinned in the command and may download on first use;
`.prettierignore` excludes generated output. No commit hook rewrites or stages
files.

Check without rewriting files:

```powershell
$env:TargetFramework='net10.0'
dotnet format GridForge.slnx --severity error --verify-no-changes
Remove-Item Env:TargetFramework
npx --yes prettier@3.9.5 --check .
```

## Coordinated source development

For coordinated unreleased changes, place Chronicler, FixedMathSharp and
SwiftCollections checkouts beside GridForge. Source mode selects these sibling
projects instead of published binaries:

```powershell
$env:UseLocalLsfStack='true'
dotnet build GridForge.slnx -c Release -p:DisableTransitiveProjectReferences=true -m:1
dotnet test GridForge.slnx -c Release --no-build -p:DisableTransitiveProjectReferences=true -m:1
dotnet build GridForge.slnx -c ReleaseLean -p:DisableTransitiveProjectReferences=true -m:1
dotnet test GridForge.slnx -c ReleaseLean --no-build -p:DisableTransitiveProjectReferences=true -m:1
```

Carry the transitive-reference flag globally through restore, build and test,
including formatting when source mode is selected. Serialize builds to avoid
shared sibling-output races. `Directory.Build.props` centralizes runtime version
selection and evergreen per-library identities. GridForge uses
`0.0.0-gf-local-lsf`; siblings use their corresponding local labels. All source
assemblies use assembly/file version `0.0.0.0`, even when GitVersion values are
present. These identities do not change for releases. Clear `UseLocalLsfStack`
before release validation: published packages remain the release source of
truth, and a source-mode build does not establish package compatibility.

## Branch-Specific CI Dependencies

Develop pushes and PRs targeting `develop` use `UseLocalLsfStack=true` and
immutable sibling source revisions in `build-and-test.yml`. Main and other
branches use released packages with no sibling checkouts; a `develop` -> `main`
PR validates packages. Release upstream dependencies before promotion and update
source pins deliberately as the coordinated stack changes. Source/package NuGet
caches are separated, and source graphs build serially to avoid shared-output
races. Restore, build and test also pass
`-p:DisableTransitiveProjectReferences=${{ env.UseLocalLsfStack }}` globally, so
inferred references in sibling projects cannot discard explicit assembly
identities. Serialization alone does not preserve those identities. Package mode
sets the flag to false. Both Release/Lean Windows/Linux lanes remain required.
