// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.Evm.Precompiles.Snarks;
using Nethermind.Int256;
using Nethermind.State;

namespace Nethermind.Evm.Precompiles.Statefull;
internal class BeaconStateRootPrecompile : IPrecompile
{
    private readonly IWorldState _state;
    public static Address Address { get; } = Address.FromNumber(UInt256.MaxValue - 2);

    private BeaconStateRootPrecompile(IWorldState state)
    {
        _state = state;
    }

    public static IPrecompile Instance(IWorldState state) => new BeaconStateRootPrecompile(state);

    public (ReadOnlyMemory<byte>, bool) Run(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        Metrics.Bn254MulPrecompile++;
        Span<byte> inputDataSpan = stackalloc byte[32];
        inputData.PrepareEthInput(inputDataSpan);

        StorageCell storageCell = new(Address, new UInt256(inputDataSpan));
        byte[] timespan = _state.Get(storageCell);

        (byte[], bool) result = (timespan, true);

        return result;
    }

    public long BaseGasCost(IReleaseSpec releaseSpec)
    {
        return 2100; // Note(Ayman) : find a way to add sload gas calculations
    }

    public long DataGasCost(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        return 0L;
    }
}
