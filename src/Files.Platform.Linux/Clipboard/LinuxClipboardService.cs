// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Clipboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Clipboard
{
	/// <summary>
	/// Publishes file lists on the X11 clipboard and drags them to other applications, in the formats Nautilus, Dolphin and the other
	/// file managers use. See <see cref="ClipboardFormats"/> for the targets.
	/// </summary>
	public sealed class LinuxClipboardService : IClipboardService, IFileDragSource, IDisposable
	{
		private readonly Lazy<X11SelectionHost?> _host;

		/// <summary>
		/// Creates the service. The X connection is opened lazily on first use.
		/// </summary>
		/// <param name="incrThreshold">Overrides the payload size above which <c>INCR</c> transfers are used (for tests).</param>
		public LinuxClipboardService(int? incrThreshold = null)
		{
			_host = new Lazy<X11SelectionHost?>(() =>
			{
				var host = X11SelectionHost.TryCreate(incrThreshold);
				if (host is not null)
					host.ClipboardOwnerLost += (_, _) => ContentChanged?.Invoke(this, EventArgs.Empty);

				return host;
			}, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		/// <inheritdoc/>
		public bool IsAvailable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && _host.Value is not null;

		/// <inheritdoc/>
		public bool IsDragSupported => IsAvailable;

		/// <inheritdoc/>
		public event EventHandler? ContentChanged;

		/// <inheritdoc/>
		public async Task<bool> SetFilesAsync(IReadOnlyList<string> paths, ClipboardOperation operation, CancellationToken cancellationToken = default)
		{
			var valid = Sanitize(paths);
			if (valid.Count == 0 || !IsAvailable)
				return false;

			var ok = await _host.Value!.SetClipboardAsync(new ClipboardFileList(valid, operation)).WaitAsync(cancellationToken).ConfigureAwait(false);
			if (ok)
				ContentChanged?.Invoke(this, EventArgs.Empty);

			return ok;
		}

		/// <inheritdoc/>
		public async Task<ClipboardFileList?> GetFilesAsync(CancellationToken cancellationToken = default)
		{
			if (!IsAvailable)
				return null;

			return await _host.Value!.GetClipboardFilesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task ClearAsync(CancellationToken cancellationToken = default)
		{
			if (!IsAvailable)
				return;

			await _host.Value!.ClearClipboardAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
			ContentChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <inheritdoc/>
		public Task<FileDragOutcome> DragFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
		{
			var valid = Sanitize(paths);
			if (valid.Count == 0 || !IsAvailable)
				return Task.FromResult(FileDragOutcome.None);

			return _host.Value!.DragAsync(new ClipboardFileList(valid, ClipboardOperation.Copy), cancellationToken);
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (_host.IsValueCreated)
				_host.Value?.Dispose();
		}

		/// <summary>Only absolute paths without NUL can be turned into file URIs that survive a round trip.</summary>
		private static List<string> Sanitize(IReadOnlyList<string> paths) =>
			paths.Where(p => !string.IsNullOrEmpty(p) && p[0] == '/' && !p.Contains('\0')).Distinct(StringComparer.Ordinal).ToList();
	}
}
