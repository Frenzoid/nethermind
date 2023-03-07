// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nethermind.Core;
using Nethermind.Core.Caching;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Evm.CodeAnalysis;
using Nethermind.Int256;
using Nethermind.Evm.Precompiles;
using Nethermind.Evm.Precompiles.Bls.Shamatar;
using Nethermind.Evm.Precompiles.Snarks.Shamatar;
using Nethermind.Evm.Tracing;
using Nethermind.State;
using System.Runtime.Intrinsics;

namespace Nethermind.Evm;
public partial class VirtualMachine
{
    private enum InstructionReturn
    {
        Success,
        Continue,
        OutOfGas,
        AccessViolation
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionTSTORE(ref EvmStack stack, ref long gasAvailable, Address executingAccount, IReleaseSpec spec)
    {
        if (!UpdateGas(GasCostOf.TStore, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 storageIndex);
        Span<byte> newValue = stack.PopBytes();
        bool newIsZero = newValue.IsZero();
        if (!newIsZero)
        {
            newValue = newValue.WithoutLeadingZeros().ToArray();
        }
        else
        {
            newValue = BytesZero;
        }

        StorageCell storageCell = new(executingAccount, storageIndex);
        byte[] currentValue = newValue.ToArray();
        _storage.SetTransientState(storageCell, currentValue);

        if (_txTracer.IsTracingOpLevelStorage)
        {
            _txTracer.SetOperationTransientStorage(storageCell.Address, storageIndex, newValue, currentValue);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionTLOAD(ref EvmStack stack, ref long gasAvailable, Address executingAccount, IReleaseSpec spec)
    {
        if (!UpdateGas(GasCostOf.TLoad, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 storageIndex);
        StorageCell storageCell = new(executingAccount, storageIndex);

        byte[] value = _storage.GetTransientState(storageCell);
        stack.PushBytes(value);

        if (_txTracer.IsTracingOpLevelStorage)
        {
            _txTracer.LoadOperationTransientStorage(storageCell.Address, storageIndex, value);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionSLOAD(ref EvmStack stack, ref long gasAvailable, EvmState vmState, Address executingAccount, IReleaseSpec spec)
    {
        Metrics.SloadOpcode++;
        if (!UpdateGas(spec.GetSLoadCost(), ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 storageIndex);
        StorageCell storageCell = new(executingAccount, storageIndex);
        if (!ChargeStorageAccessGas(
            ref gasAvailable,
            vmState,
            storageCell,
            StorageAccessType.SLOAD,
            spec)) return false;

        byte[] value = _storage.Get(storageCell);
        stack.PushBytes(value);

        if (_txTracer.IsTracingOpLevelStorage)
        {
            _txTracer.LoadOperationStorage(storageCell.Address, storageIndex, value);
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionMSTORE8(ref EvmStack stack, ref long gasAvailable, EvmState vmState)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 memPosition);
        byte data = stack.PopByte();
        if (!UpdateMemoryCost(vmState.Memory, ref gasAvailable, in memPosition, UInt256.One)) return false;
        vmState.Memory.SaveByte(in memPosition, data);
        if (_txTracer.IsTracingInstructions) _txTracer.ReportMemoryChange((long)memPosition, data);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionMSTORE(ref EvmStack stack, ref long gasAvailable, EvmState vmState)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 memPosition);

        Span<byte> data = stack.PopBytes();

        if (!UpdateMemoryCost(vmState.Memory, ref gasAvailable, in memPosition, 32)) return false;

        vmState.Memory.SaveWord(in memPosition, data);
        if (_txTracer.IsTracingInstructions) _txTracer.ReportMemoryChange((long)memPosition, data.SliceWithZeroPadding(0, 32, PadDirection.Left));

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionMLOAD(ref EvmStack stack, ref long gasAvailable, EvmState vmState)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 memPosition);

        if (!UpdateMemoryCost(vmState.Memory, ref gasAvailable, in memPosition, 32)) return false;

        Span<byte> memData = vmState.Memory.LoadSpan(in memPosition);
        if (_txTracer.IsTracingInstructions) _txTracer.ReportMemoryChange(memPosition, memData);

        stack.PushBytes(memData);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionEXTCODEHASH(ref EvmStack stack, ref long gasAvailable, EvmState vmState, IReleaseSpec spec)
    {
        if (!UpdateGas(spec.GetExtCodeHashCost(), ref gasAvailable)) return false;

        Address address = stack.PopAddress();
        if (!ChargeAccountAccessGas(ref gasAvailable, vmState, address, spec)) return false;

        if (!_state.AccountExists(address) || _state.IsDeadAccount(address))
        {
            stack.PushZero();
        }
        else
        {
            stack.PushBytes(_state.GetCodeHash(address).Bytes);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionSELFDESTRUCT(ref EvmStack stack, ref long gasAvailable, EvmState vmState, Address executingAccount, IReleaseSpec spec)
    {
        Metrics.SelfDestructs++;

        Address inheritor = stack.PopAddress();
        if (!ChargeAccountAccessGas(ref gasAvailable, vmState, inheritor, spec, false)) return false;

        vmState.DestroyList.Add(executingAccount);

        UInt256 ownerBalance = _state.GetBalance(executingAccount);
        if (_txTracer.IsTracingActions) _txTracer.ReportSelfDestruct(executingAccount, ownerBalance, inheritor);
        if (spec.ClearEmptyAccountWhenTouched && ownerBalance != 0 && _state.IsDeadAccount(inheritor))
        {
            if (!UpdateGas(GasCostOf.NewAccount, ref gasAvailable)) return false;
        }

        bool inheritorAccountExists = _state.AccountExists(inheritor);
        if (!spec.ClearEmptyAccountWhenTouched && !inheritorAccountExists && spec.UseShanghaiDDosProtection)
        {
            if (!UpdateGas(GasCostOf.NewAccount, ref gasAvailable)) return false;
        }

        if (!inheritorAccountExists)
        {
            _state.CreateAccount(inheritor, ownerBalance);
        }
        else if (!inheritor.Equals(executingAccount))
        {
            _state.AddToBalance(inheritor, ownerBalance, spec);
        }

        _state.SubtractFromBalance(executingAccount, ownerBalance, spec);

        if (_txTracer.IsTracingInstructions) EndInstructionTrace(gasAvailable, vmState.Memory?.Size ?? 0);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSHL(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        if (a >= 256UL)
        {
            stack.PopLimbo();
            stack.PushZero();
        }
        else
        {
            stack.PopUInt256(out UInt256 b);
            UInt256 res = b << (int)a.u0;
            stack.PushUInt256(in res);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSHR(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        if (a >= 256)
        {
            stack.PopLimbo();
            stack.PushZero();
        }
        else
        {
            stack.PopUInt256(out UInt256 b);
            UInt256 res = b >> (int)a.u0;
            stack.PushUInt256(in res);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSAR(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopSignedInt256(out Int256.Int256 b);
        if (a >= BigInt256)
        {
            if (b.Sign >= 0)
            {
                stack.PushZero();
            }
            else
            {
                Int256.Int256 res = Int256.Int256.MinusOne;
                stack.PushSignedInt256(in res);
            }
        }
        else
        {
            b.RightShift((int)a, out Int256.Int256 res);
            stack.PushSignedInt256(in res);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (InstructionReturn result, EvmState? callState) InstructionCREATE(Instruction instruction, ref EvmStack stack, ref long gasAvailable, ref ExecutionEnvironment env, EvmState vmState, IReleaseSpec spec)
    {
        // TODO: happens in CREATE_empty000CreateInitCode_Transaction but probably has to be handled differently
        if (!_state.AccountExists(env.ExecutingAccount))
        {
            _state.CreateAccount(env.ExecutingAccount, UInt256.Zero);
        }

        stack.PopUInt256(out UInt256 value);
        stack.PopUInt256(out UInt256 memoryPositionOfInitCode);
        stack.PopUInt256(out UInt256 initCodeLength);
        Span<byte> salt = null;
        if (instruction == Instruction.CREATE2)
        {
            salt = stack.PopBytes();
        }

        //EIP-3860
        if (spec.IsEip3860Enabled)
        {
            if (initCodeLength > spec.MaxInitCodeSize) return (InstructionReturn.OutOfGas, null);
        }

        long gasCost = GasCostOf.Create +
            (spec.IsEip3860Enabled ? GasCostOf.InitCodeWord * EvmPooledMemory.Div32Ceiling(initCodeLength) : 0) +
            (instruction == Instruction.CREATE2 ? GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(initCodeLength) : 0);

        if (!UpdateGas(gasCost, ref gasAvailable)) return (InstructionReturn.OutOfGas, null);
        if (!UpdateMemoryCost(vmState.Memory, ref gasAvailable, in memoryPositionOfInitCode, initCodeLength)) return (InstructionReturn.OutOfGas, null);

        // TODO: copy pasted from CALL / DELEGATECALL, need to move it outside?
        if (env.CallDepth >= MaxCallDepth) // TODO: fragile ordering / potential vulnerability for different clients
        {
            // TODO: need a test for this
            _returnDataBuffer = Array.Empty<byte>();
            stack.PushZero();
            return (InstructionReturn.Continue, null);
        }

        Span<byte> initCode = vmState.Memory.LoadSpan(in memoryPositionOfInitCode, initCodeLength);

        UInt256 balance = _state.GetBalance(env.ExecutingAccount);
        if (value > balance)
        {
            _returnDataBuffer = Array.Empty<byte>();
            stack.PushZero();
            return (InstructionReturn.Continue, null);
        }

        UInt256 accountNonce = _state.GetNonce(env.ExecutingAccount);
        UInt256 maxNonce = ulong.MaxValue;
        if (accountNonce >= maxNonce)
        {
            _returnDataBuffer = Array.Empty<byte>();
            stack.PushZero();
            return (InstructionReturn.Continue, null);
        }

        if (_txTracer.IsTracingInstructions) EndInstructionTrace(gasAvailable, vmState.Memory?.Size ?? 0);
        // todo: === below is a new call - refactor / move

        long callGas = spec.Use63Over64Rule ? gasAvailable - gasAvailable / 64L : gasAvailable;
        if (!UpdateGas(callGas, ref gasAvailable)) return (InstructionReturn.OutOfGas, null);

        Address contractAddress = instruction == Instruction.CREATE
            ? ContractAddress.From(env.ExecutingAccount, _state.GetNonce(env.ExecutingAccount))
            : ContractAddress.From(env.ExecutingAccount, salt, initCode);

        if (spec.UseHotAndColdStorage)
        {
            // EIP-2929 assumes that warm-up cost is included in the costs of CREATE and CREATE2
            vmState.WarmUp(contractAddress);
        }

        _state.IncrementNonce(env.ExecutingAccount);

        Snapshot snapshot = _worldState.TakeSnapshot();

        bool accountExists = _state.AccountExists(contractAddress);
        if (accountExists && (GetCachedCodeInfo(_worldState, contractAddress, spec).MachineCode.Length != 0 || _state.GetNonce(contractAddress) != 0))
        {
            /* we get the snapshot before this as there is a possibility with that we will touch an empty account and remove it even if the REVERT operation follows */
            if (_logger.IsTrace) _logger.Trace($"Contract collision at {contractAddress}");
            _returnDataBuffer = Array.Empty<byte>();
            stack.PushZero();
            return (InstructionReturn.Continue, null);
        }

        if (accountExists)
        {
            _state.UpdateStorageRoot(contractAddress, Keccak.EmptyTreeHash);
        }
        else if (_state.IsDeadAccount(contractAddress))
        {
            _storage.ClearStorage(contractAddress);
        }

        _state.SubtractFromBalance(env.ExecutingAccount, value, spec);
        ExecutionEnvironment callEnv = new();
        callEnv.TxExecutionContext = env.TxExecutionContext;
        callEnv.CallDepth = env.CallDepth + 1;
        callEnv.Caller = env.ExecutingAccount;
        callEnv.ExecutingAccount = contractAddress;
        callEnv.CodeSource = null;
        callEnv.CodeInfo = new CodeInfo(initCode.ToArray());
        callEnv.InputData = ReadOnlyMemory<byte>.Empty;
        callEnv.TransferValue = value;
        callEnv.Value = value;

        EvmState callState = new(
            callGas,
            callEnv,
            instruction == Instruction.CREATE2 ? ExecutionType.Create2 : ExecutionType.Create,
            false,
            snapshot,
            0L,
            0L,
            vmState.IsStatic,
            vmState,
            false,
            accountExists);

        return (InstructionReturn.Success, callState);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionSSTORE(ref EvmStack stack, ref long gasAvailable, Address executingAccount, EvmState vmState, IReleaseSpec spec)
    {
        // fail fast before the first storage read if gas is not enough even for reset
        if (!spec.UseNetGasMetering && !UpdateGas(spec.GetSStoreResetCost(), ref gasAvailable)) return false;
        if (spec.UseNetGasMeteringWithAStipendFix)
        {
            if (_txTracer.IsTracingRefunds) _txTracer.ReportExtraGasPressure(GasCostOf.CallStipend - spec.GetNetMeteredSStoreCost() + 1);
            if (gasAvailable <= GasCostOf.CallStipend) return false;
        }

        stack.PopUInt256(out UInt256 storageIndex);
        Span<byte> newValue = stack.PopBytes();
        bool newIsZero = newValue.IsZero();
        if (!newIsZero)
        {
            newValue = newValue.WithoutLeadingZeros().ToArray();
        }
        else
        {
            newValue = new byte[] { 0 };
        }

        StorageCell storageCell = new(executingAccount, storageIndex);

        if (!ChargeStorageAccessGas(
            ref gasAvailable,
            vmState,
            storageCell,
            StorageAccessType.SSTORE,
            spec)) return false;

        Span<byte> currentValue = _storage.Get(storageCell);
        // Console.WriteLine($"current: {currentValue.ToHexString()} newValue {newValue.ToHexString()}");
        bool currentIsZero = currentValue.IsZero();

        bool newSameAsCurrent = (newIsZero && currentIsZero) || Bytes.AreEqual(currentValue, newValue);
        long sClearRefunds = RefundOf.SClear(spec.IsEip3529Enabled);

        bool isTracingRefunds = _txTracer.IsTracingRefunds;
        if (!spec.UseNetGasMetering) // note that for this case we already deducted 5000
        {
            if (newIsZero)
            {
                if (!newSameAsCurrent)
                {
                    vmState.Refund += sClearRefunds;
                    if (isTracingRefunds) _txTracer.ReportRefund(sClearRefunds);
                }
            }
            else if (currentIsZero)
            {
                if (!UpdateGas(GasCostOf.SSet - GasCostOf.SReset, ref gasAvailable)) return false;
            }
        }
        else // net metered
        {
            if (newSameAsCurrent)
            {
                if (!UpdateGas(spec.GetNetMeteredSStoreCost(), ref gasAvailable)) return false;
            }
            else // net metered, C != N
            {
                Span<byte> originalValue = _storage.GetOriginal(storageCell);
                bool originalIsZero = originalValue.IsZero();

                bool currentSameAsOriginal = Bytes.AreEqual(originalValue, currentValue);
                if (currentSameAsOriginal)
                {
                    if (currentIsZero)
                    {
                        if (!UpdateGas(GasCostOf.SSet, ref gasAvailable)) return false;
                    }
                    else // net metered, current == original != new, !currentIsZero
                    {
                        if (!UpdateGas(spec.GetSStoreResetCost(), ref gasAvailable)) return false;

                        if (newIsZero)
                        {
                            vmState.Refund += sClearRefunds;
                            if (isTracingRefunds) _txTracer.ReportRefund(sClearRefunds);
                        }
                    }
                }
                else // net metered, new != current != original
                {
                    long netMeteredStoreCost = spec.GetNetMeteredSStoreCost();
                    if (!UpdateGas(netMeteredStoreCost, ref gasAvailable)) return false;

                    if (!originalIsZero) // net metered, new != current != original != 0
                    {
                        if (currentIsZero)
                        {
                            vmState.Refund -= sClearRefunds;
                            if (isTracingRefunds) _txTracer.ReportRefund(-sClearRefunds);
                        }

                        if (newIsZero)
                        {
                            vmState.Refund += sClearRefunds;
                            if (isTracingRefunds) _txTracer.ReportRefund(sClearRefunds);
                        }
                    }

                    bool newSameAsOriginal = Bytes.AreEqual(originalValue, newValue);
                    if (newSameAsOriginal)
                    {
                        long refundFromReversal;
                        if (originalIsZero)
                        {
                            refundFromReversal = spec.GetSetReversalRefund();
                        }
                        else
                        {
                            refundFromReversal = spec.GetClearReversalRefund();
                        }

                        vmState.Refund += refundFromReversal;
                        if (isTracingRefunds) _txTracer.ReportRefund(refundFromReversal);
                    }
                }
            }
        }

        if (!newSameAsCurrent)
        {
            Span<byte> valueToStore = newIsZero ? BytesZero : newValue;
            _storage.Set(storageCell, valueToStore.ToArray());
        }

        if (_txTracer.IsTracingInstructions)
        {
            Span<byte> valueToStore = newIsZero ? BytesZero : newValue;
            Span<byte> span = new byte[32]; // do not stackalloc here
            storageCell.Index.ToBigEndian(span);
            _txTracer.ReportStorageChange(span, valueToStore);
        }

        if (_txTracer.IsTracingOpLevelStorage)
        {
            _txTracer.SetOperationStorage(storageCell.Address, storageIndex, newValue, currentValue);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionBLOCKHASH(ref EvmStack stack, ref long gasAvailable, BlockHeader header)
    {
        Metrics.BlockhashOpcode++;

        if (!UpdateGas(GasCostOf.BlockHash, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        long number = a > long.MaxValue ? long.MaxValue : (long)a;
        Keccak blockHash = _blockhashProvider.GetBlockhash(header, number);
        stack.PushBytes(blockHash?.Bytes ?? BytesZero32);

        if (_txTracer.IsTracingInstructions)
        {
            if (_txTracer.IsTracingBlockHash && blockHash is not null)
            {
                _txTracer.ReportBlockHash(blockHash);
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private InstructionReturn InstructionRETURNDATACOPY(ref EvmStack stack, ref long gasAvailable, EvmPooledMemory? memory)
    {
        stack.PopUInt256(out UInt256 dest);
        stack.PopUInt256(out UInt256 src);
        stack.PopUInt256(out UInt256 length);
        if (!UpdateGas(GasCostOf.VeryLow + GasCostOf.Memory * EvmPooledMemory.Div32Ceiling(length), ref gasAvailable)) return InstructionReturn.OutOfGas;

        if (UInt256.AddOverflow(length, src, out UInt256 newLength) || newLength > _returnDataBuffer.Length)
        {
            return InstructionReturn.AccessViolation;
        }

        if (!length.IsZero)
        {
            if (!UpdateMemoryCost(memory, ref gasAvailable, in dest, length)) return InstructionReturn.OutOfGas;

            ZeroPaddedSpan returnDataSlice = _returnDataBuffer.AsSpan().SliceWithZeroPadding(src, (int)length);
            memory.Save(in dest, returnDataSlice);
            if (_txTracer.IsTracingInstructions)
            {
                _txTracer.ReportMemoryChange((long)dest, returnDataSlice);
            }
        }

        return InstructionReturn.Success;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InstructionRETURNDATASIZE(ref EvmStack stack)
    {
        UInt256 res = (UInt256)_returnDataBuffer.Length;
        stack.PushUInt256(in res);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionEXTCODECOPY(ref EvmStack stack, ref long gasAvailable, EvmState vmState, IReleaseSpec spec)
    {
        Address address = stack.PopAddress();
        stack.PopUInt256(out UInt256 dest);
        stack.PopUInt256(out UInt256 src);
        stack.PopUInt256(out UInt256 length);

        long gasCost = spec.GetExtCodeCost();
        if (!UpdateGas(gasCost + GasCostOf.Memory * EvmPooledMemory.Div32Ceiling(length),
            ref gasAvailable)) return false;
        if (!ChargeAccountAccessGas(ref gasAvailable, vmState, address, spec)) return false;

        if (!length.IsZero)
        {
            if (!UpdateMemoryCost(vmState.Memory, ref gasAvailable, in dest, length)) return false;

            byte[] externalCode = GetCachedCodeInfo(_worldState, address, spec).MachineCode;
            ZeroPaddedSpan callDataSlice = externalCode.SliceWithZeroPadding(src, (int)length);
            vmState.Memory.Save(in dest, callDataSlice);
            if (_txTracer.IsTracingInstructions)
            {
                _txTracer.ReportMemoryChange((long)dest, callDataSlice);
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionEXTCODESIZE(ref EvmStack stack, ref long gasAvailable, EvmState vmState, IReleaseSpec spec)
    {
        if (!UpdateGas(spec.GetExtCodeCost(), ref gasAvailable)) return false;

        Address address = stack.PopAddress();
        if (!ChargeAccountAccessGas(ref gasAvailable, vmState, address, spec)) return false;

        byte[] accountCode = GetCachedCodeInfo(_worldState, address, spec).MachineCode;
        UInt256 codeSize = (UInt256)accountCode.Length;
        stack.PushUInt256(in codeSize);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionCODECOPY(ref EvmStack stack, ref long gasAvailable, EvmPooledMemory? memory, in Span<byte> code)
    {
        stack.PopUInt256(out UInt256 dest);
        stack.PopUInt256(out UInt256 src);
        stack.PopUInt256(out UInt256 length);
        if (!UpdateGas(GasCostOf.VeryLow + GasCostOf.Memory * EvmPooledMemory.Div32Ceiling(length), ref gasAvailable)) return false;

        if (!length.IsZero)
        {
            if (!UpdateMemoryCost(memory, ref gasAvailable, in dest, length)) return false;

            ZeroPaddedSpan codeSlice = code.SliceWithZeroPadding(src, (int)length);
            memory.Save(in dest, codeSlice);
            if (_txTracer.IsTracingInstructions) _txTracer.ReportMemoryChange((long)dest, codeSlice);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionCODESIZE(ref EvmStack stack, ref long gasAvailable, int codeLength)
    {
        if (!UpdateGas(GasCostOf.Base, ref gasAvailable)) return false;

        UInt256 length = (UInt256)codeLength;
        stack.PushUInt256(in length);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionCALLDATACOPY(ref EvmStack stack, ref long gasAvailable, EvmPooledMemory? memory, in ReadOnlyMemory<byte> inputData)
    {
        stack.PopUInt256(out UInt256 dest);
        stack.PopUInt256(out UInt256 src);
        stack.PopUInt256(out UInt256 length);
        if (!UpdateGas(GasCostOf.VeryLow + GasCostOf.Memory * EvmPooledMemory.Div32Ceiling(length),
            ref gasAvailable)) return true;

        if (!length.IsZero)
        {
            if (!UpdateMemoryCost(memory, ref gasAvailable, in dest, length)) return true;

            ZeroPaddedMemory callDataSlice = inputData.SliceWithZeroPadding(src, (int)length);
            memory.Save(in dest, callDataSlice);
            if (_txTracer.IsTracingInstructions)
            {
                _txTracer.ReportMemoryChange((long)dest, callDataSlice);
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionCALLDATASIZE(ref EvmStack stack, ref long gasAvailable, in ReadOnlyMemory<byte> inputData)
    {
        if (!UpdateGas(GasCostOf.Base, ref gasAvailable)) return false;

        UInt256 callDataSize = (UInt256)inputData.Length;
        stack.PushUInt256(in callDataSize);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionCALLDATALOAD(ref EvmStack stack, ref long gasAvailable, in ReadOnlyMemory<byte> inputData)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 src);
        stack.PushBytes(inputData.SliceWithZeroPadding(src, 32));

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool InstructionBALANCE(ref EvmStack stack, ref long gasAvailable, EvmState vmState, IReleaseSpec spec)
    {
        long gasCost = spec.GetBalanceCost();
        if (gasCost != 0 && !UpdateGas(gasCost, ref gasAvailable)) return false;

        Address address = stack.PopAddress();
        if (!ChargeAccountAccessGas(ref gasAvailable, vmState, address, spec)) return false;

        UInt256 balance = _state.GetBalance(address);
        stack.PushUInt256(in balance);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSHA3(ref EvmStack stack, ref long gasAvailable, EvmPooledMemory memory)
    {
        stack.PopUInt256(out UInt256 memSrc);
        stack.PopUInt256(out UInt256 memLength);

        if (!UpdateGas(GasCostOf.Sha3 + GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(memLength),
            ref gasAvailable)) return false;

        if (!UpdateMemoryCost(memory, ref gasAvailable, in memSrc, memLength)) return false;

        Span<byte> memData = memory.LoadSpan(in memSrc, memLength);
        stack.PushBytes(ValueKeccak.Compute(memData).BytesAsSpan);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionBYTE(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 position);
        Span<byte> bytes = stack.PopBytes();

        if (position >= BigInt32)
        {
            stack.PushZero();
            return true;
        }

        int adjustedPosition = bytes.Length - 32 + (int)position;
        if (adjustedPosition < 0)
        {
            stack.PushZero();
        }
        else
        {
            stack.PushByte(bytes[adjustedPosition]);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionNOT(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        Span<byte> a = stack.PopBytes();

        Vector256<byte> aVec = MemoryMarshal.Read<Vector256<byte>>(a);
        MemoryMarshal.AsRef<Vector256<byte>>(stack.Register) = ~aVec;

        stack.PushBytes(stack.Register);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionXOR(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        Span<byte> a = stack.PopBytes();
        Span<byte> b = stack.PopBytes();

        Vector256<byte> aVec = MemoryMarshal.Read<Vector256<byte>>(a);
        Vector256<byte> bVec = MemoryMarshal.Read<Vector256<byte>>(b);
        MemoryMarshal.AsRef<Vector256<byte>>(stack.Register) = aVec ^ bVec;

        stack.PushBytes(stack.Register);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionOR(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        Span<byte> a = stack.PopBytes();
        Span<byte> b = stack.PopBytes();

        Vector256<byte> aVec = MemoryMarshal.Read<Vector256<byte>>(a);
        Vector256<byte> bVec = MemoryMarshal.Read<Vector256<byte>>(b);
        MemoryMarshal.AsRef<Vector256<byte>>(stack.Register) = aVec | bVec;

        stack.PushBytes(stack.Register);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionAND(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        Span<byte> a = stack.PopBytes();
        Span<byte> b = stack.PopBytes();

        Vector256<byte> aVec = MemoryMarshal.Read<Vector256<byte>>(a);
        Vector256<byte> bVec = MemoryMarshal.Read<Vector256<byte>>(b);
        MemoryMarshal.AsRef<Vector256<byte>>(stack.Register) = aVec & bVec;

        stack.PushBytes(stack.Register);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionISZERO(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        Span<byte> a = stack.PopBytes();
        if (a.SequenceEqual(BytesZero32))
        {
            stack.PushOne();
        }
        else
        {
            stack.PushZero();
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionEQ(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        Span<byte> a = stack.PopBytes();
        Span<byte> b = stack.PopBytes();
        if (a.SequenceEqual(b))
        {
            stack.PushOne();
        }
        else
        {
            stack.PushZero();
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSGT(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopSignedInt256(out Int256.Int256 a);
        stack.PopSignedInt256(out Int256.Int256 b);
        if (a.CompareTo(b) > 0)
        {
            stack.PushOne();
        }
        else
        {
            stack.PushZero();
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSLT(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopSignedInt256(out Int256.Int256 a);
        stack.PopSignedInt256(out Int256.Int256 b);

        if (a.CompareTo(b) < 0)
        {
            stack.PushOne();
        }
        else
        {
            stack.PushZero();
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionGT(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopUInt256(out UInt256 b);
        if (a > b)
        {
            stack.PushOne();
        }
        else
        {
            stack.PushZero();
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionLT(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopUInt256(out UInt256 b);
        if (a < b)
        {
            stack.PushOne();
        }
        else
        {
            stack.PushZero();
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSIGNEXTEND(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.Low, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        if (a >= BigInt32)
        {
            stack.EnsureDepth(1);
            return true;
        }
        int position = 31 - (int)a;

        Span<byte> b = stack.PopBytes();
        sbyte sign = (sbyte)b[position];

        if (sign >= 0)
        {
            b[..position].Clear();
        }
        else
        {
            b[..position].Fill(byte.MaxValue);
        }

        stack.PushBytes(b);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionEXP(ref EvmStack stack, ref long gasAvailable, IReleaseSpec spec)
    {
        Metrics.ModExpOpcode++;

        if (!UpdateGas(GasCostOf.Exp, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 baseInt);
        Span<byte> exp = stack.PopBytes();

        int leadingZeros = exp.LeadingZerosCount();
        if (leadingZeros != 32)
        {
            int expSize = 32 - leadingZeros;
            if (!UpdateGas(spec.GetExpByteCost() * expSize, ref gasAvailable)) return false;
        }
        else
        {
            stack.PushOne();
            return true;
        }

        if (baseInt.IsZero)
        {
            stack.PushZero();
        }
        else if (baseInt.IsOne)
        {
            stack.PushOne();
        }
        else
        {
            UInt256.Exp(baseInt, new UInt256(exp, true), out UInt256 res);
            stack.PushUInt256(in res);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionMULMOD(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.Mid, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopUInt256(out UInt256 b);
        stack.PopUInt256(out UInt256 mod);

        if (mod.IsZero)
        {
            stack.PushZero();
        }
        else
        {
            UInt256.MultiplyMod(in a, in b, in mod, out UInt256 res);
            stack.PushUInt256(in res);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionADDMOD(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.Mid, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopUInt256(out UInt256 b);
        stack.PopUInt256(out UInt256 mod);

        if (mod.IsZero)
        {
            stack.PushZero();
        }
        else
        {
            UInt256.AddMod(a, b, mod, out UInt256 res);
            stack.PushUInt256(in res);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSMOD(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.Low, ref gasAvailable)) return false;

        stack.PopSignedInt256(out Int256.Int256 a);
        stack.PopSignedInt256(out Int256.Int256 b);
        if (b.IsZero || b.IsOne)
        {
            stack.PushZero();
        }
        else
        {
            a.Mod(in b, out Int256.Int256 mod);
            stack.PushSignedInt256(in mod);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionMOD(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.Low, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopUInt256(out UInt256 b);
        UInt256.Mod(in a, in b, out UInt256 result);
        stack.PushUInt256(in result);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionDIV(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.Low, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopUInt256(out UInt256 b);
        if (b.IsZero)
        {
            stack.PushZero();
        }
        else
        {
            UInt256.Divide(in a, in b, out UInt256 res);
            stack.PushUInt256(in res);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSDIV(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.Low, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopSignedInt256(out Int256.Int256 b);
        if (b.IsZero)
        {
            stack.PushZero();
        }
        else if (b == Int256.Int256.MinusOne && a == P255)
        {
            UInt256 res = P255;
            stack.PushUInt256(in res);
        }
        else
        {
            Int256.Int256 signedA = new(a);
            Int256.Int256.Divide(in signedA, in b, out Int256.Int256 res);
            stack.PushSignedInt256(in res);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionSUB(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopUInt256(out UInt256 b);
        UInt256.Subtract(in a, in b, out UInt256 result);

        stack.PushUInt256(in result);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionMUL(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.Low, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 a);
        stack.PopUInt256(out UInt256 b);
        UInt256.Multiply(in a, in b, out UInt256 res);
        stack.PushUInt256(in res);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstructionADD(ref EvmStack stack, ref long gasAvailable)
    {
        if (!UpdateGas(GasCostOf.VeryLow, ref gasAvailable)) return false;

        stack.PopUInt256(out UInt256 b);
        stack.PopUInt256(out UInt256 a);
        UInt256.Add(in a, in b, out UInt256 c);
        stack.PushUInt256(c);

        return true;
    }
}
