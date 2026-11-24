using SixtarGateMod.Managers;
using Xunit;

namespace SixtarGateMod.Tests;

public class X64DecoderTests
{
    [Fact]
    public void TryDecode_MovLoad64WithDisp8_ExtractsRegBaseDisp()
    {
        // mov rcx, [rcx+0x18]
        byte[] code = { 0x48, 0x8B, 0x49, 0x18 };
        Assert.True(X64Decoder.TryDecode(code, 0, out var ins));
        Assert.Equal(4, ins.Length);
        Assert.Equal(X64Decoder.Kind.MovLoad64, ins.Kind);
        Assert.Equal(1, ins.Reg);
        Assert.Equal(1, ins.BaseReg);
        Assert.Equal(0x18, ins.Disp);
    }

    [Fact]
    public void TryDecode_RexWOverridesOperandSizePrefix66()
    {
        // 66 48 C7 C0 78 56 34 12  => mov rax, imm32 (8 bytes, not 6 bytes)
        byte[] code = { 0x66, 0x48, 0xC7, 0xC0, 0x78, 0x56, 0x34, 0x12 };
        Assert.True(X64Decoder.TryDecode(code, 0, out var ins));
        Assert.Equal(8, ins.Length);
        Assert.Equal(1 << 0, ins.Writes);
    }

    [Fact]
    public void TryDecode_Prefix66WithoutRexW_UsesImm16()
    {
        // 66 C7 C0 34 12 => mov ax, 0x1234 (5 bytes)
        byte[] code = { 0x66, 0xC7, 0xC0, 0x34, 0x12 };
        Assert.True(X64Decoder.TryDecode(code, 0, out var ins));
        Assert.Equal(5, ins.Length);
    }

    [Fact]
    public void TryDecode_VexMap1WithImm8_ConsumesImmediateByte()
    {
        // C5 F9 70 C1 1B => vpshufd xmm0, xmm1, 0x1B (5 bytes: C5, byte1, op=70, modrm=C1, imm8=1B)
        byte[] code = { 0xC5, 0xF9, 0x70, 0xC1, 0x1B };
        Assert.True(X64Decoder.TryDecode(code, 0, out var ins));
        Assert.Equal(5, ins.Length);
    }

    [Fact]
    public void TryDecode_RipRelativeLoad_SetsBaseRegNegativeOne()
    {
        // 48 8B 05 10 00 00 00 => mov rax, [rip+0x10]
        byte[] code = { 0x48, 0x8B, 0x05, 0x10, 0x00, 0x00, 0x00 };
        Assert.True(X64Decoder.TryDecode(code, 0, out var ins));
        Assert.Equal(7, ins.Length);
        Assert.Equal(-1, ins.BaseReg);
    }

    [Fact]
    public void TryDecode_SibWithR12Base_DecodesBaseReg12()
    {
        // 49 8B 44 24 20 => mov rax, [r12+0x20]
        byte[] code = { 0x49, 0x8B, 0x44, 0x24, 0x20 };
        Assert.True(X64Decoder.TryDecode(code, 0, out var ins));
        Assert.Equal(5, ins.Length);
        Assert.Equal(X64Decoder.Kind.MovLoad64, ins.Kind);
        Assert.Equal(0, ins.Reg);
        Assert.Equal(12, ins.BaseReg);
        Assert.Equal(0x20, ins.Disp);
    }

    [Fact]
    public void TryDecode_TruncatedInput_ReturnsFalseWithoutThrowing()
    {
        Assert.False(X64Decoder.TryDecode(System.Array.Empty<byte>(), 0, out _));
        Assert.False(X64Decoder.TryDecode(new byte[] { 0x48, 0x8B }, 0, out _));
        Assert.False(X64Decoder.TryDecode(new byte[] { 0xC5, 0xF9, 0x70, 0xC1 }, 0, out _)); // missing imm8
    }

    [Fact]
    public void TryDecode_Vzeroupper_ConsumesThreeBytesWithoutModRm()
    {
        // C5 F8 77 => vzeroupper (3 bytes, no ModRM, no GPR writes)
        byte[] code = { 0xC5, 0xF8, 0x77, 0xC3 };
        Assert.True(X64Decoder.TryDecode(code, 0, out var ins));
        Assert.Equal(3, ins.Length);
        Assert.Equal(0, ins.Writes);
    }

    [Fact]
    public void TryDecode_SibWithIndexRegister_SetsBaseRegNegativeOne()
    {
        // 48 8B 44 C1 18 => mov rax, [rcx + rax*8 + 0x18]
        byte[] code = { 0x48, 0x8B, 0x44, 0xC1, 0x18 };
        Assert.True(X64Decoder.TryDecode(code, 0, out var ins));
        Assert.Equal(5, ins.Length);
        Assert.Equal(-1, ins.BaseReg);
    }

    [Fact]
    public void TryDecode_CmpAlImm8_DoesNotWriteRax()
    {
        // 3C 01 => cmp al, 1
        byte[] code = { 0x3C, 0x01 };
        Assert.True(X64Decoder.TryDecode(code, 0, out var ins));
        Assert.Equal(2, ins.Length);
        Assert.Equal(0, ins.Writes);
    }
}
