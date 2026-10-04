// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.IO;
using Windows.Storage;

namespace Files.App.Utils
{
	/// <summary>
	/// Polls /proc/self/mountinfo (procfs does not support FileSystemWatcher) and raises device events when mounts appear or disappear.
	/// </summary>
	public sealed class LinuxStorageDeviceWatcher : IStorageDeviceWatcher
	{
		private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

		public event EventHandler<IFolder>? DeviceAdded;
		public event EventHandler<string>? DeviceRemoved;
		public event EventHandler? EnumerationCompleted;
		public event EventHandler<string>? DeviceModified;

		private CancellationTokenSource? _cts;
		private Dictionary<string, LinuxMount> _known = new(StringComparer.Ordinal);

		public bool CanBeStarted => _cts is null;

		public void Start()
		{
			if (_cts is not null)
				return;

			_cts = new CancellationTokenSource();
			var token = _cts.Token;
			_ = Task.Run(() => PollAsync(token), token);
		}

		public void Stop()
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}

		private async Task PollAsync(CancellationToken token)
		{
			// Initial enumeration is reported through the service's GetDrivesAsync; only record the baseline here
			_known = Snapshot();
			EnumerationCompleted?.Invoke(this, EventArgs.Empty);

			using var timer = new PeriodicTimer(PollInterval);
			try
			{
				while (await timer.WaitForNextTickAsync(token))
				{
					var current = Snapshot();

					foreach (var (mountPoint, mount) in current)
					{
						if (!_known.TryGetValue(mountPoint, out var old))
							await RaiseAddedAsync(mount);
						else if (old.Source != mount.Source || old.FsType != mount.FsType)
							DeviceModified?.Invoke(this, mountPoint);
					}

					foreach (var mountPoint in _known.Keys.Where(k => !current.ContainsKey(k)))
						DeviceRemoved?.Invoke(this, mountPoint);

					_known = current;
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private static Dictionary<string, LinuxMount> Snapshot()
			=> DriveHelpers.GetMounts().ToDictionary(m => m.MountPoint, StringComparer.Ordinal);

		private async Task RaiseAddedAsync(LinuxMount mount)
		{
			try
			{
				var drive = new DriveInfo(mount.MountPoint);
				var rootResult = await FilesystemTasks.Wrap(() => StorageFolder.GetFolderFromPathAsync(mount.MountPoint).AsTask());
				if (rootResult.Result is not { } root)
				{
					App.Logger.LogWarning($"{rootResult.ErrorCode}: Attempting to add the device, {mount.MountPoint},"
						+ " failed at the StorageFolder initialization step. This device will be ignored.");
					return;
				}

				var type = DriveHelpers.GetDriveType(drive);
				var label = DriveHelpers.GetExtendedDriveLabel(drive);
				var driveItem = await DriveItem.CreateFromPropertiesAsync(root, mount.MountPoint, label, type);

				DeviceAdded?.Invoke(this, driveItem);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, $"Failed to add the device {mount.MountPoint}");
			}
		}
	}
}
