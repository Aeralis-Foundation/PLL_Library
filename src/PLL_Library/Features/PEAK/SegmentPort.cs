using UnityEngine;

namespace PLL.Library;

public enum SegmentPortKind
{
    Entry,
    Exit,
    Start
}

[DisallowMultipleComponent]
public sealed class SegmentPort : MonoBehaviour
{
    [SerializeField] SegmentPortKind kind = SegmentPortKind.Entry;
    [SerializeField] string connector = "";
    bool registered;

    public SegmentPortKind Kind => kind;
    public string Connector => connector;

    internal bool Registered => registered;
    internal void Freeze() => registered = true;
    internal void Unfreeze() => registered = false;
}
