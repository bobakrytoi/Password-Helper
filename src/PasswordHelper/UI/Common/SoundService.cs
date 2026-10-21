using System;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PasswordHelper.Core;

namespace PasswordHelper.UI.Common;

/// <summary>
/// Plays a short click sound on button presses across the whole application. A small pool of pre-opened
/// <see cref="MediaPlayer"/> instances is cycled so rapid clicks can overlap without re-opening the file.
/// Honours the user's "button click sound" setting and volume. If the sound file is missing or cannot be
/// opened, the service silently disables itself.
/// </summary>
internal sealed class SoundService : IDisposable
{
    private const int PoolSize = 4;

    private readonly SettingsService _settings;
    private readonly MediaPlayer[] _players = new MediaPlayer[PoolSize];
    private int _next;
    private bool _available;

    public SoundService(SettingsService settings)
    {
        _settings = settings;
        TryInitialize();
    }

    private void TryInitialize()
    {
        string? path = ResolveSoundFile();
        if (path is null)
        {
            Log.Warn("Click sound asset not found; button sounds are disabled.");
            return;
        }

        try
        {
            var uri = new Uri(path, UriKind.Absolute);
            for (int i = 0; i < PoolSize; i++)
            {
                var player = new MediaPlayer { Volume = _settings.Current.ClickSoundVolume };
                player.Open(uri);
                _players[i] = player;
            }

            _available = true;
        }
        catch (Exception ex)
        {
            Log.Warn("Button click sound could not be initialised", ex);
            _available = false;
        }
    }

    /// <summary>
    /// Returns a playable file path for the click sound. Prefers the embedded copy (extracted once to a stable temp
    /// file so a single-file published .exe still has sound), and falls back to a loose Assets\click.mp3 if present.
    /// </summary>
    private static string? ResolveSoundFile()
    {
        try
        {
            using Stream? stream = typeof(SoundService).Assembly.GetManifestResourceStream("click.mp3");
            if (stream is not null)
            {
                string dir = Path.Combine(Path.GetTempPath(), "PasswordHelper");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "click.mp3");
                if (!File.Exists(file) || new FileInfo(file).Length != stream.Length)
                {
                    using FileStream fs = File.Create(file);
                    stream.CopyTo(fs);
                }

                return file;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not extract the embedded click sound", ex);
        }

        string onDisk = Path.Combine(AppContext.BaseDirectory, "Assets", "click.mp3");
        return File.Exists(onDisk) ? onDisk : null;
    }

    /// <summary>Registers a class handler so every button Click in the app plays the sound (except RepeatButtons).</summary>
    public void InstallGlobalHandler()
    {
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnAnyButtonClick));
    }

    private void OnAnyButtonClick(object sender, RoutedEventArgs e)
    {
        // RepeatButtons (scrollbar arrows, slider track) fire Click continuously; never click-sound those.
        if (sender is RepeatButton) return;
        PlayClick();
    }

    public void PlayClick()
    {
        if (!_available || !_settings.Current.ClickSoundEnabled) return;

        try
        {
            MediaPlayer player = _players[_next];
            _next = (_next + 1) % PoolSize;
            player.Volume = _settings.Current.ClickSoundVolume;
            player.Position = TimeSpan.Zero;
            player.Play();
        }
        catch (Exception ex)
        {
            Log.Warn("Playing the click sound failed", ex);
        }
    }

    public void Dispose()
    {
        foreach (MediaPlayer? player in _players)
        {
            try { player?.Close(); } catch { /* ignore */ }
        }

        _available = false;
    }
}
