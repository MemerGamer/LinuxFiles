// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Linux.Launching;
using Files.Platform.Linux.Mime;
using System;
using System.Collections.Generic;
using System.IO;

namespace Files.Platform.Tests.Mime
{
	/// <summary>
	/// A fake XDG environment rooted in a temp directory.
	/// </summary>
	internal sealed class XdgFixture : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), "files-xdg-" + Guid.NewGuid().ToString("N"));

		public string Home => Path.Combine(Root, "home");
		public string ConfigHome => Path.Combine(Root, "config");
		public string DataHome => Path.Combine(Root, "data");
		public string SystemData => Path.Combine(Root, "usr-share");
		public string SystemConfig => Path.Combine(Root, "etc-xdg");

		public string? CurrentDesktop { get; set; }

		public XdgFixture()
		{
			Directory.CreateDirectory(Home);
		}

		public XdgDirectories Directories => new(name => name switch
		{
			"HOME" => Home,
			"XDG_CONFIG_HOME" => ConfigHome,
			"XDG_DATA_HOME" => DataHome,
			"XDG_DATA_DIRS" => SystemData,
			"XDG_CONFIG_DIRS" => SystemConfig,
			"XDG_CURRENT_DESKTOP" => CurrentDesktop,
			_ => null,
		});

		public string Write(string path, string content)
		{
			var full = Path.Combine(Root, path);
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);
			File.WriteAllText(full, content);
			return full;
		}

		public string WriteBytes(string path, byte[] content)
		{
			var full = Path.Combine(Root, path);
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);
			File.WriteAllBytes(full, content);
			return full;
		}

		public string WriteDesktop(string dataDir, string id, string name, string exec, string extra = "")
		{
			return Write(Path.Combine(dataDir, "applications", id),
				$"[Desktop Entry]\nType=Application\nName={name}\nExec={exec}\n{extra}");
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(Root, true);
			}
			catch (IOException)
			{
			}
		}
	}

	internal sealed class FakeLocator(params string[] available) : IExecutableLocator
	{
		private readonly HashSet<string> names = new(available);

		public string? Locate(string command) => names.Contains(command) ? "/usr/bin/" + command : null;
	}
}
