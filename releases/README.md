# Releases

Packaged builds, laid out so the zip extracts straight into an SPT install:
`user/mods/Blackjack/`.

| Version | Built for | Notes |
| --- | --- | --- |
| 0.1.0 | SPT 4.1.3 | Server only -- no client plugin, so nothing to play in game yet. |

Symbols (`.pdb`) ship deliberately. None of this has run against a real server, and
a stack trace without line numbers is the difference between a report that can be
fixed and a shrug.

Rebuild with:

```
dotnet build src/Blackjack.Server/Blackjack.Server.csproj -c Release
```

then stage `Blackjack.Server.dll`, `Blackjack.Game.dll` and `config.json` under
`user/mods/Blackjack/` and zip it. Keep forward slashes in the entry names -- some
SPT servers run on Linux, where a backslash entry extracts as one literal filename.
