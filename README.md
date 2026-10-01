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
  letters, digits, space) are sent as virtual keys, unless "Type all characters
  as Unicode" (`typeAllAsUnicode`) is on; Tab and line breaks are
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

Text copied inside the remote session is typed too, once it reaches the local
clipboard (the browser client shares it; this is on by default as "Type text
copied inside remote windows"). Paster cannot see which window inside a session
has focus, so this is what makes `Ctrl+V` work in an `mstsc` session opened
inside the browser session, where paste is blocked. The cost is that pasting
inside the session itself is typed rather than pasted.

`Ctrl+V` in a listed application stays a native paste when:

- the clipboard holds no text (files and images pass through to the session),
- you copied or cut inside the remote session (`Ctrl+C`, `Ctrl+X`,
  `Ctrl+Insert`, or `Shift+Delete`) and the copy has not reached the local
  clipboard, which is the case when clipboard redirection is blocked, or
- the clipboard last changed while the remote session was in front and "Type
  text copied inside remote windows" is turned off.

Use `Ctrl+Shift+V` to type regardless.

Listed applications are also typed at the remote speed (1,000 keystrokes per
second by default; a browser-based Windows Cloud session measured about 630
delivered keystrokes per second at this setting and stopped immediately when
interrupted). Remote clients accept keystrokes far faster than they can
deliver them, and keystrokes already queued in the client or the remote session
cannot be cancelled; pacing keeps that queue small so interruption stays
immediate. If text keeps appearing after you interrupt a transfer, lower the
remote speed; if interruption is immediate, you can raise it.

To find the fastest speed your session keeps up with:

1. Create a test text with numbered lines and copy it, for example:
   `1..400 | ForEach-Object { 'Line {0:D4} abcdefghijklmnopqrstuvwxyz0123456789' -f $_ } | Set-Clipboard`
   (about 20,000 characters, below the confirmation threshold).
2. Press `Ctrl+V` in an empty remote editor and click once after a few seconds.
3. Note the last line that appears. If typing stopped within a line or two of
   the click, raise the remote speed in the settings dialog (for example 1,000,
   2,000) and repeat. If many more lines keep appearing after the click,
   the session is slower than the setting: go back to the last speed that
   stopped immediately.

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
remote window it can type in code mode (on by default; each key can be turned
on or off in the settings dialog):

- **Space, then Backspace** before each Enter and Tab (on by default, skipped
  after whitespace) ends the current word, so suggestion popups such as
  Notepad++ word completion or VS Code IntelliSense close instead of Enter or
  Tab accepting a suggestion and swallowing the line break. The space is
  removed again, so terminals, documents, and spreadsheet cells are unchanged.
- **Home** after each Enter (on by default) moves before any auto-inserted
  indentation, so the line's own indentation is typed exactly. The editor's
  indentation ends up at the end of the line; most editors remove or replace it
  on the next Enter, but the last line can keep trailing whitespace. It never
  removes existing text, so it is safe in terminals, documents, and chats.
- **Escape** before each Enter and Tab (off by default) also closes suggestion
  popups, including inline (ghost text) suggestions. It also clears the current line in
  PowerShell and cmd, cancels spreadsheet cell edits, and can close dialogs, so
  turn it on only if you paste mainly into code editors.
- **Delete** after each `(`, `[`, `{`, `"`, `'`, and `` ` `` (off by default)
  removes a closing character the editor inserted automatically. It assumes you
  are typing at the end of a document into an editor that auto-closes (VS Code
  does; Notepad++ does not by default); anywhere else it deletes the next
  character of existing text.

`Ctrl+Shift+V` always types plain text without code mode. Editors that
auto-close HTML tags are not covered.

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

Settings live in `%LOCALAPPDATA%\Paster\settings.json`. When Paster starts it
writes the file with every option (one per line) if it is missing, and adds any
options introduced since the file was written. A file that cannot be read or
contains invalid values is never overwritten at startup; Paster runs with the
defaults instead, so fix the file and restart. The settings dialog starts from
the file's current values, so edits made by hand while Paster runs are kept
when you save there.

| JSON key | Settings dialog | Default | Range or values |
| --- | --- | --- | --- |
| `captureShortcut` | Capture shortcut | `Ctrl+Shift+C` | Shortcut |
| `pasteShortcut` | Paste shortcut | `Ctrl+Shift+V` | Shortcut |
| `cancelShortcut` | Cancel shortcut | `Ctrl+Shift+X` | Shortcut |
| `anyOrderShortcuts` | Shortcuts work in any key order | `true` | `false`: the key must be pressed last |
| `startDelayMilliseconds` | Start delay (ms) | `0` | 0-60,000 |
| `characterDelayMilliseconds` | Delay per batch (ms) | `2` | 0-60,000 |
| `charactersPerBatch` | Characters per batch | `32` | 1-4,096 |
| `typeShiftedAsUnicode` | Type uppercase and symbols as Unicode | `true` | `false`: type them with Shift (can trigger Sticky Keys) |
| `typeAllAsUnicode` | Type all characters as Unicode (any keyboard layout) | `false` | `true`: letters and digits are layout-independent too |
| `stopOnUserInput` | Stop typing on key press or click | `true` | `false`: only the cancel shortcut and focus changes stop typing |
| `clipboardTimeoutMilliseconds` | Clipboard timeout (ms) | `2000` | 100-120,000 |
| `maximumTextCharacters` | Maximum characters | `16777216` | 1-268,435,456 |
| `confirmAboveLines` | Confirm pastes over (lines) | `2000` | 0 (never confirm or resume)-100,000,000 |
| `promptTimeoutSeconds` | Prompt timeout (s) | `120` | 10-3,600 |
| `typeOnCtrlV` | Type on Ctrl+V in listed apps | `true` | `false`: `Ctrl+V` is never intercepted |
| `typeOnPasteApps` | Type on Ctrl+V in apps | `mstsc.exe, msrdc.exe, msedge.exe:Windows Cloud, chrome.exe:Windows Cloud` | Comma-separated `process[:title text]` |
| `typeRemoteCopies` | Type text copied inside remote windows | `true` | `false`: such copies paste natively |
| `codeModeForRemotePaste` | Use code mode for Ctrl+V into remote windows | `true` | `false`: plain typing |
| `codeModeDismissSuggestions` | Code mode: Space+Backspace before Enter and Tab | `true` | Closes suggestion popups without removing text |
| `codeModeEscapeBeforeEnter` | Code mode: Escape before Enter and Tab | `false` | Clears terminal lines and cancels spreadsheet cell edits |
| `codeModeHomeAfterEnter` | Code mode: Home after Enter | `true` | |
| `codeModeDeleteAutoClosed` | Code mode: Delete auto-closed brackets and quotes | `false` | Only safe when typing at the end of a document into an editor that auto-closes |
| `remoteCharactersPerSecond` | Remote speed (keys/s) | `1000` | Keystrokes per second; 0 (unlimited)-100,000 |

Shortcut combinations must be distinct and contain at least one modifier plus
one supported key: `A-Z`, `0-9`, `F1-F24`, or `Escape`. Supported modifiers are
`Ctrl`, `Alt`, `Shift`, and `Win`. If a local destination drops characters,
lower the batch size or raise the delay per batch.

Open the settings dialog with `--settings`; it validates all values before
saving, and changes apply to the running process except shortcuts, which need
a restart to be re-registered. Edits made directly to `settings.json` take
effect after Paster restarts or when you save the settings dialog. The remote
speed counts keystrokes, so code mode's extra keys are paced too. With
`typeShiftedAsUnicode` off, letters typed with Shift come out in the wrong case
while Caps Lock is on.

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
whether Windows accepted the last simulated input, the current target-window
handle, the
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
- Lowercase letters, digits, and unshifted punctuation are sent as keys of the
  local keyboard layout. If a remote session uses a different layout (for
  example US locally and German remotely), turn on "Type all characters as
  Unicode" so every character arrives exactly.
- If Windows rejects simulated input (a UAC prompt or the lock screen during a
  transfer), that transfer stops and keeps its resume point; the next paste
  tries again.
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
