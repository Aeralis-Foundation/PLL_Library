using System;
using System.IO;

namespace PLL.Library;

internal abstract class NativeClimbingFixtureProgress
{
    internal enum Phase : byte { Intact, Breaking, Broken }
    private protected NativeClimbingFixtureProgress(Phase State, double Origin, bool Loaded)
    {
        if (State is not (Phase.Intact or Phase.Breaking or Phase.Broken))
            throw new InvalidDataException("Climbing fixture phase is unsupported.");
        if (double.IsNaN(Origin) || double.IsInfinity(Origin) || State == Phase.Broken && Loaded)
            throw new InvalidDataException("Climbing fixture clock or terminal load is invalid.");
        this.State = State; this.Origin = Origin; this.Loaded = Loaded;
    }
    internal Phase State { get; }
    internal double Origin { get; }
    internal bool Loaded { get; }
    internal abstract void Write(BinaryWriter Writer);
    internal abstract NativeClimbingFixtureProgress ShiftOrigin(double Offset);

    internal sealed class MountedTool : NativeClimbingFixtureProgress
    {
        internal MountedTool(Phase State, float HangRemaining, float CrackScale, int CracksRemaining, float SinceCrack,
            double Origin = 0d, bool Loaded = false) : base(State, Origin, Loaded)
        {
            if (!Finite(HangRemaining) || HangRemaining < 0f || HangRemaining > 5f
                || !Finite(CrackScale) || CrackScale < 0f || CrackScale > 3f
                || CracksRemaining < 0 || CracksRemaining > 4 || !Finite(SinceCrack) || SinceCrack < 0f
                || State == Phase.Intact && (CrackScale != 0f || CracksRemaining != 4)
                || State != Phase.Intact && HangRemaining != 0f
                || State == Phase.Breaking && CracksRemaining == 0
                || State == Phase.Broken && (CracksRemaining != 0 || SinceCrack != 0f)
                || CrackScale != (4 - CracksRemaining) * 0.75f)
                throw new InvalidDataException("Mounted tool progress is outside its native timing or crack range.");
            this.HangRemaining = HangRemaining; this.CrackScale = CrackScale;
            this.CracksRemaining = CracksRemaining; this.SinceCrack = SinceCrack;
        }
        internal float HangRemaining { get; }
        internal float CrackScale { get; }
        internal int CracksRemaining { get; }
        internal float SinceCrack { get; }
        internal override void Write(BinaryWriter Writer)
        {
            Writer.Write((byte)0); Writer.Write((byte)State); Writer.Write(HangRemaining);
            Writer.Write(CrackScale); Writer.Write(CracksRemaining); Writer.Write(SinceCrack);
            Writer.Write(Origin); Writer.Write(Loaded);
        }
        internal override NativeClimbingFixtureProgress ShiftOrigin(double Offset) =>
            new MountedTool(State, HangRemaining, CrackScale, CracksRemaining, SinceCrack, Origin + Offset, Loaded);
    }

    internal sealed class RopeAnchor : NativeClimbingFixtureProgress
    {
        internal RopeAnchor(Phase State, float BreakRemaining, float Elapsed, float StartSegments,
            double Origin = 0d, bool Loaded = false) : base(State, Origin, Loaded)
        {
            if (!Finite(BreakRemaining) || BreakRemaining < 0f || !Finite(Elapsed) || Elapsed < 0f
                || !Finite(StartSegments) || StartSegments < 0f || StartSegments >= PllHangingRopeAuthoring.MaximumSegments
                || State == Phase.Intact && (Elapsed != 0f || StartSegments != 0f)
                || State != Phase.Intact && (StartSegments < 1f || BreakRemaining != 0f))
                throw new InvalidDataException("Breakable Rope progress is outside its native timing or segment range.");
            this.BreakRemaining = BreakRemaining; this.Elapsed = Elapsed; this.StartSegments = StartSegments;
        }
        internal float BreakRemaining { get; }
        internal float Elapsed { get; }
        internal float StartSegments { get; }
        internal override void Write(BinaryWriter Writer)
        {
            Writer.Write((byte)1); Writer.Write((byte)State); Writer.Write(BreakRemaining);
            Writer.Write(Elapsed); Writer.Write(StartSegments);
            Writer.Write(Origin); Writer.Write(Loaded);
        }
        internal override NativeClimbingFixtureProgress ShiftOrigin(double Offset) =>
            new RopeAnchor(State, BreakRemaining, Elapsed, StartSegments, Origin + Offset, Loaded);
    }

    internal static NativeClimbingFixtureProgress Read(BinaryReader Reader)
    {
        var Kind = Reader.ReadByte(); var State = (Phase)Reader.ReadByte();
        return Kind switch
        {
            0 => new MountedTool(State, Reader.ReadSingle(), Reader.ReadSingle(), Reader.ReadInt32(), Reader.ReadSingle(),
                Reader.ReadDouble(), Reader.ReadBoolean()),
            1 => new RopeAnchor(State, Reader.ReadSingle(), Reader.ReadSingle(), Reader.ReadSingle(), Reader.ReadDouble(), Reader.ReadBoolean()),
            _ => throw new InvalidDataException("Climbing fixture progress kind is unsupported.")
        };
    }

    static bool Finite(float Value) => !float.IsNaN(Value) && !float.IsInfinity(Value);
}
