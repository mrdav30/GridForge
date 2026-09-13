using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Reports;

namespace GridForge.Benchmarks;

internal static class BenchmarkExitCode
{
    // Result rows can survive a failed child exit; empty summaries also represent help/list.
    internal static int Get(IEnumerable<Summary> summaries) =>
        summaries.Any(summary =>
            summary.HasCriticalValidationErrors ||
            summary.Reports.Any(report =>
                !report.Success ||
                report.ExecuteResults.Any(result => !result.FoundExecutable || result.ExitCode != 0)))
            ? 1
            : 0;
}
