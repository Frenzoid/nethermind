using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethermind.Core.Attributes;
using Nethermind.Core.Extensions;
using Nethermind.Evm.CodeAnalysis;
using Org.BouncyCastle.Crypto.Paddings;

namespace Nethermind.Evm
{
    enum SectionDividor : byte
    {
        Terminator  = 0,
        CodeSection = 1,
        DataSection = 2,
    }
    public class EofHeader
    {
        #region public construction properties
        public byte[] MachineCode { get; set; }
        public UInt16 CodeSize { get; set; }
        public UInt16 DataSize { get; set; }
        #endregion

        #region Equality methods
        public override bool Equals(object? obj)
            => this.GetHashCode() == obj.GetHashCode();
        public override int GetHashCode()
            => CodeSize.GetHashCode() ^ DataSize.GetHashCode();
        #endregion

        #region Sections Offsets
        private int? _codeStartOffset = default;
        public int CodeStartOffset
        {
            get
            {
                if(_codeStartOffset is null)
                    _codeStartOffset = DataSize == 0 ? 7 : 10;
                return _codeStartOffset.Value;
            }
        }
        private int? _codeEndOffset = default;
        public int CodeEndOffset
        {
            get
            {
                if (_codeEndOffset is null)
                    _codeEndOffset = CodeStartOffset + CodeSize;
                return _codeEndOffset.Value;
            }
        }
        #endregion
    }


    public class EvmObjectFormat
    {
        // magic prefix : EofFormatByte is the first byte, EofFormatDiff is chosen to diff from previously rejected contract according to EIP3541
        private const byte EofMagicLength = 2;
        private const byte EofFormatByte = 0xEF;
        private const byte EofFormatDiff = 0x00;

        private const byte EofVersion = 1;

        private byte[] EofMagic => new byte[]{ EofFormatByte, EofFormatDiff };

        public bool HasEOFFormat(Span<byte> code) => code.Length >= EofMagicLength && code.StartsWith(EofMagic);
        public bool ExtractHeader(Span<byte> code, out EofHeader header)
        {
            if (!HasEOFFormat(code))
            {
                header = null; return false;
            }

            int codeLen = code.Length;

            int i = EofMagicLength;

            if(i >= codeLen || code[i] != EofVersion)
            {
                header = null;  return false;
            }
            i++;

            bool continueParsing = true;
            header = new EofHeader {
                MachineCode = code.ToArray(),
            };

            while (i < codeLen && continueParsing)
            {
                var sectionKind = (SectionDividor)code[i];
                i++;

                switch (sectionKind) {
                    case SectionDividor.Terminator:
                        {
                            continueParsing = false;
                            break;
                        }
                    case SectionDividor.CodeSection:
                        {
                            // code-section must come first and there can be only one data-section
                            if (header.CodeSize > 0 || i + 2 > codeLen)
                            {
                                header = null; return false;
                            }

                            var codeSectionSlice = code.Slice(i, 2);
                            header.CodeSize = (ushort)((codeSectionSlice[0] << 8) | codeSectionSlice[1]);

                            if (header.CodeSize == 0) // code section must be non-empty (i.e : size > 0)
                            {
                                header = null; return false;
                            }

                            i += 2;
                            break;
                        }
                    case SectionDividor.DataSection:
                        {
                            // data-section must come after code-section and there can be only one data-section
                            if (header.CodeSize == 0 || header.DataSize != 0 || i + 2 > codeLen)
                            {
                                header = null; return false;
                            }

                            var dataSectionSlice = code.Slice(i, 2);
                            header.DataSize = (ushort)((dataSectionSlice[0] << 8) | dataSectionSlice[1]);

                            if (header.DataSize == 0) // if declared data section must be non-empty
                            {
                                header = null; return false;
                            }

                            i += 2;
                            break;
                        }
                    default: // if section kind is anything beside a section-limiter or a terminator byte we return false
                        {
                            header = null; return false;
                        }
                }
            }

            var contractBody = code[i..];
            var calculatedCodeLen = (int)header.CodeSize + (int)header.DataSize;
            if ((header.CodeSize == 0) || (calculatedCodeLen != contractBody.Length))
            {
                header = null; return false;
            }

            return true;
        }

        public bool ValidateEofCode(Span<byte> code) => ExtractHeader(code, out _);
        public bool ValidateEofCode(byte[] code) => ExtractHeader(code, out _);
        public bool ValidateInstructions(Span<byte> code, out EofHeader header)
        {
            // check if code is EOF compliant
            if (ExtractHeader(code, out header))
            {
                var (startOffset, endOffset) = (header.CodeStartOffset, header.CodeEndOffset);
                Instruction? opcode = null;
                for (int i = startOffset; i < endOffset;)
                {
                    opcode = (Instruction)code[i];

                    // validate opcode
                    if (!Enum.IsDefined(opcode.Value))
                    {
                        return false;
                    }

                    if (opcode is >= Instruction.PUSH1 and <= Instruction.PUSH32)
                    {
                        i += code[i] - (int)Instruction.PUSH1 + 1;
                        if (i >= endOffset)
                            return false;
                    }
                    i++;
                }

                // check if terminating opcode : STOP, RETURN, REVERT, INVALID, SELFDESTRUCT
                switch (opcode)
                {
                    case Instruction.STOP:
                    case Instruction.RETURN:
                    case Instruction.REVERT:
                    case Instruction.INVALID:
                    case Instruction.SELFDESTRUCT: // might be retired and replaced with SELLALL?
                        return true;
                    default:
                        return false;
                }
            }
            return false;
        }
    }
}
