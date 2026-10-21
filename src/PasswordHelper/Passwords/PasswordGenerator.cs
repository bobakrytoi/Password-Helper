using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PasswordHelper.Core;

namespace PasswordHelper.Passwords;

/// <summary>Which characters a generated password may contain and how long it is.</summary>
internal sealed record PasswordPolicy(
    int Length,
    bool Uppercase,
    bool Lowercase,
    bool Digits,
    bool Symbols,
    bool ExcludeAmbiguous,
    string SymbolSet)
{
    public static PasswordPolicy Default { get; } =
        new(32, true, true, true, true, false, PasswordGenerator.DefaultSymbols);
}

/// <summary>
/// Cryptographically secure password generator.
/// <list type="bullet">
/// <item>Randomness comes from the OS CSPRNG via <see cref="RandomNumberGenerator"/>; <c>System.Random</c> is never used.</item>
/// <item>Characters are drawn with <see cref="RandomNumberGenerator.GetItems{T}(ReadOnlySpan{T}, Span{T})"/>, which
/// uses rejection sampling internally, so there is no modulo bias.</item>
/// <item>Every enabled character class is guaranteed to appear. Candidates missing a class are discarded and
/// redrawn as a whole (rejection sampling), which keeps the result uniform over all valid passwords instead of
/// biasing positions the way "insert one of each class" approaches do.</item>
/// </list>
/// </summary>
internal static class PasswordGenerator
{
    public const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    public const string DigitChars = "0123456789";

    /// <summary>
    /// Printable symbols accepted by virtually all password fields. Deliberately excludes whitespace, quotes,
    /// backslash, backtick, pipe and angle brackets, which some sites reject or mangle.
    /// </summary>
    public const string DefaultSymbols = "!@#$%^&*()-_=+[]{};:,.?";

    /// <summary>Characters that are easily confused with one another (I/l/1/|, O/0/o).</summary>
    public const string AmbiguousChars = "Il1|O0o";

    public const int MinLength = 4;
    public const int MaxLength = 256;
    private const int MaxAttempts = 100_000;

    /// <summary>Keeps only distinct printable ASCII symbols (no letters, digits or whitespace).</summary>
    public static string SanitizeSymbols(string? symbols)
    {
        if (string.IsNullOrEmpty(symbols)) return string.Empty;

        var sb = new StringBuilder(symbols.Length);
        foreach (char c in symbols)
        {
            bool printableSymbol = c is > ' ' and < '\u007f' && !char.IsLetterOrDigit(c);
            if (printableSymbol && sb.ToString().IndexOf(c) < 0)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>The enabled character classes after applying the ambiguity filter.</summary>
    public static IReadOnlyList<string> GetCharacterClasses(PasswordPolicy policy)
    {
        var classes = new List<string>(4);
        void Add(bool enabled, string chars)
        {
            if (!enabled) return;
            string filtered = policy.ExcludeAmbiguous
                ? new string(chars.Where(c => AmbiguousChars.IndexOf(c) < 0).ToArray())
                : chars;
            if (filtered.Length > 0) classes.Add(filtered);
        }

        Add(policy.Uppercase, UppercaseChars);
        Add(policy.Lowercase, LowercaseChars);
        Add(policy.Digits, DigitChars);
        Add(policy.Symbols, SanitizeSymbols(policy.SymbolSet));
        return classes;
    }

    /// <summary>Returns a user-facing problem description, or null when the policy can be satisfied.</summary>
    public static string? Validate(PasswordPolicy policy)
    {
        if (policy.Length < MinLength || policy.Length > MaxLength)
        {
            return $"Password length must be between {MinLength} and {MaxLength} characters.";
        }

        IReadOnlyList<string> classes = GetCharacterClasses(policy);
        if (classes.Count == 0)
        {
            return "Enable at least one character group.";
        }

        if (policy.Length < classes.Count)
        {
            return "The password is too short to contain every enabled character group.";
        }

        return null;
    }

    /// <summary>
    /// Generates a password into a pinned buffer of exactly <see cref="PasswordPolicy.Length"/> characters.
    /// The caller owns the buffer and must wipe it with <see cref="Sensitive.Clear(char[])"/>.
    /// </summary>
    public static char[] Generate(PasswordPolicy policy)
    {
        string? problem = Validate(policy);
        if (problem is not null) throw new ArgumentException(problem, nameof(policy));

        IReadOnlyList<string> classes = GetCharacterClasses(policy);
        string alphabet = string.Concat(classes);
        char[] result = Sensitive.AllocChars(policy.Length);

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            RandomNumberGenerator.GetItems<char>(alphabet, result);
            if (ContainsEveryClass(result, classes))
            {
                return result;
            }
        }

        Sensitive.Clear(result);
        throw new InvalidOperationException("Could not generate a password that satisfies the policy.");
    }

    /// <summary>Entropy of a password drawn uniformly from the policy's alphabet, in bits.</summary>
    public static double EstimateEntropyBits(PasswordPolicy policy)
    {
        int alphabetSize = GetCharacterClasses(policy).Sum(c => c.Length);
        return alphabetSize <= 1 ? 0 : policy.Length * Math.Log2(alphabetSize);
    }

    private static bool ContainsEveryClass(ReadOnlySpan<char> password, IReadOnlyList<string> classes)
    {
        foreach (string characterClass in classes)
        {
            if (password.IndexOfAny(characterClass) < 0)
            {
                return false;
            }
        }

        return true;
    }
}
