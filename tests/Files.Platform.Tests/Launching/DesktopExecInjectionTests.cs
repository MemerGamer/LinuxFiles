// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using Files.Platform.Linux.Launching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Diagnostics;

namespace Files.Platform.Tests.Launching
{
	[TestClass]
	public sealed class DesktopExecInjectionTests
	{
		private static readonly string[] Nasty =
		[
			"/tmp/$(curl x|sh).txt",
			"/tmp/`id`.txt",
			"/tmp/a;rm -rf ~.txt",
			"/tmp/it's \"quoted\".txt",
			"/tmp/with space.txt",
			"/tmp/new\nline.txt",
			"/tmp/back\\slash.txt",
			"-rf",
		];

		private static string[][] Expand(string exec, params string[] targets) =>
			[.. DesktopExecExpander.Expand(new DesktopApplication("t.desktop", "Test App", exec, "/usr/share/applications/t.desktop"), targets).Select(x => x.ToArray())];

		private static string Safe(string name) => name.StartsWith('-') ? "./" + name : name;

		[TestMethod]
		public void PlainExec_KeepsEachValueOneLiteralArgument()
		{
			foreach (var name in Nasty)
			{
				var argv = Expand("viewer %f", name);
				Assert.AreEqual(1, argv.Length);
				Assert.AreEqual(2, argv[0].Length, name);
				Assert.AreEqual(Safe(name), argv[0][1]);
			}
		}

		[TestMethod]
		public void MultipleFiles_AreSeparateLiteralArguments()
		{
			var argv = Expand("viewer %F", Nasty)[0];

			Assert.AreEqual(Nasty.Length + 1, argv.Length);
			for (var i = 0; i < Nasty.Length; i++)
				Assert.AreEqual(Safe(Nasty[i]), argv[i + 1]);
		}

		[TestMethod]
		public void FixedShellScript_TargetsAreLiteralPositionalArguments()
		{
			foreach (var name in Nasty)
			{
				var argv = Expand("sh -c \"printf '%%s' \\\"\\$1\\\"\" sh %f", name)[0];
				CollectionAssert.AreEqual(new[] { "sh", "-c", "printf '%s' \"$1\"", "sh", Safe(name) }, argv);
				var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardOutput = true };
				foreach (var argument in argv.Skip(1)) start.ArgumentList.Add(argument);
				using var process = Process.Start(start)!;
				var output = process.StandardOutput.ReadToEnd();
				process.WaitForExit();
				Assert.AreEqual(0, process.ExitCode);
				Assert.AreEqual(Safe(name), output);

			}
		}

		[TestMethod]
		[DataRow("sh -c %f")]
		[DataRow("env -S %f")]
		[DataRow("nice bash --rcfile %f")]
		[DataRow("launcher %f")]
		public void TrustedCommands_AreNotInspected(string exec)
		{
			foreach (var name in Nasty)
				Assert.AreEqual(Safe(name), Expand(exec, name)[0][^1]);
		}

		[TestMethod]
		public void UnbalancedQuotes_AreRejected()
		{
			Assert.AreEqual(0, Expand("viewer \"unterminated %f", "/x").Length);
			Assert.AreEqual(0, Expand("viewer 'oops %f", "/x").Length);
			Assert.IsNull(DesktopExecExpander.Tokenize("a \"b"));
		}
	}
}
