using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PasswordHelper.Core;
using PasswordHelper.Passwords;
using PasswordHelper.UI.Common;
using PasswordHelper.Vault;

namespace PasswordHelper.UI.Editor;

/// <summary>
/// The compact add/edit form. Produces an <see cref="EntryDraft"/> the caller saves. Passwords are read through the
/// PasswordBox's SecureString into a pinned buffer; the only managed-string exposure is the transient reveal/prefill,
/// which is unavoidable with WPF's password controls.
/// </summary>
public partial class EntryEditorWindow : ChromeWindow
{
    private readonly AppSettings _settings;
    private readonly VaultEntry? _existing;
    private bool _revealed;
    private bool _passwordDirty;
    private bool _syncing;

    internal EntryEditorWindow(AppSettings settings, VaultEntry? existing)
    {
        _settings = settings;
        _existing = existing;
        InitializeComponent();

        if (existing is not null)
        {
            Title = "Edit password";
            SaveButton.Content = "Save changes";
            TitleBox.Text = existing.Title;
            UsernameBox.Text = existing.Username;
            UrlBox.Text = existing.Url;
            NotesBox.Text = existing.Notes;
            PinCheck.IsChecked = existing.Pinned;
            PrefillExistingPassword(existing);
        }

        PasswordBox.PasswordChanged += OnPasswordChanged;
        PasswordText.TextChanged += OnPasswordTextChanged;
        RevealButton.Click += (_, _) => ToggleReveal();
        GenerateButton.Click += (_, _) => GenerateIntoForm();
        SaveButton.Click += (_, _) => OnSave();
        CancelButton.Click += (_, _) => { DialogResult = false; };
        Closed += (_, _) => Cleanup();

        Loaded += (_, _) => TitleBox.Focus();
        UpdateStrengthFromCurrent();
    }

    /// <summary>The captured draft after a successful save, or null if cancelled. Caller owns and disposes it.</summary>
    internal EntryDraft? Result { get; private set; }

    /// <summary>True when the password should be (re)written. Always true when adding; in edit mode, only if changed.</summary>
    internal bool ReplacePassword { get; private set; }

    private void PrefillExistingPassword(VaultEntry existing)
    {
        char[] chars = existing.Password.Reveal();
        try
        {
            _syncing = true;
            PasswordBox.Password = new string(chars); // transient; PasswordBox stores it as a SecureString
        }
        finally
        {
            Sensitive.Clear(chars);
            _syncing = false;
        }

        _passwordDirty = false;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _passwordDirty = true;
        UpdateStrengthFromSecure();
    }

    private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _passwordDirty = true;
        UpdateStrength(PasswordText.Text.AsSpan());
    }

    private void ToggleReveal()
    {
        _syncing = true;
        try
        {
            if (!_revealed)
            {
                char[] chars = ReadSecure(PasswordBox.SecurePassword);
                try { PasswordText.Text = new string(chars); } finally { Sensitive.Clear(chars); }
                PasswordText.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
                RevealGlyph.Data = Icons.Get(Icons.EyeOff);
                RevealButton.ToolTip = "Hide password";
                PasswordText.CaretIndex = PasswordText.Text.Length;
                PasswordText.Focus();
            }
            else
            {
                PasswordBox.Password = PasswordText.Text;
                PasswordText.Clear();
                PasswordText.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                RevealGlyph.Data = Icons.Get(Icons.Eye);
                RevealButton.ToolTip = "Show password";
                PasswordBox.Focus();
            }

            _revealed = !_revealed;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void GenerateIntoForm()
    {
        PasswordPolicy policy = _settings.ToPasswordPolicy();
        if (PasswordGenerator.Validate(policy) is not null)
        {
            policy = PasswordPolicy.Default;
        }

        char[] generated = PasswordGenerator.Generate(policy);
        try
        {
            SetPassword(generated);
        }
        finally
        {
            Sensitive.Clear(generated);
        }
    }

    private void SetPassword(ReadOnlySpan<char> value)
    {
        _syncing = true;
        try
        {
            if (_revealed)
            {
                PasswordText.Text = new string(value);
            }
            else
            {
                PasswordBox.Password = new string(value);
            }
        }
        finally
        {
            _syncing = false;
        }

        _passwordDirty = true;
        UpdateStrength(value);
    }

    private void UpdateStrengthFromCurrent()
    {
        if (_revealed) UpdateStrength(PasswordText.Text.AsSpan());
        else UpdateStrengthFromSecure();
    }

    private void UpdateStrengthFromSecure()
    {
        char[] chars = ReadSecure(PasswordBox.SecurePassword);
        try { UpdateStrength(chars); } finally { Sensitive.Clear(chars); }
    }

    private void UpdateStrength(ReadOnlySpan<char> password)
    {
        PasswordStrength strength = PasswordStrengthEstimator.Evaluate(password);
        StrengthLabel.Text = strength.Label;

        double maxWidth = 220;
        StrengthBar.Width = strength.Fraction * maxWidth;
        Color color = strength.Level switch
        {
            StrengthLevel.Weak => Color.FromRgb(0xE5, 0x79, 0x6F),
            StrengthLevel.Fair => Color.FromRgb(0xE3, 0xC3, 0x6B),
            StrengthLevel.Good => Color.FromRgb(0xA9, 0xCE, 0x8F),
            StrengthLevel.Strong => Color.FromRgb(0x8F, 0xD3, 0x9A),
            _ => Color.FromRgb(0x5A, 0x64, 0x75),
        };
        StrengthBar.Background = new SolidColorBrush(color);
    }

    private void OnSave()
    {
        string title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            ShowError("Enter a title for this entry.");
            TitleBox.Focus();
            return;
        }

        bool replace = _existing is null || _passwordDirty;
        char[] password = replace ? GetPassword() : Array.Empty<char>();
        try
        {
            if (replace && password.Length == 0)
            {
                ShowError("Enter or generate a password.");
                return;
            }

            var draft = new EntryDraft
            {
                Title = title,
                Username = UsernameBox.Text.Trim(),
                Url = UrlBox.Text.Trim(),
                Notes = NotesBox.Text,
                Pinned = PinCheck.IsChecked == true,
                Password = Sensitive.Copy(password),
            };

            Result = draft;
            ReplacePassword = replace;
            DialogResult = true;
        }
        finally
        {
            Sensitive.Clear(password);
        }
    }

    private char[] GetPassword() =>
        _revealed ? Sensitive.Copy(PasswordText.Text.AsSpan()) : ReadSecure(PasswordBox.SecurePassword);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static char[] ReadSecure(SecureString secure)
    {
        if (secure.Length == 0) return Array.Empty<char>();
        IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try
        {
            char[] buffer = Sensitive.AllocChars(secure.Length);
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (char)Marshal.ReadInt16(ptr, i * 2);
            }

            return buffer;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    private void Cleanup()
    {
        _syncing = true;
        PasswordText.Clear();
        PasswordBox.Clear();
    }
}
