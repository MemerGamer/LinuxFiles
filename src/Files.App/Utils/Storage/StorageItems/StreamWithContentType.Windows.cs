// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Files.App.Utils.Storage
{
	public sealed partial class StreamWithContentType : IRandomAccessStreamWithContentType
	{
		private IRandomAccessStream baseStream;

		public StreamWithContentType(IRandomAccessStream stream)
		{
			baseStream = stream;
		}

		public IInputStream GetInputStreamAt(ulong position) => baseStream.GetInputStreamAt(position);

		public IOutputStream GetOutputStreamAt(ulong position) => baseStream.GetOutputStreamAt(position);

		public void Seek(ulong position) => baseStream.Seek(position);

		public IRandomAccessStream CloneStream() => baseStream.CloneStream();

		public bool CanRead => baseStream.CanRead;

		public bool CanWrite => baseStream.CanWrite;

		public ulong Position => baseStream.Position;

		public ulong Size { get => baseStream.Size; set => baseStream.Size = value; }

		public IAsyncOperationWithProgress<IBuffer, uint> ReadAsync(IBuffer buffer, uint count, InputStreamOptions options)
		{
			return baseStream.ReadAsync(buffer, count, options);
		}

		public IAsyncOperationWithProgress<uint, uint> WriteAsync(IBuffer buffer) => baseStream.WriteAsync(buffer);

		public IAsyncOperation<bool> FlushAsync() => baseStream.FlushAsync();

		public void Dispose()
		{
			baseStream.Dispose();
		}

		public string ContentType { get; set; } = "application/octet-stream";
	}

	public sealed partial class ComStreamWrapper : Stream
	{
		private IStream? iStream;
		private STATSTG iStreamStat;
		private STGMEDIUM medium;

		public ComStreamWrapper(IStream stream, STGMEDIUM medium)
		{
			iStream = stream;
			this.medium = medium;
			try
			{
				iStream.Stat(out iStreamStat, STATFLAG.STATFLAG_NONAME).ThrowOnFailure();
			}
			catch
			{
				ReleaseResources();
				throw;
			}
		}

		public override bool CanRead => true;

		public override bool CanSeek => true;

		public override bool CanWrite => false;

		public override long Length => checked((long)iStreamStat.cbSize);

		public override long Position
		{
			get => Seek(0, SeekOrigin.Current);
			set => Seek(value, SeekOrigin.Begin);
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			ObjectDisposedException.ThrowIf(iStream is null, this);
			iStream.Read(buffer.AsSpan(offset, count), out uint bytesRead).ThrowOnFailure();
			return checked((int)bytesRead);
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			ObjectDisposedException.ThrowIf(iStream is null, this);
			iStream.Seek(offset, origin, out ulong newPosition).ThrowOnFailure();
			return checked((long)newPosition);
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (iStream is null)
				return;

			base.Dispose(disposing);
			ReleaseResources();
		}

		private void ReleaseResources()
		{
			IStream? stream = iStream;
			iStream = null;
			try
			{
				if ((object?)stream is ComObject comObject)
					comObject.FinalRelease();
			}
			finally
			{
				PInvoke.ReleaseStgMedium(ref medium);
				medium = default;
			}
		}
	}
}
