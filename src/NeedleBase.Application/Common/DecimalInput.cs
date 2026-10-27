using System.Globalization;

namespace NeedleBase.Application.Common;

/// <summary>
/// One parser for typed amounts (prices, measurements), independent of the device locale.
/// Accepts "92.5" and "92,5". Rejects thousands separators and anything ambiguous: "1,000" could be
/// one thousand or one point zero, so it is refused with a hint rather than guessed.
/// </summary>
public static class DecimalInput
{
    public const string Hint = "Use digits with one '.' or ',' for decimals, without thousands separators.";

    /// <summary>Most decimal places kept for a measurement (eighths of an inch need 3: 4.125; 4 leaves room).</summary>
    public const int MeasurementDecimals = 4;

    public const string MeasurementHint = "Use digits with one '.' or ',' for decimals (up to 4 decimals, e.g. 4.125).";

    /// <summary>
    /// Measurements (cm or inches): like <see cref="TryParse"/> but "4.125" / "92,500" are fine. A measurement
    /// is capped at a few hundred, so a separator followed by three digits can't be a thousands group
    /// (the price rule in <see cref="TryParse"/> exists for amounts such as "1.000"). At most
    /// <see cref="MeasurementDecimals"/> decimals; "12." (unfinished) and thousands groups with 2 separators are refused.
    /// </summary>
    public static bool TryParseMeasurement(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        var separatorAt = -1;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch is '.' or ',')
            {
                if (separatorAt >= 0)
                {
                    return false;
                }

                separatorAt = i;
            }
            else if (ch is < '0' or > '9' && !(i == 0 && ch == '-'))
            {
                return false;
            }
        }

        var digitsAfter = separatorAt < 0 ? 0 : s.Length - separatorAt - 1;
        var digitsBefore = (separatorAt < 0 ? s : s[..separatorAt]).TrimStart('-').Length;
        if ((digitsBefore == 0 && digitsAfter == 0) || (separatorAt >= 0 && digitsAfter == 0) || digitsAfter > MeasurementDecimals)
        {
            return false;
        }

        var normalised = separatorAt < 0 ? s : s[..separatorAt] + "." + s[(separatorAt + 1)..];
        return decimal.TryParse(normalised, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        var separators = 0;
        var separatorAt = -1;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch is '.' or ',')
            {
                separators++;
                separatorAt = i;
            }
            else if (ch is < '0' or > '9' && !(i == 0 && ch == '-'))
            {
                return false;
            }
        }

        if (separators > 1)
        {
            return false;
        }

        var digitsBefore = separatorAt < 0 ? s.TrimStart('-').Length : s[..separatorAt].TrimStart('-').Length;
        var digitsAfter = separatorAt < 0 ? 0 : s.Length - separatorAt - 1;
        if (digitsBefore == 0 && digitsAfter == 0)
        {
            return false;
        }

        // "1,000" / "12.500": a separator followed by exactly three digits reads like a thousands separator in
        // one locale or the other, so it is refused (a price typed as "1.000" must not become GH₵ 1). "0.125"
        // can't be a thousands group and stays allowed.
        var integerPart = separatorAt < 0 ? string.Empty : s[..separatorAt].TrimStart('-');
        if (separatorAt >= 0 && digitsAfter == 3 && integerPart.TrimStart('0').Length > 0)
        {
            return false;
        }

        if (separatorAt >= 0 && digitsAfter == 0)
        {
            return false; // "12." / "12," is unfinished input
        }

        var normalised = separatorAt < 0 ? s : s[..separatorAt] + "." + s[(separatorAt + 1)..];
        return decimal.TryParse(normalised, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Invariant text for an amount ("92.5"), so it round-trips through <see cref="TryParse"/>.</summary>
    public static string Format(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);
}
