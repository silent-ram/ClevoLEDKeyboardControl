# ClevoLEDKeyboardControl

[![Latest release](https://img.shields.io/github/v/release/silent-ram/ClevoLEDKeyboardControl?display_name=tag)](https://github.com/silent-ram/ClevoLEDKeyboardControl/releases/latest)
[![License](https://img.shields.io/github/license/silent-ram/ClevoLEDKeyboardControl)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4)](#system-requirements)
[![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)](global.json)

[简体中文](README.md) | English

A Windows keyboard RGB control utility for Clevo / 蓝天 (Blue Sky) and compatible laptops, featuring common lighting effects, music-reactive lighting, album-art color extraction, per-app scene automation, event feedback, and tray quick controls.

This project is actively maintained as a fork of [xuha233/ClevoRGBControl](https://github.com/xuha233/ClevoRGBControl). The architecture separates a Windows service (which drives the keyboard) from a tray application (which collects user-session data and provides the UI), talking to the low-level keyboard interface through the vendor `InsydeDCHU.dll` shipped with Control Center.

Since v3.5.0 the tray app runs on WPF (the `ColorfulLedKeyboard.Tray.Wpf` project; the output binary is still `ColorfulLedKeyboard.Tray.exe`), with a dark "instrument panel" look by default, light/dark themes, and custom accent colors. The legacy WinForms tray (the `ColorfulLedKeyboard.Tray` project) stays in the solution as a reference implementation and can still be produced via `scripts/publish.ps1 -TrayWinForms`.

## Download & Install

- [Download the latest stable installer](https://github.com/silent-ram/ClevoLEDKeyboardControl/releases/latest/download/ClevoLEDKeyboardControlSetup.exe)
- [Browse all releases and notes](https://github.com/silent-ram/ClevoLEDKeyboardControl/releases)

Install steps:

1. Download and run `ClevoLEDKeyboardControlSetup.exe` as administrator.
2. The installer sets up and starts the keyboard service, then launches the tray app for the current user.
3. After first launch, open "Settings" from the system tray and pick a lighting effect or music mode.

When upgrading from legacy `ClevoRGBControl` / `ColorfulLedKeyboard`, the installer migrates the old service and registry entries, keeping configuration backups before automation and security-permission migration. Later upgrades are a simple overwrite: the installer waits for the old service and tray to exit, repairs data-directory permissions, preserves existing configuration, and starts the new tray once secure communication is available.

## Screenshots

Dark "instrument panel" visuals with the spectrum signature; light "workbench" theme and custom accents are also available.

### Lighting Effects

![Lighting effects](docs/screenshots/effect-dark.png)

### Music Mode

![Music mode](docs/screenshots/music-dark.png)

### Scene Automation

![Scene automation](docs/screenshots/automation-dark.png)

### Software Settings (dark / light)

Dark instrument panel:

![Software settings (dark)](docs/screenshots/settings-dark.png)

Light workbench:

![Software settings (light)](docs/screenshots/settings-light.png)

### About

![About](docs/screenshots/about-dark.png)

## Features

### Settings UI & Themes

- The tray UI is fully themeable with instant switching between a dark instrument panel and a light workbench; dialogs and the tray menu follow the active theme.
- The Overview page shows the effective mode, matched rules, audible apps, current track, event feedback, service status, and final brightness at a glance.
- Final brightness is computed by the service from effect brightness, music dynamic range, scene-rule caps, and idle overrides.
- UI theme, window position/size, last visited page, and advanced-panel state are stored per-user under `LocalAppData`.
- Supports 100%–200% Windows display scaling; navigation, buttons, and main edit areas adapt to DPI.

### Lighting Effects

- Static color, RGB cycle, single-color breathing, color-sequence breathing, pulse, heartbeat, and off.
- Custom multi-color sequences with per-color dwell, transition, and breathing options.
- The software default preset and custom effect presets use stable IDs, so renaming never breaks scene references.
- Global brightness plus idle dimming and idle lights-off.

### Music Mode

- Keyboard brightness and color driven by audio levels, preserving the original analysis algorithm and tuning parameters.
- Multi-color rhythm, plus "rhythm groove" and "beat response" modes; noise gate, sensitivity, attack/release, and band range are all configurable.
- Works with speakers, wired headsets, and Bluetooth headphones — anything that is the Windows default output device.
- Ships with a built-in "通用" (universal) preset and supports up to 8 custom music presets.
- The music page can bind directly to a running player; the PID is only used for first-time confirmation, and the binding follows the process identity after the player restarts.
- Player binding is independent of scene automation: with automation off, manual music mode still uses the specified app's audio and album colors.

### Album-Art Colors

- Reads track, artist, and artwork from the Windows global media session — no player-specific APIs or whitelists.
- Automatically correlates desktop players and Microsoft Store/UWP players by process identity; multiple players can't steal each other's artwork source.
- Re-discovers automatically when bindings, media sessions, or player processes change; a confirmed source stays valid while that player runs.
- Choose music preset colors, a dominant album color, or a 3–5 color album palette.
- Filters transparent, near-black, near-white, and low-saturation pixels, and boosts too-dark colors for keyboard visibility.
- Caches album palettes per song; on track change the previous palette holds until the new one arrives, then transitions over ~800 ms.
- The music page shows the media-session match, current track, actual color values, and the palette strip.

Album extraction depends on the player publishing media sessions and artwork to Windows. Players that only expose an audio session still get per-app music reactivity, falling back to music preset colors.

### Scene Automation

Automation has three rule types with a fixed base priority:

```text
audible music apps
→ foreground lighting apps
→ time schedules
→ user's manual base mode
→ idle dim / lights-off final override
```

- **Music apps**: enter after ~300 ms of continuous audio, exit after ~2 s of silence; supports process trees, music presets, album colors, brightness caps, and event policies.
- **Lighting apps**: switch effect presets by foreground app; music rules keep the music mode while lighting rules may still adjust brightness and event policies.
- **Time schedules**: weekdays, normal ranges, and overnight ranges.
- When multiple music apps play at once, the foreground player wins; if all are in the background, the first matching music rule in list order is used.
- With no rule matched, the user's manual base settings are restored automatically.
- Brightness caps take the minimum across music rules, foreground lighting rules, and idle overrides.
- A built-in scene simulator inspects the final match (time, foreground app, playing app, idle) without touching the lights.
- Rule health checks flag missing presets, empty processes, rules that would be shadowed, and players that need rebinding.

### Event Feedback

- Typing flash: briefly raises keyboard brightness while typing, in both effect and music modes.
- Windows notification flash: overlays a flash when system notifications arrive.
- Music and lighting rules can inherit the global setting or force event feedback on/off.
- Idle lights-off is the final override and suppresses all output, including notification flashes.

### Tray Quick Controls

Right-click the tray icon for common actions:

- See the current mode, player, track, matched scene, and album status.
- Toggle scene automation, or individual music / lighting / schedule rules.
- Switch the base effect or music preset used when no scene matches.
- Bind the currently audible app; switch between preset colors, dominant color, and album palette.
- Toggle typing flash and notification flash.
- Adjust base brightness (effect mode) or peak brightness (music mode).
- Open settings, diagnostics, the config folder, or restart the keyboard service.

The tray icon tooltip also shows the current player, track, scene, and any available update.

### Secure Communication & Config Recovery

- The tray submits configuration and user-session state to the service over a local named pipe protected by ACLs, validating protocol version, message size, and the active user session.
- Service configuration under `C:\ProgramData\ClevoLEDKeyboardControl` is read-only for regular users; the service writes changes atomically.
- Update-check state lives in the current user's `LocalAppData`, separate from the system-wide service config.
- If the config is corrupted, the faulty file is preserved and the most recent good backup is restored; advanced settings support import, export, and manual restore.
- When the secure pipe is unavailable, the settings UI goes read-only rather than writing the protected config directly; saving resumes once the service recovers.

### Update Notifications

- Checks for updates once a day by default; weekly, monthly, or off are available in settings.
- Silent checks happen at tray startup, whenever the settings window opens, and periodically during long-running sessions.
- No nagging when up-to-date or offline; when an update exists, entries appear in the tray menu, the "Software Settings" nav badge, and the update card.
- Update detection goes through the GitHub `releases/latest` page instead of the rate-limited GitHub REST API.

### Usage Telemetry

- "Participate in the improvement plan" is on by default and can be turned off anytime in settings.
- When on, the app sends one anonymous heartbeat per day containing only: a randomly generated device identifier (a UUID created on first run — not derived from hardware, accounts, or any personal data), the app version, and the timestamp.
- No personal data, hardware details, usage behavior, or file paths are ever collected; data travels over HTTPS and is stored in private cloud storage, used solely to count installs and active devices, and never shared with third parties.
- The reporting endpoint lives in Tencent Cloud (Guangzhou) and is reachable from mainland China and overseas; failed reports retry with backoff, and telemetry on/off never affects any feature.

## Recommended Workflows

### Plain Lighting Effects

1. Open settings and choose "Effect mode".
2. Pick static color, RGB cycle, breathing, pulse, etc.
3. Adjust color, brightness, and speed; save as a custom preset for reuse.

### Bind a Music Player and Album Colors

1. Start your player and play a song.
2. Go to the "Music" page and click "Bind the playing app".
3. Pick the player showing a PID and live level; when the main process and audio child process are separate, the process tree is followed automatically.
4. Choose "Dominant color" or "Album palette".
5. Check the "Media session" status; if auto-match fails, pick a session from the list manually.
6. Save and switch to music mode. Scene automation can stay off.

### Music in the Background, Work in the Foreground

1. Bind your player under "Music apps" with a music preset and album color source.
2. Add Word, your IDE, or other work apps under "Lighting apps" with a brightness cap and event policy.
3. While music plays, music mode is the base; work rules only layer brightness, typing, and notification policies.
4. About 2 seconds after music stops, the foreground effect, schedule, or manual base mode resumes automatically.

## Compatibility & Limitations

- Lighting rules match foreground process names, not window titles; music rules store process name and path and correlate with Windows media sessions.
- Browsers are treated as a whole process tree; individual tabs cannot be distinguished reliably.
- "Rhythm groove" uses the target app's own audio-session level; "beat response" enables adaptive band analysis and may pick up other apps' sounds when falling back to the system mix — the diagnostics row calls this out explicitly.
- Album colors require the player to publish artwork to the Windows media session; otherwise music preset colors are used.
- The tray app must keep running to capture the foreground app, per-app audio, and media artwork from the user session; the service sits in Session 0 and cannot see these.
- Currently targets Clevo / Blue Sky compatible models that use `InsydeDCHU.dll`; vendor-specific variants may differ.

## Driver DLL

`InsydeDCHU.dll` is a proprietary driver component bundled with the vendor Control Center and is **not** covered by this repository's GPL-3.0 license.

The installer searches, in order:

1. The installer payload.
2. The directory containing the installer.
3. Legacy install directories.
4. Common `ControlCenter`, `Control Center`, `ControlCenter3` folders.

Installation completes even without the DLL, but the service cannot drive the keyboard. Install the vendor Control Center, then re-run the installer and choose repair.

## System Requirements

- Windows 10 / Windows 11 x64
- A compatible machine's Control Center / `InsydeDCHU.dll`
- The official installer bundles the .NET 8 runtime — no separate .NET 8 Desktop Runtime needed
- Building from source requires the .NET 8 SDK or newer

Service name: `ClevoLEDKeyboardControlService`

Default install directory: `C:\Program Files\ClevoLEDKeyboardControl`

Config directory: `C:\ProgramData\ClevoLEDKeyboardControl`

Per-user update state: `%LocalAppData%\ClevoLEDKeyboardControl`

## Uninstall

Uninstall from Windows "Settings → Apps", or run the installer again and choose uninstall.

Command-line uninstall:

```powershell
ClevoLEDKeyboardControlSetup.exe /uninstall
```

## Build & Test

```powershell
dotnet build .\ColorfulLedKeyboard.sln -c Release
dotnet test .\ColorfulLedKeyboard.sln -c Release
.\scripts\publish.ps1 -Configuration Release
```

`global.json` pins the compatible .NET 8 SDK. The installer lands at:

```text
publish\ClevoLEDKeyboardControlSetup.exe
```

The publish script searches `assets\driver`, `CLEVO_DRIVER_DLL`, and common Control Center folders for `InsydeDCHU.dll`. Without it the installer is still produced and the search continues at install time.

## Fork Notice & Credits

This project has been forked from [xuha233/ClevoRGBControl](https://github.com/xuha233/ClevoRGBControl) (GPL-3.0) since 2026-06-10. The original project provided the Windows service, tray app, installer, and base keyboard control framework; this repository maintains, refactors, and extends it.

The original project in turn referenced [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting), which established the `InsydeDCHU.dll` / `SetDCHU_Data` approach.

The DCHU protocol was re-analyzed against the actual behavior of single-zone models such as the P955ET1; see [`docs/reverse-engineering/dchu-protocol-findings.md`](docs/reverse-engineering/dchu-protocol-findings.md).

Detailed change history: [NOTICE](NOTICE) and [CHANGES.md](CHANGES.md).

## License

This derivative work continues under GPL-3.0; see [LICENSE](LICENSE). The third-party vendor driver `InsydeDCHU.dll` is not covered by this repository's license — obtain it from your device vendor.
