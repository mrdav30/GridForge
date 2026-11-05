//=======================================================================
// GridExactChangeKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
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
