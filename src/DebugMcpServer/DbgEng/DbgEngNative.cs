using System.Runtime.InteropServices;

namespace DebugMcpServer.DbgEng;

internal static class DbgEngNative
{
    [DllImport("dbgeng.dll", PreserveSig = true)]
    public static extern int DebugCreate(ref Guid interfaceId, out IntPtr debugClient);

    public static readonly Guid IID_IDebugClient = new("27fe5639-8407-4f47-8364-ee118fb08ac8");

    public const uint DEBUG_END_ACTIVE_DETACH = 2;
    public const uint DEBUG_EXECUTE_DEFAULT = 0;
    public const uint DEBUG_OUTCTL_THIS_CLIENT = 0;
    public const uint INFINITE = 0xFFFFFFFF;
}

// Vtable layout from Windows SDK dbgeng.h (10.0.26100.0):
// IDebugClient: IUnknown(3) + 32 own methods = 35 slots
//   slot 19 = OpenDumpFile
//   slot 26 = EndSession  
//   slot 34 = SetOutputCallbacks
//
// IDebugControl: IUnknown(3) + 89 own methods = 92 slots
//   Verified from header: GetInterrupt=slot 3, Execute=slot 63, WaitForEvent=slot 90
//
// IDebugSystemObjects: IUnknown(3) + own methods
//   slot 9 = GetNumberThreads

[ComImport, Guid("27fe5639-8407-4f47-8364-ee118fb08ac8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDebugClient
{
    // Slots 3-18 (16 stubs before OpenDumpFile at slot 19)
    void S03(); void S04(); void S05(); void S06(); void S07();
    void S08(); void S09(); void S10(); void S11(); void S12();
    void S13(); void S14(); void S15(); void S16(); void S17();
    void S18();
    // Slot 19: OpenDumpFile
    void OpenDumpFile([MarshalAs(UnmanagedType.LPStr)] string dumpFile);
    // Slots 20-25 (6 stubs)
    void S20(); void S21(); void S22(); void S23(); void S24();
    void S25();
    // Slot 26: EndSession
    [PreserveSig]
    int EndSession(uint flags);
    // Slots 27-33 (7 stubs)
    void S27(); void S28(); void S29(); void S30(); void S31();
    void S32(); void S33();
    // Slot 34: SetOutputCallbacks  
    void SetOutputCallbacks(IntPtr callbacks);
}

// IDebugControl: 93 methods total (including QI from COM boilerplate)
// Real methods span slots 3-92 (92 own methods after IUnknown)
// Verified from Windows SDK dbgeng.h (10.0.26100.0) including STDMETHODV:
//   Execute = slot 66, WaitForEvent = slot 93
[ComImport, Guid("5182e668-105e-416e-ad92-24ef800424ba")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDebugControl
{
    // Slots 3-65 (63 stubs before Execute at slot 66)
    void S03(); void S04(); void S05(); void S06(); void S07();
    void S08(); void S09(); void S10(); void S11(); void S12();
    void S13(); void S14(); void S15(); void S16(); void S17();
    void S18(); void S19(); void S20(); void S21(); void S22();
    void S23(); void S24(); void S25(); void S26(); void S27();
    void S28(); void S29(); void S30(); void S31(); void S32();
    void S33(); void S34(); void S35(); void S36(); void S37();
    void S38(); void S39(); void S40(); void S41(); void S42();
    void S43(); void S44(); void S45(); void S46(); void S47();
    void S48(); void S49(); void S50(); void S51(); void S52();
    void S53(); void S54(); void S55(); void S56(); void S57();
    void S58(); void S59(); void S60(); void S61(); void S62();
    void S63(); void S64(); void S65();

    // Slot 66: Execute
    [PreserveSig]
    int Execute(uint outputControl, [MarshalAs(UnmanagedType.LPStr)] string command, uint flags);

    // Slots 67-92 (26 stubs)
    void S67(); void S68(); void S69(); void S70(); void S71();
    void S72(); void S73(); void S74(); void S75(); void S76();
    void S77(); void S78(); void S79(); void S80(); void S81();
    void S82(); void S83(); void S84(); void S85(); void S86();
    void S87(); void S88(); void S89(); void S90(); void S91();
    void S92();

    // Slot 93: WaitForEvent
    [PreserveSig]
    int WaitForEvent(uint flags, uint timeout);
}

[ComImport, Guid("6b86fe2c-2c4f-4f0c-9da2-174311acc327")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDebugSystemObjects
{
    // Slots 3-8 (6 stubs)
    void S03(); void S04(); void S05(); void S06(); void S07(); void S08();
    // Slot 9: GetNumberThreads
    [PreserveSig]
    int GetNumberThreads(out uint number);
}
