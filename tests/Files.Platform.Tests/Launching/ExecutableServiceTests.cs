// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Launching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Launching
{
	[TestClass]
	public sealed class ExecutableServiceTests
	{
		private sealed class RecordingStarter : IProcessStarter
		{
			public ProcessLaunch? Launch { get; private set; }
			public bool Fail { get; set; }

			public Task StartDetachedAsync(ProcessLaunch launch, CancellationToken cancellationToken = default)
			{
				Launch = launch;
				if (Fail)
					throw new IOException("Launch failed.");
				return Task.CompletedTask;
			}
		}

		[TestMethod]
		public async Task ValidatesExecutableOnPathAndPreservesLiteralArguments()
		{
			if (!OperatingSystem.IsLinux())
				return;

			var directory = Path.Combine(Path.GetTempPath(), "files-ide-" + Guid.NewGuid());
			Directory.CreateDirectory(directory);
			try
			{
				var executable = Path.Combine(directory, "editor 'quoted';name");
				File.WriteAllText(executable, string.Empty);
				File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
				var starter = new RecordingStarter();
				var service = new LinuxExecutableService(new PathExecutableLocator(_ => directory), starter);
				Assert.AreEqual(executable, service.Locate(Path.GetFileName(executable)));
				Assert.AreEqual(executable, service.Locate(executable));
				Assert.IsNull(service.Locate(directory));
				Assert.IsNull(service.Locate("missing-editor"));
				Assert.IsNull(service.Locate("invalid\0path"));

				string[] arguments = ["folder with spaces", "$(touch unwanted);'quoted'"];
				Assert.IsTrue(await service.StartAsync(Path.GetFileName(executable), arguments));
				Assert.AreEqual(executable, starter.Launch?.FileName);
				CollectionAssert.AreEqual(arguments, (System.Collections.ICollection)starter.Launch!.Arguments);

				starter.Fail = true;
				Assert.IsFalse(await service.StartAsync(executable, []));
				File.SetUnixFileMode(executable, UnixFileMode.UserRead);
				Assert.IsNull(service.Locate(executable));
				Assert.IsFalse(await service.StartAsync(executable, []));
			}
			finally
			{
				Directory.Delete(directory, true);
			}
		}

		[TestMethod]
		public async Task DirectStartDoesNotInterpretShellMetacharacters()
		{
			if (!OperatingSystem.IsLinux())
				return;

			var directory = Path.Combine(Path.GetTempPath(), "files-ide-" + Guid.NewGuid());
			Directory.CreateDirectory(directory);
			try
			{
				var literalPath = Path.Combine(directory, "$(false); 'literal file'");
				var service = new LinuxExecutableService(new PathExecutableLocator(), new DirectProcessStarter());
				Assert.IsTrue(await service.StartAsync("touch", ["--", literalPath]));
				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				while (!File.Exists(literalPath))
					await Task.Delay(10, timeout.Token);
				Assert.IsTrue(File.Exists(literalPath));
			}
			finally
			{
				Directory.Delete(directory, true);
			}
		}
	}
}
