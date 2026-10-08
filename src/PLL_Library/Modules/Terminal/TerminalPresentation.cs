using System;
using System.IO;
using UnityEngine;

namespace PLL.Library;

internal static class TerminalPalette
{
    internal static readonly Color NoteDark = new(0.74509805f, 0.17647059f, 0.49019608f, 1f);
    internal static readonly Color NoteLight = new(1f, 0.41176471f, 0.70588235f, 1f);
    internal static readonly Color PinkShadow = new(0.29411766f, 0.05490196f, 0.18431373f, 1f);
}

internal static class TerminalPresentation
{
    internal static void CopyRect(RectTransform Target, RectTransform Source)
    {
        Target.anchorMin = Source.anchorMin;
        Target.anchorMax = Source.anchorMax;
        Target.pivot = Source.pivot;
        Target.anchoredPosition = Source.anchoredPosition;
        Target.sizeDelta = Source.sizeDelta;
        Target.localRotation = Source.localRotation;
        Target.localScale = Source.localScale;
    }

    internal static OwnedTerminalImage LoadEmbeddedImage(
        Type ResourceOwner, string ResourceName, string AssetName,
        Func<string, Exception> MissingResourceFailure)
    {
        using var Stream = ResourceOwner.Assembly.GetManifestResourceStream(ResourceName)
            ?? throw MissingResourceFailure($"Embedded resource '{ResourceName}' is missing.");
        using var Reader = new BinaryReader(Stream);
        var Bytes = Reader.ReadBytes(checked((int)Stream.Length));
        if (Bytes.Length != Stream.Length)
            throw new EndOfStreamException($"Embedded resource '{ResourceName}' ended early.");

        var Texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = AssetName };
        if (!ImageConversion.LoadImage(Texture, Bytes, true))
        {
            UnityEngine.Object.Destroy(Texture);
            throw new InvalidDataException($"Embedded resource '{ResourceName}' is not a valid image.");
        }

        Sprite? CreatedSprite = null;
        var Created = false;
        try
        {
            CreatedSprite = Sprite.Create(
                Texture,
                new Rect(0f, 0f, Texture.width, Texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            CreatedSprite.name = AssetName;
            var Image = new OwnedTerminalImage(Texture, CreatedSprite);
            Created = true;
            return Image;
        }
        finally
        {
            if (!Created)
            {
                if (CreatedSprite) UnityEngine.Object.Destroy(CreatedSprite);
                UnityEngine.Object.Destroy(Texture);
            }
        }
    }
}

internal sealed class OwnedTerminalImage
{
    bool Released;

    internal Texture2D Texture { get; }
    internal Sprite Sprite { get; }

    internal OwnedTerminalImage(Texture2D Texture, Sprite Sprite)
    {
        this.Texture = Texture;
        this.Sprite = Sprite;
    }

    internal void Release()
    {
        if (Released) return;
        Released = true;
        if (Sprite) UnityEngine.Object.Destroy(Sprite);
        if (Texture) UnityEngine.Object.Destroy(Texture);
    }
}
