// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Mime;
using Files.Platform.Linux.Launching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace Files.Platform.Tests.Launching
{
	[TestClass]
	public sealed class DesktopExecExpanderTests
	{
		private static DesktopApplication App(string exec, string? icon = null) =>
			new("t.desktop", "Test App", exec, "/usr/share/applications/t.desktop", icon);

		private static string[][] Expand(string exec, string? icon = null, params string[] targets) =>
			[.. DesktopExecExpander.Expand(App(exec, icon), targets).Select(x => x.ToArray())];

		[TestMethod]
		public void Tokenize_HandlesQuotesAndEscapes()
		{
			CollectionAssert.AreEqual(
				new[] { "my app", "--arg=a b", "say \"hi\"", "$HOME", "" },
				DesktopExecExpander.Tokenize("\"my app\" \"--arg=a b\" \"say \\\"hi\\\"\" \"\\$HOME\" \"\""));
		}

		[TestMethod]
		[DataRow("viewer 'single quotes'")]
		[DataRow("viewer prefix\"quoted\"")]
		[DataRow("viewer \"quoted\"suffix")]
		[DataRow("viewer $(id)")]
		[DataRow("viewer a;b")]
		[DataRow("viewer a\\b")]
		[DataRow("viewer \"$HOME\"")]
		public void InvalidExecQuoting_IsRejected(string exec)
		{
			Assert.IsNull(DesktopExecExpander.Tokenize(exec));
		}

		[TestMethod]
		public void LiteralPercent_IsUnescapedOnlyOnce()
		{
			CollectionAssert.AreEqual(new[] { "viewer", "%f", "100%", "" }, Expand("viewer %%f \"100%%\" \"\"")[0]);
		}

		[TestMethod]
		public void LowercaseCode_RunsOncePerTarget()
		{
			var result = Expand("viewer --open %f", null, "/a b/1.txt", "/c.txt");

			Assert.AreEqual(2, result.Length);
			CollectionAssert.AreEqual(new[] { "viewer", "--open", "/a b/1.txt" }, result[0]);
			CollectionAssert.AreEqual(new[] { "viewer", "--open", "/c.txt" }, result[1]);
		}

		[TestMethod]
		public void UppercaseF_PassesAllInOneInvocation()
		{
			var result = Expand("viewer %F", null, "/a", "file:///b%20c");

			Assert.AreEqual(1, result.Length);
			CollectionAssert.AreEqual(new[] { "viewer", "/a", "/b c" }, result[0]);
		}

		[TestMethod]
		public void UppercaseU_ConvertsPathsToUris()
		{
			var result = Expand("viewer %U", null, "/a b/é.txt", "https://example.com/x");

			CollectionAssert.AreEqual(new[] { "viewer", "file:///a%20b/%C3%A9.txt", "https://example.com/x" }, result[0]);
		}

		[TestMethod]
		public void IconNameAndKeyCodes()
		{
			var result = Expand("viewer %i --title %c --desktop %k 100%% %f", "my-icon", "/x");

			CollectionAssert.AreEqual(
				new[] { "viewer", "--icon", "my-icon", "--title", "Test App", "--desktop", "/usr/share/applications/t.desktop", "100%", "/x" },
				result[0]);
		}

		[TestMethod]
		public void IconCodeWithoutIcon_IsDropped()
		{
			CollectionAssert.AreEqual(new[] { "viewer" }, Expand("viewer %i")[0]);
		}

		[TestMethod]
		public void NoTargets_LowercaseCodeExpandsToNothing()
		{
			var result = Expand("viewer %f");

			Assert.AreEqual(1, result.Length);
			CollectionAssert.AreEqual(new[] { "viewer" }, result[0]);
		}

		[TestMethod]
		public void NoFieldCode_IgnoresTargets()
		{
			var result = Expand("viewer --fresh", null, "/x");

			Assert.AreEqual(1, result.Length);
			CollectionAssert.AreEqual(new[] { "viewer", "--fresh" }, result[0]);
		}

		[TestMethod]
		public void QuotedFieldCode_IsRejected()
		{
			Assert.AreEqual(0, Expand("viewer \"--file=%f\"", null, "/x y").Length);
		}

		[TestMethod]
		public void DeprecatedCodes_AreRemoved()
		{
			CollectionAssert.AreEqual(new[] { "viewer" }, Expand("viewer %d %D %n %N %v %m")[0]);
		}

		[TestMethod]
		[DataRow("viewer --file=%f")]
		[DataRow("viewer prefix%c")]
		[DataRow("viewer \"%f\"")]
		[DataRow("viewer %x")]
		[DataRow("viewer %")]
		[DataRow("viewer %f %U")]
		[DataRow("viewer prefix%F")]
		[DataRow("viewer --icon=%i")]
		[DataRow("viewer \"%c\"")]
		public void InvalidFieldCodes_AreRejected(string exec)
		{
			Assert.AreEqual(0, Expand(exec, null, "/x").Length);
		}

		[TestMethod]
		public void SingleCode_IsExpandedOnlyOnce()
		{
			CollectionAssert.AreEqual(new[] { "viewer", "/x %U $(id)" }, Expand("viewer %f", null, "/x %U $(id)")[0]);
		}

		[TestMethod]
		public void EmptyExec_YieldsNothing()
		{
			Assert.AreEqual(0, Expand("  ").Length);
		}
	}
}
