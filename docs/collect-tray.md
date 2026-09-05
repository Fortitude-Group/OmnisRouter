# The collect tray (Windows)

`omnisrouter collect --watch` reads Claude Code's local transcripts and reports your subscription
usage to your OmnisVigil dashboard. On Windows you don't have to run it as a console command and
leave the window open. Install the tray instead: it runs quietly in the background from login and
shows a small icon that tells you, at a glance, whether collection is healthy.

The command line still works everywhere (Linux, CI, or if you just prefer it). The tray is the same
engine with a Windows face.

## Install

```powershell
winget install OmnisRouter
```

Or download `OmnisRouter-<version>-win-x64.msi` from the
[latest release](https://github.com/Fortitude-Group/OmnisRouter/releases/latest) and run it. The
installer is per-user, so it never asks for administrator rights, and it lands in "Installed apps"
like any other program.

The first time it runs it asks for two things: your OmnisVigil dashboard URL (already filled in with
`https://app.omnisvigil.com`) and your project key. The "Get a key from OmnisVigil" button opens the
dashboard's Connect page, where you can generate one. Paste it, save, and that's it. The key is
encrypted with your Windows account and stored in `%APPDATA%\OmnisRouter\collect.json`. It never sits
on a command line and never leaves your machine except as the Bearer token on the receipts it posts.

From then on it starts on its own every time you log in.

## The tray icon

- **Green** means it's watching and the last post went through.
- **Grey** means you've paused it.
- **Amber** means the last post failed and it's retrying. Collection isn't lost, it catches up.

Hover for the last-post time and today's count. Left-click for a small panel with the same plus any
error, and a link straight to the dashboard where the real spend and savings numbers live. Right-click
for the menu: pause and resume, open the dashboard, open or clear the logs, settings, and quit.

The tray shows liveness only, on purpose. The analytics belong in the dashboard, which already does
them well, so there's no second copy to drift out of step.

## Where things live

| What | Where |
|---|---|
| Config (URL + encrypted key) | `%APPDATA%\OmnisRouter\collect.json` |
| Logs (rolling, size-capped) | `%LOCALAPPDATA%\OmnisRouter\logs\` |
| Auto-start | a per-user "at log on" Scheduled Task, `OmnisRouter Collect`, with restart-on-failure |

## Uninstall

Remove it from "Installed apps" (or `winget uninstall OmnisRouter`). That takes the program, its
Start-Menu entry, and the login task with it. Your config is left in place in case you reinstall.

## A note on the SmartScreen warning

Until the installer is signed with a certificate, Windows SmartScreen will warn that the publisher is
unknown. Choose "More info" then "Run anyway". Signing is on the list.
