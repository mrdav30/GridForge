//=======================================================================
// GridCommittedChange.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace GridForge.Grids;

internal enum GridExactChangeKind : byte
{
    None = 0,
    ObstacleAdded = 1,
    ObstacleRemoved = 2,
    ObstaclesCleared = 3,
}

internal readonly struct GridCommittedChange
{
    public readonly GridEventInfo GridEvent;
    public readonly GridExactChangeKind ExactKind;
    public readonly ObstacleEventInfo ObstacleEvent;
    public readonly ObstacleClearEventInfo ObstacleClearEvent;
    public readonly Voxel? TargetVoxel;

    public GridCommittedChange(GridEventInfo gridEvent)
    {
        GridEvent = gridEvent;
        ExactKind = GridExactChangeKind.None;
        ObstacleEvent = default;
        ObstacleClearEvent = default;
        TargetVoxel = null;
    }

    public GridCommittedChange(
        GridEventInfo gridEvent,
        GridExactChangeKind exactKind,
        ObstacleEventInfo obstacleEvent,
        Voxel targetVoxel)
    {
        GridEvent = gridEvent;
        ExactKind = exactKind;
        ObstacleEvent = obstacleEvent;
        ObstacleClearEvent = default;
        TargetVoxel = targetVoxel;
    }

    public GridCommittedChange(
        GridEventInfo gridEvent,
        ObstacleClearEventInfo obstacleClearEvent,
        Voxel targetVoxel)
    {
        GridEvent = gridEvent;
        ExactKind = GridExactChangeKind.ObstaclesCleared;
        ObstacleEvent = default;
        ObstacleClearEvent = obstacleClearEvent;
        TargetVoxel = targetVoxel;
    }
}
