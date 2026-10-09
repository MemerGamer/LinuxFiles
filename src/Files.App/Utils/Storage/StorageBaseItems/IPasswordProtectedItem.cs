// Copyright (c) Files Community
// Licensed under the MIT License.

using FluentFTP.Exceptions;
using Files.Platform.Abstractions.Archives;
using System;
using System.Threading.Tasks;
#if WINDOWS
using SevenZip;
#endif

namespace Files.App.Utils.Storage
{
	public interface IPasswordProtectedItem
	{
		StorageCredential? Credentials { get; set; }

		Func<IPasswordProtectedItem, Task<StorageCredential>>? PasswordRequestedCallback { get; set; }

		async Task<TOut> RetryWithCredentialsAsync<TOut>(Func<Task<TOut>> func, Exception exception)
		{
			var handled =
#if WINDOWS
				exception is SevenZipOpenFailedException szofex && szofex.Result is OperationResult.WrongPassword ||
				exception is ExtractionFailedException efex && efex.Result is OperationResult.WrongPassword ||
#endif
				exception is ArchivePasswordException ||
				exception is FtpAuthenticationException ||
				exception is ICSharpCode.SharpZipLib.Zip.ZipException szlzex && szlzex.Message.Contains("password");

			if (!handled || PasswordRequestedCallback is null)
				throw exception;

			Credentials = await PasswordRequestedCallback(this);

			return await func();
		}

		async Task RetryWithCredentialsAsync(Func<Task> func, Exception exception)
		{
			var handled =
#if WINDOWS
				exception is SevenZipOpenFailedException szofex && szofex.Result is OperationResult.WrongPassword ||
				exception is ExtractionFailedException efex && efex.Result is OperationResult.WrongPassword ||
#endif
				exception is ArchivePasswordException ||
				exception is FtpAuthenticationException ||
				exception is ICSharpCode.SharpZipLib.Zip.ZipException szlzex && szlzex.Message.Contains("password");

			if (!handled || PasswordRequestedCallback is null)
				throw exception;

			Credentials = await PasswordRequestedCallback(this);

			await func();
		}

		void CopyFrom(IPasswordProtectedItem parent)
		{
			Credentials = parent.Credentials;
			PasswordRequestedCallback = parent.PasswordRequestedCallback;
		}
	}
}
