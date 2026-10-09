// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Files.Platform.Linux.Elevation
{
	internal static class ElevationDeploymentPolicy
	{
		internal static bool Matches(string text, string helper)
		{
			if (text.Length > 8192) return false;
			using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
			{
				DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 8192,
			});
			var document = XDocument.Load(reader);
			var actions = document.Root?.Elements("action").ToArray();
			if (document.Root?.Name != "policyconfig" || actions is not { Length: 1 }) return false;
			var action = actions[0];
			var annotations = action.Elements("annotate").ToArray();
			var defaults = action.Elements("defaults").ToArray();
			return (string?)action.Attribute("id") == "io.github.memergamer.LinuxFiles.root-actions"
				&& annotations is { Length: 1 } && (string?)annotations[0].Attribute("key") == "org.freedesktop.policykit.exec.path"
				&& annotations[0].Value == helper && defaults is { Length: 1 }
				&& defaults[0].Elements().Count() == 3
				&& defaults[0].Element("allow_any")?.Value == "no"
				&& defaults[0].Element("allow_inactive")?.Value == "no"
				&& defaults[0].Element("allow_active")?.Value == "auth_admin";
		}
	}
}
