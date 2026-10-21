using System;

namespace PasswordHelper.Passwords;

internal enum StrengthLevel
{
    Empty,
    Weak,
    Fair,
    Good,
    Strong,
}

internal readonly record struct PasswordStrength(StrengthLevel Level, double Bits, string Label)
{
    /// <summary>A 0..1 fraction for a strength bar (saturates around 100 bits).</summary>
    public double Fraction => Math.Clamp(Bits / 100.0, 0, 1);
}

/// <summary>
/// A lightweight, local strength estimate based on length and the variety of character classes present. It is a
/// rough guide for the UI only (no dictionary checks, no network), computed over a span so no string copy is made.
/// </summary>
internal static class PasswordStrengthEstimator
{
    public static PasswordStrength Evaluate(ReadOnlySpan<char> password)
    {
        if (password.Length == 0)
        {
            return new PasswordStrength(StrengthLevel.Empty, 0, string.Empty);
        }

        bool lower = false, upper = false, digit = false, symbol = false;
        foreach (char c in password)
        {
            if (c is >= 'a' and <= 'z') lower = true;
            else if (c is >= 'A' and <= 'Z') upper = true;
            else if (c is >= '0' and <= '9') digit = true;
            else symbol = true;
        }

        int alphabet = (lower ? 26 : 0) + (upper ? 26 : 0) + (digit ? 10 : 0) + (symbol ? 32 : 0);
        double bits = alphabet <= 1 ? 0 : password.Length * Math.Log2(alphabet);

        // Penalise very short passwords regardless of alphabet.
        if (password.Length < 8) bits = Math.Min(bits, 34);

        StrengthLevel level = bits switch
        {
            < 40 => StrengthLevel.Weak,
            < 60 => StrengthLevel.Fair,
            < 80 => StrengthLevel.Good,
            _ => StrengthLevel.Strong,
        };

        string label = level switch
        {
            StrengthLevel.Weak => "Weak",
            StrengthLevel.Fair => "Fair",
            StrengthLevel.Good => "Good",
            StrengthLevel.Strong => "Strong",
            _ => string.Empty,
        };

        return new PasswordStrength(level, bits, label);
    }
}
