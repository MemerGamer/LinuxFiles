# P4-B verification

- Normal desktop build: passed (`net10.0-desktop`, low priority, two MSBuild workers, node reuse disabled).
- History project: build passed with warnings as errors; 12 tests passed. Covers copy/move/rename/trash undo,
  redo, restore identity, permanent deletion, partial failure, and conflicts preserving unrelated files.
- Platform project: build passed with warnings as errors. Suite: 720 passed, 4 failed, 7 skipped (731 total).
  The four failures are the installed-font render test and three `TrustedNativeDirectory` tests.
  In this sandbox `/`, `/usr` and `/home` have UID 65534; those unmodified security checks require root/current-user
  ownership. Xvfb is unavailable, so seven clipboard integration tests skip. The platform suite does not compile
  the app files changed by this work package. The full platform-test gate needs a run outside this sandbox.
- Compat-off ratchet: passed, no new keys; 46 remaining, 11 fixed. Baseline left unchanged.
- Owned-source coupling scan: zero matches for `BaseStorage|StorageItems\.|Windows\.Win32|PInvoke\.|Win32Helper\.`
  outside `*.Windows.cs`, excluding P4-K's `FileSizeCalculator.cs`.
- The three Windows implementation moves are byte-identical to the parent commit, retaining their original
  LF endings. Edited and new sources use CRLF/tabs. Windows execution/build and UI smoke were not verified here.

## Fixed compat-off keys

```text
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0103: The name 'SHOW_WINDOW_CMD' does not exist in the current context
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0122: 'HRESULT' is inaccessible due to its protection level
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0122: 'HWND' is inaccessible due to its protection level
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0234: The type or namespace name 'FILEOPERATION_FLAGS' does not exist in the namespace 'Windows.Win32.UI.Shell' (are you missing an assembly reference?)
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0234: The type or namespace name 'PROPERTYKEY' does not exist in the namespace 'Windows.Win32.Foundation' (are you missing an assembly reference?)
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0234: The type or namespace name 'SLR_FLAGS' does not exist in the namespace 'Windows.Win32.UI.Shell' (are you missing an assembly reference?)
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0246: The type or namespace name 'Disposable' could not be found (are you missing a using directive or an assembly reference?)
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0246: The type or namespace name 'SHOW_WINDOW_CMD' could not be found (are you missing a using directive or an assembly reference?)
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0246: The type or namespace name 'ShellFileOperations2' could not be found (are you missing a using directive or an assembly reference?)
src/Files.App/Utils/Storage/Operations/FileOperationsHelpers.cs: error CS0246: The type or namespace name 'ShellItem' could not be found (are you missing a using directive or an assembly reference?)
src/Files.App/Utils/Storage/Operations/FilesystemHelpers.cs: error CS0234: The type or namespace name 'FileSystem' does not exist in the namespace 'Windows.Win32.Storage' (are you missing an assembly reference?)
```

## Remaining seams

The desktop compatibility facade marks Windows shortcut parsing/editing/icons, principal picking and shutdown
operation tracking with `LINUX-TODO`. The creation result still uses the existing `IStorageItem` return contract
until P4-H migrates `UIFilesystemHelpers`; operation inputs and history entries stay on `IStorageItemWithPath`.
Existing limitations remain: transfer staging can clash when a requested name differs from the source name,
and trash restore honours the recorded original location rather than an arbitrary destination.
