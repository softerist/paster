# Paster

Paster is a Windows background utility that replays plain text as simulated
keyboard input. It is intended for Remote Desktop clients, including
browser-based ones, and local applications where normal clipboard sharing is
unavailable in the direction you need.

## Quick start

1. Copy text locally with the normal `Ctrl+C`.
2. Click into the Remote Desktop window and press the normal `Ctrl+V`.
3. Paster types the text into the remote session. Everywhere else, `Ctrl+V`
   stays an ordinary Windows paste.

`Ctrl+Shift+V` types the clipboard text into any application, and
`Ctrl+Shift+X` cancels a running transfer.

## Shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+V` in a remote-application window | Types locally copied text instead of pasting it |
| `Ctrl+Shift+C` | Captures the current selection (sends `Ctrl+C` and keeps the text) |
| `Ctrl+Shift+V` | Types the newest text into the foreground window, in any application |
| `Ctrl+Shift+X` | Cancels a running transfer |

Shortcut keys can be pressed in any order: the action fires on whichever key
completes the combination, so pressing `C` slightly before `Shift` still works.
The modifiers must match exactly, so `Ctrl+Alt+Shift+C` does not trigger
`Ctrl+Shift+C`. Pressing a paste shortcut while text is being typed stops the
transfer instead of starting it again.

## How typing works

Paster waits until the shortcut keys are physically released, then types into
the foreground window. Typing while the keys are still held would let held or
auto-repeating modifiers turn the typed text into application shortcuts.

Paste types whichever is newer: the explicit `Ctrl+Shift+C` capture or Unicode
text copied to the clipboard after it. If the clipboard has since changed to
something other than text, the capture is kept. Capture and transfer operations
cannot overlap.

Text is sent in small batches, each as one atomic `SendInput` call:

- Characters that need no modifier on the active keyboard layout (lowercase
  letters, digits, space) are sent as virtual keys; Tab and line breaks are
  sent as the Tab and Enter keys, and CRLF line endings become a single Enter
  press.
- Everything else (uppercase letters, shifted symbols, AltGr and non-layout
  characters) is sent as Unicode input. Paster never presses Shift, Ctrl, or
  Alt while typing, so rapid typing cannot trigger the Sticky Keys prompt or
  application shortcuts, locally or on the remote host.
- Paster requests a 1 ms system timer while typing and opts out of Windows 11
  power throttling, so short batch delays are honored even though it has no
  visible window.

## Ctrl+V in Remote Desktop

Pressing the normal Windows `Ctrl+V` while a listed remote application is the
foreground window types the clipboard text instead of pasting it, as long as
that text was copied locally.

The default list covers the Remote Desktop clients `mstsc.exe` and `msrdc.exe`
and the browser-based Windows Cloud client in Edge or Chrome. Entries are
comma-separated process names (`.exe` is optional); add `:text` to require the
window title to contain that text (for example `msedge.exe:Windows Cloud`), so
other browser tabs keep their normal paste. Edit the list in the settings
dialog; changes apply immediately, and an empty list disables the feature.

`Ctrl+V` in a listed application stays a native paste when:

- the clipboard holds no text (files and images pass through to the session),
- you copied or cut inside the remote session (`Ctrl+C`, `Ctrl+X`,
  `Ctrl+Insert`, or `Shift+Delete`) since the last local copy, or
- the clipboard last changed while the remote session was in front, which is
  how redirected remote copies arrive.

Copy locally again to switch back to typing, or use `Ctrl+Shift+V` to type
regardless.

If a native paste does nothing, for example in an `mstsc` session opened inside
the browser session that blocks paste, press `Ctrl+V` again within a second:
the second press types the clipboard text instead. This works only when text
copied inside the remote session reached the local clipboard (the browser
client shares it); otherwise the second press stays a native paste, so older
local text is never typed by mistake.

Listed applications are also typed at the remote speed (500 characters per
second by default). Remote clients accept keystrokes far faster than they can
deliver them, and keystrokes already queued in the client or the remote session
cannot be cancelled; pacing keeps that queue small so interruption stays
immediate. If text keeps appearing after you interrupt a transfer, lower the
remote speed; if interruption is immediate, you can raise it.

To find the fastest speed your session keeps up with:

1. Copy a long text locally (about 20,000 characters).
2. Paste it into a remote editor, and click once while it is typing.
3. If typing stops immediately, raise the remote speed in the settings dialog
   (for example 500, 1,000, 2,000) and repeat. If text keeps appearing after
   the click, the session is slower than the setting: go back to the last
   speed that stopped immediately.

Limitations:

- Paster cannot tell whether a native paste succeeded, so the choice is made by
  destination application and clipboard origin, not by trying a paste first.
- A copy made inside the remote session with the mouse (context menu) is not
  detected; use `Ctrl+C` there or copy locally.
- Copying from the remote session to the local machine is not possible when
  clipboard redirection is blocked, because the copied text stays on the remote
  clipboard.
- A full-screen session that forwards Windows key combinations to the remote
  computer (or a browser in full screen that locks the keyboard) may receive
  keys before Paster sees them. In `mstsc`, set "Apply Windows key
  combinations" to "On this computer" if `Ctrl+V` or key interruption stops
  working in full screen.

### Code mode

Code editors such as VS Code and Notepad++ react to typed keys: they add
indentation after Enter, insert closing brackets and quotes, and accept
autocomplete or inline suggestions on Enter and Tab. Typed code then arrives
with doubled indentation, extra brackets, or replaced words. Paster cannot see
which application is focused inside a remote session, so for `Ctrl+V` into a
remote window it can type in code mode (on by default; toggle it in the
settings dialog):

- **Escape** before each Enter and Tab closes suggestion popups first.
- **Home** after each Enter moves before any auto-inserted indentation, so the
  line's own indentation is typed exactly. The editor's indentation ends up at
  the end of the line; most editors remove or replace it on the next Enter, but
  the last line can keep trailing whitespace.
- **Delete** after each `(`, `[`, `{`, `"`, `'`, and `` ` `` removes a closing
  character the editor inserted automatically.

Code mode assumes you are typing at the end of a document (for example into an
empty file). Its extra keys can have side effects elsewhere: Escape can close
dialogs or cancel cell edits, and Delete removes the character after the cursor
when the editor did not auto-close. Turn code mode off for remote pastes into
documents, spreadsheets, or forms, or use `Ctrl+Shift+V`, which always types
plain text. Editors that auto-close HTML tags are not covered.

## Transfer safety and interruption

The destination is the local foreground window at the moment transfer starts.
Paster stops the transfer when any of the following occurs:

- The cancel shortcut (`Ctrl+Shift+X` by default) is pressed.
- A physical keyboard key or mouse button is pressed. Paster's own simulated
  keystrokes are never mistaken for user input.
- The local foreground window changes.
- Windows rejects simulated input.
- The application exits.

An interrupted or failed transfer keeps the valid captured text in memory. A
failed capture clears the previous capture to prevent stale text from being
transferred.

Texts with more than 2,000 lines (configurable; 0 turns it off) are handled as
large transfers. Shorter texts are never interrupted by a prompt and always type
from the start.

- Before typing, a large transfer asks for confirmation, showing the line and
  character counts and the expected duration (remote windows use the remote
  speed for the estimate).
- If a large transfer is interrupted, Paster remembers how many characters were
  typed. Pasting the same text again asks whether to **Resume** from that
  point, **Start over**, or cancel. Pasting different text, a completed
  transfer, or `--clear` discards the resume point. Characters already sent
  before an interruption count as typed, because input that has left Paster
  cannot be recalled.
- Choosing **Type** or **Resume** returns focus to the destination window and
  waits for the answering key or mouse button to be released before typing
  starts. The cancel shortcut closes the prompt, and an unanswered prompt
  cancels itself after two minutes.

Paster works with a Remote Desktop window while that window remains the local
foreground window and the client accepts simulated keyboard input. You can
change focus between applications inside the remote desktop because Paster can
only observe the local client window, not the remote window hierarchy.
Minimizing the client or switching to another local application stops the
transfer: Windows `SendInput` targets the active input session rather than a
particular background window.

## Safety and privacy

Paster has no network access, telemetry, captured-text log, keystroke log, or
persistent clipboard history. Captured text is held only in memory and is
discarded when the process exits. Operational status never displays captured
text.

Paster runs without elevation and uses Windows global hotkeys, a low-level
keyboard hook, a clipboard-change listener, the `SendInput` API, a per-user
named mutex, and a local management pipe. The keyboard hook only checks for
Paster's shortcuts, copy keys pressed in a remote window, and user
interruptions; it does not record keystrokes. To recognize remote applications,
Paster reads the foreground window's process name and title, but only when a
paste or copy shortcut is pressed, a transfer starts, or the clipboard changes. It cannot confirm
that a destination, especially a remote application, accepted every simulated
key.

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

## Configuration

| Setting | Default | Range |
| --- | --- | --- |
| Capture shortcut | `Ctrl+Shift+C` | |
| Paste shortcut | `Ctrl+Shift+V` | |
| Cancel shortcut | `Ctrl+Shift+X` | |
| Start delay | 0 ms | 0-60,000 ms |
| Delay per batch | 2 ms | 0-60,000 ms |
| Characters per batch | 32 | 1-4,096 |
| Clipboard timeout | 2,000 ms | 100-120,000 ms |
| Maximum text | 16,777,216 UTF-16 characters | 1-268,435,456 |
| Confirm pastes over | 2,000 lines | 0 (never)-100,000,000 lines |
| Type on Ctrl+V in apps | `mstsc.exe, msrdc.exe, msedge.exe:Windows Cloud, chrome.exe:Windows Cloud` | |
| Remote speed | 500 characters per second | 0 (unlimited)-100,000 |
| Use code mode for Ctrl+V into remote windows | On | On or off |

Shortcut combinations must be distinct and contain at least one modifier plus
one supported key: `A-Z`, `0-9`, `F1-F24`, or `Escape`. Supported modifiers are
`Ctrl`, `Alt`, `Shift`, and `Win`. If a local destination drops characters,
lower the batch size or raise the delay per batch.

Open the settings dialog with `--settings`; it validates all values before
saving. Settings are stored in `%LOCALAPPDATA%\Paster\settings.json`; settings
missing from an older file load with their defaults. Delay, speed, size, and
application-list changes apply to subsequent operations in the running process.
Restart Paster after changing shortcuts so they can be re-registered.

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
whether Windows input remains available, the current target-window handle, the
resume point of an interrupted transfer, and the latest error. `--clear`
removes the in-memory capture and resume point.

## Remove

```powershell
.\uninstall.ps1
```

Uninstall disables the per-user Run entry, asks a running instance to exit,
and removes the installed executable and configuration from the exact install
directory. It does not remove unrelated user files.

## Current limitations

- Windows 10/11 interactive user sessions are the supported environment.
- Unicode `SendInput` handling depends on the destination application; a few
  older console programs and games ignore Unicode input.
- Clipboard ownership and Windows permission policy can prevent capture or
  simulated input.
- Local foreground monitoring cannot detect focus changes inside a remote
  session.
- Very large texts are practical only locally: at the default remote speed,
  16 million characters take about 9 hours. For bulk data, use file transfer
  or drive redirection instead.
- Simulated typing preserves plain-text structure where supported, but does not
  guarantee identical bytes, encoding, or newline conventions. Editors that
  auto-indent or auto-close brackets may alter typed code.

Core tests cover structured text, tabs, blank lines, punctuation, Unicode,
batching and line-ending normalization, surrogate pairs, size limits, settings
validation and migration, stale-capture clearing, newest-text selection,
interruption and cancellation, foreground changes, shortcut validation,
remote-application matching, remote pacing, resume points, confirmation
prompts, capture preservation, and overlapping-operation prevention. Windows integration (hotkeys, the keyboard
hook, and input injection) still depends on the live desktop environment.
