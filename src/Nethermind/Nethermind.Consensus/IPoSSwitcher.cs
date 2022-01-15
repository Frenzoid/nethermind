﻿//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.Consensus
{
    public interface IPoSSwitcher
    {
        void ForkchoiceUpdated(BlockHeader newHeadHash, Keccak finalizedHash);

        bool IsPos(BlockHeader header);

        bool HasEverReachedTerminalPoWBlock();

        event EventHandler TerminalPoWBlockReached;

        UInt256? TerminalTotalDifficulty { get; }

        bool IsTerminalPoWBlock(BlockHeader blockHeader, BlockHeader? parent = null);
        
        // We can get TerminalBlock from three different points in the system:
        // 1) Block Processing - it is needed because we need to switch classes, for example, block production, during the transition
        // 2) forkchoice - it will handle reorgs in terminal blocks during the transition process
        // 3) reverse header sync - we need to find the terminal block to process blocks correctly
        // Note: In the first post-merge release, the terminal block will be known, it explains why we can override it through settings.
        void UpdateTerminalBlock(BlockHeader blockHeader);
    }
}
