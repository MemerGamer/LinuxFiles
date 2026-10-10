// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.EventArguments;
using Files.App.Helpers;
using Files.App.Views.Layouts;
using Files.App.Views.Shells;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Navigation
{
	[TestClass]
	public sealed class ArchiveNavigationTests
	{
		[TestMethod]
		public void DuplicateOpen_AfterLeaseReleasedBeforeLoadCompletes_DoesNotAddHistory()
		{
			var shell = new TestShellPage();
			var gate = new InFlightOpenGate();
			using (var lease = gate.TryEnter("tab:/a.zip"))
			{
				Assert.IsNotNull(lease);
				shell.NavigateWithArguments(typeof(BaseLayoutPage), new() { NavPathParam = "/a.zip" });
			}

			using var secondLease = gate.TryEnter("tab:/a.zip");
			Assert.IsNotNull(secondLease);
			shell.NavigateWithArguments(typeof(BaseLayoutPage), new() { NavPathParam = "/a.zip" });
			Assert.AreEqual(1, shell.NavigationCount);

			// Completing the load retains the frame's destination, so a later open is also idempotent.
			shell.NavigateWithArguments(typeof(BaseLayoutPage), new() { NavPathParam = "/a.zip" });
			Assert.AreEqual(1, shell.NavigationCount);
		}

		[TestMethod]
		public void LeavingArchive_AllowsReopeningIt()
		{
			var shell = new TestShellPage();
			foreach (var path in new[] { "/a.zip", "/folder", "/a.zip" })
				shell.NavigateWithArguments(typeof(BaseLayoutPage), new() { NavPathParam = path });
			Assert.AreEqual(3, shell.NavigationCount);
		}

		[TestMethod]
		public void DifferentArchive_SearchAndLayoutSwitch_AreNotSuppressed()
		{
			var shell = new TestShellPage();
			shell.NavigateWithArguments(typeof(BaseLayoutPage), new() { NavPathParam = "/a.zip" });
			shell.NavigateWithArguments(typeof(BaseLayoutPage), new() { NavPathParam = "/A.zip" });
			shell.NavigateWithArguments(typeof(BaseLayoutPage), new() { NavPathParam = "/A.zip", IsLayoutSwitch = true });
			shell.NavigateWithArguments(typeof(BaseLayoutPage), new() { NavPathParam = "/A.zip", IsSearchResultPage = true });
			shell.NavigateWithArguments(typeof(BaseLayoutPage), new() { NavPathParam = "/A.zip" });
			Assert.AreEqual(5, shell.NavigationCount);
		}

		private sealed class TestShellPage : BaseShellPage
		{
			public int NavigationCount { get; private set; }

			protected override void NavigateToPath(string? path, Type sourcePageType, NavigationArguments args)
			{
				NavigationCount++;
				ItemDisplay.Content = new BaseLayoutPage { Arguments = args };
			}
		}
	}
}

// Only the frame and its synchronously captured arguments are needed by the linked navigation entry point.
namespace Files.App.Views.Shells
{
	public abstract partial class BaseShellPage
	{
		protected TestFrame ItemDisplay { get; } = new();
		protected abstract void NavigateToPath(string? path, Type sourcePageType, NavigationArguments args);
		protected sealed class TestFrame
		{
			public object? Content { get; set; }
		}
	}
}

namespace Files.App.Views.Layouts
{
	public sealed class BaseLayoutPage
	{
		public NavigationArguments Arguments { get; init; } = new();
		internal string? NavigationPath => Arguments.IsSearchResultPage ? null : Arguments.NavPathParam;
	}
}

namespace Files.App.Data.EventArguments
{
	public sealed class NavigationArguments
	{
		public string? NavPathParam { get; init; }
		public bool IsSearchResultPage { get; init; }
		public bool IsLayoutSwitch { get; init; }
	}
}

namespace Files.App.Utils.Storage
{
	internal static class ZipStorageFolder
	{
		public static bool IsZipPath(string? path) => Files.Shared.Helpers.FileExtensionHelpers.IsZipPath(path);
	}
}
