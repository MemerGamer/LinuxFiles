// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Items
{
	/// <summary>
	/// Represents a principal of an ACE or an owner of an ACL.
	/// </summary>
	public sealed partial class AccessControlPrincipal : ObservableObject
	{
		/// <summary>
		/// Account type.
		/// </summary>
		public AccessControlPrincipalType PrincipalType { get; private set; }

		/// <summary>
		/// Account security identifier (SID).
		/// </summary>
		public string? Sid { get; private set; }

		/// <summary>
		/// A domain the account belongs to
		/// </summary>
		public string? Domain { get; private set; }

		/// <summary>
		/// Account name
		/// </summary>
		public string? Name { get; private set; }

		/// <summary>
		/// Indicates whether this instance is valid or not
		/// </summary>
		public bool IsValid { get; private set; }

		/// <summary>
		/// Account type glyph.
		/// </summary>
		public string Glyph
			=> PrincipalType switch
			{
				AccessControlPrincipalType.User => "\xE77B",
				AccessControlPrincipalType.Group => "\xE902",
				_ => "\xE716",
			};

		/// <summary>
		/// Account display name
		/// </summary>
		public string? DisplayName
			=> string.IsNullOrEmpty(Name) ? Sid : Name;

		/// <summary>
		/// Account full name or just name
		/// </summary>
		public string? FullNameHumanized
			=> string.IsNullOrEmpty(Domain) ? Name : $"{Domain}\\{Name}";

		/// <summary>
		/// Account humanized full name.
		/// </summary>
		public string FullNameHumanizedWithBrackes
			=> string.IsNullOrEmpty(Domain) ? string.Empty : $"({Domain}\\{Name})";

		public AccessControlPrincipal(string sid)
		{
			if (string.IsNullOrEmpty(sid))
				return;

			Sid = sid;
			ResolveAccount(sid);
		}

		/// <summary>
		/// Looks up the account name, domain and type of the SID; implemented on Windows only.
		/// </summary>
		partial void ResolveAccount(string sid);
	}
}
