using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace BootScanner.Core.Parsing;

/// <summary>Read-only Windows Structured Storage adapter. No third-party CFB implementation.</summary>
[SupportedOSPlatform("windows")]
internal sealed class CompoundStorage : IDisposable
{
    private const int Missing = unchecked((int)0x80030002);
    private IStorageNative? _storage;
    private readonly bool _uninitialize;
    private CompoundStorage(IStorageNative storage, bool uninitialize = false)
    { _storage = storage; _uninitialize = uninitialize; }

    public static CompoundStorage OpenRead(string path)
    {
        var initialize = CoInitializeEx(IntPtr.Zero, 0);
        if (initialize < 0 && initialize != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(initialize);
        var release = initialize >= 0;
        try
        {
            var iid = typeof(IStorageNative).GUID;
            // STGM_READ | STGM_SHARE_DENY_WRITE, STGFMT_DOCFILE. Never requests write access.
            Marshal.ThrowExceptionForHR(StgOpenStorageEx(path, 0x20, 5, 0, IntPtr.Zero, IntPtr.Zero, ref iid, out var storage));
            return new CompoundStorage(storage, release);
        }
        catch { if (release) CoUninitialize(); throw; }
    }

    public bool TryOpenStorage(string name, out CompoundStorage? storage)
    {
        ObjectDisposedException.ThrowIf(_storage is null, this);
        var hr = _storage!.OpenStorage(name, IntPtr.Zero, 0x10, IntPtr.Zero, 0, out var child);
        if (hr == Missing) { storage = null; return false; }
        Marshal.ThrowExceptionForHR(hr);
        storage = new CompoundStorage(child);
        return true;
    }

    public bool TryOpenStream(string name, out Stream? stream)
    {
        ObjectDisposedException.ThrowIf(_storage is null, this);
        var hr = _storage!.OpenStream(name, IntPtr.Zero, 0x10, 0, out var native);
        if (hr == Missing) { stream = null; return false; }
        Marshal.ThrowExceptionForHR(hr);
        try { stream = new StorageStream(native); return true; }
        catch { Marshal.ReleaseComObject(native); throw; }
    }

    public void Dispose()
    {
        if (_storage is null) return;
        Marshal.ReleaseComObject(_storage);
        _storage = null;
        if (_uninitialize) CoUninitialize();
    }

    private sealed class StorageStream : Stream
    {
        private IStream? _stream;
        private long _position;
        private readonly long _length;
        public StorageStream(IStream stream)
        { _stream = stream; stream.Stat(out var stat, 1); _length = stat.cbSize; }
        public override bool CanRead => _stream is not null;
        public override bool CanSeek => _stream is not null;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            ObjectDisposedException.ThrowIf(_stream is null, this);
            if (count == 0) return 0;
            var target = offset == 0 ? buffer : new byte[count];
            var read = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(read, 0);
                _stream!.Read(target, count, read);
                var length = Marshal.ReadInt32(read);
                if (length < 0 || length > count) throw new InvalidDataException("Storage stream returned invalid byte count.");
                if (offset != 0) Buffer.BlockCopy(target, 0, buffer, offset, length);
                _position += length;
                return length;
            }
            finally { Marshal.FreeHGlobal(read); }
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_stream is null, this);
            var position = Marshal.AllocHGlobal(8);
            try { _stream!.Seek(offset, (int)origin, position); return _position = Marshal.ReadInt64(position); }
            finally { Marshal.FreeHGlobal(position); }
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException("Read-only storage.");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Read-only storage.");
        protected override void Dispose(bool disposing)
        {
            if (_stream is not null) { Marshal.ReleaseComObject(_stream); _stream = null; }
            base.Dispose(disposing);
        }
    }

    // IStorage vtable prefix through OpenStorage. Methods that create objects are never invoked.
    [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorageNative
    {
        [PreserveSig] int CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStream stream);
        [PreserveSig] int OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr reserved1, uint mode, uint reserved2, out IStream stream);
        [PreserveSig] int CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStorageNative storage);
        [PreserveSig] int OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr priority, uint mode, IntPtr exclude, uint reserved, out IStorageNative storage);
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgOpenStorageEx(string name, uint mode, uint format, uint flags, IntPtr options, IntPtr security, ref Guid iid, out IStorageNative storage);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
