# OmnisRouter tray — WiX MSI

Per-user MSI for the OmnisRouter collect tray (feature `002-collect-tray-app`, task US5).

- **Scope:** per-user, no administrator elevation (`MSIINSTALLPERUSER`).
- **Payload:** the self-contained `win-x64` publish of `src/OmnisRouter.Tray`.
- **Installs:** a Start-Menu shortcut, ARP (Installed apps) metadata (publisher "Fortitude Omnis",
  version from the release tag), and a per-user "at log on" Scheduled Task `OmnisRouter Collect`
  with restart-on-failure.
- **Uninstall:** removes the app, the shortcut, and the Scheduled Task (FR-019).

Built by the `windows` job in `.github/workflows/release.yml` (WiX v5 .NET tool). See
`specs/002-collect-tray-app/research.md` D7 and `quickstart.md` scenario 7.
