using System;

namespace PLL.Library;

internal readonly struct PeakQuicksaveState
{
    internal PeakQuicksaveState(byte[] States, byte[] Outputs)
    {
        if (States.Length != Outputs.Length)
            throw new ArgumentException("Peak quicksave arrays do not match.");
        this.States = States;
        this.Outputs = Outputs;
    }

    internal byte[] States { get; }
    internal byte[] Outputs { get; }
    internal static PeakQuicksaveState Empty(int SlotCount) =>
        new(new byte[SlotCount], new byte[SlotCount]);
}

internal enum PeakRewardState : byte { Available, Opening, OpeningReward, OpeningRevive, Spent }

internal sealed class PeakCompletionProjection
{
    const byte OutputMask = 31;
    readonly byte[] States;
    readonly int[] RewardActors;
    readonly byte[] Outputs;
    readonly int OpeningIndex;

    PeakCompletionProjection(
        bool[] FixtureSlots,
        bool[] HonorSlots,
        byte[] States,
        int[] RewardActors,
        byte[] Outputs)
    {
        if (HonorSlots.Length != FixtureSlots.Length || States.Length != FixtureSlots.Length
            || RewardActors.Length != FixtureSlots.Length || Outputs.Length != FixtureSlots.Length)
            throw new ArgumentException("Peak completion arrays do not match the logical slot count.");
        var Opening = -1;
        for (var i = 0; i < FixtureSlots.Length; i++)
        {
            var State = States[i];
            var Actor = RewardActors[i];
            var Output = Outputs[i];
            if (!FixtureSlots[i])
            {
                if (State != 0 || Actor != 0 || Output != 0 || HonorSlots[i])
                    throw new ArgumentException("A nonfixture slot carries Peak completion state.");
                continue;
            }
            if (!TryDecode(State, out var CampfireLit, out var HonorComplete, out var RewardState)
                || !HonorSlots[i] && HonorComplete || HonorSlots[i] && CampfireLit && !HonorComplete
                || Actor < 0 || Output > OutputMask
                || RewardState == PeakRewardState.Available && Actor != 0)
                throw new ArgumentException("A Peak fixture slot carries malformed completion state.");
            if (!InFlight(RewardState)) continue;
            if (Opening >= 0)
                throw new ArgumentException("Peak completion state contains multiple opening rewards.");
            Opening = i;
        }
        this.States = (byte[])States.Clone();
        this.RewardActors = (int[])RewardActors.Clone();
        this.Outputs = (byte[])Outputs.Clone();
        OpeningIndex = Opening;
    }

    internal int Length => States.Length;
    internal bool RewardOpening => OpeningIndex >= 0;
    internal int RewardOpeningIndex => OpeningIndex;

    internal bool CampfireLit(int Index)
    {
        RequireIndex(Index);
        return TryDecode(States[Index], out var Lit, out _, out _) && Lit;
    }

    internal bool HonorComplete(int Index)
    {
        RequireIndex(Index);
        return TryDecode(States[Index], out _, out var Complete, out _) && Complete;
    }

    internal PeakRewardState RewardState(int Index)
    {
        RequireIndex(Index);
        return TryDecode(States[Index], out _, out _, out var State)
            ? State
            : PeakRewardState.Available;
    }

    internal int RewardActor(int Index)
    {
        RequireIndex(Index);
        return RewardActors[Index];
    }

    internal byte OutputState(int Index)
    {
        RequireIndex(Index);
        return Outputs[Index];
    }

    internal object[] Serialize() => new object[]
    {
        (byte[])States.Clone(), (int[])RewardActors.Clone(), (byte[])Outputs.Clone()
    };

    internal PeakCompletionProjection WithCampfireLit(
        bool[] FixtureSlots,
        bool[] HonorSlots,
        int Index,
        byte OutputState)
    {
        if (!FixtureAt(FixtureSlots, Index) || OutputState > OutputMask
            || HonorSlots[Index] && !HonorComplete(Index))
            throw new InvalidOperationException("Peak campfire completion targeted a nonfixture slot.");
        if (CampfireLit(Index) && OutputState == Outputs[Index]) return this;
        if ((Outputs[Index] & ~OutputState) != 0)
            throw new InvalidOperationException("Peak campfire output state regressed.");
        var NextStates = (byte[])States.Clone();
        var NextOutputs = (byte[])Outputs.Clone();
        NextStates[Index] = Encode(true, HonorComplete(Index), RewardState(Index));
        NextOutputs[Index] = OutputState;
        return new PeakCompletionProjection(
            FixtureSlots, HonorSlots, NextStates, RewardActors, NextOutputs);
    }

    internal PeakCompletionProjection WithHonorComplete(
        bool[] FixtureSlots,
        bool[] HonorSlots,
        int Index)
    {
        if (!FixtureAt(FixtureSlots, Index) || !HonorSlots[Index])
            throw new InvalidOperationException("Scouts Honor completion targeted a non-honor fixture slot.");
        if (HonorComplete(Index)) return this;
        var Next = (byte[])States.Clone();
        Next[Index] = Encode(CampfireLit(Index), true, RewardState(Index));
        return new PeakCompletionProjection(FixtureSlots, HonorSlots, Next, RewardActors, Outputs);
    }

    internal PeakCompletionProjection WithOutputs(
        bool[] FixtureSlots,
        bool[] HonorSlots,
        int Index,
        byte OutputState)
    {
        if (!FixtureAt(FixtureSlots, Index) || OutputState > OutputMask
            || (Outputs[Index] & ~OutputState) != 0)
            throw new InvalidOperationException("Peak campfire output publication is invalid.");
        if (OutputState == Outputs[Index]) return this;
        var Next = (byte[])Outputs.Clone();
        Next[Index] = OutputState;
        return new PeakCompletionProjection(FixtureSlots, HonorSlots, States, RewardActors, Next);
    }
    internal PeakCompletionProjection WithRewardOpening(
        bool[] FixtureSlots,
        bool[] HonorSlots,
        int Index,
        int Actor)
    {
        if (!FixtureAt(FixtureSlots, Index) || OpeningIndex >= 0
            || RewardState(Index) != PeakRewardState.Available
            || Actor < 0)
            throw new InvalidOperationException("Peak reward opening targeted an unavailable fixture slot.");
        var NextStates = (byte[])States.Clone();
        var NextActors = (int[])RewardActors.Clone();
        NextStates[Index] = Encode(CampfireLit(Index), HonorComplete(Index), PeakRewardState.Opening);
        NextActors[Index] = Actor;
        return new PeakCompletionProjection(
            FixtureSlots, HonorSlots, NextStates, NextActors, Outputs);
    }

    internal PeakCompletionProjection WithRewardResolution(
        bool[] FixtureSlots,
        bool[] HonorSlots,
        int Index,
        PeakRewardState Resolution)
    {
        if (!FixtureAt(FixtureSlots, Index) || RewardState(Index) != PeakRewardState.Opening
            || Resolution is not (PeakRewardState.OpeningReward or PeakRewardState.OpeningRevive))
            throw new InvalidOperationException("Peak reward resolution targeted a nonopening fixture slot.");
        var Next = (byte[])States.Clone();
        Next[Index] = Encode(CampfireLit(Index), HonorComplete(Index), Resolution);
        return new PeakCompletionProjection(FixtureSlots, HonorSlots, Next, RewardActors, Outputs);
    }

    internal PeakCompletionProjection WithRewardSpent(
        bool[] FixtureSlots,
        bool[] HonorSlots,
        int Index)
    {
        if (!FixtureAt(FixtureSlots, Index)
            || RewardState(Index) is not (PeakRewardState.OpeningReward or PeakRewardState.OpeningRevive))
            throw new InvalidOperationException("Peak reward completion targeted a nonopening fixture slot.");
        var Next = (byte[])States.Clone();
        Next[Index] = Encode(CampfireLit(Index), HonorComplete(Index), PeakRewardState.Spent);
        return new PeakCompletionProjection(FixtureSlots, HonorSlots, Next, RewardActors, Outputs);
    }

    internal bool CanFollow(PeakCompletionProjection Previous)
    {
        if (Previous.States.Length != States.Length) return false;
        for (var i = 0; i < States.Length; i++)
        {
            if (Previous.States[i] == 0)
            {
                if (States[i] != 0 || RewardActors[i] != 0 || Outputs[i] != 0) return false;
                continue;
            }
            if (States[i] == 0 || Previous.CampfireLit(i) && !CampfireLit(i)
                || Previous.HonorComplete(i) && !HonorComplete(i)
                || (Previous.Outputs[i] & ~Outputs[i]) != 0) return false;
            var PreviousState = Previous.RewardState(i);
            var State = RewardState(i);
            var PreviousActor = Previous.RewardActor(i);
            var Actor = RewardActor(i);
            var Valid = PreviousState switch
            {
                PeakRewardState.Available => State == PeakRewardState.Available && Actor == 0
                    || State == PeakRewardState.Opening,
                PeakRewardState.Opening => State == PeakRewardState.Opening && Actor == PreviousActor
                    || State is PeakRewardState.OpeningReward or PeakRewardState.OpeningRevive
                        && Actor == PreviousActor,
                PeakRewardState.OpeningReward => State == PeakRewardState.OpeningReward
                    && Actor == PreviousActor || State == PeakRewardState.Spent && Actor == PreviousActor,
                PeakRewardState.OpeningRevive => State == PeakRewardState.OpeningRevive
                    && Actor == PreviousActor || State == PeakRewardState.Spent && Actor == PreviousActor,
                PeakRewardState.Spent => State == PeakRewardState.Spent && Actor == PreviousActor,
                _ => throw new InvalidOperationException($"Peak reward state {PreviousState} is unsupported.")
            };
            if (!Valid) return false;
        }
        return true;
    }

    internal bool Same(PeakCompletionProjection Other) =>
        PhotonPropertyProtocol.SameValue(States, Other.States)
        && PhotonPropertyProtocol.SameValue(RewardActors, Other.RewardActors)
        && PhotonPropertyProtocol.SameValue(Outputs, Other.Outputs);

    internal PeakQuicksaveState CapturePersistent(
        bool[] FixtureSlots,
        bool[] HonorSlots)
    {
        if (OpeningIndex >= 0)
            throw new InvalidOperationException("A quicksave cannot capture an opening Peak statue reward.");
        var PersistentStates = new byte[States.Length];
        for (var i = 0; i < PersistentStates.Length; i++)
        {
            if (!FixtureSlots[i]) continue;
            var State = RewardState(i);
            PersistentStates[i] = PersistentState(
                CampfireLit(i), HonorSlots[i] && HonorComplete(i), State == PeakRewardState.Spent);
        }
        return new PeakQuicksaveState(PersistentStates, (byte[])Outputs.Clone());
    }

    internal bool CampfireCompletionReady(int Index, byte RequiredOutputs)
    {
        RequireIndex(Index);
        return OpeningIndex < 0 && CampfireLit(Index)
            && (Outputs[Index] & RequiredOutputs) == RequiredOutputs;
    }

    internal static PeakCompletionProjection Initial(
        bool[] FixtureSlots,
        bool[] HonorSlots,
        PeakQuicksaveState? Persistent)
    {
        var States = new byte[FixtureSlots.Length];
        var Actors = new int[FixtureSlots.Length];
        var Outputs = new byte[FixtureSlots.Length];
        if (Persistent is { } Saved && (Saved.States.Length != FixtureSlots.Length
                || Saved.Outputs.Length != FixtureSlots.Length))
            throw MapQuicksave.RejectQuicksave(
                "the Peak checkpoint state does not match the rebuilt logical slot count.");
        for (var i = 0; i < FixtureSlots.Length; i++)
        {
            var SavedState = Persistent?.States[i] ?? (byte)(FixtureSlots[i] ? 1 : 0);
            var SavedOutputs = Persistent?.Outputs[i] ?? (byte)0;
            if (!FixtureSlots[i])
            {
                if (SavedState != 0 || SavedOutputs != 0)
                    throw MapQuicksave.RejectQuicksave(
                        $"nonfixture logical slot {i} carries Peak checkpoint state.");
                continue;
            }
            if (!TryReadPersistent(
                    SavedState, out var Lit, out var HonorComplete, out var Spent)
                || !HonorSlots[i] && HonorComplete || HonorSlots[i] && Lit && !HonorComplete
                || SavedOutputs > OutputMask)
                throw MapQuicksave.RejectQuicksave(
                    $"logical slot {i} carries malformed Peak checkpoint state.");
            States[i] = Encode(
                Lit, HonorComplete, Spent ? PeakRewardState.Spent : PeakRewardState.Available);
            Outputs[i] = SavedOutputs;
        }
        return new PeakCompletionProjection(FixtureSlots, HonorSlots, States, Actors, Outputs);
    }

    internal static bool TryRead(
        object[] Value,
        bool[] FixtureSlots,
        bool[] HonorSlots,
        out PeakCompletionProjection Projection)
    {
        Projection = null!;
        if (Value is not [byte[] States, int[] Actors, byte[] Outputs]) return false;
        try
        {
            Projection = new PeakCompletionProjection(
                FixtureSlots, HonorSlots, States, Actors, Outputs);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static bool ValidPersistentState(byte State) => State <= 8;
    internal static bool ValidPersistentOutputs(byte Outputs) => Outputs <= OutputMask;

    internal static bool PersistentCampfireLit(byte State) =>
        State is >= 1 and <= 8 && ((State - 1) & 1) != 0;

    static byte Encode(
        bool CampfireLit,
        bool HonorComplete,
        PeakRewardState RewardState) => checked((byte)(1
            + (CampfireLit ? 1 : 0)
            + (HonorComplete ? 2 : 0)
            + (int)RewardState * 4));

    static bool TryDecode(
        byte State,
        out bool CampfireLit,
        out bool HonorComplete,
        out PeakRewardState RewardState)
    {
        CampfireLit = false;
        HonorComplete = false;
        RewardState = PeakRewardState.Available;
        if (State is < 1 or > 20) return false;
        var Value = State - 1;
        CampfireLit = (Value & 1) != 0;
        HonorComplete = (Value & 2) != 0;
        RewardState = (PeakRewardState)(Value >> 2);
        return true;
    }

    static byte PersistentState(
        bool CampfireLit,
        bool HonorComplete,
        bool RewardSpent) => checked((byte)(1
            + (CampfireLit ? 1 : 0)
            + (HonorComplete ? 2 : 0)
            + (RewardSpent ? 4 : 0)));

    static bool TryReadPersistent(
        byte State,
        out bool CampfireLit,
        out bool HonorComplete,
        out bool RewardSpent)
    {
        CampfireLit = false;
        HonorComplete = false;
        RewardSpent = false;
        if (State is < 1 or > 8) return false;
        var Value = State - 1;
        CampfireLit = (Value & 1) != 0;
        HonorComplete = (Value & 2) != 0;
        RewardSpent = (Value & 4) != 0;
        return true;
    }

    static bool InFlight(PeakRewardState State) =>
        State is PeakRewardState.Opening or PeakRewardState.OpeningReward
            or PeakRewardState.OpeningRevive;

    static bool FixtureAt(bool[] FixtureSlots, int Index) =>
        Index >= 0 && Index < FixtureSlots.Length && FixtureSlots[Index];

    void RequireIndex(int Index)
    {
        if (Index < 0 || Index >= States.Length)
            throw new ArgumentOutOfRangeException(nameof(Index));
    }
}
