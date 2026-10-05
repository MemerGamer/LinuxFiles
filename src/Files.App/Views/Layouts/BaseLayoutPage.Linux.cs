// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Microsoft.UI.Xaml.Controls;

namespace Files.App.Views.Layouts
{
	public abstract partial class BaseLayoutPage
	{
		private LinuxGroupedItemsSource? linuxGroupedItemsSource;

		/// <summary>
		/// Whether the list shows the flattened grouped rows, whose group headers are items that must be skipped.
		/// </summary>
		protected bool IsLinuxGrouped => linuxGroupedItemsSource is not null;

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
