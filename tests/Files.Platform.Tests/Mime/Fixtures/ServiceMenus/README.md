These fixtures were copied read-only from `/usr/share/kio/servicemenus` on the development machine. Their content is preserved, including distro duplicate keys, localized execution keys and shell commands; only line endings were converted to CRLF.

- `10-rootactions-folders.desktop`, `11-rootactions-files.desktop`: kf6-servicemenus-rootactions
- `com.mitchellh.ghostty.desktop`: Ghostty
- `converseen_import.desktop`: Converseen
- `installfont.desktop`, `konsolerun.desktop`: KDE
- `mat2.desktop`: mat2

Tests cover translated multi-action menus, safe actions beside shell actions, legacy MIME lists, authorization constraints and the narrowly normalized Ghostty directory action. Discovery does not execute these commands.
