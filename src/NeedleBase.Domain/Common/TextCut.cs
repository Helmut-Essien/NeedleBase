namespace NeedleBase.Domain.Common;

/// <summary>Shortens text without splitting an emoji (a UTF-16 surrogate pair), which would make invalid text.</summary>
public static class TextCut
{
    public static string Max(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        var keep = max;
        if (keep > 0 && char.IsHighSurrogate(value[keep - 1]))
        {
            keep--;
        }

        return value[..keep];
    }
}
