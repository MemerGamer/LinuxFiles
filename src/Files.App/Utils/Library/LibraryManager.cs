// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Dialogs;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Windows.System;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace Files.App.Utils.Library
{
	public sealed partial class LibraryManager : IDisposable
	{
		public EventHandler<NotifyCollectionChangedEventArgs>? DataChanged;

		private FileSystemWatcher? librariesWatcher;
		private readonly List<LibraryLocationItem> libraries = [];
		private static readonly Lazy<LibraryManager> lazy = new(() => new LibraryManager());

		public static LibraryManager Default
			=> lazy.Value;

		public IReadOnlyList<LibraryLocationItem> Libraries
		{
			get
			{
				lock (libraries)
				{
					return libraries.ToList().AsReadOnly();
				}
			}
		}

		public LibraryManager()
		{
#if WINDOWS
			InitializeWatcher();
#endif
		}


		/// <summary>
		/// Get libraries of the current user with the help of the FullTrust process.
		/// </summary>
		/// <returns>List of library items</returns>
#if !WINDOWS
		// LINUX-TODO(libraries): shell libraries are unavailable on Linux.
		public static Task<List<LibraryLocationItem>> ListUserLibraries()
			=> Task.FromResult<List<LibraryLocationItem>>([]);
#endif

		public async Task UpdateLibrariesAsync()
		{
			lock (libraries)
			{
				libraries.Clear();
			}
			var libs = await ListUserLibraries();
			if (libs is not null)
			{
				libs.Sort();
				lock (libraries)
				{
					libraries.AddRange(libs);
				}
			}
			DataChanged?.Invoke(SectionType.Library, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}

		public bool TryGetLibrary(string? path, [NotNullWhen(true)] out LibraryLocationItem? library)
		{
			if (!Ioc.Default.GetRequiredService<Files.Platform.Abstractions.IPlatformCapabilities>().SupportsLibraries || string.IsNullOrWhiteSpace(path) || !path.EndsWith(ShellLibraryItem.EXTENSION, StringComparison.OrdinalIgnoreCase))
			{
				library = null;
				return false;
			}
			library = Libraries.FirstOrDefault(l => string.Equals(path, l.Path, StringComparison.OrdinalIgnoreCase));
			return library is not null;
		}

		/// <summary>
		/// Create a new library with the specified name.
		/// </summary>
		/// <param name="name">The name of the new library (must be unique)</param>
		/// <returns>The new library if successfully created</returns>
#if !WINDOWS
		public Task<bool> CreateNewLibrary(string name)
			=> Task.FromResult(false);
#endif

		/// <summary>
		/// Update library details.
		/// </summary>
		/// <param name="libraryFilePath">Library file path</param>
		/// <param name="defaultSaveFolder">Update the default save folder or null to keep current</param>
		/// <param name="folders">Update the library folders or null to keep current</param>
		/// <param name="isPinned">Update the library pinned status or null to keep current</param>
		/// <returns>The new library if successfully updated</returns>
#if !WINDOWS
		public Task<LibraryLocationItem?> UpdateLibrary(string libraryPath, string? defaultSaveFolder = null, string[]? folders = null, bool? isPinned = null)
			=> Task.FromResult<LibraryLocationItem?>(null);
#endif

		public (bool result, string reason) CanCreateLibrary(string name)
		{
			if (!Ioc.Default.GetRequiredService<Files.Platform.Abstractions.IPlatformCapabilities>().SupportsLibraries)
				return (false, string.Empty);

			if (string.IsNullOrWhiteSpace(name))
			{
				return (false, Strings.ErrorInputEmpty.GetLocalizedResource());
			}
			if (FilesystemHelpers.ContainsRestrictedCharacters(name))
			{
				return (false, Strings.ErrorNameInputRestrictedCharacters.GetLocalizedResource());
			}
			if (FilesystemHelpers.ContainsRestrictedFileName(name))
			{
				return (false, Strings.ErrorNameInputRestricted.GetLocalizedResource());
			}
			if (Libraries.Any((item) => string.Equals(name, item.Text, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(name, Path.GetFileNameWithoutExtension(item.Path), StringComparison.OrdinalIgnoreCase)))
			{
				return (false, Strings.CreateLibraryErrorAlreadyExists.GetLocalizedResource());
			}
			return (true, string.Empty);
		}

#if !WINDOWS
		public static Task ShowRestoreDefaultLibrariesDialogAsync()
			=> Task.CompletedTask;
#endif

#if !WINDOWS
		public static Task ShowCreateNewLibraryDialogAsync()
			=> Task.CompletedTask;
#endif



		public static bool IsLibraryPath(string path)
			=> Ioc.Default.GetRequiredService<Files.Platform.Abstractions.IPlatformCapabilities>().SupportsLibraries && !string.IsNullOrEmpty(path) && path.EndsWith(ShellLibraryItem.EXTENSION, StringComparison.OrdinalIgnoreCase);

		public void Dispose()
			=> librariesWatcher?.Dispose();
	}
}
