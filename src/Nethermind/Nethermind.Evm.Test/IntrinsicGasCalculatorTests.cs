// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Nethermind.Core;
using Nethermind.Core.Eip2930;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Builders;
using Nethermind.Int256;
using Nethermind.Specs.Forks;
using NUnit.Framework;

namespace Nethermind.Evm.Test
{
    [TestFixture]
    public class IntrinsicGasCalculatorTests
    {
        public static IEnumerable<(Transaction Tx, long cost, string Description)> TestCaseSource()
        {
            yield return (Build.A.Transaction.SignedAndResolved().TestObject, 21000, "empty");
        }

        public static IEnumerable<(List<object> orderQueue, long Cost)> AccessTestCaseSource()
        {
            yield return (new List<object> { }, 0);
            yield return (new List<object> { Address.Zero }, 2400);
            yield return (new List<object> { Address.Zero, (UInt256)1 }, 4300);
            yield return (new List<object> { Address.Zero, (UInt256)1, TestItem.AddressA, (UInt256)1 }, 8600);
            yield return (new List<object> { Address.Zero, (UInt256)1, Address.Zero, (UInt256)1 }, 8600);
        }

        public static IEnumerable<(byte[] Data, int OldCost, int NewCost)> DataTestCaseSource()
        {
            yield return (new byte[] { 0 }, 4, 4);
            yield return (new byte[] { 1 }, 68, 16);
            yield return (new byte[] { 0, 0, 1 }, 76, 24);
            yield return (new byte[] { 1, 1, 0 }, 140, 36);
            yield return (new byte[] { 0, 0, 1, 1 }, 144, 40);
        }
        [TestCaseSource(nameof(TestCaseSource))]
        public void Intrinsic_cost_is_calculated_properly((Transaction Tx, long Cost, string Description) testCase)
        {
            IntrinsicGasCalculator.Calculate(testCase.Tx, Berlin.Instance).Should().Be(testCase.Cost);
        }

        [TestCaseSource(nameof(AccessTestCaseSource))]
        public void Intrinsic_cost_is_calculated_properly((List<object> orderQueue, long Cost) testCase)
        {
            AccessListBuilder accessListBuilder = new();
            foreach (object o in testCase.orderQueue)
            {
                if (o is Address address)
                {
                    accessListBuilder.AddAddress(address);
                }
                else if (o is UInt256 index)
                {
                    accessListBuilder.AddStorage(index);
                }
            }

            AccessList accessList = accessListBuilder.ToAccessList();
            Transaction tx = Build.A.Transaction.SignedAndResolved().WithAccessList(accessList).TestObject;

            void Test(IReleaseSpec spec, bool supportsAccessLists)
            {
                if (!supportsAccessLists)
                {
                    Assert.Throws<InvalidDataException>(() => IntrinsicGasCalculator.Calculate(tx, spec));
                }
                else
                {
                    IntrinsicGasCalculator.Calculate(tx, spec).Should().Be(21000 + testCase.Cost, spec.Name);
                }
            }

            Test(Homestead.Instance, false);
            Test(Frontier.Instance, false);
            Test(SpuriousDragon.Instance, false);
            Test(TangerineWhistle.Instance, false);
            Test(Byzantium.Instance, false);
            Test(Constantinople.Instance, false);
            Test(ConstantinopleFix.Instance, false);
            Test(Istanbul.Instance, false);
            Test(MuirGlacier.Instance, false);
            Test(Berlin.Instance, true);
        }

        [TestCaseSource(nameof(DataTestCaseSource))]
        public void Intrinsic_cost_of_data_is_calculated_properly((byte[] Data, int OldCost, int NewCost) testCase)
        {
            Transaction tx = Build.A.Transaction.SignedAndResolved().WithData(testCase.Data).TestObject;

            void Test(IReleaseSpec spec, bool isAfterRepricing)
            {
                IntrinsicGasCalculator.Calculate(tx, spec).Should()
                    .Be(21000 + (isAfterRepricing ? testCase.NewCost : testCase.OldCost), spec.Name,
                        testCase.Data.ToHexString());
            }

            Test(Homestead.Instance, false);
            Test(Frontier.Instance, false);
            Test(SpuriousDragon.Instance, false);
            Test(TangerineWhistle.Instance, false);
            Test(Byzantium.Instance, false);
            Test(Constantinople.Instance, false);
            Test(ConstantinopleFix.Instance, false);
            Test(Istanbul.Instance, true);
            Test(MuirGlacier.Instance, true);
            Test(Berlin.Instance, true);
            Test(GrayGlacier.Instance, true);
            Test(Shanghai.Instance, true);
            Test(Cancun.Instance, true);
        }

        [TestCaseSource(nameof(ExcessDataGasTestCaseSource))]
        public void Excess_data_gas_is_calculated_correctly((ulong parentExcessDataGas, int parentBlobsCount, ulong expectedExcessDataGas) testCase)
        {
            void Test(IReleaseSpec spec, bool areBlobsEnabled)
            {
                BlockHeader parentHeader = Build.A.BlockHeader
                    .WithDataGasUsed(IntrinsicGasCalculator.CalculateDataGas(testCase.parentBlobsCount))
                    .WithExcessDataGas(testCase.parentExcessDataGas).TestObject;
                IntrinsicGasCalculator.CalculateExcessDataGas(parentHeader, spec).Should()
                    .Be(areBlobsEnabled ? testCase.expectedExcessDataGas : null);
            }

            Test(Homestead.Instance, false);
            Test(Frontier.Instance, false);
            Test(SpuriousDragon.Instance, false);
            Test(TangerineWhistle.Instance, false);
            Test(Byzantium.Instance, false);
            Test(Constantinople.Instance, false);
            Test(ConstantinopleFix.Instance, false);
            Test(Istanbul.Instance, false);
            Test(MuirGlacier.Instance, false);
            Test(Berlin.Instance, false);
            Test(GrayGlacier.Instance, false);
            Test(Shanghai.Instance, false);
            Test(Cancun.Instance, true);
        }

        [TestCaseSource(nameof(BlobDataGasCostTestCaseSource))]
        public void Blobs_intrinsic_cost_is_calculated_properly(
            (Transaction tx, ulong excessDataGas, UInt256 expectedCost) testCase)
        {
            BlockHeader header = Build.A.BlockHeader.WithExcessDataGas(testCase.excessDataGas).TestObject;
            IntrinsicGasCalculator.CalculateDataGasPrice(header, testCase.tx).Should()
                .Be(testCase.expectedCost);
        }

        public static IEnumerable<(ulong parentExcessDataGas, int parentBlobsCount, ulong expectedExcessDataGas)> ExcessDataGasTestCaseSource()
        {
            yield return (0, 0, 0);
            yield return (0, (int)(Eip4844Constants.TargetDataGasPerBlock / Eip4844Constants.DataGasPerBlob) - 1, 0);
            yield return (0, (int)(Eip4844Constants.TargetDataGasPerBlock / Eip4844Constants.DataGasPerBlob), 0);
            yield return (100000, (int)(Eip4844Constants.TargetDataGasPerBlock / Eip4844Constants.DataGasPerBlob), 100000);
            yield return (0, (int)(Eip4844Constants.TargetDataGasPerBlock / Eip4844Constants.DataGasPerBlob) + 1, Eip4844Constants.DataGasPerBlob * 1);
            yield return (Eip4844Constants.TargetDataGasPerBlock, 1, Eip4844Constants.DataGasPerBlob * 1);
            yield return (Eip4844Constants.TargetDataGasPerBlock, 0, 0);
            yield return (Eip4844Constants.TargetDataGasPerBlock, 2, Eip4844Constants.DataGasPerBlob * 2);
            yield return (Eip4844Constants.MaxDataGasPerBlock, 1, Eip4844Constants.TargetDataGasPerBlock + Eip4844Constants.DataGasPerBlob * 1);
            yield return (
                Eip4844Constants.MaxDataGasPerBlock,
                (int)(Eip4844Constants.TargetDataGasPerBlock / Eip4844Constants.DataGasPerBlob),
                Eip4844Constants.MaxDataGasPerBlock);
            yield return (
                Eip4844Constants.MaxDataGasPerBlock,
                (int)(Eip4844Constants.MaxDataGasPerBlock / Eip4844Constants.DataGasPerBlob),
                Eip4844Constants.MaxDataGasPerBlock * 2 - Eip4844Constants.TargetDataGasPerBlock
                );
        }

        public static IEnumerable<(Transaction tx, ulong excessDataGas, UInt256 expectedCost)> BlobDataGasCostTestCaseSource()
        {
            yield return (Build.A.Transaction.TestObject, 0, 0);
            yield return (Build.A.Transaction.TestObject, 1000, 0);
            yield return (Build.A.Transaction.WithType(TxType.Blob).WithBlobVersionedHashes(0).TestObject, 1000, 0);
            yield return (Build.A.Transaction.WithType(TxType.Blob).WithBlobVersionedHashes(1).TestObject, 0, 131072);
            yield return (Build.A.Transaction.WithType(TxType.Blob).WithBlobVersionedHashes(1).TestObject, 10000000, 2490368);
            yield return (Build.A.Transaction.WithType(TxType.Blob).WithBlobVersionedHashes(1000).TestObject, 0, 131072000);
            yield return (Build.A.Transaction.WithType(TxType.Blob).WithBlobVersionedHashes(1000).TestObject, 10000000, 2490368000);
        }
    }
}
