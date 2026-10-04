// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Files.Platform.Abstractions.FileOperations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.FileOperations
{
	[SupportedOSPlatform("linux")]
	public abstract class FileOperationsTestBase
	{
		protected string Root { get; private set; } = null!;

		protected string Src => Path.Combine(Root, "src");

		protected string Dst => Path.Combine(Root, "dst");

		[TestInitialize]
		public void CreateRoot()
		{
			Root = Path.Combine(Path.GetTempPath(), "files-fileops-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Src);
			Directory.CreateDirectory(Dst);
		}

		[TestCleanup]
		public void DeleteRoot()
		{
			if (!Directory.Exists(Root))
				return;

			foreach (var entry in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
			{
				try
				{
					if (new FileInfo(entry).LinkTarget is null)
						File.SetUnixFileMode(entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
				}
			}

			Directory.Delete(Root, true);
		}

		protected static string Write(string path, string content = "content")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, content);
			return path;
		}

		protected static Func<FileConflict, ValueTask<ConflictResolution>> Resolver(ConflictAction action, bool applyToAll = false, List<FileConflict>? log = null)
			=> conflict =>
			{
				log?.Add(conflict);
				return ValueTask.FromResult(new ConflictResolution(action, applyToAll));
			};

		protected static FileOperationOptions Resolving(ConflictAction action, bool applyToAll = false, List<FileConflict>? log = null)
			=> new() { ConflictResolver = Resolver(action, applyToAll, log) };

		protected static bool RunningAsRoot() => Environment.UserName == "root" || Environment.GetEnvironmentVariable("USER") == "root";

		protected sealed class SyncProgress : IProgress<FileOperationProgress>
		{
			private readonly Action<FileOperationProgress>? _onReport;

			public SyncProgress(Action<FileOperationProgress>? onReport = null)
			{
				_onReport = onReport;
			}

			public List<FileOperationProgress> Reports { get; } = [];

			public void Report(FileOperationProgress value)
			{
				Reports.Add(value);
				_onReport?.Invoke(value);
			}
		}
	}
}
