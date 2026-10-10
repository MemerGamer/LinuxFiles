// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.FileTags
{
	[RegistrySerializable]
	public sealed class TaggedFile
	{
		public ulong? Frn { get; set; }
		public string FilePath { get; set; } = string.Empty;
		public string[] Tags { get; set; } = [];
		/// <summary>Linux: the database is newer than the file's extended attribute, which could not be updated.</summary>
		public bool XattrStale { get; set; }
	}
}
