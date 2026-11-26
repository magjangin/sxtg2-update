using System.Reflection;
using SixtarGateMod.Managers;
using Xunit;

namespace SixtarGateMod.Tests;

public class PatchSignatureCheckTests
{
    private enum Lane : int { A, B }
    private enum SmallLane : byte { A }

    // ---- 훅 대상(게임 메서드 역할) ----
    private class Base { }
    private class Derived : Base { }
    private class Other { }

    private class Target
    {
        private int field = 0;
        private string _title = "test";
        public Target(int id) { _ = field; _ = _title; }
        public int Prop { get; set; }
        public void Judge(int lane, float time, Derived data) { }
        public static void StaticCall(int lane) { }
        public bool IsOwned() => true;
        public void Set(Lane lane) { }
        public void Ref(ref int count) { }
    }

    private static MethodBase T(string name) => typeof(Target).GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)!;

    // ---- 패치 클래스들 ----
    private static class GoodPatch
    {
        public static bool Prefix(Target __instance, int lane, Derived data, ref bool __runOriginal) => true;
        public static void Postfix(float time) { }
    }

    private static class RenamedArgPatch { public static void Prefix(int laneIndex) { } }
    private static class WrongValueTypePatch { public static void Prefix(long lane) { } }
    private static class FloatVsIntPatch { public static void Prefix(float lane) { } }
    private static class InstanceOnStaticPatch { public static void Prefix(object __instance) { } }
    private static class ResultOnVoidPatch { public static void Postfix(bool __result) { } }
    private static class ResultOkPatch { public static void Postfix(ref bool __result) { } }
    private static class ResultWrongPatch { public static void Postfix(ref int __result) { } }
    private static class EnumAsIntPatch { public static void Prefix(int lane) { } }
    private static class EnumAsByteEnumPatch { public static void Prefix(SmallLane lane) { } }
    private static class BaseTypePatch { public static void Prefix(Base data) { } }
    private static class UnrelatedRefPatch { public static void Prefix(Other data) { } }
    private static class ObjectPatch { public static void Prefix(object data) { } }
    private static class ObjectResultPatch { public static void Postfix(object __result) { } }
    private static class RefArgPatch { public static void Prefix(ref int count) { } }
    private static class SpecialArgsPatch { public static void Prefix(int __state, object[] __args, MethodBase __originalMethod, int ___field, string ___title) { } }

    private static string? Run(Type patch, string target, List<string>? warnings = null)
        => PatchSignatureCheck.Check(patch, new[] { T(target) }, warnings ?? new List<string>());

    [Fact]
    public void MatchingSignature_Passes()
    {
        var w = new List<string>();
        Assert.Null(Run(typeof(GoodPatch), nameof(Target.Judge), w));
        Assert.Empty(w);
    }

    [Fact]
    public void RenamedTargetArgument_IsError()
        => Assert.Contains("laneIndex", Run(typeof(RenamedArgPatch), nameof(Target.Judge)));

    [Theory]
    [InlineData(typeof(WrongValueTypePatch))]
    [InlineData(typeof(FloatVsIntPatch))]
    public void ValueTypeChange_IsError(Type patch)
        => Assert.NotNull(Run(patch, nameof(Target.Judge)));

    [Fact]
    public void InstanceOnStaticTarget_IsError()
        => Assert.NotNull(Run(typeof(InstanceOnStaticPatch), nameof(Target.StaticCall)));

    [Fact]
    public void ResultOnVoidTarget_IsError()
        => Assert.NotNull(Run(typeof(ResultOnVoidPatch), nameof(Target.Judge)));

    [Fact]
    public void ResultTypeMatch_Passes_AndMismatch_IsError()
    {
        Assert.Null(Run(typeof(ResultOkPatch), nameof(Target.IsOwned)));
        Assert.NotNull(Run(typeof(ResultWrongPatch), nameof(Target.IsOwned)));
    }

    [Fact]
    public void EnumReceivedAsSameSizeInt_Passes_ButSmallerEnum_IsError()
    {
        Assert.Null(Run(typeof(EnumAsIntPatch), nameof(Target.Set)));
        Assert.NotNull(Run(typeof(EnumAsByteEnumPatch), nameof(Target.Set)));
    }

    [Fact]
    public void ReferenceTypes_BaseIsSilent_UnrelatedIsWarningOnly()
    {
        var w = new List<string>();
        Assert.Null(Run(typeof(BaseTypePatch), nameof(Target.Judge), w));
        Assert.Empty(w);

        Assert.Null(Run(typeof(UnrelatedRefPatch), nameof(Target.Judge), w));
        Assert.Single(w);
    }

    // ---- 업데이트로 오버로드가 생긴 대상 ----
    private class Overloaded
    {
        public void Play(string audioName, int channel) { }
        public void Play(string audioName, int channel, float position) { }
        public void Play(long audioId) { }
        public void Stop() { }
        public void Stop(int channel) { }
    }

    private static class PlayByPositionPatch { public static void Prefix(string audioName, float position) { } }
    private static class PlayByNamePatch { public static void Prefix(string audioName, int channel) { } }
    private static class NoArgsPatch { public static void Postfix() { } }

    private static List<MethodInfo> Overloads(string name)
        => typeof(Overloaded).GetMethods().Where(m => m.Name == name).ToList();

    [Fact]
    public void PickOverload_UsesPatchArgumentsAsSignatureHint()
    {
        var pick = PatchSignatureCheck.PickOverload(typeof(PlayByPositionPatch), Overloads("Play"), out _);
        Assert.NotNull(pick);
        Assert.Equal(3, pick!.GetParameters().Length);
    }

    [Fact]
    public void PickOverload_RefusesWhenSeveralFit()
    {
        // (string audioName, int channel)는 2인자·3인자 오버로드 둘 다에 맞음 → 고르지 않음
        Assert.Null(PatchSignatureCheck.PickOverload(typeof(PlayByNamePatch), Overloads("Play"), out var note));
        Assert.Contains("2개", note);
        // 인자를 안 받는 패치는 단서가 없음
        Assert.Null(PatchSignatureCheck.PickOverload(typeof(NoArgsPatch), Overloads("Stop"), out _));
    }

    [Fact]
    public void ObjectByRefAndSpecialArguments_Pass()
    {
        Assert.Null(Run(typeof(ObjectPatch), nameof(Target.Judge)));
        Assert.Null(Run(typeof(ObjectResultPatch), nameof(Target.IsOwned)));
        Assert.Null(Run(typeof(RefArgPatch), nameof(Target.Ref)));
        Assert.Null(Run(typeof(SpecialArgsPatch), nameof(Target.Judge)));
    }

    private static class PositionalOkPatch { public static void Prefix(int __0, float __1) { } }
    private static class PositionalWrongTypePatch { public static void Prefix(long __0) { } }
    private static class PositionalOutOfRangePatch { public static void Prefix(int __9) { } }
    private static class TypoSpecialArgPatch { public static void Prefix(object __instnace) { } }
    private static class ValueTypeInstancePatch { public static void Prefix(int __instance) { } }
    private static class UnrelatedInstancePatch { public static void Prefix(Other __instance) { } }
    private static class FinalizerPatch { public static Exception? Finalizer(Exception __exception, ref bool __result) => null; }
    private static class CtorResultPatch { public static void Postfix(int __result) { } }
    private static class CtorInstancePatch { public static void Postfix(Target __instance, int id) { } }
    private static class GetterPatch { public static void Postfix(ref int __result) { } }
    private static class SetterPatch { public static void Prefix(int value) { } }

    private class TieBreakerTarget
    {
        public void Send(Derived item) { }
        public void Send(Other item) { }
    }
    private static class SendDerivedPatch { public static void Prefix(Derived item) { } }
    private static class NoMatchPatch { public static void Prefix(string nonexistent) { } }

    [Fact]
    public void PositionalArguments_ValidateBoundsAndTypes()
    {
        Assert.Null(Run(typeof(PositionalOkPatch), nameof(Target.Judge)));
        Assert.NotNull(Run(typeof(PositionalWrongTypePatch), nameof(Target.Judge)));
        Assert.NotNull(Run(typeof(PositionalOutOfRangePatch), nameof(Target.Judge)));
    }

    [Fact]
    public void UnknownDoubleUnderscoreArg_EmitsWarning()
    {
        var w = new List<string>();
        Assert.Null(Run(typeof(TypoSpecialArgPatch), nameof(Target.Judge), w));
        Assert.Single(w);
    }

    [Fact]
    public void InstanceTypeMismatch_ValueIsError_UnrelatedRefIsWarning()
    {
        Assert.NotNull(Run(typeof(ValueTypeInstancePatch), nameof(Target.Judge)));
        var w = new List<string>();
        Assert.Null(Run(typeof(UnrelatedInstancePatch), nameof(Target.Judge), w));
        Assert.Single(w);
    }

    [Fact]
    public void Finalizer_Constructor_AndPropertyAccessors_ValidateCorrectly()
    {
        Assert.Null(Run(typeof(FinalizerPatch), nameof(Target.IsOwned)));

        var ctor = typeof(Target).GetConstructor(new[] { typeof(int) })!;
        Assert.NotNull(PatchSignatureCheck.Check(typeof(CtorResultPatch), new MethodBase[] { ctor }, new List<string>()));
        Assert.Null(PatchSignatureCheck.Check(typeof(CtorInstancePatch), new MethodBase[] { ctor }, new List<string>()));

        var prop = typeof(Target).GetProperty(nameof(Target.Prop))!;
        Assert.Null(PatchSignatureCheck.Check(typeof(GetterPatch), new MethodBase[] { prop.GetGetMethod()! }, new List<string>()));
        Assert.Null(PatchSignatureCheck.Check(typeof(SetterPatch), new MethodBase[] { prop.GetSetMethod()! }, new List<string>()));
    }

    [Fact]
    public void PickOverload_PrefersWarningFreeOverload_AndReturnsNullWhenNoneFit()
    {
        var cands = typeof(TieBreakerTarget).GetMethods().Where(m => m.Name == nameof(TieBreakerTarget.Send)).ToList();
        var pick = PatchSignatureCheck.PickOverload(typeof(SendDerivedPatch), cands, out _);
        Assert.NotNull(pick);
        Assert.Equal(typeof(Derived), pick!.GetParameters()[0].ParameterType);

        Assert.Null(PatchSignatureCheck.PickOverload(typeof(NoMatchPatch), cands, out _));
    }

    // ---- 업데이트로 시그니처가 바뀐 대상(인자 형식을 지정한 [HarmonyPatch]가 정확히 맞는 메서드를 못 찾음) ----
    private class ChangedBase { public void Start(int track, float level) { } }
    private class ChangedDerived : ChangedBase { }

    private class Changed
    {
        // 원래 Goto(string track, int level) → 업데이트로 bool practice 인자가 늘어남
        public void Goto(string track, int level, bool practice) { }
        // 원래 Resume(int channel) → 사라지고 뜻이 다른 형제만 남음
        public void Resume(float time, int channel) { }
        public void Resume(int channel, bool fade) { }
        // 원래 Seek(int ms) → 인자 형식이 long으로 바뀜
        public void Seek(long ms) { }
    }

    private static class GotoPatch { public static void Prefix(string track, int level) { } }
    private static class ResumePatch { public static void Prefix(int channel) { } }
    private static class SeekPatch { public static void Prefix(int ms) { } }
    private static class StartPatch { public static void Prefix(int track) { } }

    [Fact]
    public void PickChangedSignature_PicksTheOnlySameNameMethodThatFitsThePatch()
    {
        var pick = PatchSignatureCheck.PickChangedSignature(
            typeof(GotoPatch), PatchSignatureCheck.SameNameMethods(typeof(Changed), nameof(Changed.Goto)), out var note);
        Assert.NotNull(pick);
        Assert.Equal(3, pick!.GetParameters().Length);
        Assert.Contains("고름", note);
    }

    [Fact]
    public void PickChangedSignature_RefusesSiblingOverloads_ValueTypeChange_AndMissingName()
    {
        // 같은 이름이 여럿: 지정한 오버로드만 사라졌을 수 있음 → 고르지 않음
        Assert.Null(PatchSignatureCheck.PickChangedSignature(
            typeof(ResumePatch), PatchSignatureCheck.SameNameMethods(typeof(Changed), nameof(Changed.Resume)), out var n1));
        Assert.Contains("2개", n1);
        // 하나뿐이지만 패치가 받는 값 형식이 바뀜(int → long) → 고르지 않음
        Assert.Null(PatchSignatureCheck.PickChangedSignature(
            typeof(SeekPatch), PatchSignatureCheck.SameNameMethods(typeof(Changed), nameof(Changed.Seek)), out var n2));
        Assert.Contains("맞지 않음", n2);
        // 이름이 없어짐
        Assert.Null(PatchSignatureCheck.PickChangedSignature(
            typeof(SeekPatch), PatchSignatureCheck.SameNameMethods(typeof(Changed), "Rewind"), out var n3));
        Assert.Contains("없음", n3);
    }

    [Fact]
    public void SameNameMethods_WalksUpToBaseType()
    {
        var cands = PatchSignatureCheck.SameNameMethods(typeof(ChangedDerived), nameof(ChangedBase.Start));
        Assert.Single(cands);
        Assert.NotNull(PatchSignatureCheck.PickChangedSignature(typeof(StartPatch), cands, out _));
    }

    private static class RefObjectValueTypePatch { public static void Prefix(ref object lane) { } }
    private static class BoolAsBytePatch { public static void Postfix(ref byte __result) { } }
    private static class MissingFieldInjectPatch { public static void Prefix(int ___noSuchField) { } }
    private static class WrongFieldTypeInjectPatch { public static void Prefix(long ___field) { } }

    [Fact]
    public void RefObjectForValueType_BoolVsByte_AndMissingInjectedField_AreErrors()
    {
        Assert.NotNull(Run(typeof(RefObjectValueTypePatch), nameof(Target.Judge)));
        Assert.NotNull(Run(typeof(BoolAsBytePatch), nameof(Target.IsOwned)));
        Assert.NotNull(Run(typeof(MissingFieldInjectPatch), nameof(Target.Judge)));
        Assert.NotNull(Run(typeof(WrongFieldTypeInjectPatch), nameof(Target.Judge)));
    }
}
