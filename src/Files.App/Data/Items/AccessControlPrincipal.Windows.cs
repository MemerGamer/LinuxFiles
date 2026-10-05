// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Security;

namespace Files.App.Data.Items
{
	public sealed partial class AccessControlPrincipal
	{
		partial void ResolveAccount(string sid)
		{
			ResolveAccountCore(sid);
		}

		private unsafe void ResolveAccountCore(string sid)
		{
			if (!PInvoke.ConvertStringSidToSid(sid, out var lpSid))
				return;

			try
			{
				char[] lpName = [];
				char[] lpDomain = [];
				uint cchName = 0, cchDomainName = 0;

				// Get size of account name and domain name
				_ = PInvoke.LookupAccountSid(string.Empty, lpSid, lpName, ref cchName, lpDomain, ref cchDomainName, out _);

				// Ensure requested capacity
				lpName = new char[cchName];
				lpDomain = new char[cchDomainName];

				// Get account name and domain
				bool bResult = PInvoke.LookupAccountSid(string.Empty, lpSid, lpName, ref cchName, lpDomain, ref cchDomainName, out var snu);
				if (!bResult)
					return;

				PrincipalType = snu switch
				{
					// Group
					var x when
						(x == SID_NAME_USE.SidTypeAlias ||
						x == SID_NAME_USE.SidTypeGroup ||
						x == SID_NAME_USE.SidTypeWellKnownGroup)
						=> AccessControlPrincipalType.Group,

					// User
					SID_NAME_USE.SidTypeUser
						=> AccessControlPrincipalType.User,

					// Unknown
					_ => AccessControlPrincipalType.Unknown
				};

				// Replace domain name with computer name if the account type is user or alias type
				if (snu == SID_NAME_USE.SidTypeUser || snu == SID_NAME_USE.SidTypeAlias)
				{
					uint size = 256;
					lpDomain = new char[size];
					bResult = PInvoke.GetComputerName(lpDomain, ref size);
					if (!bResult)
						return;
				}

				Name = lpName.AsSpan().ToString();
				Domain = lpDomain.AsSpan().ToString().ToLower();

				IsValid = true;
			}
			finally
			{
				Marshal.FreeHGlobal((nint)lpSid.Value);
			}
		}
	}
}
