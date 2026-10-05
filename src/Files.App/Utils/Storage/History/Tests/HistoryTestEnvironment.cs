// Copyright (c) Files Community
// Licensed under the MIT License.

#if STORAGE_HISTORY_TESTS
global using Files.App.Data.Enums;
global using Files.App.Helpers;
global using Files.App.Utils.Storage;
global using Files.App.Utils.StatusCenter;
global using CommunityToolkit.Mvvm.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection;
global using OwlCore.Storage;
global using System.ComponentModel;
global using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.App.Utils.Storage
{
	// UI-only dependencies; the operations, history, progress and platform services are linked unchanged.
	public interface IShellPage
	{
		IFilesystemHelpers FilesystemHelpers { get; }
		ShellViewModel GetRequiredShellViewModel();
	}

	public sealed class ShellViewModel
	{
		public Task RemoveFileOrFolderAsync(string path) => Task.CompletedTask;
	}

	internal static class App
	{
		public static ILogger Logger { get; } = NullLogger.Instance;
		public static StorageHistoryWrapper HistoryWrapper { get; } = new();
	}

	internal static class DialogDisplayHelper
	{
		public static Task ShowDialogAsync(string title, string message) => Task.CompletedTask;
	}

	internal static class LogPathHelper
	{
		public static string RedactPath(string path) => path;
	}

	internal static class StatusCenterHelper
	{
		public static void AddCard_RestoreWarning(IEnumerable<string> paths) { }
	}

	internal static class TestExtensions
	{
		public static List<T> CreateList<T>(this T item) => [item];
		public static Task<List<T>> ToListAsync<T>(this IEnumerable<T> items) => Task.FromResult(items.ToList());
		public static string GetLocalizedResource(this string key) => key;
	}

	internal static class Strings
	{
		public const string AccessDenied = "AccessDenied";
		public const string AccessDeniedCreateDialogText = "AccessDeniedCreateDialogText";
		public const string ErrorDialogNameNotAllowed = "ErrorDialogNameNotAllowed";
		public const string ErrorDialogThisActionCannotBeDone = "ErrorDialogThisActionCannotBeDone";
		public const string FileNotFoundDialogText = "FileNotFoundDialogText";
		public const string FileNotFoundDialogTitle = "FileNotFoundDialogTitle";
		public const string ItemAlreadyExistsDialogContent = "ItemAlreadyExistsDialogContent";
		public const string ItemAlreadyExistsDialogTitle = "ItemAlreadyExistsDialogTitle";
		public const string RenameErrorItemDeletedText = "RenameErrorItemDeletedText";
		public const string RenameErrorItemDeletedTitle = "RenameErrorItemDeletedTitle";
	}
}
#endif
