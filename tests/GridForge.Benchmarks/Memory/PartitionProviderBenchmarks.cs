using BenchmarkDotNet.Attributes;
using GridForge.Spatial;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class PartitionProviderBenchmarks
{
    private readonly EntryA _first = new();
    private readonly EntryB _second = new();
    private readonly EntryC _third = new();
    private PartitionProvider<object> _reusedProvider;

    [GlobalSetup]
    public void Setup()
    {
        _reusedProvider = new PartitionProvider<object>();
        _ = _reusedProvider.TryAdd(typeof(EntryA), _first);
        _ = _reusedProvider.TryAdd(typeof(EntryB), _second);
        _ = _reusedProvider.TryRemove(typeof(EntryB), out _);
        _ = _reusedProvider.TryRemove(typeof(EntryA), out _);

        PartitionProvider<object> promotionWarmup = new();
        _ = promotionWarmup.TryAdd(typeof(EntryA), _first);
        _ = promotionWarmup.TryAdd(typeof(EntryB), _second);
        _ = promotionWarmup.TryAdd(typeof(EntryC), _third);
    }

    [Benchmark(Baseline = true, Description = "Reuse provider with two concrete types")]
    [BenchmarkCategory("Memory", "Partitions")]
    public int ReuseTwoConcreteTypes()
    {
        int completedOperations = 0;
        if (_reusedProvider.TryAdd(typeof(EntryA), _first))
            completedOperations++;
        if (_reusedProvider.TryAdd(typeof(EntryB), _second))
            completedOperations++;
        if (_reusedProvider.TryRemove(typeof(EntryB), out _))
            completedOperations++;
        if (_reusedProvider.TryRemove(typeof(EntryA), out _))
            completedOperations++;

        return completedOperations;
    }

    [Benchmark(Description = "Create provider with two concrete types")]
    [BenchmarkCategory("Memory", "Partitions")]
    public PartitionProvider<object> CreateWithTwoConcreteTypes()
    {
        PartitionProvider<object> provider = new();
        _ = provider.TryAdd(typeof(EntryA), _first);
        _ = provider.TryAdd(typeof(EntryB), _second);
        return provider;
    }

    [Benchmark(Description = "Create provider with three concrete types")]
    [BenchmarkCategory("Memory", "Partitions")]
    public PartitionProvider<object> CreateWithThreeConcreteTypes()
    {
        PartitionProvider<object> provider = new();
        _ = provider.TryAdd(typeof(EntryA), _first);
        _ = provider.TryAdd(typeof(EntryB), _second);
        _ = provider.TryAdd(typeof(EntryC), _third);
        return provider;
    }

    private sealed class EntryA { }

    private sealed class EntryB { }

    private sealed class EntryC { }
}
