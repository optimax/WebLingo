using System.Runtime.InteropServices;
namespace WebLingo.Windows.Com;

/// <summary>
/// Windows's COM as <c>windows:com</c> calls it (specification, section 3.10, "How it is built"): its functions by platform
/// invoke, and its interfaces' methods through unmanaged function pointers on their tables of methods, never .NET's own COM
/// interop, which Native C# will not have, so that one source compiles with either toolchain. A <c>VARIANT</c> is memory
/// of <see cref="VariantSize"/> bytes, its type at its start and its value 8 bytes in. Adapter-only (section 7.14).
/// </summary>
internal static unsafe class WebLingoComInterop
{
    public const int Ok = 0;
    public const int False = 1;
    public const int UnknownName = unchecked((int)0x80020006);
    public const int MemberNotFound = unchecked((int)0x80020003);
    public const int Exception = unchecked((int)0x80020009);
    public const int ParameterNotFound = unchecked((int)0x80020004);
    public const int BadParameterCount = unchecked((int)0x8002000E);
    public const int TypeMismatch = unchecked((int)0x80020005);

    public const int ValueMember = 0;
    public const int NewEnumMember = -4;
    public const int PropertyPutMember = -3;

    public const ushort Method = 1;
    public const ushort PropertyGet = 2;
    public const ushort PropertyPut = 4;
    public const ushort PropertyPutRef = 8;

    public const ushort Empty = 0, Null = 1, I2 = 2, I4 = 3, R4 = 4, R8 = 5, Currency = 6, Date = 7, Bstr = 8, Dispatch = 9, Error = 10,
        Bool = 11, Variant = 12, Unknown = 13, DecimalType = 14, I1 = 16, UI1 = 17, UI2 = 18, UI4 = 19, I8 = 20, UI8 = 21, Int = 22, UInt = 23;
    public const ushort ArrayFlag = 0x2000;
    public const ushort ByRefFlag = 0x4000;

    /// <summary>The invariant locale, so that no COM object reads or writes a value by the server's culture.</summary>
    public const uint Locale = 0x007F;

    public static readonly Guid DispatchInterface = new("00020400-0000-0000-C000-000000000046");
    public static readonly Guid EnumVariantInterface = new("00020404-0000-0000-C000-000000000046");

    /// <summary>A <c>VARIANT</c>'s size: 16 bytes on a 32-bit process, 24 on a 64-bit one.</summary>
    public static readonly int VariantSize = 8 + 2 * IntPtr.Size;

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(Guid* classId, nint outer, uint context, Guid* interfaceId, nint* result);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    public static extern int CLSIDFromProgID(string progId, Guid* classId);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    public static extern int CLSIDFromString(string text, Guid* classId);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    public static extern int CoGetObject(string name, nint bindOptions, Guid* interfaceId, nint* result);

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    public static extern nint SysAllocStringLen(string text, uint length);

    [DllImport("oleaut32.dll")]
    public static extern uint SysStringLen(nint bstr);

    [DllImport("oleaut32.dll")]
    public static extern void SysFreeString(nint bstr);

    [DllImport("oleaut32.dll")]
    public static extern int VariantClear(byte* variant);

    [DllImport("oleaut32.dll")]
    public static extern nint SafeArrayCreateVector(ushort type, int lowerBound, uint count);

    [DllImport("oleaut32.dll")]
    public static extern int SafeArrayDestroy(nint array);

    [DllImport("oleaut32.dll")]
    public static extern uint SafeArrayGetDim(nint array);

    [DllImport("oleaut32.dll")]
    public static extern int SafeArrayGetLBound(nint array, uint dimension, int* bound);

    [DllImport("oleaut32.dll")]
    public static extern int SafeArrayGetUBound(nint array, uint dimension, int* bound);

    [DllImport("oleaut32.dll")]
    public static extern int SafeArrayGetVartype(nint array, ushort* type);

    [DllImport("oleaut32.dll")]
    public static extern int SafeArrayGetElement(nint array, int* indices, void* value);

    [DllImport("oleaut32.dll")]
    public static extern int SafeArrayPutElement(nint array, int* indices, void* value);

    [DllImport("oleaut32.dll")]
    public static extern int SafeArrayAccessData(nint array, void** data);

    [DllImport("oleaut32.dll")]
    public static extern int SafeArrayUnaccessData(nint array);

    [DllImport("oleaut32.dll")]
    public static extern int GetErrorInfo(uint reserved, nint* errorInfo);

    [DllImport("oleaut32.dll")]
    public static extern int SetErrorInfo(uint reserved, nint errorInfo);

    /// <summary>
    /// The thread's error information, which an object that fails without an <c>EXCEPINFO</c> may have set, as VBScript reads
    /// it: its source and its description, "" where it has none; taken, so that it is not read twice.
    /// </summary>
    public static (string Source, string Description) TakeErrorInfo()
    {
        nint info;
        if (GetErrorInfo(0, &info) != Ok || info == 0) return ("", "");
        nint source = 0, description = 0;
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Methods(info)[4])(info, &source);
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Methods(info)[5])(info, &description);
        Release(info);
        return (TakeString(source), TakeString(description));
    }

    /// <summary>The arguments of <c>IDispatch::Invoke</c>, last first.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DispatchParameters
    {
        public byte* Arguments;
        public int* NamedArguments;
        public uint ArgumentCount;
        public uint NamedArgumentCount;
    }

    /// <summary>What <c>IDispatch::Invoke</c> tells of a failure: its code, its source's and its description's text.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ExceptionInfo
    {
        public ushort Code;
        public ushort Reserved;
        public nint Source;
        public nint Description;
        public nint HelpFile;
        public uint HelpContext;
        public nint ReservedPointer;
        public nint DeferredFillIn;
        public int Scode;
    }

    private static void** Methods(nint unknown) => *(void***)unknown;

    public static int QueryInterface(nint unknown, Guid* interfaceId, nint* result) =>
        ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Methods(unknown)[0])(unknown, interfaceId, result);

    public static uint AddRef(nint unknown) => ((delegate* unmanaged[Stdcall]<nint, uint>)Methods(unknown)[1])(unknown);

    public static uint Release(nint unknown) => ((delegate* unmanaged[Stdcall]<nint, uint>)Methods(unknown)[2])(unknown);

    /// <summary><c>IDispatch::GetIDsOfNames</c>, for one name.</summary>
    public static int GetIdOfName(nint dispatch, string name, int* id)
    {
        var none = Guid.Empty;
        fixed (char* text = name)
        {
            var names = text;
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, char**, uint, uint, int*, int>)Methods(dispatch)[5])(dispatch, &none, &names, 1,
                Locale, id);
        }
    }

    /// <summary><c>IDispatch::Invoke</c>.</summary>
    public static int Invoke(nint dispatch, int member, ushort flags, DispatchParameters* parameters, byte* result, ExceptionInfo* exception,
        uint* argumentError)
    {
        var none = Guid.Empty;
        return ((delegate* unmanaged[Stdcall]<nint, int, Guid*, uint, ushort, DispatchParameters*, byte*, ExceptionInfo*, uint*, int>)
            Methods(dispatch)[6])(dispatch, member, &none, Locale, flags, parameters, result, exception, argumentError);
    }

    /// <summary><c>IEnumVARIANT::Next</c>, for one item.</summary>
    public static int Next(nint enumerator, byte* item, uint* fetched) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, byte*, uint*, int>)Methods(enumerator)[3])(enumerator, 1, item, fetched);

    /// <summary>
    /// The text of an <c>EXCEPINFO</c>'s strings, which it frees, its deferred filling-in run first if it has one; and its
    /// code, its <c>scode</c>, or its <c>wCode</c> when it has none.
    /// </summary>
    public static (int Code, string Source, string Description) Read(ExceptionInfo* exception)
    {
        if (exception->DeferredFillIn != 0) ((delegate* unmanaged[Stdcall]<ExceptionInfo*, int>)exception->DeferredFillIn)(exception);
        var source = TakeString(exception->Source);
        var description = TakeString(exception->Description);
        TakeString(exception->HelpFile);
        return (exception->Scode != 0 ? exception->Scode : exception->Code, source, description);
    }

    /// <summary>A <c>BSTR</c>'s text, which it frees; "" for none.</summary>
    public static string TakeString(nint bstr)
    {
        if (bstr == 0) return "";
        var text = new string((char*)bstr, 0, (int)SysStringLen(bstr));
        SysFreeString(bstr);
        return text;
    }
}
