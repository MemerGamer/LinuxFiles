// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.Platform.Abstractions
{
	/// <summary>
	/// Provides the well-known per-user folders (replaces <c>UserDataPaths</c> and <c>Environment.SpecialFolder</c> usages).
	/// </summary>
	public interface IUserDirectories
	{
		/// <summary>
		/// Gets the user's home directory.
		/// </summary>
		string Home { get; }

		/// <summary>
		/// Gets the Desktop directory.
		/// </summary>
		string Desktop { get; }

		/// <summary>
		/// Gets the Documents directory.
		/// </summary>
		string Documents { get; }

		/// <summary>
		/// Gets the Downloads directory.
		/// </summary>
		string Downloads { get; }

		/// <summary>
		/// Gets the Music directory.
		/// </summary>
		string Music { get; }

		/// <summary>
		/// Gets the Pictures directory.
		/// </summary>
		string Pictures { get; }

		/// <summary>
		/// Gets the Videos directory.
		/// </summary>
		string Videos { get; }

		/// <summary>
		/// Gets the Templates directory.
		/// </summary>
		string Templates { get; }

		/// <summary>
		/// Gets the Public share directory.
		/// </summary>
		string PublicShare { get; }
	}
}
