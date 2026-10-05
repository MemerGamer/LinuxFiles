// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Microsoft.UI.Xaml.Controls;

namespace Files.App.Views.Layouts
{
	public abstract partial class BaseLayoutPage
	{
		private LinuxGroupedItemsSource? linuxGroupedItemsSource;

		private void UpdateLinuxGroupedItemsSource()
		{
			DisposeLinuxGroupedItemsSource();
			if (CollectionViewSource.IsSourceGrouped &&
				CollectionViewSource.Source is BulkConcurrentObservableCollection<GroupedCollection<ListedItem>> groups &&
				ItemsControl is ListViewBase list)
			{
				linuxGroupedItemsSource = new LinuxGroupedItemsSource(groups, list);
			}
		}

		private void DisposeLinuxGroupedItemsSource()
		{
			linuxGroupedItemsSource?.Dispose();
			linuxGroupedItemsSource = null;
		}
	}
}
#endif
