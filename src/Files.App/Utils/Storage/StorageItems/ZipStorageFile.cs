// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;

namespace Files.App.Utils.Storage
{
	// LINUX-TODO(archives): remove this inactive type after legacy consumers migrate to ArchiveEntryFile.
	public abstract class ZipStorageFile : BaseStorageFile
	{
		public static IAsyncOperation<BaseStorageFile?> FromPathAsync(string path) => Task.FromResult<BaseStorageFile?>(null).AsAsyncOperation();
	}
}
#endif
