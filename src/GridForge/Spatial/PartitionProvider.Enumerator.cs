//=======================================================================
// PartitionProvider.Enumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace GridForge.Spatial;

public sealed partial class PartitionProvider<TPartitionBase>
    where TPartitionBase : class
{
    internal struct Enumerator
    {
        private readonly TPartitionBase? _firstPartition;
        private readonly TPartitionBase? _secondPartition;
        private readonly bool _hasOverflow;
        private SwiftList<OverflowPartition>.SwiftListEnumerator _overflowEnumerator;
        private int _inlineState;

        internal Enumerator(PartitionProvider<TPartitionBase> provider)
        {
            _firstPartition = provider._firstPartition;
            _secondPartition = provider._secondPartition;
            _overflowEnumerator = provider._overflowPartitions != null
                ? provider._overflowPartitions.GetEnumerator()
                : default;
            _hasOverflow = provider._overflowPartitions != null
                && provider._overflowPartitions.Count > 0;
            _inlineState = _firstPartition != null ? 0 : 2;
            Current = default!;
        }

        public TPartitionBase Current { get; private set; }

        public bool MoveNext()
        {
            if (_inlineState == 0)
            {
                Current = _firstPartition!;
                _inlineState = 1;
                return true;
            }

            if (_inlineState == 1)
            {
                _inlineState = 2;
                if (_secondPartition != null)
                {
                    Current = _secondPartition;
                    return true;
                }
            }

            if (!_hasOverflow || !_overflowEnumerator.MoveNext())
                return false;

            Current = _overflowEnumerator.Current.Partition;
            return true;
        }
    }
}
