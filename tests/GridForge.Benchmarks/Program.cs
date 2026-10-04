using System;
using System.Linq;
using System.Reflection;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace GridForge.Benchmarks;

internal static class Program
{
    private static readonly BenchmarkCatalog _catalog = BenchmarkCatalog.Create(typeof(Program).Assembly);

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return RunBenchmarks(BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly), args);
        }

        string command = args[0];

        if ((string.Equals(command, "list", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(command, "ls", StringComparison.OrdinalIgnoreCase)) &&
            args.Length == 1)
        {
            _catalog.WriteAvailableSelections(Console.Out);
            return 0;
        }

        if (string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
        {
            WriteUsage();
            return 0;
        }

        if (string.Equals(command, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 1 && !args[1].StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("The 'all' selection cannot be combined with other benchmark aliases.");
                Console.Error.WriteLine();
                WriteUsage();
                return 1;
            }

            return RunBenchmarks(BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly), args.Skip(1).ToArray());
        }

        int aliasCount = 0;
        while (aliasCount < args.Length && !args[aliasCount].StartsWith("-", StringComparison.Ordinal))
            aliasCount++;

        if (aliasCount == 0)
        {
            return RunBenchmarks(BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly), args);
        }

        Type[] selectedTypes = _catalog.Resolve(args.Take(aliasCount).ToArray(), out string unknownAlias);
        if (unknownAlias != null)
        {
            Console.Error.WriteLine($"Unknown benchmark selection '{unknownAlias}'.");
            Console.Error.WriteLine();
            WriteUsage();
            return 1;
        }

        return RunBenchmarks(BenchmarkSwitcher.FromTypes(selectedTypes), args.Skip(aliasCount).ToArray());
    }

    private static int RunBenchmarks(BenchmarkSwitcher switcher, string[] args)
    {
        var build = Job.Default.WithCustomBuildConfiguration(
            typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>().Configuration);
#if USE_LOCAL_LSF_STACK
        // Generated child builds must preserve the versioned source-stack edges
        // and serialize projects that share their benchmark output directory.
        build = build.WithMsBuildArguments("/p:UseLocalLsfStack=true",
            "/p:DisableTransitiveProjectReferences=true", "/p:BuildInParallel=false");
#endif
        // Mutate build settings only so CLI jobs, including Dry, retain their shape.
        var config = DefaultConfig.Instance.AddJob(build.AsMutator());
        return BenchmarkExitCode.Get(switcher.Run(args, config));
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0");
        Console.WriteLine("  dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0 -- all --list flat");
        Console.WriteLine("  dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0 -- grid-navigation-body-trace --job Dry");
        Console.WriteLine();
        Console.WriteLine("Leading arguments that do not start with '-' are treated as benchmark selections.");
        Console.WriteLine("Remaining arguments are forwarded to BenchmarkDotNet.");
        Console.WriteLine();
        _catalog.WriteAvailableSelections(Console.Out);
    }
}
