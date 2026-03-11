using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace GridForge.Benchmarks;

internal sealed class InProcessShortRunConfig : ManualConfig
{
    public InProcessShortRunConfig()
    {
        AddJob(Job.ShortRun.WithToolchain(InProcessNoEmitToolchain.Instance));
    }
}
