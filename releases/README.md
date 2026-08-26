# Releases

Packaged builds, laid out so the zip extracts straight into an SPT install.

| Version | Built for | Layout | Notes |
| --- | --- | --- | --- |
| 0.1.0 | SPT 4.1.3 | `SPT_Runtime/user/mods/Blackjack/` | Server only -- no client plugin, so nothing to play in game yet. |

## The layout matters

4.1.x keeps the server under `SPT_Runtime\`, and `user\mods\` sits **inside** it,
beside `SPT.Server.exe`. A zip laid out as a bare `user/mods/` extracts one level
too high, and the server never scans it -- the mod is simply absent, with nothing
in the log to say so.

This was checked against a real install: all 40 mods there live under
`SPT_Runtime\user\mods\`, and the two folders sitting in that install's root
`user\mods\` have never loaded.

The earlier `Blackjack-0.1.0.zip` had the bare layout and was replaced rather than
kept, so there is no wrong one left to pick by mistake. It remains in git history.

Note this differs from 4.0.x, where the folder is `SPT\` rather than `SPT_Runtime\`.

## What ships

`Blackjack.Server.dll`, `Blackjack.Game.dll`, `config.json`, and both `.pdb`s.

SPT's own assemblies are not bundled -- the server provides them.

Symbols ship deliberately. Nothing has run against a real server, and a stack trace
without line numbers is the difference between a report that can be fixed and a
shrug.

## Rebuilding

```
dotnet build src/Blackjack.Server/Blackjack.Server.csproj -c Release
```

Then stage the five files under `SPT_Runtime/user/mods/Blackjack/` and zip them.

**Keep forward slashes in the entry names.** PowerShell's `Compress-Archive` writes
backslash entries, which extract as one literal filename on Linux. Pack with
`System.IO.Compression` or Python's `zipfile` instead.

The version lives in **two** places and they must agree:
`Blackjack.Server.csproj` `<Version>` and `ModMetadata.Version`. `SptVersion` in
that same record is a hard load gate -- outside its range the server loads nothing
and logs nothing.
