using SixtarGateMod.Managers;
using Xunit;

namespace SixtarGateMod.Tests;

public class NativeLambdaScanTests
{
    [Fact]
    public void Scan_ClosureToOwnerToTarget_ReturnsHit()
    {
        // mov rcx, [rcx+0x18] ; mov rcx, [rcx+0x30] ; ret
        byte[] code = { 0x48, 0x8B, 0x49, 0x18, 0x48, 0x8B, 0x49, 0x30, 0xC3 };
        Assert.Equal(NativeLambdaScan.Result.Hit, NativeLambdaScan.Scan(code, 0x18, 0x30));
    }

    [Fact]
    public void Scan_OuterInstanceLambda_ReadsFieldDirectlyFromRcx()
    {
        // ownerPtrOffset == 0 -> rcx is owner directly
        // mov rcx, [rcx+0x40] ; ret
        byte[] code = { 0x48, 0x8B, 0x49, 0x40, 0xC3 };
        Assert.Equal(NativeLambdaScan.Result.Hit, NativeLambdaScan.Scan(code, 0, 0x40));
    }

    [Fact]
    public void Scan_ClosureCopiedToCalleeSavedRbx_SurvivesCall()
    {
        // mov rbx, rcx (48 89 CB)
        // call +0 (E8 00 00 00 00) -> clobbers rcx, keeps rbx
        // mov rax, [rbx+0x18] (48 8B 43 18)
        // mov rcx, [rax+0x28] (48 8B 48 28)
        byte[] code =
        {
            0x48, 0x89, 0xCB,
            0xE8, 0x00, 0x00, 0x00, 0x00,
            0x48, 0x8B, 0x43, 0x18,
            0x48, 0x8B, 0x48, 0x28,
            0xC3,
        };
        Assert.Equal(NativeLambdaScan.Result.Hit, NativeLambdaScan.Scan(code, 0x18, 0x28));
    }

    [Fact]
    public void Scan_VolatileRcxClobberedByCall_RejectsFalseHit()
    {
        // mov rcx, [rcx+0x18] ; call +0 ; mov rcx, [rcx+0x30]
        byte[] code =
        {
            0x48, 0x8B, 0x49, 0x18,
            0xE8, 0x00, 0x00, 0x00, 0x00,
            0x48, 0x8B, 0x49, 0x30,
            0xC3,
        };
        Assert.Equal(NativeLambdaScan.Result.OwnerOnly, NativeLambdaScan.Scan(code, 0x18, 0x30, trackRegisters: true));
    }

    [Fact]
    public void Scan_XorAndLeaClobberOwner_RejectsFalseHit()
    {
        // mov rcx, [rcx+0x18] ; xor ecx, ecx ; mov rcx, [rcx+0x30]
        byte[] xorCode = { 0x48, 0x8B, 0x49, 0x18, 0x31, 0xC9, 0x48, 0x8B, 0x49, 0x30, 0xC3 };
        Assert.Equal(NativeLambdaScan.Result.OwnerOnly, NativeLambdaScan.Scan(xorCode, 0x18, 0x30));

        // mov rcx, [rcx+0x18] ; lea rcx, [rcx+0x10] ; mov rcx, [rcx+0x30]
        byte[] leaCode = { 0x48, 0x8B, 0x49, 0x18, 0x48, 0x8D, 0x49, 0x10, 0x48, 0x8B, 0x49, 0x30, 0xC3 };
        Assert.Equal(NativeLambdaScan.Result.OwnerOnly, NativeLambdaScan.Scan(leaCode, 0x18, 0x30));
    }

    [Fact]
    public void Scan_FakeMovInsideImmediate_IgnoredInStrictMode()
    {
        // mov rax, 0x30498B4818498B48 (contains bytes of mov rcx,[rcx+0x18] and mov rcx,[rcx+0x30] inside imm64)
        byte[] code = { 0x48, 0xB8, 0x48, 0x8B, 0x49, 0x18, 0x48, 0x8B, 0x49, 0x30, 0xC3 };
        Assert.Equal(NativeLambdaScan.Result.Inconclusive, NativeLambdaScan.Scan(code, 0x18, 0x30, trackRegisters: true));
    }

    [Fact]
    public void Scan_StopsAtDoubleInt3Padding()
    {
        // mov rcx, [rcx+0x18] ; int3 ; int3 ; mov rcx, [rcx+0x30] (next function)
        byte[] code = { 0x48, 0x8B, 0x49, 0x18, 0xCC, 0xCC, 0x48, 0x8B, 0x49, 0x30 };
        Assert.Equal(NativeLambdaScan.Result.OwnerOnly, NativeLambdaScan.Scan(code, 0x18, 0x30));
    }

    [Fact]
    public void Scan_NonPositiveTargetOffset_ReturnsInconclusive()
    {
        byte[] code = { 0x48, 0x8B, 0x49, 0x18, 0xC3 };
        Assert.Equal(NativeLambdaScan.Result.Inconclusive, NativeLambdaScan.Scan(code, 0x18, 0));
        Assert.Equal(NativeLambdaScan.Result.Inconclusive, NativeLambdaScan.Scan(code, 0x18, -8));
    }
}
