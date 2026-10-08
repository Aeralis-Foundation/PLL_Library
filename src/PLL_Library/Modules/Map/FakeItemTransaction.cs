using System.Collections.Generic;

namespace PLL.Library;

internal sealed class FakeItemTransaction
{
    readonly FakeItemManager? Manager;
    readonly List<FakeItem> Items;
    readonly List<(FakeItem Item, int Index, bool PickedUp, bool Active)> States = new();
    readonly List<int> HiddenItems;
    readonly int HighWater;

    internal FakeItemTransaction(MapHandler Handler)
    {
        Manager = FakeItemManager.Instance;
        Items = Manager && Manager.allFakeItems is { } Current
            ? new List<FakeItem>(Current)
            : new List<FakeItem>();
        HiddenItems = Manager && Manager.fakeItemData.hiddenItems is { } Hidden
            ? new List<int>(Hidden)
            : new List<int>();
        HighWater = Map.CurrentRun!.Network.ViewBatches.FakeItemIdHighWater;
        var Seen = new HashSet<int>();
        foreach (var Item in Items) Capture(Item, Seen);
        foreach (var Item in Handler.GetComponentsInChildren<FakeItem>(true)) Capture(Item, Seen);
    }

    void Capture(FakeItem Item, HashSet<int> Seen)
    {
        if (Item && Seen.Add(Item.GetInstanceID()))
            States.Add((Item, Item.index, Item.pickedUp, Item.gameObject.activeSelf));
    }

    internal void Restore()
    {
        if (!Manager) return;
        Manager.allFakeItems = new List<FakeItem>(Items);
        Manager.fakeItemData = new FakeItemManager.FakeItemData { hiddenItems = new List<int>(HiddenItems) };
        foreach (var State in States)
        {
            if (!State.Item) continue;
            State.Item.index = State.Index;
            State.Item.pickedUp = State.PickedUp;
            State.Item.gameObject.SetActive(State.Active);
        }
        Map.CurrentRun!.Network.ViewBatches.RestoreFakeItemHighWater(HighWater);
    }
}
