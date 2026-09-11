# Paster

Paster is a Windows background utility that captures selected plain text and
replays it as simulated keyboard input. It is intended for local applications
and Remote Desktop clients where normal clipboard sharing is unavailable in
the direction you need.

## Core workflow

1. Select text in the source application and press the capture shortcut
   (`Ctrl+Shift+C` by default).
2. Paster waits for the activation keys to be released, sends `Ctrl+C`, and
   accepts only fresh Unicode text from the clipboard.
3. Focus the destination application or Remote Desktop window and press the
   paste shortcut (`Ctrl+Shift+V` by default).
4. Paster types the captured text into that foreground window one unit at a
   time. Tabs and line breaks are emitted as keyboard input, and CRLF line
   endings are normalized to a single Enter press.

If no explicit capture is available, paste falls back to the current Unicode
clipboard text. Capture and transfer operations cannot overlap.

## Transfer safety and interruption

The destination is the local foreground window at the moment transfer starts.
Paster stops the transfer when any of the following occurs:

- The cancel shortcut (`Ctrl+Shift+X` by default) is pressed.
- A physical keyboard key or mouse button is pressed.
- The local foreground window changes.
- Windows rejects simulated input.
- The application exits.

An interrupted or failed transfer keeps the valid captured text in memory so
you can focus the destination and retry. A failed capture clears the previous
capture to prevent stale text from being transferred. Input failures also use
best-effort key release so partially injected modifier keys are not left down.

## Remote Desktop behavior

Paster works with a Remote Desktop Connection window while that window remains
the local foreground window and the client accepts simulated keyboard input.
You can change focus between applications inside the remote desktop because
Paster can only observe the local RDP window, not the remote window hierarchy.

Minimizing the RDP client or switching to another local application changes the
local foreground window and stops the transfer. Paster cannot reliably type
into an unfocused or minimized RDP window: Windows `SendInput` targets the
active input session rather than a particular background window.

## Safety and privacy

Paster has no network access, telemetry, captured-text log, or persistent
clipboard history. Captured text is held only in memory and is discarded when
the process exits. Operational status never displays captured text.

Paster runs without elevation and uses Windows global hotkeys, the clipboard,
the `SendInput` API, a per-user named mutex, and a local management pipe. It
cannot confirm that a destination—especially a remote application—accepted
every simulated key.

## Build and test

The repository uses the C# compiler included with Windows and the .NET
Framework 4.x base class library. No package manager or network access is
required.

```powershell
.\build.ps1 -RunTests
```

This creates `dist\paster.exe` as a GUI-subsystem executable and runs the
dependency-free core test harness.

## Install

```powershell
.\setup.ps1
```

Setup installs and immediately starts a stable per-user copy in
`%LOCALAPPDATA%\Paster`, then registers it in the current user's Run key for
future sign-ins. Paster has no tray icon, taskbar entry, or normal application
window; it appears as a background process in Task Manager.

Every setup run builds the current source, gracefully stops any running Paster
instance (forcing termination only if it does not exit), replaces the installed
executable, and starts that newly built version.

## Default configuration

- Capture shortcut: `Ctrl+Shift+C`
- Paste/type shortcut: `Ctrl+Shift+V`
- Cancel shortcut: `Ctrl+Shift+X`
- Start delay: 0 ms
- Inter-character delay: 8 ms
- Clipboard timeout: 2,000 ms
- Maximum text: 1,048,576 UTF-16 characters

Shortcut combinations must be distinct and contain at least one modifier plus
one supported key: `A-Z`, `0-9`, `F1-F24`, or `Escape`. Supported modifiers are
`Ctrl`, `Alt`, `Shift`, and `Win`. Character delay is constrained to 8-60,000
ms; the settings dialog validates all configured ranges before saving.

Settings are stored in `%LOCALAPPDATA%\Paster\settings.json`. Delay and size
changes apply to subsequent operations in the running process. Restart Paster
after changing shortcuts so the global hotkeys can be re-registered.

## Management commands

Run the installed executable with one of these explicit commands:

```powershell
& "$env:LOCALAPPDATA\Paster\paster.exe" --settings
& "$env:LOCALAPPDATA\Paster\paster.exe" --status
& "$env:LOCALAPPDATA\Paster\paster.exe" --clear
& "$env:LOCALAPPDATA\Paster\paster.exe" --exit
& "$env:LOCALAPPDATA\Paster\paster.exe" --enable-autostart
& "$env:LOCALAPPDATA\Paster\paster.exe" --disable-autostart
```

`--status` reports whether a capture exists, whether transfer is active,
whether Windows input remains available, the current target-window handle, and
the latest error. `--clear` removes only the in-memory capture.

## Remove

```powershell
.\uninstall.ps1
```

Uninstall disables the per-user Run entry, asks a running instance to exit,
and removes the installed executable and configuration from the exact install
directory. It does not remove unrelated user files.

## Current limitations

- Windows 10/11 interactive user sessions are the supported environment.
- Character mapping depends on the active keyboard layout.
- Unicode `SendInput` handling depends on the destination application.
- Clipboard ownership and Windows permission policy can prevent capture or
  simulated input.
- Local foreground monitoring cannot detect focus changes inside an RDP window.
- Simulated typing preserves plain-text structure where supported, but does not
  guarantee identical bytes, encoding, or newline conventions.

Core tests cover structured text, tabs, blank lines, punctuation, Unicode,
size limits, stale-capture clearing, interruption and cancellation, foreground
changes, shortcut validation, capture preservation, and overlapping-operation
prevention. Windows integration still depends on the live desktop environment.
