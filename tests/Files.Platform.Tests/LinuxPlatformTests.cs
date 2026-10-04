// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions;
using Files.Platform.Linux;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests
{
	[TestClass]
	public sealed class LinuxPlatformTests
	{
		[TestMethod]
		public void AddLinuxPlatform_RegistersLinuxCapabilities()
		{
			using var provider = new ServiceCollection().AddLinuxPlatform().BuildServiceProvider();

			var capabilities = provider.GetRequiredService<IPlatformCapabilities>();

			Assert.IsInstanceOfType<LinuxPlatformCapabilities>(capabilities);
			Assert.AreEqual(PlatformKind.Linux, capabilities.Kind);
		}

		[TestMethod]
		public void LinuxCapabilities_AllFlagsAreFalse()
		{
			IPlatformCapabilities c = new LinuxPlatformCapabilities();

			Assert.IsFalse(c.SupportsShellContextMenuExtensions);
			Assert.IsFalse(c.SupportsPreviewHandlers);
			Assert.IsFalse(c.SupportsLibraries);
			Assert.IsFalse(c.SupportsAclSecurity);
			Assert.IsFalse(c.SupportsDigitalSignatures);
			Assert.IsFalse(c.SupportsCompatibilityProperties);
			Assert.IsFalse(c.SupportsJumpLists);
			Assert.IsFalse(c.SupportsSystemBackdrop);
			Assert.IsFalse(c.SupportsCustomTitleBar);
			Assert.IsFalse(c.SupportsTabTearOut);
			Assert.IsFalse(c.SupportsWsl);
			Assert.IsFalse(c.SupportsDriveFormatting);
			Assert.IsFalse(c.SupportsShortcutFiles);
		}
	}
}
