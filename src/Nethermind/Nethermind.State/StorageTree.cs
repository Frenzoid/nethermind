// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Logging;
using Nethermind.Int256;
using Nethermind.Serialization.Rlp;
using Nethermind.Trie;
using Nethermind.Trie.Pruning;

namespace Nethermind.State
{
    public class StorageTree : PatriciaTree
    {
        private static readonly UInt256 CacheSize = 1024;

        private static readonly int CacheSizeInt = (int)CacheSize;

        private static readonly Dictionary<UInt256, byte[]> Cache = new(CacheSizeInt);

        private Address? AccountAddress { get; }

        static StorageTree()
        {
            Span<byte> buffer = stackalloc byte[32];
            for (int i = 0; i < CacheSizeInt; i++)
            {
                UInt256 index = (UInt256)i;
                index.ToBigEndian(buffer);
                Cache[index] = Keccak.Compute(buffer).Bytes;
            }
        }

        public StorageTree(ITrieStore trieStore, ILogManager? logManager, Address? accountAddress = null)
            : base(trieStore, Keccak.EmptyTreeHash, false, true, logManager)
        {
            TrieType = TrieType.Storage;
            if (trieStore.Capability == TrieNodeResolverCapability.Path)
            {
                AccountAddress = accountAddress ?? throw new ArgumentException("this cannot be null while using path based trie store");
                StoragePrefix = AccountAddress.Bytes;
            }
        }

        public StorageTree(ITrieStore trieStore, Keccak rootHash, ILogManager? logManager, Address? accountAddress = null)
            : base(trieStore, rootHash, false, true, logManager)
        {
            TrieType = TrieType.Storage;
            if (trieStore.Capability == TrieNodeResolverCapability.Path)
            {
                AccountAddress = accountAddress ?? throw new ArgumentException("this cannot be null while using path based trie store");
                StoragePrefix = AccountAddress.Bytes;
            }
        }

        private static void GetKey(in UInt256 index, in Span<byte> key)
        {
            if (index < CacheSize)
            {
                Cache[index].CopyTo(key);
                return;
            }

            index.ToBigEndian(key);

            // in situ calculation
            KeccakHash.ComputeHashBytesToSpan(key, key);
        }


        [SkipLocalsInit]
        public byte[] Get(in UInt256 index, Keccak? storageRoot = null)
        {
            Span<byte> key = stackalloc byte[_trieStore.Capability == TrieNodeResolverCapability.Hash ? 32 : 32 + 20];

            switch (_trieStore.Capability)
            {
                case TrieNodeResolverCapability.Hash:
                    GetKey(index, key);
                    break;
                case TrieNodeResolverCapability.Path:
                    if (AccountAddress != null) AccountAddress.Bytes.CopyTo(key);
                    GetKey(index, key.Slice(20));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            byte[]? value = Get(key, storageRoot);
            if (value is null)
            {
                return new byte[] { 0 };
            }

            Rlp.ValueDecoderContext rlp = value.AsRlpValueContext();
            return rlp.DecodeByteArray();
        }

        [SkipLocalsInit]
        public void Set(in UInt256 index, byte[] value)
        {
            Span<byte> key = stackalloc byte[32];
            GetKey(index, key);
            SetInternal(key, value);
        }

        public void Set(Keccak key, byte[] value, bool rlpEncode = true)
        {
            SetInternal(key.Bytes, value, rlpEncode);
        }

        private void SetInternal(Span<byte> rawKey, byte[] value, bool rlpEncode = true)
        {
            if (value.IsZero())
            {
                Set(rawKey, Array.Empty<byte>());
            }
            else
            {
                Rlp rlpEncoded = rlpEncode ? Rlp.Encode(value) : new Rlp(value);
                Set(rawKey, rlpEncoded);
            }
        }
    }
}
