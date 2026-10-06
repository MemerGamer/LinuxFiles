// Copyright (c) Files Community
// Licensed under the MIT License.

extern alias ElevationHelper;

using Files.Platform.Abstractions.Elevation;
using Files.Platform.Linux.Elevation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using HelperAuthorizationVerifier = ElevationHelper::Files.Platform.Linux.Elevation.HelperAuthorization;
using HelperEntryPoint = ElevationHelper::Files.Platform.Linux.ElevationHelper.HelperEntryPoint;

namespace Files.Platform.Tests.SystemIntegration
{
	[TestClass]
	public sealed class HelperAuthorizationTests
	{
		[TestMethod]
		public void PromptArgumentsBindTheExactUtf8PayloadAndEverySummaryField()
		{
			var request = new HelperRequest(1, "copy", ["/home/u/café", "/home/u/b"], "/etc/sudoers.d");
			var json = ElevationHelperProtocol.Serialize(request);
			var data = Encoding.UTF8.GetBytes(json);
			var args = HelperAuthorization.Arguments(json);
			Assert.AreEqual(Convert.ToHexString(SHA256.HashData(data)), args[1]);
			StringAssert.Contains(args[0], "copy; sources=2; first source=\"/home/u/café\"; target=\"/etc/sudoers.d\"");
			Assert.AreEqual(request.Target, HelperAuthorizationVerifier.Verify(data, args).Target);
			foreach (var changed in new[] { json + " ", json.Replace("/home/u/b", "/root/secret"), json.Replace("sudoers.d", "other") })
				Assert.ThrowsExactly<InvalidDataException>(() => HelperAuthorizationVerifier.Verify(Encoding.UTF8.GetBytes(changed), args));
			foreach (var changed in new[] { args[0].Replace("copy", "move"), args[0].Replace("sources=2", "sources=1"),
				args[0].Replace("café", "other"), args[0].Replace("sudoers.d", "other") })
				Assert.ThrowsExactly<InvalidDataException>(() => HelperAuthorizationVerifier.Verify(data, [changed, args[1]]));
			Assert.ThrowsExactly<InvalidDataException>(() => HelperAuthorizationVerifier.Verify(data, []));
			Assert.ThrowsExactly<InvalidDataException>(() => HelperAuthorizationVerifier.Verify(data, [args[0], "0"]));
		}

		[TestMethod]
		public void SummaryAndPreviewKeepUnicodeReadableAndEscapePromptSpoofingCharacters()
		{
			var request = new HelperRequest(1, "delete", ["/home/u/日本語-café\n\u202E\u2066\u200B"], null);
			var json = ElevationHelperProtocol.Serialize(request);
			var summary = HelperAuthorization.Summary(request);
			StringAssert.Contains(summary, "日本語-café");
			Assert.IsFalse(summary.Any(c => c is '\n' or '\r' or '\u202E' or '\u2066' or '\u200B'));
			var preview = ElevationPlanPreview.Format(new(ElevatedOperation.Delete, request.Sources, null,
				[new(ElevationHelperProtocol.HelperPath, [json])]))!;
			StringAssert.Contains(preview, "日本語-café");
			StringAssert.Contains(preview, "\\u202E");
			StringAssert.Contains(json, "\\u65E5"); // Display encoding must never change the wire encoding.
			Assert.AreEqual(request.Sources[0], HelperAuthorizationVerifier.Verify(Encoding.UTF8.GetBytes(json), HelperAuthorization.Arguments(json)).Sources[0]);
		}

		[TestMethod]
		public void InvalidUtf8AndUnexpectedTopLevelExceptionsFailClosed()
		{
			byte[] invalid = [0xFF];
			Assert.ThrowsExactly<DecoderFallbackException>(() => HelperAuthorizationVerifier.Verify(invalid, ["delete", HelperAuthorization.Digest(invalid)]));
			foreach (var exception in new Exception[] { new NullReferenceException("secret"), new InvalidOperationException("secret"), new DllNotFoundException("secret") })
			{
				using var output = new StringWriter();
				Assert.AreEqual(1, HelperEntryPoint.Run(() => throw exception, output));
				Assert.AreEqual("Privileged operation failed.", ElevationHelperProtocol.ParseResponse(output.ToString()).Error);
				Assert.IsFalse(output.ToString().Contains("secret"));
			}
		}
	}
}
