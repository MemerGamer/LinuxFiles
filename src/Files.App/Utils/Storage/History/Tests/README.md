# Linux storage history tests

Source-links the app's Linux operations, history replay, progress model and path contracts.
Only UI dependencies are test facades; file operations and trash use the real Linux services
with temporary files and an isolated trash data directory. No display or real user settings are used.

Run from the repository root, sequentially:

```bash
MSBUILDDISABLENODEREUSE=1 nice -n 19 dotnet build src/Files.App/Utils/Storage/History/Tests/Files.StorageHistory.Tests.csproj -nodeReuse:false -m:2 -warnaserror
nice -n 19 dotnet test --project src/Files.App/Utils/Storage/History/Tests/Files.StorageHistory.Tests.csproj --no-build
```

The test sources compile only with `STORAGE_HISTORY_TESTS`. Generated files are kept under
`/tmp/files-p4-b-history` so the app's source glob cannot include them. This project stays
inside P4-B ownership and runs alongside `tests/Files.Platform.Tests`.
