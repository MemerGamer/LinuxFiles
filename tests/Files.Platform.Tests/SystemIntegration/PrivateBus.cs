// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;

namespace Files.Platform.Tests.SystemIntegration
{
	/// <summary>
	/// A throwaway <c>dbus-daemon</c> with no service activation, so tests never touch (or launch anything on) the user's real session bus.
	/// </summary>
	internal sealed class PrivateBus : IDisposable
	{
		private readonly Process process;
		private readonly string directory;

		public string Address { get; }

		private PrivateBus(Process process, string directory, string address)
		{
			this.process = process;
			this.directory = directory;
			Address = address;
		}

		public static PrivateBus Start()
		{
			var directory = Path.Combine(Path.GetTempPath(), "fbus-" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(directory);
			var config = Path.Combine(directory, "bus.conf");
			File.WriteAllText(config, $"""
				<!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN" "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
				<busconfig>
				  <type>session</type>
				  <listen>unix:path={directory}/bus</listen>
				  <auth>EXTERNAL</auth>
				  <policy context="default">
				    <allow send_destination="*" eavesdrop="true"/>
				    <allow eavesdrop="true"/>
				    <allow own="*"/>
				  </policy>
				</busconfig>
				""");

			var startInfo = new ProcessStartInfo("dbus-daemon")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = false,
			};
			startInfo.ArgumentList.Add("--nofork");
			startInfo.ArgumentList.Add("--nosyslog");
			startInfo.ArgumentList.Add("--print-address");
			startInfo.ArgumentList.Add("--config-file=" + config);

			Process? process;
			try
			{
				process = Process.Start(startInfo);
			}
			catch (System.ComponentModel.Win32Exception)
			{
				Directory.Delete(directory, true);
				Assert.Inconclusive("dbus-daemon is not installed.");
				throw;
			}

			var address = process!.StandardOutput.ReadLine();
			if (string.IsNullOrEmpty(address))
			{
				process.Kill();
				Directory.Delete(directory, true);
				Assert.Inconclusive("dbus-daemon did not start.");
			}

			return new PrivateBus(process, directory, address!.Trim());
		}

		public void Dispose()
		{
			try { process.Kill(); } catch (InvalidOperationException) { }
			process.WaitForExit(2000);
			process.Dispose();
			try { Directory.Delete(directory, true); } catch (IOException) { }
		}
	}
}
