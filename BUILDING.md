# Build and regression checks

The plugin and tests target Windows and .NET Framework 4.8, matching the existing plugin target. Install the .NET SDK selected by `global.json`, then run:

```powershell
.\build.ps1
```

Use `-DotNetPath 'C:\path\to\dotnet.exe'` if the SDK is not on PATH. Framework reference assemblies restore automatically. Restore uses the committed dependency locks; `dotnet test` is not the test entry point.

The script builds Release, runs a console regression suite in both 64-bit and 32-bit processes on 64-bit Windows, and packages only `mb_NeteaseLyrics.dll`. Each test run uses an isolated directory containing the test executable/config and plugin DLL, verifying that embedded runtime dependencies load without loose dependency DLLs. A failure stops packaging. ZIPs, SHA-256 checksums and test reports are written to `artifacts/dist`.

The suite covers malformed/refused API answers, successful answers without lyrics, instrumental markers, callback metadata, file-owned Custom10 overrides, missing candidate fields, fractional/invalid durations, configuration failures and migration, request/deadline budgets, HTTPS transport failures, HTTP opt-out, cookie isolation and redirects. Controlled HTTP handlers exercise production request construction, encrypted forms, parsing and fallback. Loopback servers exercise the real client for cookies and redirect handling. No private music files, paths, audio or downloaded lyrics are included.

An optional live probe sends a fixed public query and song ID to NetEase, prints counts/status only and saves no lyric text:

```powershell
.\tests\bin\Release\net48\NeteaseLyricsTest.exe --live
```

Live availability depends on the service and network. Fixture/loopback results do not reproduce MusicBee's plugin loader, skin engine or native HTTPS environment.

## Native MusicBee review before merging

- Install the CI ZIP and verify plugin loading and all three existing lyric formats.
- Re-fetch a known song with missing tags and a background lookup while a different song is playing; verify Custom10 belongs to the requested file.
- Check desktop and `y.music.163.com/m/song` share links, no-lyrics/instrumental responses, and settings save/reopen.
- Check slow/blocked HTTPS, available legacy fallback and the HTTP opt-out inside MusicBee. The lookup has a 30-second cancellation budget, reserving 12 seconds for lyrics; native DNS/network operations can affect observed wall-clock cancellation time.
- Back up the DLL/configuration and check rollback to the previously installed upstream build.

This contribution retains the upstream matching algorithms and three output formats. Revised ranking, recording-equivalence rules, translation alignment and pronunciation displays are separate follow-up work. Assembly versioning remains with the upstream maintainer.
