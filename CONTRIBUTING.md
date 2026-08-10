# Contributing

Focused fixes can proceed through a pull request. Open an issue or discussion
first when a change affects public API shape, deterministic behavior,
serialization layout, topology semantics, storage strategy, or several
subsystems at once.

Please note we have a code of conduct, please follow it in all your interactions
with the project.

## Development setup

Install the SDK selected by `global.json` and the .NET 8 runtime used by the
tests and benchmarks. Then restore and run the normal Debug workflow:

```powershell
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug --no-restore
dotnet test GridForge.slnx --configuration Debug --no-build
```

GridForge targets `netstandard2.1` and `net8.0`. Keep shared runtime code
compatible with both targets and free of game-engine dependencies.

## Pull Request Process

1. Keep the change focused and preserve deterministic, world-scoped behavior.
2. Add or update the closest tests for behavior changes, especially around
   snapping, identity, topology, sparse storage, pooling, and query lifetimes.
3. Update XML documentation, the README, wiki, migration guide, or benchmarks
   whenever the public contract or evidence changes.
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
architecture guidance belongs under `docs/wiki`, which is synchronized to the
GitHub Wiki. Keep links between those Markdown pages relative and include their
`.md` extension.

Build the API site from a Release assembly:

```powershell
dotnet build src/GridForge/GridForge.csproj --configuration Release
dotnet tool restore
dotnet tool run docfx docs/api/docfx.json --warningsAsErrors
```

The generated site is disposable under `docs/api/obj`. Do not edit or commit
generated API metadata or HTML.

## Code of Conduct

### Our Pledge

In the interest of fostering an open and welcoming environment, we as
contributors and maintainers pledge to making participation in our project and
our community a harassment-free experience for everyone, regardless of age, body
size, disability, ethnicity, gender identity and expression, level of
experience, nationality, personal appearance, race, religion, or sexual identity
and orientation.

### Our Standards

Examples of behavior that contributes to creating a positive environment
include:

- Using welcoming and inclusive language
- Being respectful of differing viewpoints and experiences
- Gracefully accepting constructive criticism
- Focusing on what is best for the community
- Showing empathy towards other community members

Examples of unacceptable behavior by participants include:

- The use of sexualized language or imagery and unwelcome sexual attention or
  advances
- Trolling, insulting/derogatory comments, and personal or political attacks
- Public or private harassment
- Publishing others' private information, such as a physical or electronic
  address, without explicit permission
- Other conduct which could reasonably be considered inappropriate in a
  professional setting

### Our Responsibilities

Project maintainers are responsible for clarifying the standards of acceptable
behavior and are expected to take appropriate and fair corrective action in
response to any instances of unacceptable behavior.

Project maintainers have the right and responsibility to remove, edit, or reject
comments, commits, code, wiki edits, issues, and other contributions that are
not aligned to this Code of Conduct, or to ban temporarily or permanently any
contributor for other behaviors that they deem inappropriate, threatening,
offensive, or harmful.

### Scope

This Code of Conduct applies both within project spaces and in public spaces
when an individual is representing the project or its community. Examples of
representing a project or community include using an official project e-mail
address, posting via an official social media account, or acting as an appointed
representative at an online or offline event. Representation of a project may be
further defined and clarified by project maintainers.

### Enforcement

Instances of abusive, harassing, or otherwise unacceptable behavior may be
reported by contacting the project team at `david.oravsky@gmail.com`. All
complaints will be reviewed and investigated and will result in a response that
is deemed necessary and appropriate to the circumstances. The project team is
obligated to maintain confidentiality with regard to the reporter of an
incident. Further details of specific enforcement policies may be posted
separately.

Project maintainers who do not follow or enforce the Code of Conduct in good
faith may face temporary or permanent repercussions as determined by other
members of the project's leadership.

### Attribution

This Code of Conduct is adapted from the [Contributor Covenant][homepage],
version 1.4, available at [http://contributor-covenant.org/version/1/4][version]

[homepage]: http://contributor-covenant.org
[version]: http://contributor-covenant.org/version/1/4/
