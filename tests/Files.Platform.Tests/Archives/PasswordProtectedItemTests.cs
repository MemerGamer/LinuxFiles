// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Files.App.Utils.Storage;
using Files.Platform.Abstractions.Archives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.Platform.Tests.Archives
{
	[TestClass]
	public sealed class PasswordProtectedItemTests
	{
		private sealed class ProtectedItem : IPasswordProtectedItem
		{
			public StorageCredential? Credentials { get; set; }
			public Func<IPasswordProtectedItem, Task<StorageCredential>>? PasswordRequestedCallback { get; set; }
		}

		[TestMethod]
		public async Task ArchivePasswordRetriesWithNewCredentials()
		{
			IPasswordProtectedItem item = new ProtectedItem();
			var credential = new StorageCredential("", "secret");
			int prompts = 0;
			item.PasswordRequestedCallback = current =>
			{
				Assert.AreSame(item, current);
				prompts++;
				return Task.FromResult(credential);
			};

			var result = await item.RetryWithCredentialsAsync(() => Task.FromResult(item.Credentials!.Password), new ArchivePasswordException("wrong password"));
			Assert.AreEqual("secret", result);
			Assert.AreEqual(1, prompts);
			Assert.AreSame(credential, item.Credentials);

			bool retried = false;
			await item.RetryWithCredentialsAsync(() =>
			{
				retried = true;
				Assert.AreSame(credential, item.Credentials);
				return Task.CompletedTask;
			}, new ArchivePasswordException("password required"));
			Assert.IsTrue(retried);
			Assert.AreEqual(2, prompts);
		}

		[TestMethod]
		public async Task UnrelatedErrorsDoNotPromptOrRetry()
		{
			IPasswordProtectedItem item = new ProtectedItem();
			item.PasswordRequestedCallback = _ => throw new AssertFailedException("Must not prompt");
			var error = new IOException("archive corrupt");
			var caught = await Assert.ThrowsExactlyAsync<IOException>(() => item.RetryWithCredentialsAsync(() => Task.FromResult(1), error));
			Assert.AreSame(error, caught);
			caught = await Assert.ThrowsExactlyAsync<IOException>(() => item.RetryWithCredentialsAsync(() => Task.CompletedTask, error));
			Assert.AreSame(error, caught);
		}

		[TestMethod]
		public async Task MissingCallbackPreservesPasswordError()
		{
			IPasswordProtectedItem item = new ProtectedItem();
			var error = new ArchivePasswordException("password required");
			var caught = await Assert.ThrowsExactlyAsync<ArchivePasswordException>(() => item.RetryWithCredentialsAsync(() => Task.FromResult(1), error));
			Assert.AreSame(error, caught);
			caught = await Assert.ThrowsExactlyAsync<ArchivePasswordException>(() => item.RetryWithCredentialsAsync(() => Task.CompletedTask, error));
			Assert.AreSame(error, caught);
		}
	}
}
