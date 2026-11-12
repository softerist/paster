---
name: verify
description: How to run and drive Paster for runtime verification on this Windows machine (build, swap settings, trigger paste paths, read status), including endpoint-security caveats.
---

# Verifying Paster at runtime

Paster is a background WinForms app with no main window. Its surfaces are a hidden window that receives `WM_HOTKEY`, the
management pipe (`--status`, `--settings`, `--clear`, `--exit`), notifications, and the windows it types or pastes into.

## Build and launch

- `.\build.ps1 -RunTests` builds `dist\paster.exe` and runs the core tests. Only one Paster runs per session (mutex), so stop the
  installed copy first with `& "$env:LOCALAPPDATA\Paster\paster.exe" --exit`, and start it again (`--background`) when done.
- Paster reads `%LOCALAPPDATA%\Paster\settings.json`. Back it up, edit it, and restore it byte for byte afterwards. Write it as
  UTF-8 without a BOM (`[IO.File]::WriteAllText(path, text, (New-Object Text.UTF8Encoding $false))`) and edit the raw text (regex
  replace) rather than round-tripping through `ConvertTo-Json`.

## Driving the paste paths

- Paster ignores injected keys (`LLKHF_INJECTED`), so SendInput cannot press Ctrl+V for it. Instead, post the message its hook posts:
  `WM_HOTKEY` (0x0312) with wParam **4** = Ctrl+V in a listed app (pastes normally first), **2** = the paste shortcut
  (Ctrl+Shift+V, always types). Post it to every top-level window of the Paster process (EnumWindows by pid); only the platform
  window acts on it.
- **Post it only after checking that the stand-in is the foreground window.** Paster acts on whatever window is in front, so a
  posted Ctrl+V while the user has another window in front would paste or type there. For the same reason, clear text boxes with
  `WM_SETTEXT`, never with a Delete key press.
- Stand-in remote program: the built-in Character Map (`charmap.exe`), listed with `"typeOnPasteApps": "charmap.exe"` (paste
  normally first is on by default, `pasteNormallyFirst`):
  - Its "Characters to copy" box (RICHEDIT50W) reads the clipboard on Ctrl+V: a session where paste works. It prefers a picture
    over text, so a text-plus-picture paste shows a blank (the picture).
  - Its character grid (CharGridWClass) does not read the clipboard: a blocked session in the same window, which exercises the
    per-session results (move focus with AttachThreadInput + SetFocus and confirm it with GetGUIThreadInfo).
  - Its Copy button (BM_CLICK; enabled only after the box changed through a paste) makes Character Map the clipboard owner: a copy
    made inside the remote program.
- Clipboard content: from Windows PowerShell 5.1, a WinForms `DataObject` put with `Clipboard.SetDataObject($d, $true)`:
  UnicodeText plus Html (a VS Code or browser copy), `SetText` plus `SetImage` (an Office copy), or UnicodeText plus a
  `MemoryStream` under "ExcludeClipboardContentFromMonitorProcessing" (a password manager). The clipboard owner's pid shows whether
  Paster republished the content.
- Status: run `paster.exe --status`, find the "Paster status" window, and read its child texts. The last line shows
  "Paste normally first: works in N, blocked in M session(s)". Read it before anything types: a transfer clears the last error.
  The status window takes the foreground, so bring the stand-in back (and check) before the next press.
- Notifications: capture the bottom-right corner of the primary screen (`Graphics.CopyFromScreen`) about 4 s after a blocked paste.
- Settings dialog: `--settings`, bring it to front, check it is in front, `SendKeys ^{TAB}` to switch tabs, capture with PrintWindow.

## Gotchas

- Add-Type with System.Drawing: run under `powershell.exe` (5.1) with `-ReferencedAssemblies System.Drawing`. PowerShell 7 needs
  System.Drawing.Common instead, and lacks the WinForms clipboard calls in an STA thread by default.
- CrowdStrike Falcon and Defender for Endpoint run here. Two self-built helpers were quarantined: a clipboard dump/backup tool on
  launch, and a WinForms test target right after clipboard-setting modes were added to it. Prefer built-in programs (Character Map)
  as stand-ins. Ask before running new clipboard or keyboard binaries, and never work around a quarantine.
- The tests change the user's clipboard (and add entries to its history): save `Get-Clipboard -Raw` first and `Set-Clipboard` it
  back (as plain text).
- Real nested RDP sessions can't be reached from here; the user tests those.
