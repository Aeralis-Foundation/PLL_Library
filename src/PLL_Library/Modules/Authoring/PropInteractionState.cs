using System;
using System.IO;

namespace PLL.Library;

internal enum PropBreakPhase : byte { Intact, OverloadDelay, Warning, Fallen }

internal readonly struct PropMotionTimeline : IEquatable<PropMotionTimeline>
{
    internal PropMotionTimeline(bool Running, double ActiveSeconds, double Origin, double FirstClock)
    { this.Running = Running; this.ActiveSeconds = ActiveSeconds; this.Origin = Origin; this.FirstClock = FirstClock; }
    internal bool Running { get; }
    internal double ActiveSeconds { get; }
    internal double Origin { get; }
    internal double FirstClock { get; }
    internal double ActiveAt(double Clock) => ActiveSeconds + (Running ? Math.Max(0d, Clock - Origin) : 0d);
    internal PropMotionTimeline Rebase(double Shift) => new(Running, ActiveSeconds, Origin + Shift, FirstClock + Shift);
    public bool Equals(PropMotionTimeline Other) => Running == Other.Running && ActiveSeconds == Other.ActiveSeconds
        && Origin == Other.Origin && FirstClock == Other.FirstClock;
    public override bool Equals(object? Other) => Other is PropMotionTimeline Timeline && Equals(Timeline);
    public override int GetHashCode() => HashCode.Combine(Running, ActiveSeconds, Origin, FirstClock);
}

internal sealed class PropInteractionIntent : IEquatable<PropInteractionIntent>
{
    PropInteractionIntent(long Revision, double? BreakClock, bool? BreakEnabled, PropMotionTimeline? Motion)
    { this.Revision = Revision; this.BreakClock = BreakClock; this.BreakEnabled = BreakEnabled; this.Motion = Motion; }
    internal long Revision { get; }
    internal double? BreakClock { get; }
    internal bool BreakRequested => BreakClock.HasValue;
    internal bool? BreakEnabled { get; }
    internal PropMotionTimeline? Motion { get; }
    internal PropInteractionIntent Rebase(double Shift) => new(Revision, BreakClock + Shift, BreakEnabled, Motion?.Rebase(Shift));
    internal static PropInteractionIntent? Apply(PropInteractionIntent? Prior, PllPropOperation Operation, bool Enabled,
        bool InitiallyEnabled, double Clock, long Revision)
    {
        var BreakClock = Prior?.BreakClock;
        var Policy = Prior?.BreakEnabled; var Motion = Prior?.Motion;
        switch (Operation)
        {
            case PllPropOperation.Break:
                if (!(Policy ?? InitiallyEnabled)) return null;
                BreakClock ??= Clock; break;
            case PllPropOperation.SetBreakableEnabled: Policy = Enabled; break;
            case PllPropOperation.StartMotion:
            case PllPropOperation.PauseMotion:
            case PllPropOperation.ResumeMotion:
                Motion = new PropMotionTimeline(Operation != PllPropOperation.PauseMotion,
                    Motion?.ActiveAt(Clock) ?? 0d, Clock, Motion?.FirstClock ?? Clock);
                break;
            default: throw new InvalidOperationException("Prop interaction operation is unsupported.");
        }
        return new(Revision, BreakClock, Policy, Motion);
    }
    public bool Equals(PropInteractionIntent? Other) => Other is not null && Revision == Other.Revision
        && BreakClock == Other.BreakClock && BreakEnabled == Other.BreakEnabled && Nullable.Equals(Motion, Other.Motion);
    public override bool Equals(object? Other) => Other is PropInteractionIntent Intent && Equals(Intent);
    public override int GetHashCode() => HashCode.Combine(Revision, BreakClock, BreakEnabled, Motion);
    internal byte[] Encode()
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true))
        {
            Writer.Write((byte)1); Writer.Write(Revision); Writer.Write(BreakClock.HasValue);
            if (BreakClock is { } BrokenAt) Writer.Write(BrokenAt);
            Writer.Write(BreakEnabled.HasValue); if (BreakEnabled is { } Enabled) Writer.Write(Enabled);
            Writer.Write(Motion.HasValue);
            if (Motion is { } Timeline)
            {
                Writer.Write(Timeline.Running); Writer.Write(Timeline.ActiveSeconds);
                Writer.Write(Timeline.Origin); Writer.Write(Timeline.FirstClock);
            }
        }
        return Stream.ToArray();
    }
    internal static PropInteractionIntent Decode(byte[] Bytes)
    {
        if (Bytes.Length is < 12 or > 46) throw new InvalidDataException("Prop intent exceeds its scalar payload shape.");
        using var Stream = new MemoryStream(Bytes, false); using var Reader = new BinaryReader(Stream);
        if (Reader.ReadByte() != 1) throw new InvalidDataException("Prop intent format is unsupported.");
        var Revision = Reader.ReadInt64(); double? Break = Reader.ReadBoolean() ? Reader.ReadDouble() : null;
        bool? Enabled = Reader.ReadBoolean() ? Reader.ReadBoolean() : null;
        PropMotionTimeline? Motion = Reader.ReadBoolean()
            ? new PropMotionTimeline(Reader.ReadBoolean(), Reader.ReadDouble(), Reader.ReadDouble(), Reader.ReadDouble()) : null;
        if (Stream.Position != Stream.Length || Revision <= 0 || Break is null && Enabled is null && Motion is null
            || Break is { } BrokenAt && !double.IsFinite(BrokenAt)
            || Motion is { } Value && (!double.IsFinite(Value.ActiveSeconds) || Value.ActiveSeconds < 0d
                || !double.IsFinite(Value.Origin) || !double.IsFinite(Value.FirstClock) || Value.FirstClock > Value.Origin
                || !double.IsFinite(Value.Origin - Value.FirstClock) || Value.ActiveSeconds > Value.Origin - Value.FirstClock))
            throw new InvalidDataException("Prop intent has invalid revision, clock or active time.");
        return new(Revision, Break, Enabled, Motion);
    }
}

internal readonly struct PropMotionBaseline : IEquatable<PropMotionBaseline>
{
    internal PropMotionBaseline(double ActiveSeconds, float Phase) { this.ActiveSeconds = ActiveSeconds; this.Phase = Phase; }
    internal double ActiveSeconds { get; }
    internal float Phase { get; }
    public bool Equals(PropMotionBaseline Other) => ActiveSeconds == Other.ActiveSeconds && Phase == Other.Phase;
    public override bool Equals(object? Other) => Other is PropMotionBaseline Baseline && Equals(Baseline);
    public override int GetHashCode() => HashCode.Combine(ActiveSeconds, Phase);
}

internal readonly struct PropInteractionState : IEquatable<PropInteractionState>
{
    internal PropInteractionState(bool BreakEnabled, PropBreakPhase BreakPhase, double BreakOrigin,
        float BreakDelaySeconds, int Capacity, bool MotionRunning, float MotionPhase, double MotionOrigin,
        long IntentRevision = 0, double? BornAt = null, PropMotionBaseline? MotionBaseline = null)
    {
        this.BreakEnabled = BreakEnabled; this.BreakPhase = BreakPhase; this.BreakOrigin = BreakOrigin;
        this.BreakDelaySeconds = BreakDelaySeconds; this.Capacity = Capacity;
        this.MotionRunning = MotionRunning; this.MotionPhase = MotionPhase; this.MotionOrigin = MotionOrigin;
        this.IntentRevision = IntentRevision; this.BornAt = BornAt ?? MotionOrigin; this.MotionBaseline = MotionBaseline;
    }
    internal bool BreakEnabled { get; }
    internal PropBreakPhase BreakPhase { get; }
    internal double BreakOrigin { get; }
    internal float BreakDelaySeconds { get; }
    internal int Capacity { get; }
    internal bool MotionRunning { get; }
    internal float MotionPhase { get; }
    internal double MotionOrigin { get; }
    internal bool IsBroken => BreakPhase == PropBreakPhase.Fallen;
    internal long IntentRevision { get; }
    internal double BornAt { get; }
    internal PropMotionBaseline? MotionBaseline { get; }
    internal PropInteractionState WithBreak(PropBreakPhase Phase, double Clock) =>
        new(BreakEnabled, Phase, Clock, BreakDelaySeconds, Capacity, MotionRunning && Phase != PropBreakPhase.Fallen, MotionPhase, MotionOrigin,
            IntentRevision, BornAt, MotionBaseline);
    PropInteractionState WithMotion(bool Running, float Phase, double Clock) =>
        new(BreakEnabled, BreakPhase, BreakOrigin, BreakDelaySeconds, Capacity, Running, Phase, Clock, IntentRevision, BornAt, MotionBaseline);
    PropInteractionState WithPolicy(bool Enabled) => new(Enabled,
        IsBroken || Enabled ? BreakPhase : PropBreakPhase.Intact, BreakOrigin,
        BreakDelaySeconds, Capacity, MotionRunning, MotionPhase, MotionOrigin, IntentRevision, BornAt, MotionBaseline);
    PropInteractionState WithIntent(long Revision, PropMotionBaseline? Baseline) =>
        new(BreakEnabled, BreakPhase, BreakOrigin, BreakDelaySeconds, Capacity, MotionRunning, MotionPhase, MotionOrigin,
            Revision, BornAt, Baseline);
    internal PropInteractionState ApplyIntent(PropInteractionIntent Intent, float Period, double Now, bool Birth = false)
    {
        if (IntentRevision >= Intent.Revision) return this;
        var Value = this; var Baseline = MotionBaseline;
        if (Intent.Motion is { } Motion)
        {
            if (Baseline is null)
            {
                var FirstClock = Birth ? Now : Math.Max(BornAt, Motion.FirstClock);
                if (!Birth && BornAt > Motion.FirstClock && BornAt < Motion.Origin)
                    throw new InvalidOperationException($"Prop birth {BornAt:R} needs a baseline inside the motion interval {Motion.FirstClock:R} to {Motion.Origin:R}.");
                var Phase = MotionRunning ? NormalizePhase(MotionPhase + (FirstClock - MotionOrigin) / Period) : MotionPhase;
                Baseline = new PropMotionBaseline(Birth ? Motion.ActiveAt(Now)
                    : BornAt > Motion.FirstClock ? Motion.ActiveAt(BornAt) : 0d, Phase);
            }
            if (!IsBroken)
            {
                var Active = Motion.ActiveAt(Now) - Baseline.Value.ActiveSeconds;
                if (Active < 0d) throw new InvalidOperationException($"Prop motion active time {Active:R} is below zero after its instance baseline.");
                Value = Value.WithMotion(Motion.Running, NormalizePhase(Baseline.Value.Phase + Active / Period), Now);
            }
        }
        if (Intent.BreakEnabled is { } Enabled) Value = Value.WithPolicy(Enabled);
        if (Intent.BreakClock is { } BrokenAt && !Value.IsBroken) Value = Value.WithBreak(PropBreakPhase.Fallen, BrokenAt);
        return Value.WithIntent(Intent.Revision, Baseline);
    }
    internal static float NormalizePhase(double Phase) => (float)((Phase % 1d + 1d) % 1d) % 1f;

    public bool Equals(PropInteractionState Other) => BreakEnabled == Other.BreakEnabled
        && BreakPhase == Other.BreakPhase && BreakOrigin.Equals(Other.BreakOrigin)
        && BreakDelaySeconds.Equals(Other.BreakDelaySeconds) && Capacity == Other.Capacity
        && MotionRunning == Other.MotionRunning && MotionPhase.Equals(Other.MotionPhase)
        && MotionOrigin.Equals(Other.MotionOrigin) && IntentRevision == Other.IntentRevision && BornAt == Other.BornAt
        && Nullable.Equals(MotionBaseline, Other.MotionBaseline);
    public override bool Equals(object? Other) => Other is PropInteractionState State && Equals(State);
    public override int GetHashCode() => HashCode.Combine(HashCode.Combine(BreakEnabled, BreakPhase, BreakOrigin,
        BreakDelaySeconds, Capacity, MotionRunning, MotionPhase, MotionOrigin), IntentRevision, BornAt, MotionBaseline);

    internal byte[] Encode()
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true)) Write(Writer);
        return Stream.ToArray();
    }
    internal byte[] EncodeCheckpoint(double Clock)
    {
        using var Stream = new MemoryStream();
        using (var Writer = new BinaryWriter(Stream, System.Text.Encoding.UTF8, true)) WriteCheckpoint(Writer, Clock);
        return Stream.ToArray();
    }
    internal static PropInteractionState Decode(byte[] Bytes)
    {
        if (Bytes.Length is < 31 or > 60) throw new InvalidDataException("Prop interaction exceeds its scalar state shape.");
        using var Stream = new MemoryStream(Bytes, false); using var Reader = new BinaryReader(Stream);
        var State = Read(Reader);
        if (Stream.Position != Stream.Length) throw new InvalidDataException("Prop interaction has trailing state bytes.");
        return State;
    }
    internal static byte[] Restore(byte[] Bytes, double Clock)
    {
        if (Bytes.Length is < 39 or > 68) throw new InvalidDataException("Prop checkpoint exceeds its scalar state shape.");
        using var Stream = new MemoryStream(Bytes, false); using var Reader = new BinaryReader(Stream);
        var State = ReadCheckpoint(Reader, Clock);
        if (Stream.Position != Stream.Length) throw new InvalidDataException("Prop checkpoint has trailing state bytes.");
        return State.Encode();
    }

    internal void Write(BinaryWriter Writer)
    {
        Writer.Write(BreakEnabled); Writer.Write((byte)BreakPhase); Writer.Write(BreakOrigin);
        Writer.Write(BreakDelaySeconds); Writer.Write(Capacity); Writer.Write(MotionRunning);
        Writer.Write(MotionPhase); Writer.Write(MotionOrigin);
        Writer.Write(IntentRevision); Writer.Write(BornAt); Writer.Write(MotionBaseline.HasValue);
        if (MotionBaseline is { } Baseline) { Writer.Write(Baseline.ActiveSeconds); Writer.Write(Baseline.Phase); }
    }
    internal static PropInteractionState Read(BinaryReader Reader)
    {
        var Enabled = Reader.ReadBoolean(); var Phase = (PropBreakPhase)Reader.ReadByte();
        var Origin = Reader.ReadDouble(); var Delay = Reader.ReadSingle(); var Capacity = Reader.ReadInt32();
        var Running = Reader.ReadBoolean(); var Motion = Reader.ReadSingle(); var MotionOrigin = Reader.ReadDouble();
        if (Phase is < PropBreakPhase.Intact or > PropBreakPhase.Fallen || !double.IsFinite(Origin)
            || !double.IsFinite(MotionOrigin) || !float.IsFinite(Delay) || Delay < 0f || Capacity < 1
            || !float.IsFinite(Motion) || Motion < 0f || Motion >= 1f || Phase == PropBreakPhase.Fallen && Running
            || !Enabled && Phase is PropBreakPhase.OverloadDelay or PropBreakPhase.Warning)
            throw new InvalidDataException("Prop interaction state has invalid phase, timing or capacity.");
        if (Reader.BaseStream.Position == Reader.BaseStream.Length)
            return new(Enabled, Phase, Origin, Delay, Capacity, Running, Motion, MotionOrigin);
        var Revision = Reader.ReadInt64(); var BornAt = Reader.ReadDouble();
        PropMotionBaseline? Baseline = Reader.ReadBoolean() ? new(Reader.ReadDouble(), Reader.ReadSingle()) : null;
        if (Revision < 0 || !double.IsFinite(BornAt) || Revision == 0 && Baseline.HasValue || Baseline is { } Value
            && (!double.IsFinite(Value.ActiveSeconds) || Value.ActiveSeconds < 0d
                || !float.IsFinite(Value.Phase) || Value.Phase < 0f || Value.Phase >= 1f))
            throw new InvalidDataException("Prop interaction intent receipt or birth baseline is invalid.");
        return new(Enabled, Phase, Origin, Delay, Capacity, Running, Motion, MotionOrigin, Revision, BornAt, Baseline);
    }
    internal void WriteCheckpoint(BinaryWriter Writer, double Clock)
    {
        Writer.Write(Clock); Write(Writer);
    }
    internal static PropInteractionState ReadCheckpoint(BinaryReader Reader, double Clock)
    {
        var PriorClock = Reader.ReadDouble();
        if (!double.IsFinite(PriorClock) || !double.IsFinite(Clock))
            throw new InvalidDataException("Prop checkpoint clock must be finite.");
        var Prior = Read(Reader); var Shift = Clock - PriorClock;
        var BreakOrigin = Prior.BreakOrigin + Shift; var MotionOrigin = Prior.MotionOrigin + Shift; var BornAt = Prior.BornAt + Shift;
        if (!double.IsFinite(BreakOrigin) || !double.IsFinite(MotionOrigin) || !double.IsFinite(BornAt))
            throw new InvalidDataException("Prop checkpoint clock conversion overflowed.");
        return new(Prior.BreakEnabled, Prior.BreakPhase, BreakOrigin,
            Prior.BreakDelaySeconds, Prior.Capacity, Prior.MotionRunning, Prior.MotionPhase, MotionOrigin,
            Prior.IntentRevision, BornAt, Prior.MotionBaseline);
    }
}
