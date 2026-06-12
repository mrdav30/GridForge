# Repository Layout and Build

This page is the operational map of the repo: where things live, how the solution is put together, and what the build actually does.

If you are onboarding as a contributor, this is the page that turns "I know what GridForge is" into "I know where to work and which command to run."

## Solution Shape

The solution currently contains three projects:

| Project | Path | Purpose |
| --- | --- | --- |
| `GridForge` | `src/GridForge/GridForge.csproj` | Main deterministic voxel-grid library |
| `GridForge.Tests` | `tests/GridForge.Tests/GridForge.Tests.csproj` | xUnit validation suite |
| `GridForge.Benchmarks` | `tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj` | BenchmarkDotNet performance and allocation benchmarks |

The root solution file is `GridForge.slnx`.

## Repository Layout

| Path | What it contains |
| --- | --- |
| `src/GridForge` | Library source, package metadata, XML docs generation, and NuGet packaging configuration |
| `src/GridForge/Configuration` | Grid creation inputs and snapped bounds identity |
| `src/GridForge/Grids` | Core grid model, managers, node types, storage, topology, and pooling |
| `src/GridForge/Grids/Storage` | Dense and sparse physical voxel storage |
| `src/GridForge/Grids/Topology` | Per-grid topology metrics, snapping, dimensions, and world/index projection |
| `src/GridForge/Spatial` | Shared indices, occupants, partitions, directions, and awareness types |
| `src/GridForge/Blockers` | Bounds-based obstacle application built on tracer coverage |
| `src/GridForge/Utility` | Tracing and logging support |
| `tests/GridForge.Tests` | Unit tests organized by subsystem |
| `tests/GridForge.Benchmarks` | Focused perf and allocation scenarios |
| `.github/workflows` | CI automation |
| `.assets/scripts` | Release-oriented PowerShell helpers |
| `docs/wiki` | GitHub wiki source content used as the deeper documentation companion |

The solution also exposes a small set of root-level "solution items" such as `.editorconfig`, `README.md`, `AGENTS.md`, `LICENSE`, and `coverlet.runsettings`.

## Main Library Build Facts

The library project in `src/GridForge/GridForge.csproj` establishes a few important build behaviors:

- language version is C# 11
- target frameworks are `netstandard2.1` and `net8.0`
- `ImplicitUsings` is disabled
- `Nullable` is enabled in the library project
- XML documentation files are generated
- deterministic and CI build flags are enabled
- `GeneratePackageOnBuild` is enabled
- the supported configurations are `Debug`, `Release`, and `ReleaseLean`

That last item is easy to miss: building the library also produces NuGet artifacts.

## Package And Dependency Notes

The standard package currently depends on:

- `FixedMathSharp`
- `SwiftCollections`
- `MemoryPack`
- `System.Text.Json` for the `netstandard2.1` target only

The lean package is built by `ReleaseLean` or `DisableMemoryPack=true` and uses:

- `FixedMathSharp.Lean`
- `SwiftCollections.Lean`
- the `GRIDFORGE_DISABLE_MEMORYPACK` compilation symbol

Both package variants expose the same core voxel-grid API.

Packaging also includes the root `README.md`, `LICENSE`, `NOTICE`, `COPYRIGHT`, and `icon.png`.

## Versioning Behavior

The project is wired to consume GitVersion environment variables when they are available.

In practice that means:

- CI can stamp semantic version, assembly version, and informational version automatically
- local builds still work without GitVersion and fall back to version `0.0.0`

That fallback is intentional and keeps local iteration simple.

## What `dotnet build` Produces

When you build `src/GridForge/GridForge.csproj`, expect:

- compiled library outputs under `src/GridForge/bin/<Configuration>/<TargetFramework>/`
- XML documentation alongside the assembly
- `.nupkg` and `.snupkg` package artifacts because package generation is enabled on build

If you are only expecting a DLL and a testable build, this packaging behavior can look surprising the first time you notice it.

## Validation Project Facts

### `GridForge.Tests`

The test project:

- targets `net8.0`
- is marked as a test project and not packable
- references the main library project directly
- supports `Debug`, `Release`, and `ReleaseLean`
- uses xUnit v3 plus the Visual Studio runner and console runner
- includes `coverlet.collector`
- points to `coverlet.runsettings`

The runsettings currently exclude generated MemoryPack files from coverage collection so coverage numbers are less noisy.

### `GridForge.Benchmarks`

The benchmark project:

- targets `net8.0`
- is an executable
- references the main library project directly
- supports `Debug`, `Release`, and `ReleaseLean`
- uses BenchmarkDotNet
- is optimized for benchmark runs

This project is where allocation-sensitive or throughput-sensitive changes should be checked.

## Common Build Commands

The usual local flow is:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

When you specifically want release-like library artifacts:

```bash
dotnet build src/GridForge/GridForge.csproj --configuration Release
```

Because packaging is enabled on build, this also emits the package artifacts.

## CI Expectations

The current CI workflow lives in `.github/workflows/build-and-test.yml`.

At a high level it:

- runs on `ubuntu-latest` and `windows-latest`
- triggers on pushes except `dependabot/**`, `gh-pages`, and version tags like `v*`
- triggers on pull requests targeting `main` or `develop`
- installs .NET 8
- installs and executes GitVersion
- restores dependencies
- builds the solution in both `Release` and `ReleaseLean`
- runs the test suite in both `Release` and `ReleaseLean`

This is a useful sanity check when deciding whether a change is likely to be cross-platform safe.

## Release Helper Script

`.assets/scripts/set-version-and-build.ps1` is the repo helper for version-aware release builds.

Its flow is:

1. locate the solution root
2. ensure GitVersion environment variables are present
3. build both `Release` and `ReleaseLean`
4. walk each release target-framework output folder
5. zip each framework output into a versioned archive under `artifacts/releases`

That makes it the practical handoff script for release packaging, not just a convenience wrapper around `dotnet build`.

## Build And Repo Pitfalls

- Forgetting that the library builds for two target frameworks
- Forgetting that build also creates NuGet packages
- Assuming CI only validates on one operating system
- Editing files under `bin`, `obj`, or `TestResults` instead of the source that produces them
- Treating the benchmark project as optional when changing pooling, tracing, storage, topology, or registration behavior

## Read This Next

- [Testing and Benchmarking](Testing-and-Benchmarking.md) for the validation workflow
- [Determinism, Snapping, and Pooling](Determinism-Snapping-and-Pooling.md) for the invariants that build and tests are protecting
- [Home](Home.md) for the broader architecture map and page roadmap
