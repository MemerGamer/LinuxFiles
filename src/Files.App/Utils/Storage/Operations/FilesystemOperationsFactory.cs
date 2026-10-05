// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	internal static class FilesystemOperationsFactory
	{
		public static IFilesystemOperations Create(IShellPage? associatedInstance)
		{
#if WINDOWS
			return new ShellFilesystemOperations(associatedInstance!);
#else
			return new LinuxFilesystemOperations(associatedInstance);
#endif
		}
	}
}
