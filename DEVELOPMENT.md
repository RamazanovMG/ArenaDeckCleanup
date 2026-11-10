# Arena Deck Cleanup

Windows x64 plugin for MTG Arena: choose decks to keep, review the remainder,
and delete them through Arena's internal deck API. No simulated mouse clicks.

## Build and install

Requires Windows, an installed Mono-based MTG Arena client, PowerShell 5.1+
and the .NET 8 runtime or SDK. Building uses your own Arena assemblies; game
assemblies and decompiled game source are not distributed here.

From PowerShell in this folder:

```powershell
./build.ps1
./test.ps1
# Close Arena before installing; elevation may be needed for Program Files.
./install.ps1
```

Common standalone/Steam locations and registered installations are detected.
For other locations or multiple installations, pass the same explicit path:

```powershell
./build.ps1 -GamePath 'D:/Games/MTGA'
./install.ps1 -GamePath 'D:/Games/MTGA'
./uninstall.ps1 -GamePath 'D:/Games/MTGA'
```

Dependencies are downloaded from official BepInEx GitHub releases and NuGet,
with pinned versions and SHA-256 checks. They are stored in ignored `.tools/`.
BepInEx 5.4.23.5 is installed in the isolated `DeckCleanupRuntime` folder.
The installer refuses to overwrite an unrelated loader or modified files.

## Use

Open **Decks > F8**. Large green checked rows marked KEEP are protected.
Empty red outlined boxes marked DELETE are deletion candidates. Click anywhere
on a row to toggle it. Review deletion, then confirm once. New decks default
to KEEP. Keep all/none affect all decks, even when search is filtering the list.

Deletion runs in batches of four after backups and inventory revalidation.
Server round trips mean it is not literally instantaneous. An uncertain
server response stops the operation without automatic retries.

## Backups and recovery

Data is per Windows user at `%LOCALAPPDATA%/ArenaDeckCleanup/`:

- `preferences.json`: remembered choices by deck ID.
- `backups/<run>/`: full JSON backups and operation results.
- `exports/`: Arena import text generated from saved decks.

Choose **F8 > Backups > Copy for Import**, close the panel, then click Arena's
**Import** button. This recreates the card list including commander/sideboard;
set name, format and cosmetics as needed. Importing an existing deck creates
a duplicate. Cards are never automatically crafted. Original JSON is retained.

To migrate from the earlier personal version, close Arena and copy the contents
of its `permanent/data` directory into the data directory above. Copy the older
`state/backup.json` to `legacy-backup.json` there. Uninstall the old loader using
its own uninstall script before installing this version. Never upload that data.

## Updates and removal

Future Arena updates may replace loader files or change internal APIs. Reinstall
if loader files were removed; API changes may require source updates/rebuilding.
This is an unofficial plugin and future compatibility is not guaranteed.

Close Arena and run `./uninstall.ps1`. Only tracked, unchanged plugin files are
removed. Backups and preferences are retained. Other plugins are not loaded by
this isolated runtime. An existing different loader requires manual resolution.

## Development / uploading

Source is in `src/`; selection policy tests are in `tests/`.
`./test.ps1` runs checks for ID-based protection, inventory changes and defaults.
Do not commit `.tools`, `dist`, game assemblies, logs, backups or preferences.
The source upload ZIP contains only source, scripts and this documentation.

Tested against Arena 2026.65.0.14672 on Windows x64. Recovery export was checked
with a 100-card saved Brawl deck; no restoration or deletion runs during tests.

## Distribution

The main README is for end users. Install.cmd and Uninstall.cmd launch
scripts/Setup.ps1, which handles elevation, folder selection, progress and
friendly error messages. payload/ArenaMassDelete.dll is the prebuilt app and
must be included in source downloads and releases. build.ps1 refreshes it.
End users do not need dotnet or a compiler. Setup downloads the pinned,
checksum-verified BepInEx package; an internet connection is required.

After downloading dependencies, tests/InstallTests.ps1 tests installation,
reinstallation, modified-file protection and removal in a temporary fake game
folder. It deliberately leaves the fixture available for inspection.
