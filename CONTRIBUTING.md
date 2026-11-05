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
