// Copyright (c) Files Community
// Licensed under the MIT License.


namespace Files.App.Data.Items
{
	public class Win32ContextMenuItem
	{
		public byte[]? Icon { get; set; }
		public int ID { get; set; } // Valid only in current menu to invoke item
		public string? Label { get; set; }
		public string? CommandString { get; set; }
		public List<Win32ContextMenuItem>? SubItems { get; set; }
	}
}
