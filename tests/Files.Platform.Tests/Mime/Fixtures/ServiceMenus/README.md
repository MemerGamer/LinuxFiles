These fixtures were copied read-only from `/usr/share/kio/servicemenus` on the development machine. Their content is preserved, including distro duplicate keys, localized execution keys and shell commands; line endings are CRLF and Ghostty's trailing blank line is removed.

Attribution below records the installed source package/version and its declared licence from the package database. These third-party fixtures retain their upstream licences.

| Fixture | Source package | Licence | Upstream |
|---|---|---|---|
| `10-rootactions-folders.desktop` | kf6-servicemenus-rootactions 1.2.0-1 | GPL-2.0-or-later | https://gitlab.com/stefanwimmer128/kf6-servicemenus-rootactions |
| `11-rootactions-files.desktop` | kf6-servicemenus-rootactions 1.2.0-1 | GPL-2.0-or-later | https://gitlab.com/stefanwimmer128/kf6-servicemenus-rootactions |
| `com.mitchellh.ghostty.desktop` | ghostty 1.3.1-2 | MIT | https://github.com/ghostty-org/ghostty |
| `converseen_import.desktop` | converseen 0.15.2.8-1 | GPL-3.0-or-later | https://github.com/Faster3ck/Converseen |
| `installfont.desktop` | plasma-workspace 6.7.5-1 | LGPL-2.0-or-later | https://invent.kde.org/plasma/plasma-workspace |
| `konsolerun.desktop` | konsole 26.08.1-1 | GPL-2.0-or-later, LGPL-2.0-or-later | https://invent.kde.org/utilities/konsole |
| `mat2.desktop` | mat2 0.15.0-1 | LGPL-3.0-or-later | https://github.com/jvoisin/mat2 |

Tests cover translated multi-action menus, safe actions beside shell actions, legacy MIME lists, authorization constraints and literal long-option value expansion, including Ghostty for a single folder. Discovery does not execute these commands.
