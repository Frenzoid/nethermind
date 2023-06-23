// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Int256;
using Nethermind.State;

namespace Nethermind.Evm.Precompiles.Statefull;
public class BeaconStateRootPrecompile : IPrecompile
{
    private readonly IWorldState _state;
    public static Address Address { get; } = Address.FromNumber(0x0B);
    public static UInt256 HISTORICAL_ROOTS_LENGTH = 98304;
    private BeaconStateRootPrecompile(IWorldState state)
    {
        _state = state;
    }

    public static IPrecompile Instance(IWorldState state) => new BeaconStateRootPrecompile(state);

    private byte[] SloadFromStorage(UInt256 index)
    {
        StorageCell storageCell = new(Address, index);
        return _state.Get(storageCell);
    }

    public (ReadOnlyMemory<byte>, bool) Run(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        Metrics.BeaconStatePrecompile++;
        Span<byte> inputDataSpan = stackalloc byte[32];
        inputData.PrepareEthInput(inputDataSpan);

        UInt256 timestamp = new UInt256(inputDataSpan);
        UInt256.Mod(timestamp, HISTORICAL_ROOTS_LENGTH, out UInt256 timestampReduced);
        UInt256 recordedTimestamp = new UInt256(SloadFromStorage(timestampReduced));

        if (recordedTimestamp != timestamp)
        {
            return (UInt256.Zero.ToBigEndian(), true);
        }
        else
        {
            UInt256 timestampExtended = timestampReduced + HISTORICAL_ROOTS_LENGTH;
            byte[] recordedRoot = SloadFromStorage(timestampExtended);
            return (recordedRoot, true);
        }
    }

    public long BaseGasCost(IReleaseSpec releaseSpec)
    {
        return 2100;
    }

    public long DataGasCost(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        return 0L;
    }
}
