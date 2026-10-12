// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Foundation.Metadata;
using Windows.System;

namespace Files.App.Utils.Storage
{
	internal sealed class StorageSenseHelper
	{
		public static async Task OpenStorageSenseAsync(string path)
		{
			// LINUX-TODO(storage-sense): no system storage cleanup page to open
			await Task.CompletedTask;
		}
	}
}
