//=======================================================================
// GridBoundaryContact.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Configuration;

namespace GridForge.Grids.Topology;

/// <summary>
/// Pairs an exact cell-contact manifold with both durable normalized grid identities.
/// </summary>
public readonly struct GridBoundaryContact
{
    /// <summary>The canonical source grid identity.</summary>
    public GridConfigurationKey SourceConfigurationKey { get; }

    /// <summary>The canonical target grid identity.</summary>
    public GridConfigurationKey TargetConfigurationKey { get; }

    /// <summary>The exact contact between the source and target cells.</summary>
    public VoxelContactManifold Contact { get; }

    internal GridBoundaryContact(
        GridConfigurationKey sourceConfigurationKey,
        GridConfigurationKey targetConfigurationKey,
        VoxelContactManifold contact)
    {
        SourceConfigurationKey = sourceConfigurationKey;
        TargetConfigurationKey = targetConfigurationKey;
        Contact = contact;
    }
}
