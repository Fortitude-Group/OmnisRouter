# OmnisRouter — winget manifest

winget package manifest for the OmnisRouter collect tray (feature `002-collect-tray-app`, task US5).

Three-file manifest set (version, installer, default-locale) referencing the released per-user MSI
with its silent-install switch. Validated with `winget validate` in the release gate; publishing to
`microsoft/winget-pkgs` is a release-process step, like the existing npm publish.

`winget install OmnisRouter` installs the same MSI. See `specs/002-collect-tray-app/research.md` D8.
