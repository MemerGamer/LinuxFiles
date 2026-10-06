// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Elevation;
using Files.Platform.Linux.Native;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Files.Platform.Linux.ElevationHelper
{
	internal static class Program
	{
		private static int Main(string[] args)
		{
			try
			{
				if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture is not (System.Runtime.InteropServices.Architecture.X64 or System.Runtime.InteropServices.Architecture.Arm64)
					|| args.Length != 0 || ElevationNative.GetEffectiveUid() != 0
					|| !uint.TryParse(Environment.GetEnvironmentVariable("PKEXEC_UID"), NumberStyles.None, CultureInfo.InvariantCulture, out var caller) || caller == 0)
					throw new InvalidDataException("This helper requires pkexec authorization.");
				foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
					Environment.SetEnvironmentVariable((string)entry.Key, null);
				ElevationNative.Umask(0x3F); // 0077
				Directory.SetCurrentDirectory("/");
				using var input = Console.OpenStandardInput();
				using var data = new MemoryStream();
				var buffer = new byte[4096];
				int count;
				while ((count = input.Read(buffer)) != 0)
				{
					if (data.Length + count > ElevationHelperProtocol.MaximumBytes) throw new InvalidDataException("Plan too large.");
					data.Write(buffer, 0, count);
				}
				var request = ElevationHelperProtocol.ParseRequest(new UTF8Encoding(false, true).GetString(data.ToArray()));
				var response = new HelperEngine(caller).Execute(request);
				Console.WriteLine(ElevationHelperProtocol.Serialize(response));
				return Array.TrueForAll(response.Items, item => item.Succeeded) ? 0 : 1;
			}
			catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException or System.Text.Json.JsonException or ArgumentException)
			{
				Console.WriteLine(ElevationHelperProtocol.Serialize(new HelperResponse(1, [], ex.Message.Length > 512 ? ex.Message[..512] : ex.Message)));
				return 1;
			}
		}
	}
}
