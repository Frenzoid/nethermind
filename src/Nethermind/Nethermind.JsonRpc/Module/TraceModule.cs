/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nethermind.Blockchain;
using Nethermind.Config;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Logging;
using Nethermind.Dirichlet.Numerics;
using Nethermind.Evm.Tracing;
using Nethermind.JsonRpc.DataModel;
using Nethermind.JsonRpc.DataModel.Trace;
using Newtonsoft.Json;
using Transaction = Nethermind.JsonRpc.DataModel.Transaction;

namespace Nethermind.JsonRpc.Module
{
    public class TraceModule : ModuleBase, ITraceModule
    {
        private readonly ITracer _tracer;

        public TraceModule(IConfigProvider configProvider, ILogManager logManager, IJsonSerializer jsonSerializer, ITracer tracer)
            : base(configProvider, logManager, jsonSerializer)
        {
            _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        }

        private ParityTraceTypes GetParityTypes(string[] types)
        {
            return types.Select(s => (ParityTraceTypes) Enum.Parse(typeof(ParityTraceTypes), s, true)).Aggregate((t1, t2) => t1 | t2);
        }

        public ResultWrapper<ParityLikeTxTrace> trace_call(Transaction message, string[] traceTypes, BlockParameter quantity)
        {
            throw new NotImplementedException();
        }

        public ResultWrapper<ParityLikeTxTrace[]> trace_callMany((Transaction message, string[] traceTypes, BlockParameter numberOrTag)[] a)
        {
            throw new NotImplementedException();
        }

        public ResultWrapper<ParityLikeTxTrace> trace_rawTransaction(Data data, string[] traceTypes)
        {
            throw new NotImplementedException();
        }

        public ResultWrapper<ParityLikeTxTrace> trace_replayTransaction(Data hash, string[] traceTypes)
        {
            return ResultWrapper<ParityLikeTxTrace>.Success(_tracer.ParityTrace(new Keccak(hash.Value), GetParityTypes(traceTypes)));
        }

        public ResultWrapper<ParityLikeTxTrace[]> trace_replayBlockTransactions(BlockParameter numberOrTag, string[] traceTypes)
        {
            UInt256? blockNo = numberOrTag.BlockId.AsNumber();
            if (!blockNo.HasValue)
            {
                throw new InvalidDataException("Block number value incorrect");
            }

            return ResultWrapper<ParityLikeTxTrace[]>.Success(_tracer.ParityTraceBlock(blockNo.Value, GetParityTypes(traceTypes)));
        }

        public ResultWrapper<ParityLikeTxTrace[]> trace_block(BlockParameter block)
        {
            throw new NotImplementedException();
        }

        public ResultWrapper<ParityLikeTxTrace> trace_get(Data txHash, int[] positions)
        {
            throw new NotImplementedException();
        }

        public ResultWrapper<ParityLikeTxTrace> trace_transaction(Data transactionHash)
        {
            throw new NotImplementedException();
        }

        public override ModuleType ModuleType => ModuleType.Trace;

        public override IReadOnlyCollection<JsonConverter> GetConverters()
        {
            return new JsonConverter[]
            {
                new ParityLikeTxTraceConverter(),
                new ParityAccountStateChangeConverter(),
                new ParityTraceActionConverter(),
                new ParityTraceResultConverter(),
            };
        }

        public void Initialize()
        {
        }
    }
}