// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.Shared.Helpers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Foundation;
using Windows.Storage;

namespace Files.App.Utils.Storage
{
	// LINUX-TODO(archives): remove these inactive legacy consumer hooks after P4-C/E/H/I migrate to ArchiveFolder.
	public abstract class ZipStorageFolder : BaseStorageFolder, IPasswordProtectedItem
	{
		private static readonly ConcurrentDictionary<string, Encoding?> Encodings = new(StringComparer.Ordinal);
		public StorageCredential? Credentials { get; set; }
		public Func<IPasswordProtectedItem, Task<StorageCredential>>? PasswordRequestedCallback { get; set; }
		public static string? GetContainerPath(string path) => FileExtensionHelpers.GetArchiveContainerPath(path);
		public static bool IsZipPath([NotNullWhen(true)] string? path, bool includeRoot = true) => FileExtensionHelpers.IsZipPath(path, includeRoot);
		internal static bool TryGetEncodingForContainerPath(string path, out Encoding? encoding) => Encodings.TryGetValue(path, out encoding);
		internal static void SetEncodingForContainerPath(string path, Encoding? encoding) => Encodings[path] = encoding;
		public static Task<bool> CheckDefaultZipApp(string path) => Task.FromResult(true);
		public static IAsyncOperation<BaseStorageFolder?> FromPathAsync(string path) => Task.FromResult<BaseStorageFolder?>(null).AsAsyncOperation();
		public static IAsyncOperation<BaseStorageFolder?> FromStorageFileAsync(BaseStorageFile file) => Task.FromResult<BaseStorageFolder?>(null).AsAsyncOperation();
		public static Task<bool> InitArchive(IStorageFile file, SevenZip.OutArchiveFormat format) => Task.FromException<bool>(new NotSupportedException());
		public Task<long> GetUncompressedSize() => Task.FromException<long>(new NotSupportedException());
		public Task<bool> ValidateCredentialsAsync() => Task.FromResult(false);
		public IAsyncOperation<BaseStorageFile?> CreateFileAsync(Stream contents, string name, CreationCollisionOption options) => Task.FromException<BaseStorageFile?>(new NotSupportedException()).AsAsyncOperation();
	}
}
#endif
