# AGENTS.md

AltSpacy: a WPF launcher (global hotkey -> `key value` URL shortcuts, Win+R-style commands, Windows actions,
calculator, email/web/Teams, clipboard suggestions, frecency). A dependency-free alternative to Flow Launcher (whose
`squirrel.windows` NuGet dependency some corporate networks block). Public, GPL-3.0 (`LICENSE`); the name and logo are
reserved (`TRADEMARKS.md`). Keep the repo free of employer- or person-specific hosts, emails and paths.

- Toolchain: `net10.0-windows` (LTS); any .NET SDK 10+ builds it (`global.json` rolls forward). No third-party runtime
  packages. Tests use xunit.
- Test: `dotnet test AltSpacy.slnx` (~275 tests, ~20 s). Window tests (`WindowTests.cs`, `MainWindowTests.cs`) open the
  real windows off-screen on an STA thread via `tests/AltSpacy.Tests/Ui.cs` and click through UI Automation; `Ui.Run`
  serializes them (WPF resource lookup races across parallel UI threads). Never let
  tests open a browser, launch Teams/mail, flip the theme or touch the real Run key: inject `BarServices` fakes and a
  `StartupRegistration` on a throwaway key. `TestSetup.cs` redirects the log away from %APPDATA%.
- Tests use `TestConfig` (a work-style Jira/GitLab config on example.com hosts). The shipped `shortcuts.default.json`
  holds public sites only (Google, GitHub, ...) and must stay generic (tested).
- Release build: `./build.ps1` (PowerShell 5.1 or 7) runs the tests, publishes both downloads, smoke-starts each with
  `--export-icon`, and writes `dist\release\` (versioned exes, `SHA256SUMS.txt`, `release-notes.md` from `ReleaseNotes`).
  - `FrameworkDependent.pubxml` -> `*-x64-dotnet10.exe` (~0.4 MB, needs .NET Desktop Runtime 10+, `RollForward=Major`).
  - `SelfContained.pubxml` -> `*-x64-standalone.exe` (~75 MB). WPF needs `IncludeAllContentForSelfExtract`
    (DirectWriteForwarder is C++/CLI); the NETSDK1244 warning is expected.
  - Both are single-file: `AppContext.BaseDirectory` may be a temp extraction folder, use `Environment.ProcessPath`.
  - `Directory.Build.props` sets `PathMap` for Release so embedded PDBs hold `/_/src/...`, not local user paths.
  - Exit a copy running from `dist` first (`:exit`), or publish can't overwrite it.
- CI: `.github/workflows/ci.yml` runs `build.ps1` on pushes/PRs. Pushing tag `v<Version>` runs `release.yml`, which
  checks the tag matches `<Version>` and publishes the GitHub release with both exes and checksums.
- Data lives in `%APPDATA%\AltSpacy` (`AppPaths`; override with `%ALTSPACY_HOME%`): `shortcuts.json`, `history.json`,
  `altspacy.log`, `icons\`. An old `shortcuts.json` next to the exe is migrated once on startup.
- Renamed from Omnibar in 1.5.0. On start, `AppPaths.MigrateFrom` copies `%APPDATA%\Omnibar` (config, history, icons)
  once, and `StartupRegistration.TakeOverFrom` replaces an `Omnibar` Run value. Keep those old names there only.
- Start with Windows: HKCU Run value `AltSpacy` = `"<exe>" --background` (no bar on start). Toggle in Settings.
- `{project}` shortcuts: first word is a `projects` alias (or a group/repo path when the shortcut has no
  `defaultProject`), else `defaultProject`. See `ShortcutResolver.Resolve`.
- Teams: `t <email>` uses the supported `msteams:/l/chat/0/0?users=` link. `t <name>` has no supported link, so
  `TeamsLauncher.Search` opens Teams, sends Ctrl+E and types the name (SendKeys, escaped).
- Favicons: some servers redirect https -> http; `IconService` follows same-host redirects but stays on https. It sends
  a User-Agent (Wikipedia/Stack Overflow 403 without one). Failed icons retry after `RetryAfter` (5 min).
- Modeless windows (Settings, About) close via explicit `Click` handlers: `IsCancel` only works for `ShowDialog`.
- Theming: the bar uses DWM acrylic (`Backdrop.cs`) with palettes in `MainWindow.ApplyPalette`. It must keep
  `WindowStyle="None"` (tested): with a caption, DWM paints a title-bar strip, tinted by "Show accent color on title
  bars", across the top of the extended frame. Settings/About use
  `ThemeMode="System"` + `Brand.xaml`. Don't add `BasedOn="{StaticResource {x:Type DataGridRow}}"` overrides in Fluent
  windows (they resolve to the classic theme: white rows in dark mode).
- Visual check: screenshot the real screen (UIA bounding rect + `Graphics.CopyFromScreen`, DPI-aware). In PowerShell
  E2E scripts don't name a function `Type` (alias of Get-Content) and wrap single-item arrays with `return ,@(...)`.
- Version: `<Version>` in `AltSpacy.csproj`; add the matching first entry to `ReleaseNotes.All` (`SmartInput.cs`), tested.
- About shows copyright (`<Copyright>` in the csproj) and the GPL/no-warranty notice (GPL section 5d): keep them.
- Logo is drawn in code (`Logo.cs`); regenerate the exe icon with
  `dotnet run --project src/AltSpacy -- --export-icon src\AltSpacy\Assets\AltSpacy.ico`.
