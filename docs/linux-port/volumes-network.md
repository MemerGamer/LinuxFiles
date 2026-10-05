# Volumes and network locations (W-NET)

## Volumes (UDisks2)
- `IVolumeService` (`Files.Platform.Abstractions/Volumes`) is implemented by `UDisks2VolumeService` over `org.freedesktop.UDisks2` on the **system** bus (`Tmds.DBus.Protocol`).
- Listing: block devices that have a `Filesystem` interface and are not `HintIgnore`. Unmounted internal system partitions, loop devices and plumbing mounts (`/boot`, `/snap`, ...) are hidden. Unmounted removable volumes are listed so the sidebar can offer them.
- Mount: `Filesystem.Mount` with `auth.no_user_interaction=false`, so polkit prompts when needed. Unmount: `Filesystem.Unmount`. Eject: unmounts every mounted volume of the drive, then `Drive.Eject` (or `Drive.PowerOff` when the drive is not ejectable). `PowerOffAsync` calls `Drive.PowerOff`.
- Change notifications: every signal of `org.freedesktop.UDisks2` (InterfacesAdded/Removed, PropertiesChanged) schedules a debounced re-read that is diffed against the previous list. `LinuxStorageDeviceWatcher` re-scans on those events. Without UDisks2 it falls back to polling `/proc/self/mountinfo` every 2 s; with it, mountinfo is only a 30 s safety net for mounts nobody announces (hand-mounted cifs/nfs).
- Unmounted volumes appear in Drives with the pseudo path `udisks2:<object path>`; clicking one mounts it and opens the mount point.
- `IVolumeService` is constructed with an optional bus address. **Tests always pass a private bus** with `FakeUDisks2` (tests/Files.Platform.Tests/Volumes). Headless app runs should set `DBUS_SYSTEM_BUS_ADDRESS=unix:path=/nonexistent` so they never reach the real system bus.
- Format: opens `gnome-disks` (with `--block-device=`) or `partitionmanager` when installed; otherwise the format commands are hidden.

## GVfs and network locations
- `INetworkLocationService` lists `$XDG_RUNTIME_DIR/gvfs/*` (override: `$FILES_GVFS_DIR`, used by headless runs so no gvfs daemon is needed). Directory names such as `smb-share:server=nas,share=media` or `sftp:host=h,user=u` are parsed into friendly names and `gio` URIs (`GvfsNameParser`). MTP and camera mounts show under Drives, everything else under Network (sidebar section and Network locations widget). The sidebar "Network" entry opens the gvfs directory.
- Connect to server (widget menu "Map network drive"): runs `gio mount <uri>`; Disconnect and Eject run `gio mount -u`. `gio` is started with arguments (no shell) and stdin closed. Only `scheme://...` URIs are accepted.
- **Credentials**: Files never asks for or stores passwords. `gio` uses credentials saved by the desktop keyring (libsecret/gnome-keyring/kwallet) or the `user@` part of the URI. Without saved credentials a password-protected share fails with "could not connect". LINUX-TODO(network-auth): feed a password prompt to gio's stdin or use a GMountOperation askpass helper.
- FluentFTP based FTP browsing is unchanged.

## Remaining LINUX-TODOs
- `udisks2-luks`: encrypted volumes.
- `network-discovery`: browsing the LAN (Avahi/`network:///`).
- `network-auth`: password prompts for `gio mount`.
- `gvfs-tracker`: `org.gtk.vfs.MountTracker` on the session bus (the FUSE directory is used instead).
