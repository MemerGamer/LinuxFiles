// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Files.Platform.Abstractions.Instance
{
	/// <summary>
	/// What a request sent to the running instance asks for.
	/// </summary>
	public enum InstanceRequestKind
	{
		/// <summary>The arguments hold a command line (paths, <c>--select</c>, <c>-t</c>, <c>-n</c>); an empty list just raises the window.</summary>
		CommandLine,

		/// <summary>Show the folders given as absolute paths (FileManager1.ShowFolders).</summary>
		ShowFolders,

		/// <summary>Show the parent folders of the items given as absolute paths, with the items selected (FileManager1.ShowItems).</summary>
		ShowItems,

		/// <summary>Show the properties of the items given as absolute paths (FileManager1.ShowItemProperties).</summary>
		ShowItemProperties,
	}

	/// <summary>
	/// A request delivered to the running instance by a second launch or another application.
	/// </summary>
	/// <param name="Kind">What is requested.</param>
	/// <param name="WorkingDirectory">The directory relative paths in <paramref name="Arguments"/> are resolved against.</param>
	/// <param name="Arguments">The arguments (command line arguments, or absolute paths for the FileManager1 kinds).</param>
	public sealed record InstanceRequest(InstanceRequestKind Kind, string WorkingDirectory, IReadOnlyList<string> Arguments);
}
