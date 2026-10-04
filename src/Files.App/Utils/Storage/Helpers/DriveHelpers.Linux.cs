// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// One entry of /proc/self/mountinfo, filtered down to "real" (user visible) mounts.
	/// </summary>
	public sealed record LinuxMount(string MountPoint, string FsType, string Source, string DeviceId);

	/// <summary>
	/// Linux implementation of DriveHelpers (the Windows one uses Windows.Devices.Portable and is excluded on desktop).
	/// </summary>
	public static class DriveHelpers
	{
		private const string MountInfoPath = "/proc/self/mountinfo";

		private static readonly HashSet<string> _networkFs = new(StringComparer.Ordinal)
		{
			"nfs", "nfs4", "cifs", "smb3", "smbfs", "afs", "ceph", "9p", "davfs", "fuse.sshfs", "fuse.rclone", "fuse.gvfsd-fuse",
		};

		private static readonly HashSet<string> _opticalFs = new(StringComparer.Ordinal) { "iso9660", "udf" };

		/// <summary>
		/// Reads /proc/self/mountinfo and returns the mounts that should be shown as drives (pseudo file systems are filtered out).
		/// </summary>
		public static IReadOnlyList<LinuxMount> GetMounts()
		{
			var result = new List<LinuxMount>();
			var seenDevices = new HashSet<string>(StringComparer.Ordinal);

			string[] lines;
			try { lines = SystemIO.File.ReadAllLines(MountInfoPath); }
			catch { return result; }

			foreach (var line in lines)
			{
				// id parent major:minor root mountpoint opts [optional...] - fstype source superopts
				var sep = line.IndexOf(" - ", StringComparison.Ordinal);
				if (sep < 0)
					continue;

				var pre = line[..sep].Split(' ');
				var post = line[(sep + 3)..].Split(' ');
				if (pre.Length < 6 || post.Length < 2)
					continue;

				var deviceId = pre[2];
				var mountPoint = Unescape(pre[4]);
				var fsType = post[0];
				var source = Unescape(post[1]);

				if (!IsRealMount(mountPoint, fsType, source))
					continue;

				// Skip bind mounts / btrfs subvolumes of an already listed device
				if (!_networkFs.Contains(fsType) && !seenDevices.Add(deviceId))
					continue;

				result.Add(new LinuxMount(mountPoint, fsType, source, deviceId));
			}

			return result;
		}

		private static bool IsRealMount(string mountPoint, string fsType, string source)
		{
			if (mountPoint != "/" &&
				(mountPoint.StartsWith("/snap/", StringComparison.Ordinal) ||
				 mountPoint.StartsWith("/var/lib/", StringComparison.Ordinal) ||
				 mountPoint.StartsWith("/boot", StringComparison.Ordinal) ||
				 mountPoint.StartsWith("/sys", StringComparison.Ordinal) ||
				 mountPoint.StartsWith("/proc", StringComparison.Ordinal) ||
				 mountPoint.StartsWith("/dev", StringComparison.Ordinal)))
				return false;

			if (_networkFs.Contains(fsType))
				return true;

			// Real block devices only; this drops proc, sysfs, tmpfs, cgroup, overlay, squashfs loop mounts etc.
			return source.StartsWith("/dev/", StringComparison.Ordinal) &&
				!source.StartsWith("/dev/loop", StringComparison.Ordinal) &&
				fsType != "squashfs";
		}

		// mountinfo escapes space, tab, newline and backslash as \ooo (octal)
		private static string Unescape(string value)
		{
			if (!value.Contains('\\'))
				return value;

			var sb = new System.Text.StringBuilder(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				if (value[i] == '\\' && IsOctal(value, i + 1))
				{
					sb.Append((char)Convert.ToInt32(value.Substring(i + 1, 3), 8));
					i += 3;
				}
				else
				{
					sb.Append(value[i]);
				}
			}
			return sb.ToString();

			static bool IsOctal(string s, int start)
			{
				if (start + 3 > s.Length)
					return false;
				for (int k = start; k < start + 3; k++)
					if (s[k] is < '0' or > '7')
						return false;
				return true;
			}
		}

		/// <summary>
		/// Whether the block device behind the source (e.g. /dev/sdb1) is flagged removable by the kernel.
		/// </summary>
		private static bool IsRemovableBlockDevice(string source)
		{
			try
			{
				if (!source.StartsWith("/dev/", StringComparison.Ordinal))
					return false;

				var name = SystemIO.Path.GetFileName(source);
				var sysPath = $"/sys/class/block/{name}";
				if (!SystemIO.Directory.Exists(sysPath))
					return false;

				// Partitions live below their parent disk: /sys/devices/.../sdb/sdb1
				var disk = sysPath;
				if (SystemIO.File.Exists($"{sysPath}/partition"))
				{
					var resolved = SystemIO.Directory.ResolveLinkTarget(sysPath, true)?.FullName;
					var parent = resolved is null ? null : SystemIO.Path.GetDirectoryName(resolved);
					if (parent is not null)
						disk = parent;
				}

				return SystemIO.File.ReadAllText($"{disk}/removable").Trim() == "1";
			}
			catch
			{
				return false;
			}
		}

		private static LinuxMount? FindMount(string path)
		{
			var full = path.Length > 1 ? path.TrimEnd('/') : path;
			return GetMounts()
				.Where(m => m.MountPoint == "/" || full == m.MountPoint || full.StartsWith(m.MountPoint + "/", StringComparison.Ordinal))
				.OrderByDescending(m => m.MountPoint.Length)
				.FirstOrDefault();
		}

		private static Data.Items.DriveType ClassifyMount(LinuxMount mount)
		{
			if (_networkFs.Contains(mount.FsType))
				return Data.Items.DriveType.Network;
			if (_opticalFs.Contains(mount.FsType) || mount.Source.StartsWith("/dev/sr", StringComparison.Ordinal))
				return Data.Items.DriveType.CDRom;
			if (mount.MountPoint.StartsWith("/media/", StringComparison.Ordinal) ||
				mount.MountPoint.StartsWith("/run/media/", StringComparison.Ordinal) ||
				IsRemovableBlockDevice(mount.Source))
				return Data.Items.DriveType.Removable;
			return Data.Items.DriveType.Fixed;
		}

		public static async void EjectDeviceAsync(string path)
		{
			// LINUX-TODO(udisks2): eject via org.freedesktop.UDisks2 (udisksctl unmount/power-off); currently a no-op
			await Task.CompletedTask;
		}

		public static async Task<bool> CheckEmptyDrive(string? drivePath)
		{
			if (string.IsNullOrWhiteSpace(drivePath))
				return false;

			var drivesViewModel = Ioc.Default.GetRequiredService<DrivesViewModel>();

			var matchingDrive = drivesViewModel.Drives.Cast<DriveItem>().FirstOrDefault(x => drivePath.StartsWith(x.Path!, StringComparison.Ordinal));
			if (matchingDrive is null || matchingDrive.Type != Data.Items.DriveType.CDRom || matchingDrive.MaxSpace != ByteSizeLib.ByteSize.FromBytes(0))
				return false;

			var ejectButton = await DialogDisplayHelper.ShowDialogAsync(
				Strings.InsertDiscDialogTitle.GetLocalizedResource(),
				string.Format(Strings.InsertDiscDialogText.GetLocalizedResource(), matchingDrive.Path),
				Strings.InsertDiscDialog_OpenDriveButton.GetLocalizedResource(),
				Strings.Close.GetLocalizedResource());
			if (ejectButton)
				EjectDeviceAsync(matchingDrive.Path!);
			return true;
		}

		public static Task<StorageFolderWithPath?> GetRootFromPathAsync(string? devicePath)
		{
			// LINUX-TODO(mtp): MTP/gvfs and network share roots; on Linux normal paths work with StorageFolder.GetFolderFromPathAsync
			return Task.FromResult<StorageFolderWithPath?>(null);
		}

		// LINUX-TODO(mtp): MTP devices (gvfs mtp://) are not recognised
		public static bool IsMtpPath(string path) => false;

		public static bool IsNetworkPath(string path)
		{
			try
			{
				return FindMount(path) is { } mount && _networkFs.Contains(mount.FsType);
			}
			catch
			{
				return false;
			}
		}

		public static Task<bool> IsNetworkStorageItemAsync(string path)
			=> Task.Run(() => IsNetworkPath(path));

		public static Data.Items.DriveType GetDriveType(SystemIO.DriveInfo drive)
		{
			try
			{
				var mountPoint = drive.Name.Length > 1 ? drive.Name.TrimEnd('/') : drive.Name;
				var mount = GetMounts().FirstOrDefault(m => m.MountPoint == mountPoint);
				if (mount is not null)
					return ClassifyMount(mount);
			}
			catch
			{
			}

			return drive.DriveType switch
			{
				SystemIO.DriveType.CDRom => Data.Items.DriveType.CDRom,
				SystemIO.DriveType.Fixed => Data.Items.DriveType.Fixed,
				SystemIO.DriveType.Network => Data.Items.DriveType.Network,
				SystemIO.DriveType.NoRootDirectory => Data.Items.DriveType.NoRootDirectory,
				SystemIO.DriveType.Ram => Data.Items.DriveType.Ram,
				SystemIO.DriveType.Removable => Data.Items.DriveType.Removable,
				_ => Data.Items.DriveType.Unknown,
			};
		}

		public static string GetExtendedDriveLabel(SystemIO.DriveInfo drive)
		{
			return SafetyExtensions.IgnoreExceptions(() =>
			{
				if (drive.Name == "/")
					return "/";

				var label = drive.VolumeLabel;
				return string.IsNullOrWhiteSpace(label) || label == drive.Name
					? SystemIO.Path.GetFileName(drive.Name.TrimEnd('/'))
					: label;
			}) ?? "";
		}

		public static Task<StorageItemThumbnail?> GetThumbnailAsync(StorageFolder folder)
		{
			// LINUX-TODO(thumbnails): drive thumbnails; the generic drive icon is used instead
			return Task.FromResult<StorageItemThumbnail?>(null);
		}
	}
}
