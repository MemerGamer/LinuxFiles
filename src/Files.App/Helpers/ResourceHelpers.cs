// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Markup;
using Windows.ApplicationModel.Resources;

namespace Files.App.Helpers
{
	[MarkupExtensionReturnType(ReturnType = typeof(string))]
	public sealed partial class ResourceString : MarkupExtension
	{
		private static readonly ResourceLoader resourceLoader = new();

		// Not called "Name": Uno's XAML generator treats a markup-extension member named Name as an x:Name inside x:Load subtrees
		public string Key { get; set; } = string.Empty;

		protected override object ProvideValue() => resourceLoader.GetString(Key);
	}
}
