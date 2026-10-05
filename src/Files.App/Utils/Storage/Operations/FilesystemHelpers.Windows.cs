// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.Shell;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Utils.Storage
{
	public sealed partial class FilesystemHelpers
	{
		public static bool HasDraggedStorageItems(DataPackageView packageView)
		{
			return packageView is not null && (packageView.Contains(StandardDataFormats.StorageItems) || packageView.Contains("FileDrop"));
		}

		public static async Task<IEnumerable<IStorageItemWithPath>> GetDraggedStorageItems(DataPackageView packageView)
		{
			var itemsList = new List<IStorageItemWithPath>();
			var hasVirtualItems = false;

			if (packageView.Contains(StandardDataFormats.StorageItems))
			{
				try
				{
					var source = await packageView.GetStorageItemsAsync();
					itemsList.AddRange(source.Select(item => item.FromStorageItem()
						?? throw new InvalidOperationException("A dragged storage item could not be converted.")));
				}
				catch (Exception ex) when ((uint)ex.HResult == 0x80040064 || (uint)ex.HResult == 0x8004006A)
				{
					hasVirtualItems = true;
				}
				catch (Exception ex)
				{
					App.Logger.LogWarning(ex, ex.Message);
					return itemsList;
				}
			}

			// workaround for pasting folders from remote desktop (#12318)
			try
			{
				if (hasVirtualItems && packageView.Contains("FileContents"))
				{
					var dataObject = ShellDataObject.GetClipboard();
					if (dataObject is null)
						return itemsList;

					var descriptors = ShellDataObject.GetFileDescriptors(dataObject);
					for (var ii = 0; ii < descriptors.Count; ii++)
					{
						var descriptor = descriptors[ii];
						if (descriptor.Attributes.HasFlag(FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_DIRECTORY))
							itemsList.Add(new VirtualStorageFolder(descriptor.Name).FromStorageItem()!);
						else if (ShellDataObject.TryGetFileContents(dataObject, ii, out var stream, out var medium))
						{
							var streamContent = new ComStreamWrapper(stream!, medium);
							itemsList.Add(new VirtualStorageFile(streamContent, descriptor.Name).FromStorageItem()!);
						}
					}
				}
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, ex.Message);
			}

			// workaround for GetStorageItemsAsync() bug that only yields 16 items at most
			// https://learn.microsoft.com/windows/win32/shell/clipboard#cf_hdrop
			if (packageView.Contains("FileDrop"))
			{
				var fileDropData = await SafetyExtensions.IgnoreExceptions(
					() => packageView.GetDataAsync("FileDrop").AsTask());
				if (fileDropData is IRandomAccessStream stream)
				{
					stream.Seek(0);

					byte[]? dropBytes = null;
					int bytesRead = 0;
					try
					{
						dropBytes = new byte[stream.Size];
						bytesRead = await stream.AsStreamForRead().ReadAsync(dropBytes);
					}
					catch (COMException)
					{
					}

					if (bytesRead > 0)
					{
						IntPtr dropStructPointer = Marshal.AllocHGlobal(dropBytes!.Length);

						try
						{
							Marshal.Copy(dropBytes, 0, dropStructPointer, dropBytes.Length);
							HDROP dropStructHandle = new(dropStructPointer);

							var itemPaths = new List<string>();
							uint filesCount = PInvoke.DragQueryFile(dropStructHandle, uint.MaxValue, Span<char>.Empty);
							for (uint i = 0; i < filesCount; i++)
							{
								uint charsNeeded = PInvoke.DragQueryFile(dropStructHandle, i, Span<char>.Empty);
								uint bufferSpaceRequired = charsNeeded + 1; // include space for terminating null character
								char[] buffer = new char[bufferSpaceRequired];
								uint charsCopied = PInvoke.DragQueryFile(dropStructHandle, i, buffer);

								if (charsCopied > 0)
								{
									string path = new(buffer, 0, (int)charsCopied);
									itemPaths.Add(Path.GetFullPath(path));
								}
							}

							foreach (var path in itemPaths)
							{
								var isDirectory = Win32Helper.HasFileAttribute(path, FileAttributes.Directory);
								itemsList.Add(StorageHelpers.FromPathAndType(path, isDirectory ? FilesystemItemType.Directory : FilesystemItemType.File));
							}
						}
						finally
						{
							Marshal.FreeHGlobal(dropStructPointer);
						}
					}
				}
			}

			itemsList = itemsList.DistinctBy(x => string.IsNullOrEmpty(x.Path) ? x.Name : x.Path).ToList();
			return itemsList;
		}

	}
}
