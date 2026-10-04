// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Abstractions.Notifications
{
	/// <summary>
	/// Shows desktop notifications.
	/// </summary>
	public interface INotificationService
	{
		/// <summary>
		/// Shows a notification. Returns false if no notification service is available.
		/// </summary>
		Task<bool> NotifyAsync(string title, string body, CancellationToken cancellationToken = default);
	}
}
