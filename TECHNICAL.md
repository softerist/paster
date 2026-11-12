# Paster: technical details

This is the reference for how Paster works, every setting, the command-line
options, and building from source. For installing and everyday use, see the
[README](README.md).

Paster is a Windows background utility that replays plain text as simulated
keyboard input. It is intended for Remote Desktop clients, including
browser-based ones, and local applications where normal clipboard sharing is
unavailable in the direction you need.

- [Shortcuts](#shortcuts)
- [How typing works](#how-typing-works)
- [Ctrl+V in remote windows](#ctrlv-in-remote-windows)
- [Paste normally first](#paste-normally-first)
- [Remote speed and nested sessions](#remote-speed-and-nested-sessions)
- [Code mode](#code-mode)
- [Transfer safety and interruption](#transfer-safety-and-interruption)
- [Large transfers](#large-transfers)
- [Notifications and status](#notifications-and-status)
- [Safety and privacy](#safety-and-privacy)
- [Configuration](#configuration)
- [Management commands](#management-commands)
- [Install and remove](#install-and-remove)
- [Build and test](#build-and-test)
- [Limitations](#limitations)

## Shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+V` in a remote-application window | Pastes normally where the session accepts it, types where clipboard sharing is blocked (browser-based clients: always types) |
| `Ctrl+Shift+C` | Captures the current selection (sends `Ctrl+C` and keeps the text) |
| `Ctrl+Shift+V` | Types the newest text into the foreground window, in any application |
| `Ctrl+Shift+X` | Cancels a running transfer |

Shortcut keys can be pressed in any order: the action fires on whichever key
completes the combination, so pressing `C` slightly before `Shift` still works.
The modifiers must match exactly, so `Ctrl+Alt+Shift+C` does not trigger
`Ctrl+Shift+C`. Pressing a paste shortcut while text is being typed stops the
transfer instead of starting it again.

Shortcut combinations must be distinct and contain at least one modifier plus
one supported key: `A-Z`, `0-9`, `F1-F24`, or `Escape`. Supported modifiers are
`Ctrl`, `Alt`, `Shift`, and `Win`.

## How typing works

Paster waits until the shortcut keys are physically released, then types into
the foreground window. Typing while the keys are still held would let held or
auto-repeating modifiers turn the typed text into application shortcuts.

Paste types whichever is newer: the explicit `Ctrl+Shift+C` capture or Unicode
text copied to the clipboard after it. If the clipboard has since changed to
something other than text, the capture is kept. Capture and transfer operations
cannot overlap.

Text is sent in small batches, each as one atomic `SendInput` call:

- In remote windows (the listed applications) every character is sent as a
  Unicode character, so it arrives exactly regardless of the remote keyboard
  layout, and nested sessions (a remote client inside a remote session) cannot
  duplicate it.
- Elsewhere, characters that need no modifier on the destination window's
  keyboard layout (lowercase letters, digits, space) are sent as virtual keys
  with their real scan codes, like physical keys, unless "Type all characters
  as Unicode" (`typeAllAsUnicode`) is on.
- Tab and line breaks are sent as the Tab and Enter keys, and CRLF line endings
  become a single Enter press. In remote windows they are sent separately from
  the surrounding characters, and the characters after them wait briefly, so
  the keys cannot arrive late (see
  [Remote speed and nested sessions](#remote-speed-and-nested-sessions)).
- Everything else (uppercase letters, shifted symbols, AltGr and non-layout
  characters) is sent as Unicode input. Paster never presses Shift, Ctrl, or
  Alt while typing, so rapid typing cannot trigger the Sticky Keys prompt or
  application shortcuts, locally or on the remote host.
- Paster requests a 1 ms system timer while typing and opts out of Windows 11
  power throttling, so short batch delays are honored even though it has no
  visible window.

If a local destination drops characters, lower the batch size or raise the
delay per batch. With `typeShiftedAsUnicode` off, letters typed with Shift come
out in the wrong case while Caps Lock is on.

## Ctrl+V in remote windows

Pressing the normal Windows `Ctrl+V` while a listed remote application is the
foreground window hands the paste to Paster when the clipboard holds text. It
pastes normally where the session accepts it and types the text where it
doesn't (see [Paste normally first](#paste-normally-first)); in browser-based
clients, and with that setting off, it always types.

The default list (`typeOnPasteApps`) covers:

| Entry | Program |
| --- | --- |
| `mstsc.exe`, `msrdc.exe` | Remote Desktop Connection; the Remote Desktop and Windows App desktop clients |
| `RDCMan.exe`, `mRemoteNG.exe`, `RoyalTS.exe`, `RemoteDesktopManager.exe`, `RemoteDesktopManager64.exe` | Connection managers: Remote Desktop Connection Manager, mRemoteNG, Royal TS, Devolutions Remote Desktop Manager |
| `vmconnect.exe` | Hyper-V virtual machine connection |
| `wfica32.exe`, `CDViewer.exe` | Citrix Workspace session window and Desktop Viewer |
| `vmware-remotemks.exe` | VMware Horizon session window |
| `TeamViewer.exe`, `AnyDesk.exe` | TeamViewer, AnyDesk |
| `vncviewer.exe`, `tvnviewer.exe` | RealVNC and TigerVNC viewers, TightVNC viewer |
| `msedge.exe:Windows App`, `chrome.exe:Windows App`, `msedge.exe:Windows Cloud`, `chrome.exe:Windows Cloud` | Windows 365 and Azure Virtual Desktop in Edge or Chrome, recognized by the window title ("Windows App", formerly "Windows Cloud") |

A settings file that still holds an earlier default list exactly (it was never
edited) gains the programs added since; an edited list is kept as it is, so
add new programs there yourself. Entries are comma-separated process names
(`.exe` is optional); add `:text` to require the window title to contain that
text (for example `msedge.exe:Windows App`), so other browser tabs keep their
normal paste. If a browser-based remote session is not recognized (Ctrl+V
pastes normally there, and Ctrl+Shift+V types without code mode), add its
window title text to the list. Edit the list in the settings dialog; changes
apply immediately, and an empty list disables the feature.

Text copied inside the remote session is handled the same way once it reaches
the local clipboard (on by default as "Type text copied inside remote
windows"). Paster cannot see which window inside a session has focus, so this
is what makes `Ctrl+V` work in a session nested inside another one, where
paste may be blocked. Pasting inside the session itself stays a normal paste
where the session accepts it; in browser-based clients it is typed.

`Ctrl+V` in a listed application stays a native paste when:

- the clipboard holds no text (files and images pass through to the session),
- you copied or cut inside the remote session (`Ctrl+C`, `Ctrl+X`,
  `Ctrl+Insert`, or `Shift+Delete`) and the copy has not reached the local
  clipboard, which is the case when clipboard redirection is blocked, or
- the clipboard last changed while the remote session was in front and "Type
  text copied inside remote windows" is turned off, or
- the application runs as administrator (Windows does not let Paster type
  into it; see [Transfer safety and interruption](#transfer-safety-and-interruption)).

Use `Ctrl+Shift+V` to type regardless (except into programs running as
administrator).

Limitations of `Ctrl+V` handling:

- In browser-based clients, and with [Paste normally first](#paste-normally-first)
  turned off, Paster does not try a native paste first: the choice is made by
  destination application and clipboard origin.
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

## Paste normally first

Many remote sessions allow pasting and some block it. In every listed
application except browser-based clients, `Ctrl+V` tries the session's own
paste first and types only where it is blocked ("Paste normally where the
session allows it", `pasteNormallyFirst`, on by default):

1. Paster puts the clipboard content back on the clipboard in a way that makes
   Windows tell it when a program reads it (delayed rendering), and sends the
   normal `Ctrl+V`.
2. If the remote client reads the content within 3 seconds, the paste reached
   your session (also through a nested session, which asks back through each
   hop): done, instantly and exactly.
3. If not, clipboard sharing is blocked somewhere on the way. Paster tells you,
   and `Ctrl+V` types into that session from then on. A blocked session pastes
   its own clipboard instead (whatever was last copied inside it), so if
   something else appeared, undo it (`Ctrl+Z`) and press `Ctrl+V` again.

What is put back on the clipboard:

- The complete content, in every format: plain data (text, HTML from browsers
  and VS Code, rich text, Office and editor data) byte for byte, and pictures
  (bitmaps and metafiles, as in copies from Office) as copies of the picture.
  The paste is therefore identical to one without Paster. Only bookkeeping is
  left out: OLE's private formats ("Ole Private Data", "DataObject"), which
  only the program that copied can use, and display-only formats.
- The republished copy is kept out of the Windows clipboard history and the
  cloud clipboard, where the original already is (if its source allowed it).
- Content that cannot be put back faithfully is left untouched: formats that
  hold handles only their own program understands, or more than 128 MB. It is
  pasted normally in a session where a normal paste has already been seen to
  work, and typed elsewhere.
- Text marked private by password managers ("Clipboard Viewer Ignore",
  "ExcludeClipboardContentFromMonitorProcessing", or excluded from the
  clipboard history) is never put back: it is pasted normally in a session
  where a normal paste has already been seen to work, and typed elsewhere.
- A copy made inside the remote session reaches this PC through the remote
  desktop program, which then owns the clipboard. In a session where a normal
  paste is known to work, such a copy is put back (to be checked again) only
  when it is plain text; richer content stays with the session, which pastes
  it as it would without Paster: nothing travels to this PC and back, and the
  program that copied keeps its own copy (Excel's copy mode, for example). In a
  session not checked yet, the full content is put back once to check it.

How results are kept:

- Per session: Paster remembers the result for the focused window inside the
  client, so programs that show several sessions in one window (RDCMan,
  mRemoteNG, Royal TS) keep a result for each session.
- Paster cannot see inside a session, so a session nested in another one
  shares the outer one's result: after a blocked paste in the nested session,
  the outer session types too, until its window closes or `--clear`.
- A working session is checked again with every paste of text, because the
  focus inside it can move to a nested session that blocks sharing. A blocked
  session stays blocked: typing always works there, and another attempt could
  paste the session's old clipboard again.

Details:

- While Paster watches a paste, other programs (such as clipboard managers)
  cannot read the content, so they cannot be mistaken for the remote client.
- Browser-based clients (Edge, Chrome, Firefox, and similar) read the clipboard
  as soon as `Ctrl+V` is pressed, even when a nested session will reject the
  text, so a read proves nothing there: they always type.
- Some programs send the clipboard to the remote computer as soon as it
  changes rather than when the remote side pastes (TeamViewer, AnyDesk, VNC
  viewers). Their read shows that they took the content, not that the remote
  side accepted it: a VNC server that ignores clipboard text counts as
  working. Use `Ctrl+Shift+V` there.
- No read can also mean that the program inside the session does not paste
  with `Ctrl+V` (a Linux terminal, for example); typing is then the right
  choice too.
- The client gets a 300 ms head start to announce the republished content to
  its session before the `Ctrl+V` follows. After an earlier paste a session
  holds its own copy, and a `Ctrl+V` arriving before the announcement would
  paste that copy without asking, so the paste would work but look blocked.
- Paster reads every format of a copy once before putting it back, so pasting
  a very large Office selection into a session takes a moment longer than a
  paste without Paster.
- It needs "Type on Ctrl+V in listed apps" (`typeOnCtrlV`) on.
- `Ctrl+Shift+V` always types. `--status` shows in how many sessions the
  normal paste works or is blocked; `--clear` and restarting Paster forget
  them.
- If Paster exits while it still holds the republished content, the content is
  left on the clipboard.

## Remote speed and nested sessions

Listed applications are typed at the remote speed (1,000 keystrokes per
second by default; a browser-based Windows Cloud session measured about 630
delivered keystrokes per second at this setting and stopped immediately when
interrupted). Remote clients accept keystrokes far faster than they can
deliver them, and keystrokes already queued in the client or the remote session
cannot be cancelled; pacing keeps that queue small so interruption stays
immediate. If text keeps appearing after you interrupt a transfer, lower the
remote speed; if interruption is immediate, you can raise it. The remote speed
counts keystrokes, so code mode's extra keys are paced too.

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

Tab, Enter, and code mode's extra keys are always sent as keys, in remote
windows too: most programs do not treat a Unicode line break or tab as the
Enter or Tab key. Keys and Unicode characters can travel separately through a
nested session, and keys were seen arriving up to about 60 ms after the
characters typed next. So in remote windows the characters after Enter or Tab
wait 150 ms (`remoteKeyPauseMilliseconds`), about 7 seconds for a 50-line
paste. If a nested session still mixes up lines, raise it; for a single remote
session that never does, it can be lowered.

## Code mode

Code editors such as VS Code and Notepad++ react to typed keys: they add
indentation after Enter, insert closing brackets and quotes, and accept
autocomplete or inline suggestions on Enter and Tab. Typed code then arrives
with doubled indentation, extra brackets, or replaced words. Paster cannot see
which application is focused inside a remote session, so it types into remote
windows in code mode, whether `Ctrl+V` or `Ctrl+Shift+V` started the paste (on
by default; each key can be turned on or off in the settings dialog):

- **Space, then Backspace** before each Enter and Tab (on by default, skipped
  after whitespace) ends the current word, so suggestion popups such as
  Notepad++ word completion or VS Code IntelliSense close instead of Enter or
  Tab accepting a suggestion and swallowing the line break. The space is
  removed again, so terminals, documents, and spreadsheet cells are unchanged.
- **Home** after each Enter (on by default) moves before any auto-inserted
  indentation, so the line's own indentation is typed exactly. The editor's
  indentation ends up at the end of the line, so **End** is pressed before each
  Enter: otherwise Enter would carry that indentation to the next line, where a
  smart Home (Notepad++, VS Code) stops after it and every second line gains
  indentation. Lines can therefore keep trailing whitespace in editors that do
  not remove it. Neither key removes existing text, so both are safe in
  terminals, documents, and chats.
- **Escape** before each Enter and Tab (off by default) also closes suggestion
  popups, including inline (ghost text) suggestions. It also clears the current
  line in PowerShell and cmd, cancels spreadsheet cell edits, and can close
  dialogs, so turn it on only if you paste mainly into code editors.
- **Delete** after each `(`, `[`, `{`, `"`, `'`, and `` ` `` (off by default)
  removes a closing character the editor inserted automatically. It assumes you
  are typing at the end of a document into an editor that auto-closes (VS Code
  does; Notepad++ does not by default); anywhere else it deletes the next
  character of existing text.

In local windows, `Ctrl+Shift+V` types plain text without code mode. Editors
that auto-close HTML tags are not covered.

## Transfer safety and interruption

The destination is the local foreground window at the moment transfer starts.
Paster stops the transfer when any of the following occurs:

- The cancel shortcut (`Ctrl+Shift+X` by default) is pressed.
- A physical keyboard key or mouse button is pressed. Paster's own simulated
  keystrokes are never mistaken for user input, and while a paste runs every
  physical click is counted, so even a quick touchpad tap stops it.
- The local foreground window changes.
- Windows rejects simulated input.
- The application exits.

What you do yourself stays quiet: a key press, a click, the cancel shortcut,
declining a prompt, or switching to another window (any key press or click
within the second before the focus changed counts as yours, even with "Stop
typing on key press or click" off). When the focus moves without any input from
you, another program took it (a chat notification, a dialog, an app activating
itself); Paster then names that program and says how much was typed, for
example "Typing stopped because Microsoft Teams took the focus (1,200 of 5,000
characters typed)". Large transfers can be resumed by pasting again in the
original window. Every stop, quiet or not, appears in `--status` as the last
error.

Windows silently discards simulated input sent to a program running as
administrator (while Paster itself is not), so Paster checks the destination
first:

- `Ctrl+V` in a listed remote application running as administrator stays a
  normal paste, which Windows allows.
- `Ctrl+Shift+V` into any program running as administrator types nothing and
  reports it, suggesting `Ctrl+V` instead. Run Paster as administrator to type
  into elevated programs.

An interrupted or failed transfer keeps the valid captured text in memory. A
failed capture clears the previous capture to prevent stale text from being
transferred.

Paster works with a Remote Desktop window while that window remains the local
foreground window and the client accepts simulated keyboard input. You can
change focus between applications inside the remote desktop because Paster can
only observe the local client window, not the remote window hierarchy.
Minimizing the client or switching to another local application stops the
transfer: Windows `SendInput` targets the active input session rather than a
particular background window.

## Large transfers

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

## Notifications and status

Paster runs without any visible window. When something goes wrong that you did
not cause (a shortcut that could not be registered, an unusable settings file,
another program taking the focus while Paster types, a blocked normal paste),
it shows a Windows notification; its icon appears in the notification area
only while the notification is on screen. Clicking the notification opens the
status. "Show tray icon" in the settings dialog keeps the icon visible, with a
menu for the status, settings, and exit commands; "Show notifications when
something goes wrong" turns notifications off.

`--status` reports whether a capture exists, whether transfer is active,
whether Windows accepted the last simulated input, the current target-window
handle, the resume point of an interrupted transfer, and the latest error; with
paste normally first on, also in how many sessions the normal paste works or is
blocked.

## Safety and privacy

Paster has no network access, telemetry, captured-text log, keystroke log, or
persistent clipboard history. Captured text is held only in memory and is
discarded when the process exits. To check a normal paste, Paster holds a copy
of the clipboard content in memory until something else is copied; content
marked private by its source is never copied. Operational status never
displays captured text.

Paster runs without elevation and uses Windows global hotkeys, a low-level
keyboard hook, a clipboard-change listener, the `SendInput` API, a per-session
named mutex, and a per-session local management pipe. The keyboard hook only
checks for Paster's shortcuts, copy keys pressed in a remote window, and user
interruptions; it does not record keystrokes. Windows silently removes a
low-level hook that once responds too slowly (for example under heavy load or
while resuming from sleep), so Paster re-installs it every minute and after
resume, unlock, and session reconnect. While a paste runs, and only then, a
low-level mouse hook counts physical clicks; it records no positions.

To recognize remote applications, Paster reads the foreground window's process
name and title, but only when a paste or copy shortcut is pressed, a transfer
starts, or the clipboard changes. Notifications name programs by their
description (such as "Microsoft Teams"), never by window title, so document
names and message subjects stay out of them. Paster cannot confirm that a
destination, especially a remote application, accepted every simulated key.

## Configuration

Settings live in `%LOCALAPPDATA%\Paster\settings.json`. When Paster starts it
writes the file with every option (one per line) if it is missing, and adds any
options introduced since the file was written. A file that cannot be read or
contains invalid values is never overwritten at startup; Paster runs with the
defaults instead and reports why, so fix the file and restart. Files saved
with a byte-order mark (Windows PowerShell, Notepad) are read too.

Open the settings dialog with `--settings` or from the tray icon. It starts
from the file's current values, so edits made by hand while Paster runs are
kept when you save there. It validates all values before saving; an invalid
value keeps the dialog open and names the field. Saved changes, shortcuts
included, apply to the running process at once. Edits made directly to
`settings.json` take effect after Paster restarts or when you save the
settings dialog.

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
| `typeAllAsUnicode` | Type all characters as Unicode (any keyboard layout) | `false` | `true`: letters and digits are layout-independent in local windows too (remote windows always get Unicode) |
| `stopOnUserInput` | Stop typing on key press or click | `true` | `false`: only the cancel shortcut and focus changes stop typing |
| `clipboardTimeoutMilliseconds` | Clipboard timeout (ms) | `2000` | 100-120,000 |
| `maximumTextCharacters` | Maximum characters | `16777216` | 1-268,435,456 |
| `confirmAboveLines` | Confirm pastes over (lines) | `2000` | 0 (never confirm or resume)-100,000,000 |
| `promptTimeoutSeconds` | Prompt timeout (s) | `120` | 10-3,600 |
| `typeOnCtrlV` | Type on Ctrl+V in listed apps | `true` | `false`: `Ctrl+V` is never intercepted |
| `typeOnPasteApps` | Type on Ctrl+V in apps | The programs listed under [Ctrl+V in remote windows](#ctrlv-in-remote-windows) | Comma-separated `process[:title text]` |
| `pasteNormallyFirst` | Paste normally where the session allows it (type only where blocked) | `true` | `false`: `Ctrl+V` always types in listed apps. Browser-based clients always type. Replaces the earlier `pasteFirstApps` list, which is ignored and removed from the file |
| `typeRemoteCopies` | Type text copied inside remote windows | `true` | `false`: such copies paste natively |
| `codeModeForRemotePaste` | Use code mode in remote windows | `true` | Applies to `Ctrl+V` and `Ctrl+Shift+V`; `false`: plain typing |
| `codeModeDismissSuggestions` | Code mode: Space+Backspace before Enter and Tab | `true` | Closes suggestion popups without removing text |
| `codeModeEscapeBeforeEnter` | Code mode: Escape before Enter and Tab | `false` | Clears terminal lines and cancels spreadsheet cell edits |
| `codeModeHomeAfterEnter` | Code mode: Home after Enter | `true` | |
| `codeModeDeleteAutoClosed` | Code mode: Delete auto-closed brackets and quotes | `false` | Only safe when typing at the end of a document into an editor that auto-closes |
| `remoteCharactersPerSecond` | Remote speed (keys/s) | `1000` | Keystrokes per second; 0 (unlimited)-100,000 |
| `remoteKeyPauseMilliseconds` | Pause after Enter and Tab (ms) | `150` | 0-1,000; raise it if lines still arrive out of order in a nested session, 0 turns it off |
| `showTrayIcon` | Show tray icon | `false` | `true`: the notification-area icon (with its menu) stays visible; when off, it appears only while a notification is shown |
| `showNotifications` | Show notifications when something goes wrong | `true` | `false`: no notifications; failures appear only in `--status` |

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

`--status` is described under [Notifications and status](#notifications-and-status).
`--clear` removes the in-memory capture and resume point and forgets which
sessions were checked for paste normally first.

## Install and remove

```powershell
.\setup.ps1
```

Setup installs and immediately starts a stable per-user copy in
`%LOCALAPPDATA%\Paster`, then registers it in the current user's Run key for
future sign-ins. Paster has no taskbar entry or normal application window; by
default it appears only as a background process in Task Manager, and its
notification-area icon shows only while a notification is on screen. "Show
tray icon" keeps the icon visible.

Every setup run builds the current source, gracefully stops any running Paster
instance (forcing termination only if it does not exit), replaces the installed
executable, and starts that newly built version.

```powershell
.\uninstall.ps1
```

Uninstall disables the per-user Run entry, asks a running instance to exit
(and stops the installed copy if it has not exited after five seconds, since it
keeps its executable locked), and removes the installed executable and
configuration from the exact install directory. It does not remove unrelated
user files.

## Build and test

The repository uses the C# compiler included with Windows and the .NET
Framework 4.x base class library. No package manager or network access is
required.

```powershell
.\build.ps1 -RunTests
```

This creates `dist\paster.exe` as a GUI-subsystem executable and runs the
dependency-free core test harness.

Core tests cover structured text, tabs, blank lines, punctuation, Unicode,
batching and line-ending normalization, surrogate pairs, size limits, settings
validation and migration, stale-capture clearing, newest-text selection,
interruption and cancellation, foreground changes, shortcut validation,
remote-application matching, remote pacing, resume points, confirmation
prompts, capture preservation, overlapping-operation prevention, blocked
(elevated) destinations, which failures are reported, telling your own window
switches from another program taking the focus, and the paste normally first
decisions. Windows integration (hotkeys, the keyboard hook, input injection,
and the clipboard) still depends on the live desktop environment.

## Limitations

- Windows 10/11 interactive user sessions are the supported environment.
- Unicode `SendInput` handling depends on the destination application; a few
  older console programs and games ignore Unicode input.
- In local windows, lowercase letters, digits, and unshifted punctuation are
  sent as keys of the destination window's keyboard layout; turn on "Type all
  characters as Unicode" if a local program interprets them with a different
  layout. Remote windows always receive Unicode characters.
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
