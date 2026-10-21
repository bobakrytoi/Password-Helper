using System;
using System.Linq;
using PasswordHelper.Core;
using PasswordHelper.Passwords;
using Xunit;

namespace PasswordHelper.Tests;

public class PasswordGeneratorTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(128)]
    public void Generate_ProducesExactLength(int length)
    {
        var policy = new PasswordPolicy(length, true, true, true, true, false, PasswordGenerator.DefaultSymbols);
        char[] password = PasswordGenerator.Generate(policy);
        try
        {
            Assert.Equal(length, password.Length);
        }
        finally
        {
            Sensitive.Clear(password);
        }
    }

    [Fact]
    public void Generate_DefaultPolicy_ContainsEveryCharacterClass()
    {
        // Run many times: every password must contain all four enabled classes (guaranteed, not probabilistic).
        for (int i = 0; i < 2000; i++)
        {
            char[] password = PasswordGenerator.Generate(PasswordPolicy.Default);
            try
            {
                Assert.Contains(password, char.IsUpper);
                Assert.Contains(password, char.IsLower);
                Assert.Contains(password, char.IsDigit);
                Assert.Contains(password, c => PasswordGenerator.DefaultSymbols.Contains(c));
            }
            finally
            {
                Sensitive.Clear(password);
            }
        }
    }

    [Fact]
    public void Generate_OnlyUsesAllowedCharacters()
    {
        var policy = new PasswordPolicy(40, true, false, true, false, false, PasswordGenerator.DefaultSymbols);
        for (int i = 0; i < 500; i++)
        {
            char[] password = PasswordGenerator.Generate(policy);
            try
            {
                Assert.All(password, c => Assert.True(char.IsUpper(c) || char.IsDigit(c)));
            }
            finally
            {
                Sensitive.Clear(password);
            }
        }
    }

    [Fact]
    public void Generate_ExcludeAmbiguous_OmitsConfusableCharacters()
    {
        var policy = new PasswordPolicy(60, true, true, true, false, true, PasswordGenerator.DefaultSymbols);
        for (int i = 0; i < 500; i++)
        {
            char[] password = PasswordGenerator.Generate(policy);
            try
            {
                Assert.DoesNotContain(password, c => PasswordGenerator.AmbiguousChars.Contains(c));
            }
            finally
            {
                Sensitive.Clear(password);
            }
        }
    }

    [Fact]
    public void Generate_ProducesDifferentPasswordsEachTime()
    {
        string First() { char[] p = PasswordGenerator.Generate(PasswordPolicy.Default); try { return new string(p); } finally { Sensitive.Clear(p); } }
        string a = First();
        string b = First();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Validate_RejectsNoCharacterClasses()
    {
        var policy = new PasswordPolicy(16, false, false, false, false, false, "");
        Assert.NotNull(PasswordGenerator.Validate(policy));
    }

    [Fact]
    public void Validate_RejectsLengthTooShortForClasses()
    {
        var policy = new PasswordPolicy(3, true, true, true, true, false, PasswordGenerator.DefaultSymbols);
        Assert.NotNull(PasswordGenerator.Validate(policy));
    }

    [Fact]
    public void SanitizeSymbols_KeepsOnlyDistinctPrintableSymbols()
    {
        string result = PasswordGenerator.SanitizeSymbols("!!@@ aZ9#");
        Assert.Equal("!@#", result);
    }

    [Fact]
    public void EstimateEntropy_IncreasesWithLength()
    {
        double short32 = PasswordGenerator.EstimateEntropyBits(PasswordPolicy.Default);
        var longer = PasswordPolicy.Default with { Length = 64 };
        Assert.True(PasswordGenerator.EstimateEntropyBits(longer) > short32);
        Assert.True(short32 > 180); // 32 chars over ~87-char alphabet
    }

    [Theory]
    [InlineData("")]
    [InlineData("password")]
    [InlineData("Str0ng!Passw0rd#2026Longer")]
    public void StrengthEstimator_DoesNotThrowAndRanks(string value)
    {
        PasswordStrength strength = PasswordStrengthEstimator.Evaluate(value);
        Assert.InRange(strength.Fraction, 0, 1);
        if (value.Length == 0) Assert.Equal(StrengthLevel.Empty, strength.Level);
    }
}
