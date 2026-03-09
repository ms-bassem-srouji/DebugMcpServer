using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DebugMcpServer.DbgEng;

/// <summary>
/// COM-visible implementation of IDebugOutputCallbacks.
/// Must be a public top-level class for Marshal.GetComInterfaceForObject to work.
/// Windows only — guarded by [SupportedOSPlatform].
/// </summary>
[ComVisible(true)]
[Guid("4bf58045-d654-4c40-b0af-683090f356dc")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDebugOutputCallbacksManaged
{
    [PreserveSig]
    int Output(uint mask, [MarshalAs(UnmanagedType.LPStr)] string text);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[SupportedOSPlatform("windows")]
public sealed class DbgEngOutputCapture : IDebugOutputCallbacksManaged, IDisposable
{
    private readonly StringBuilder _output = new();
    private IntPtr _comPointer;
    private bool _disposed;

    public DbgEngOutputCapture()
    {
        _comPointer = Marshal.GetComInterfaceForObject(this, typeof(IDebugOutputCallbacksManaged));
    }

    public IntPtr ComPointer => _comPointer;

    public int Output(uint mask, string text)
    {
        _output.Append(text);
        return 0; // S_OK
    }

    public string GetOutput()
    {
        var result = _output.ToString();
        _output.Clear();
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_comPointer != IntPtr.Zero)
        {
            Marshal.Release(_comPointer);
            _comPointer = IntPtr.Zero;
        }
    }
}
