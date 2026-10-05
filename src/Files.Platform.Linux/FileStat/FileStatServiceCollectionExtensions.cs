// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.FileStat;
using Microsoft.Extensions.DependencyInjection;

namespace Files.Platform.Linux.FileStat
{
	public static class FileStatServiceCollectionExtensions
	{
		/// <summary>Registers <see cref="IFileStatService"/>.</summary>
		public static IServiceCollection AddLinuxFileStat(this IServiceCollection services)
			=> services.AddSingleton<IFileStatService, LinuxFileStatService>();
	}
}
