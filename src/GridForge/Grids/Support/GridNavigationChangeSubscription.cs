//=======================================================================
// GridNavigationChangeSubscription.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace GridForge.Grids;

/// <summary>
/// Owns one committed-change subscription and the atomic baseline captured after attachment.
/// </summary>
public sealed class GridNavigationChangeSubscription : IDisposable
{
    private GridWorld? _world;
    private Action<GridEventInfo>? _handler;

    /// <summary>
    /// The requested-address baseline captured atomically with subscription attachment.
    /// </summary>
    public GridNavigationBaseline Baseline { get; }

    internal GridNavigationChangeSubscription(
        GridWorld world,
        Action<GridEventInfo> handler,
        GridNavigationBaseline baseline)
    {
        _world = world;
        _handler = handler;
        Baseline = baseline;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GridWorld? world = Interlocked.Exchange(ref _world, null);
        Action<GridEventInfo>? handler = Interlocked.Exchange(ref _handler, null);
        if (world != null && handler != null)
            world.OnChangeCommitted -= handler;

        GC.SuppressFinalize(this);
    }
}
