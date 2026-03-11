using FixedMathSharp;
using GridForge.Configuration;
using System;

namespace GridForge.Benchmarks;

internal static class BenchmarkScenarioFactory
{
    public static GridConfiguration[] CreateTiledFlatGridConfigurations(
        int tilesX,
        int tilesZ,
        int extent,
        int scanCellSize = GridConfiguration.DefaultScanCellSize,
        bool overlapBoundaries = false,
        int originX = 0,
        int originZ = 0)
    {
        GridConfiguration[] configurations = new GridConfiguration[tilesX * tilesZ];
        int step = overlapBoundaries ? extent : extent + 1;
        int index = 0;

        for (int z = 0; z < tilesZ; z++)
        {
            for (int x = 0; x < tilesX; x++)
            {
                int minX = originX + x * step;
                int minZ = originZ + z * step;

                configurations[index++] = new GridConfiguration(
                    new Vector3d(minX, 0, minZ),
                    new Vector3d(minX + extent, 0, minZ + extent),
                    scanCellSize);
            }
        }

        return configurations;
    }

    public static BoundingArea[] CreateBlockerAreas(
        int count,
        int span,
        int columns,
        int stride,
        int offset = 4)
    {
        BoundingArea[] areas = new BoundingArea[count];

        for (int i = 0; i < areas.Length; i++)
        {
            int row = i / columns;
            int column = i % columns;
            int x = offset + column * stride + (row & 1);
            int z = offset + row * stride + (column & 1);

            Vector3d min = new Vector3d(x, 0, z);
            Vector3d max = new Vector3d(x + span, 0, z + span);

            areas[i] = new BoundingArea(min, max);
        }

        return areas;
    }

    public static BenchmarkOccupant[] CreateOccupants(
        int count,
        int width,
        int depth,
        int y = 0,
        int groupCount = 8)
    {
        BenchmarkOccupant[] occupants = new BenchmarkOccupant[count];

        int index = 0;
        int groupId = 0;

        for (int z = 0; z < depth && index < count; z++)
        {
            for (int x = 0; x < width && index < count; x++)
            {
                occupants[index++] = new BenchmarkOccupant(
                    new Vector3d(x, y, z),
                    (byte)(groupId++ % groupCount));
            }
        }

        if (index != count)
            throw new InvalidOperationException($"Only generated {index} of {count} requested occupants.");

        return occupants;
    }
}
