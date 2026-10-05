// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Permissions;
using System.IO;

namespace Files.App.ViewModels.Properties
{
	/// <summary>
	/// Backs the Linux "Permissions" page: owner, group and the read/write/execute bits for owner, group and others.
	/// </summary>
	public sealed partial class PermissionsViewModel : ObservableObject
	{
		private const UnixFileMode PermissionBits = (UnixFileMode)0x1FF;
		private const UnixFileMode ExecuteBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

		private readonly IFilePermissionsService _service = Ioc.Default.GetRequiredService<IFilePermissionsService>();

		private FilePermissionsInfo? _info;

		public string Path { get; }

		public PermissionsViewModel(string path)
		{
			Path = path;
			Reload();
		}

		[ObservableProperty]
		public partial bool IsAvailable { get; set; }

		[ObservableProperty]
		public partial bool CanChangeMode { get; set; }

		[ObservableProperty]
		public partial bool CanChangeOwner { get; set; }

		public bool IsReadOnlyNoteVisible => IsAvailable && !CanChangeMode;

		public bool IsOwnerNoteVisible => IsAvailable && CanChangeMode && !CanChangeOwner;

		[ObservableProperty]
		public partial bool IsDirectory { get; set; }

		public bool IsFile => !IsDirectory;

		[ObservableProperty]
		public partial string OwnerName { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string GroupName { get; set; } = string.Empty;

		[ObservableProperty]
		public partial bool ApplyRecursively { get; set; }

		[ObservableProperty]
		public partial bool OwnerRead { get; set; }

		[ObservableProperty]
		public partial bool OwnerWrite { get; set; }

		[ObservableProperty]
		public partial bool OwnerExecute { get; set; }

		[ObservableProperty]
		public partial bool GroupRead { get; set; }

		[ObservableProperty]
		public partial bool GroupWrite { get; set; }

		[ObservableProperty]
		public partial bool GroupExecute { get; set; }

		[ObservableProperty]
		public partial bool OtherRead { get; set; }

		[ObservableProperty]
		public partial bool OtherWrite { get; set; }

		[ObservableProperty]
		public partial bool OtherExecute { get; set; }

		/// <summary>
		/// Gets or sets whether the file may be run as a program; mirrors the three execute bits.
		/// </summary>
		public bool IsExecutable
		{
			get => OwnerExecute || GroupExecute || OtherExecute;
			set
			{
				OwnerExecute = value;
				GroupExecute = value;
				OtherExecute = value;
				OnPropertyChanged();
			}
		}

		public bool HasPendingMode => _info is not null && (CurrentMode & PermissionBits) != (_info.Mode & PermissionBits);

		public UnixFileMode CurrentMode
		{
			get
			{
				// Special bits (setuid, setgid, sticky) are preserved
				var mode = (_info?.Mode ?? 0) & ~PermissionBits;
				if (OwnerRead) mode |= UnixFileMode.UserRead;
				if (OwnerWrite) mode |= UnixFileMode.UserWrite;
				if (OwnerExecute) mode |= UnixFileMode.UserExecute;
				if (GroupRead) mode |= UnixFileMode.GroupRead;
				if (GroupWrite) mode |= UnixFileMode.GroupWrite;
				if (GroupExecute) mode |= UnixFileMode.GroupExecute;
				if (OtherRead) mode |= UnixFileMode.OtherRead;
				if (OtherWrite) mode |= UnixFileMode.OtherWrite;
				if (OtherExecute) mode |= UnixFileMode.OtherExecute;
				return mode;
			}
		}

		public bool IsOwnerEdited => _info is not null && OwnerName != _info.OwnerName;

		public bool IsGroupEdited => _info is not null && GroupName != _info.GroupName;

		partial void OnOwnerExecuteChanged(bool value) => OnPropertyChanged(nameof(IsExecutable));

		partial void OnGroupExecuteChanged(bool value) => OnPropertyChanged(nameof(IsExecutable));

		partial void OnOtherExecuteChanged(bool value) => OnPropertyChanged(nameof(IsExecutable));

		public void Reload()
		{
			if (!_service.TryGetPermissions(Path, out var info))
			{
				_info = null;
				IsAvailable = false;
				return;
			}

			_info = info;
			IsAvailable = true;
			CanChangeMode = info.CanChangeMode;
			CanChangeOwner = info.CanChangeOwner;
			IsDirectory = info.IsDirectory;
			OnPropertyChanged(nameof(IsFile));
			OnPropertyChanged(nameof(IsReadOnlyNoteVisible));
			OnPropertyChanged(nameof(IsOwnerNoteVisible));
			OwnerName = info.OwnerName;
			GroupName = info.GroupName;

			var mode = info.Mode;
			OwnerRead = mode.HasFlag(UnixFileMode.UserRead);
			OwnerWrite = mode.HasFlag(UnixFileMode.UserWrite);
			OwnerExecute = mode.HasFlag(UnixFileMode.UserExecute);
			GroupRead = mode.HasFlag(UnixFileMode.GroupRead);
			GroupWrite = mode.HasFlag(UnixFileMode.GroupWrite);
			GroupExecute = mode.HasFlag(UnixFileMode.GroupExecute);
			OtherRead = mode.HasFlag(UnixFileMode.OtherRead);
			OtherWrite = mode.HasFlag(UnixFileMode.OtherWrite);
			OtherExecute = mode.HasFlag(UnixFileMode.OtherExecute);
		}

		/// <summary>
		/// Bits the user turned on and off relative to what is on disk, used for the recursive change.
		/// </summary>
		public (UnixFileMode Set, UnixFileMode Clear) GetChangedBits()
		{
			var original = (_info?.Mode ?? 0) & PermissionBits;
			var current = CurrentMode & PermissionBits;
			return (current & ~original, original & ~current);
		}

		/// <summary>
		/// Writes the edited mode (and owner/group when permitted) to disk. Returns the number of recursive failures.
		/// </summary>
		public async Task<int> ApplyAsync(bool recursive, CancellationToken cancellationToken)
		{
			if (_info is null || !CanChangeMode)
				return 0;

			var failed = 0;
			var (setBits, clearBits) = GetChangedBits();

			if (HasPendingMode)
				_service.SetMode(Path, CurrentMode);

			if (recursive && IsDirectory && (setBits != 0 || clearBits != 0))
			{
				var result = await _service.SetModeRecursiveAsync(Path, setBits, clearBits, cancellationToken);
				failed = result.Failed;
			}

			if (CanChangeOwner && (IsOwnerEdited || IsGroupEdited))
			{
				uint? ownerId = null, groupId = null;
				if (IsOwnerEdited)
					ownerId = _service.FindUserId(OwnerName) ?? (uint.TryParse(OwnerName, out var u) ? u : throw new InvalidOperationException($"Unknown user '{OwnerName}'."));
				if (IsGroupEdited)
					groupId = _service.FindGroupId(GroupName) ?? (uint.TryParse(GroupName, out var g) ? g : throw new InvalidOperationException($"Unknown group '{GroupName}'."));

				_service.SetOwner(Path, ownerId, groupId);
			}

			Reload();
			return failed;
		}
	}
}
