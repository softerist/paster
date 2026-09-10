# Paster

Paster is a Windows background utility that captures selected plain text and
replays it as simulated keyboard input. It is designed for text entry into a
local application or a remote-desktop client when clipboard sharing is not
available in the direction you need.

## Safety and scope

Paster has no network access, telemetry, captured-text log, or persistent
clipboard history. Captured text is held only in memory and is discarded when
the process exits. Configuration is stored under `%LOCALAPPDATA%\Paster`;
operational status never includes captured text. Paster does not elevate its
privileges and cannot confirm that a remote application accepted its input.

The initial adapter targets Windows 10/11 interactive user sessions. It uses
the user32 global-hotkey and `SendInput` APIs, the Windows clipboard, and a
per-user named mutex. A remote desktop session must accept simulated keyboard
input; local focus monitoring cannot tell when focus changes inside the remote
desktop window.

## Build and test

The repository deliberately uses only the C# compiler included with Windows
(.NET Framework 4.x) and the base class library. From a PowerShell prompt:

```powershell
.\build.ps1 -RunTests
```

This creates `dist\Paster.exe` as a GUI subsystem executable and runs the
dependency-free core test harness. No package manager or network access is
needed to build it.

## Install and use

```powershell
.\setup.ps1
```

The setup flow installs a stable per-user copy in `%LOCALAPPDATA%\Paster` and
registers it in the current user's Run key. It does not require administrator
rights. Paster starts silently at sign-in when an interactive session is
available; it has no tray icon, taskbar entry, or normal window.

Defaults:

* Capture: `Ctrl+C/Ctrl+Alt+C`
* Paste/type: `Ctrl+Shift+V`
* Cancel: `Ctrl+Alt+X`
* Start delay: 0 ms
* Inter-character delay: 8 ms minimum
* Clipboard timeout: 2 seconds
* Maximum captured text: 1 MiB of UTF-16 text

The management interface is explicitly invoked from PowerShell or a shortcut:

```powershell
& "$env:LOCALAPPDATA\Paster\Paster.exe" --settings
& "$env:LOCALAPPDATA\Paster\Paster.exe" --status
& "$env:LOCALAPPDATA\Paster\Paster.exe" --clear
& "$env:LOCALAPPDATA\Paster\Paster.exe" --exit
& "$env:LOCALAPPDATA\Paster\Paster.exe" --enable-autostart
& "$env:LOCALAPPDATA\Paster\Paster.exe" --disable-autostart
```

Settings, current status, clear, exit, and autostart controls are available
there. Conflicting shortcuts and unavailable Windows capabilities are reported
in Status. A capture failure clears any previous capture, and a transfer stops
on cancellation, foreground-window change, or an input API failure; it is not
automatically retried.

## Remove

```powershell
.\uninstall.ps1
```

This disables the per-user Run entry, asks a running instance to exit, and
removes the installed executable and configuration under the exact install
directory. It does not remove unrelated user files.

## Acceptance and limitations

Core tests cover JSON-like content, Python/YAML indentation, tabs, blank lines,
punctuation, Unicode, size limits, stale-capture clearing, cancellation,
focus changes, and overlapping activation. Windows integration still depends
on the active keyboard layout, target application's handling of Unicode
`SendInput`, clipboard ownership, permission policy, and the remote desktop
client. Simulated typing preserves plain text structure where the destination
supports it; it does not promise identical file bytes, encoding, or newline
conventions.
