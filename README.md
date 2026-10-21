# Password helper

A lightweight Windows desktop utility that lives in the system tray and, on a global hotkey, opens a **circular
radial overlay** for generating secure passwords, picking saved passwords, and re-using recent clipboard entries —
copying each one and pasting it straight back into the app you were working in.

No main window, no browser, no cloud. A native **C# / .NET 10 + WPF** application with a premium dark glassmorphism
interface.

---

## Highlights

- **Radial overlay** over the current desktop context, with a frosted-glass backdrop (a genuine blur of the real
  desktop behind it) and a central **X / Esc** close button.
- **Cryptographically secure password generator** — 32 characters by default, every enabled character class
  guaranteed, no modulo bias (`RandomNumberGenerator`, never `System.Random`).
- **Encrypted vault** for saved passwords, protected with **Windows DPAPI (current-user scope)**. Another account or
  another PC cannot read it.
- **Clipboard history** — event-driven (no polling), in-memory only, never written to disk.
- **Intelligent insertion** — restores the previously active window and sends a paste, with strict validation so text
  never lands in the wrong window; falls back to "copied — press Ctrl+V" when it can't be done safely.
- **System-tray only** — tray menu + a single compact Settings window. No dashboard, no welcome screen.
- **Button click sound**, staggered open/close animations, reduced-motion support, and full keyboard navigation.

---

## Requirements

| | |
|---|---|
| OS | Windows 10 version 1809 (build 17763) or later, and Windows 11 |
| Runtime | **.NET 10 Desktop Runtime** (for the framework-dependent build) — or use the self-contained build, which needs nothing installed |
| Build | **.NET 10 SDK** (`dotnet --version` ≥ 10.0) |
| Privileges | Standard user. Administrator rights are **not** required. |

> **Tested on:** Windows 11 (build 26200), .NET 10.0.13, dual-monitor, 100% DPI. It has **not** yet been verified on
> Windows 10 or under per-monitor high-DPI scaling; the code targets and handles both, but those environments have not
> been exercised. See *Testing status* below.

---

## Quick start

```powershell
# from the repository root
.\build.ps1 build        # compile (Debug)
.\build.ps1 test         # run the unit tests
.\build.ps1 run          # build and launch (appears in the system tray)
```

Press **Ctrl + Shift + P** to open the overlay. Right-click the tray icon for **Settings**.

### Plain `dotnet` commands

```powershell
dotnet build  src\PasswordHelper\PasswordHelper.csproj -c Release
dotnet test   tests\PasswordHelper.Tests\PasswordHelper.Tests.csproj
dotnet run    --project src\PasswordHelper\PasswordHelper.csproj
```

### Producing a release / installer-free package

```powershell
# Framework-dependent (small; needs the .NET 10 Desktop Runtime on the target PC)
.\build.ps1 publish

# Self-contained single .exe (larger, ~150 MB; no runtime needed on the target PC)
.\build.ps1 publish -SelfContained
```

The result is written to `.\dist\PasswordHelper.exe`. Copy that file (plus the `Assets` folder it brings along) to run
it portably, or place a shortcut to it in `shell:startup`. (The in-app **Launch at Windows startup** toggle does this
for you via the per-user `Run` registry key.)

---

## The global hotkey — a deliberate design decision

The specification's default was the **modifier-only** chord `Ctrl + Shift`. Windows' standard global-hotkey API
(`RegisterHotKey`) **cannot** express a shortcut made only of modifier keys, and the only way to detect one is a
**system-wide low-level keyboard hook** that observes *every* keystroke — the same primitive a keylogger uses.

This build therefore uses **`RegisterHotKey` exclusively**, with a default of **`Ctrl + Shift + P`**:

- Windows notifies the app **only** when the one registered combination is pressed. The application **never observes
  any other keystroke** — there is no global keyboard monitoring of any kind.
- `MOD_NOREPEAT` guarantees a single press fires exactly once, even when held.
- The shortcut is fully configurable in **Settings → Shortcut** (record any Ctrl/Alt/Win + key combination). Invalid,
  reserved, or already-in-use combinations are rejected with a clear message and never silently overwrite the working
  one.

Trade-off: the literal modifier-only `Ctrl + Shift` default is not available. This is the more privacy-respecting
design and is explicitly permitted by the spec ("Allow the user to configure a conventional combination such as
`Ctrl + Shift + P` if the modifier-only combination is unreliable").

---

## How it works (architecture)

The code is organized into focused modules; all native calls live in a single auditable interop file.

```
src/PasswordHelper/
  Interop/Win32.cs            All P/Invoke declarations, in one place.
  Core/                       Paths, logging, settings, single-instance, startup registration,
                              sensitive-memory helpers, the hidden message-hub window, screen/DPI utils.
  Passwords/                  Secure generator (rejection sampling, no modulo bias) + strength estimate.
  Vault/                      DPAPI wrapper, in-memory CryptProtectMemory secrets, the on-disk format
                              (magic + version + SHA-256 + DPAPI payload), and the vault service with
                              atomic writes, backups and corruption recovery.
  Input/                      Hotkey gesture model + RegisterHotKey-based HotkeyManager.
  ClipboardServices/          Low-level clipboard I/O, the stateful manager (owner-token tracking,
                              auto-clear) and the event-driven history service.
  Insertion/                  Foreground-window capture + safe restore-and-paste with validation.
  UI/
    Theme/                    Colors, control styles, window chrome (dark glassmorphism).
    Common/                   Click-sound service, screen-capture blur, icons, chrome window base.
    Tray/                     Shell_NotifyIcon tray icon + themed context menu.
    Overlay/                  Radial layout math, the overlay window, panels, and the action controller.
    Toast/                    Non-activating confirmation toasts.
    Editor/ Dialogs/ Settings/ Add-edit form, confirm/details/recovery dialogs, tabbed settings.
  AppHost.cs                  Wires everything together; owns the application lifecycle.
```

**Lifecycle.** `App` parses the command line, enforces a single instance per data directory (a second launch signals
the running one to show the overlay / open settings / exit), then `AppHost` creates the tray icon, registers the
hotkey, starts the clipboard listener, loads the vault in the background, and registers for session-lock and
power-resume notifications. There is no main window.

---

## Security model

- **Vault at rest:** AES-less by design — Windows **DPAPI** (`CryptProtectData`, current-user scope) encrypts a
  UTF-8 JSON payload. The file carries a magic header, a format version, and a **SHA-256 integrity hash** that is
  verified *before* decryption. Writes are **atomic** (`ReplaceFile`) and keep a `.bak`; an interrupted save is
  recovered on next load. **An unreadable vault is never silently emptied** — the user is offered *restore backup*,
  *start new* (moving the bad file aside), or *leave it*.
- **Secrets in memory:** saved passwords are held encrypted in-process with `CryptProtectMemory` and decrypted only
  for the moment they are used, into **pinned** buffers that are zeroed afterwards. Generated passwords and vault
  plaintext use pinned, wiped buffers and avoid managed-string copies where practical. Managed strings are immutable
  and cannot be reliably zeroed — the app does not claim perfect erasure.
- **Clipboard hygiene:** copied passwords are flagged to stay out of **Windows Clipboard History (Win+V)** and
  **Cloud Clipboard** sync. Optional auto-clear only empties the clipboard **if it still holds the exact value the
  app placed there** (tracked by a random owner-token) — it never erases something another app copied afterwards.
  Clipboard history is in-memory only and ignores known password-manager processes by default.
- **Insertion safety:** the target window is captured *before* the overlay appears and re-validated immediately
  before input is sent (same handle, same process, not hung, not higher-integrity). It never pastes into a window
  merely because it became active after the overlay opened, and falls back to copy-only when anything is off.
- **Logging:** a small rolling log records errors and fixed status messages only. Passwords, usernames, notes,
  clipboard contents, vault data and key material are **never** logged.

---

## Data & file locations

Everything is stored under `%LOCALAPPDATA%\PasswordHelper\`:

| File | Contents |
|---|---|
| `vault.dat` (+ `vault.dat.bak`) | DPAPI-encrypted saved passwords |
| `settings.json` | Preferences (no secrets) |
| `logs\app.log` | Diagnostics (no secrets) |

"Launch at Windows startup" is the per-user registry Run entry `HKCU\...\Run\Password helper` (not stored in
`settings.json`).

Run with `--data-dir "<path>"` to use a portable/isolated data directory instead.

---

## Settings (all via the tray)

**General** — startup, start minimized, insert into active app, close-on-selection, close-on-click-outside, button
sound, overlay placement.
**Shortcut** — record / reset the activation hotkey, with conflict reporting.
**Passwords** — default length, character classes, exclude look-alikes, symbol set, live preview.
**Clipboard** — enable history, entries to keep, auto-clear delay for copied passwords, ignored apps.
**Security** — lock/unlock vault, lock on Windows lock, auto-lock after inactivity, storage location, delete all data.
**Appearance** — glass effect, glass opacity, blur intensity, animation speed/toggle, reduced motion, button size,
ring capacity, sound volume.
**About** — version, environment, logs folder.

---

## Testing status

- **Unit tests (50, all passing):** password generation (exact length, every class guaranteed, no ambiguous chars,
  allowed-charset, uniqueness, validation, entropy), hotkey parsing/validation, radial-layout geometry
  (multi-ring, spacing, finiteness), DPAPI round-trip, `ProtectedSecret`, vault serialize/deserialize, **corruption
  and truncation detection** (file preserved, not emptied), backup creation, and the full add/update/delete/lock
  round-trip through `VaultService`. Run with `.\build.ps1 test`.
- **Runtime (Windows 11):** verified that the app starts to the tray, registers `Ctrl+Shift+P`, opens and closes the
  overlay, renders the radial menu + saved-passwords panel + settings window, and exits cleanly with no leaked
  processes or logged errors.
- **Not yet verified:** Windows 10, per-monitor high-DPI scaling, insertion into the full range of target
  applications (elevated apps, custom controls), and sleep/resume behaviour on real hardware.

---

## Known limitations

- The modifier-only `Ctrl+Shift` default is intentionally unavailable (see *The global hotkey* above).
- Manual DPAPI `ReplaceFile` falls back to copy+move on file systems that don't support atomic replace.
- The frosted-glass backdrop is a one-time capture of the desktop behind the overlay; it does not update live while
  the overlay is open (by design, for a transient menu). If transparency effects are disabled system-wide, a solid
  dark backdrop is used instead.

---

## License / attribution

Icon glyphs are from the [Lucide](https://lucide.dev) icon set (ISC license). The remainder is original project code.
