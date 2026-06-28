// This shim allows consumers to disable MemoryPack entirely, and use their own serialization solution instead.
#if GRIDFORGE_DISABLE_MEMORYPACK
using System;

namespace MemoryPack;

/// <summary>
/// MemoryPack compatibility attribute used when built without MemoryPack.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
internal sealed class MemoryPackableAttribute : Attribute { }

/// <summary>
/// MemoryPack compatibility attribute used when built without MemoryPack.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
internal sealed class MemoryPackIncludeAttribute : Attribute { }

/// <summary>
/// MemoryPack compatibility attribute used when built without MemoryPack.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
internal sealed class MemoryPackIgnoreAttribute : Attribute { }

/// <summary>
/// MemoryPack compatibility attribute used when built without MemoryPack.
/// </summary>
[AttributeUsage(AttributeTargets.Constructor)]
internal sealed class MemoryPackConstructorAttribute : Attribute { }

/// <summary>
/// MemoryPack compatibility attribute used when built without MemoryPack.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
internal sealed class MemoryPackAllowSerializeAttribute : Attribute { }

/// <summary>
/// MemoryPack compatibility attribute used when built without MemoryPack.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
internal sealed class MemoryPackOrderAttribute : Attribute
{
        /// <summary>
        /// Initializes the compatibility attribute.
        /// </summary>
#pragma warning disable IDE0060 // Remove unused parameter
        public MemoryPackOrderAttribute(ushort order) { }
#pragma warning restore IDE0060 // Remove unused parameter      

        /// <summary>
        /// Initializes the compatibility attribute.
        /// </summary>
#pragma warning disable IDE0060 // Remove unused parameter
        public MemoryPackOrderAttribute(int order) { }
#pragma warning restore IDE0060 // Remove unused parameter
}

#endif
