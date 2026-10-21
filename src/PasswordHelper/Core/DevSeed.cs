#if DEBUG
using System;
using System.Threading.Tasks;
using System.Windows;
using PasswordHelper.Vault;

namespace PasswordHelper.Core;

/// <summary>DEBUG-only helper that seeds a few demo entries so the overlay can be exercised during development.</summary>
internal static class DevSeed
{
    public static async Task RunThenExitAsync(Application app)
    {
        try
        {
            using var vault = new VaultService(AppPaths.VaultFile);
            await vault.LoadAsync();

            await Add(vault, "GitHub", "octocat", "Gh!7xQ2#mLpZ", "github.com", pinned: true);
            await Add(vault, "Steam", "player_one", "St3@m-Vault-99", "store.steampowered.com", pinned: true);
            await Add(vault, "Email", "me@example.com", "C0rrect-H0rse-Battery", "mail.example.com", pinned: false);
            await Add(vault, "Work Account", "e.turing", "Ac#9241-work-Pw", "", pinned: false);
            await Add(vault, "Discord", "nebula", "d1sc0rd!Secret42", "discord.com", pinned: false);

            Log.Info("Demo entries seeded.");
        }
        catch (Exception ex)
        {
            Log.Error("Seeding demo entries failed", ex);
        }
        finally
        {
            app.Shutdown();
        }
    }

    private static async Task Add(VaultService vault, string title, string user, string password, string url, bool pinned)
    {
        using var draft = new EntryDraft
        {
            Title = title,
            Username = user,
            Password = password.ToCharArray(),
            Url = url,
            Pinned = pinned,
        };
        await vault.AddAsync(draft);
    }
}
#endif
