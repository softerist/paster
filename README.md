# Paster

Paster makes `Ctrl+V` work in remote desktop windows. Where the remote session
allows pasting, you get a normal paste. Where the session blocks the
clipboard, Paster types the text for you, as if you had typed it yourself.

Paster runs quietly in the background on Windows 10 and 11. In all other
windows, `Ctrl+V` stays a normal paste.

## Install

1. Get the Paster folder (download it from GitHub or clone it).
2. Open PowerShell in that folder and run:

   ```powershell
   .\setup.ps1
   ```

   If PowerShell says that running scripts is disabled, run this instead:
   `powershell -ExecutionPolicy Bypass -File .\setup.ps1`

Paster starts right away and starts again each time you sign in. It has no
window of its own; you can find it as "paster" in Task Manager. No extra
software is needed.

To update, run `.\setup.ps1` again. To remove Paster, run `.\uninstall.ps1`.

## Everyday use

1. Copy something on your computer with `Ctrl+C`, from any program.
2. Click into the remote desktop window where you want it.
3. Press `Ctrl+V`.

Paster finds out by itself whether the session accepts a normal paste:

- **If it does**, you get a normal paste: instant, and with formatting and
  pictures wherever the program supports them.
- **If it doesn't**, Paster shows a short message the first time (within about
  3 seconds) and types the text in that session from then on. The session may
  have pasted something else first (whatever was last copied inside it). If
  so, press `Ctrl+Z`, then `Ctrl+V` again.

Each session is checked on its own, so a program that shows several sessions
in one window (such as RDCMan) can paste normally in one session and type in
another.

| Keys | What it does |
| --- | --- |
| `Ctrl+V` in a remote desktop window | Pastes normally, or types where pasting is blocked |
| `Ctrl+Shift+V` | Always types the copied text, in any window |
| `Ctrl+Shift+X` | Stops typing |
| `Ctrl+Shift+C` | Copies the selected text and keeps it for typing |

Typing also stops as soon as you press a key, click, or switch to another
window.

### Which windows count as remote desktop windows

These work out of the box:

- Remote Desktop Connection (`mstsc`)
- The Remote Desktop and Windows App desktop clients (`msrdc`)
- Remote Desktop Connection Manager (`RDCMan`), mRemoteNG, Royal TS, and
  Devolutions Remote Desktop Manager
- Hyper-V virtual machine windows
- Citrix Workspace and VMware Horizon
- TeamViewer and AnyDesk
- VNC viewers (RealVNC, TigerVNC, TightVNC)
- Windows 365 and Azure Virtual Desktop in Edge or Chrome (the window titled
  "Windows App" or "Windows Cloud"). In a browser, Paster can't tell whether a
  normal paste reached the session, so `Ctrl+V` always types there.

If you installed an earlier version and never changed this list, it is
updated automatically. You can add other programs in the settings, see
[Adding a program](#adding-a-program).

## Good to know

- **Long texts:** Before typing more than 2,000 lines, Paster asks first and
  shows how long it will take. If typing gets interrupted, paste the same text
  again to continue where it stopped.
- **Code:** In remote windows, Paster types so that code editors such as
  VS Code and Notepad++ don't add extra indentation, extra closing brackets,
  or autocomplete suggestions.
- **Speed:** Remote windows are typed at about 1,000 keystrokes per second. If
  text keeps appearing after you stop it, lower "Remote speed" in the settings.
- **Remote desktop inside a remote desktop:** Paster sees only the outer
  window, so a session opened inside another session shares its result. If
  the inner session blocks pasting, `Ctrl+V` types in the outer one too. To
  let Paster check again, close that session window or run
  `& "$env:LOCALAPPDATA\Paster\paster.exe" --clear`. If typed lines come out in
  the wrong order there, raise "Pause after Enter and Tab" in the settings.
- **Copying inside a remote session:** after you copy something inside a
  session (`Ctrl+C`, `Ctrl+X`) or open a right-click menu there, `Ctrl+V`
  there is a normal paste until you copy something on your computer, so files
  and folders copied in a session paste as usual. Press `Ctrl+Shift+V` to type
  your computer's text there anyway. If you copy something on your computer
  in between, `Ctrl+V` types that, so copy the files again first. Copying with
  a toolbar button inside the session isn't noticed; use `Ctrl+C` or the
  right-click menu.
- **Passwords:** Paster never makes its own copy of text from a password
  manager. Where a normal paste already worked, `Ctrl+V` pastes it normally;
  anywhere else, Paster types it.
- **Keyboard layout:** A different keyboard layout on the remote computer is no
  problem; remote windows always get the exact characters.
- **Programs running as administrator:** Paster can't type into these. Use a
  normal `Ctrl+V` there instead.

## When something goes wrong

Paster shows a Windows notification when something unexpected happens, for
example when another program takes the focus while Paster is typing, or when a
setting can't be used. Click the notification to see the details.

If `Ctrl+V` pastes the wrong thing or nothing at all in a session, use
`Ctrl+Shift+V`: it always types.

To check on Paster at any time, run:

```powershell
& "$env:LOCALAPPDATA\Paster\paster.exe" --status
```

It also shows what `Ctrl+V` does in remote windows right now, and why.

## Settings

Open the settings with:

```powershell
& "$env:LOCALAPPDATA\Paster\paster.exe" --settings
```

Changes apply right away. The settings you're most likely to need:

| Setting | Tab | What it's for |
| --- | --- | --- |
| Type on Ctrl+V in apps | Remote windows | The programs where `Ctrl+V` is handled by Paster |
| Paste normally where the session allows it | Remote windows | Turn it off to always type in those programs |
| Remote speed | Remote windows | Lower it if text keeps coming after you stop typing |
| Pause after Enter and Tab | Remote windows | Raise it if lines get mixed up in a nested session |
| Stop typing on key press or click | General | Turn it off if your own typing shouldn't stop Paster |
| Show tray icon | General | Keeps Paster's icon next to the clock |
| Show notifications when something goes wrong | General | Turn it off to hide notifications |
| Type all characters as Unicode | Typing | Turn it on if a program on your own computer gets wrong characters |

### Adding a program

In "Type on Ctrl+V in apps", add the program's file name, separated from the
others by a comma, for example `myremote.exe`. For a remote desktop that runs
in a browser, add the browser and part of the window title, for example
`msedge.exe:My Remote Desktop`. That way, other browser tabs keep their normal
paste.

## Privacy

Paster works only on your computer. It has no network access, keeps copied
text only in memory while it runs, and doesn't record your keystrokes. To see
whether a normal paste reaches a session, Paster briefly puts an exact copy of
what you copied back on the clipboard; that copy is kept out of the Windows
clipboard history.

## More details

[TECHNICAL.md](TECHNICAL.md) explains how Paster works, every setting, all
command-line options, building from source, and known limitations.
