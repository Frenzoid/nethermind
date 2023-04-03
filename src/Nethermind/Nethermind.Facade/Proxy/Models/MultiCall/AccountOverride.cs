// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Int256;
using Nethermind.State.Snap;

namespace Nethermind.Facade.Proxy.Models.MultiCall;

public class AccountOverride
{
    public AccountOverrideType Type
    {
        get => State != null ? AccountOverrideType.AccountOverrideState : AccountOverrideType.AccountOverrideStateDiff;
    }

    public bool IsState => Type == AccountOverrideType.AccountOverrideState;
    public bool IsStateDiff => Type == AccountOverrideType.AccountOverrideStateDiff;

    /// AccountOverrideState and AccountOverrideStateDiff base
    public Address Address { get; set; }
    public UInt256 Nonce { get; set; }

    public UInt256 Balance { get; set; }
    public byte[] Code { get; set; }

    //Storage for AccountOverrideState
    public PathWithAccount? State { get; set; }

    //Storage difference for AccountOverrideStateDiff
    public PathWithAccount? StateDiff { get; set; }

}
