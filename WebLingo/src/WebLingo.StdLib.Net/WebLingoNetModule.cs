using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Net;

/// <summary>
/// <c>std:net</c>, a standard module (specification, sections 3.9 and 7.9), a native module installed with the runtime in
/// the <c>std</c> scheme's folder as <c>net.dll</c>: TCP connections through the platform's network
/// (<see cref="WebLingoHeap.Network"/>), the class <c>Socket</c>, made by <c>Socket.connect(host, port)</c>, a task of a
/// connection; its <c>read()</c>, a task of the bytes that have come, waiting for at least one, <c>null</c> at the end;
/// its <c>write(data)</c>, a task, the data a string, sent as UTF-8, or bytes; and its <c>dispose()</c>, which closes it,
/// once. Each operation runs off the worker. A host the application may not reach is a <c>NetworkPermissionError</c>
/// (WL3017); a connection that fails a <c>NetworkError</c> (WL3014).
/// </summary>
public sealed class WebLingoNetModule : IWebLingoNativeModule
{
    /// <summary>The most bytes one <c>read()</c> gives.</summary>
    private const int ReadSize = 64 * 1024;

    public void Register(IWebLingoNativeRegistry registry)
    {
        var socket = registry.Class("Socket");
        socket.DeclareFactory("connect", 2, 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            var classValue = a[0];
            if (a[1].Kind != WebLingoValueKind.String || heap.GetString(a[1]).Length == 0)
            {
                error = Wrong($"A host is a name or an address, a string, not {WebLingoValueFormatter.Describe(a[1], heap)}.");
                return false;
            }
            if (a[2].Kind != WebLingoValueKind.Int64 || a[2].AsInt64 is < 1 or > 65535)
            {
                error = Wrong($"A port is an integer from 1 to 65535, not {WebLingoValueFormatter.Describe(a[2], heap)}.");
                return false;
            }
            if (!TryServices(heap, out var network, out var scheduler, out error)) return false;
            var (host, port) = (heap.GetString(a[1]), (int)a[2].AsInt64);
            return scheduler.TryRunOffWorker(() => network.Connect(host, port),
                (IWebLingoConnection connection, WebLingoHeap h, out WebLingoValue made, out WebLingoRuntimeError? wrong) =>
                {
                    if (socket.TryMake(classValue, h, out made, out wrong) && socket.TrySetHostObject(made, connection, h, out wrong)) return true;
                    connection.Dispose();
                    return false;
                }, Failed, out result, out error);
        });
        socket.DeclareMethod("read", 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!socket.TryGetHostObject<IWebLingoConnection>(a[0], heap, out var connection, out error)
                || !TryServices(heap, out _, out var scheduler, out error))
            {
                return false;
            }
            return scheduler.TryRunOffWorker(() => connection.Read(ReadSize),
                (byte[]? data, WebLingoHeap h, out WebLingoValue read, out WebLingoRuntimeError? wrong) =>
                {
                    if (data is null)
                    {
                        (read, wrong) = (WebLingoValue.Null, null);
                        return true;
                    }
                    return h.TryAllocateBytes(data, out read, out wrong);
                }, Failed, out result, out error);
        });
        socket.DeclareMethod("write", 1, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!socket.TryGetHostObject<IWebLingoConnection>(a[0], heap, out var connection, out error)
                || !TryServices(heap, out _, out var scheduler, out error))
            {
                return false;
            }
            byte[] data;
            if (a[1].Kind == WebLingoValueKind.String) data = WebLingoTextCodec.Encode(heap.GetString(a[1]), WebLingoCharset.Utf8);
            else if (a[1].Kind == WebLingoValueKind.Bytes) data = heap.GetBytes(a[1]);
            else
            {
                error = Wrong($"A socket writes a string or bytes, not {WebLingoValueFormatter.Describe(a[1], heap)}.");
                return false;
            }
            return scheduler.TryRunOffWorker(() =>
            {
                connection.Write(data);
                return true;
            }, (bool _, WebLingoHeap h, out WebLingoValue written, out WebLingoRuntimeError? wrong) =>
            {
                (written, wrong) = (WebLingoValue.Null, null);
                return true;
            }, Failed, out result, out error);
        });
        socket.DeclareMethod("dispose", 0, (ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            // Closed once: disposing again does nothing (section 3.4).
            result = WebLingoValue.Null;
            if (!socket.TryReleaseHostObject(a[0], heap, out var held, out error)) return false;
            (held as IDisposable)?.Dispose();
            return true;
        });
    }

    private static bool TryServices(WebLingoHeap heap, out IWebLingoNetwork network, out WebLingoScheduler scheduler, out WebLingoRuntimeError? error)
    {
        (network, scheduler, error) = (heap.Network!, heap.Scheduler!, null);
        if (heap.Network is null) error = Wrong("This host gives scripts no network.");
        else if (heap.Scheduler is null) error = Wrong("std:net's operations need a VM that runs tasks.");
        return error is null;
    }

    private static WebLingoRuntimeError? Failed(Exception exception) => exception is WebLingoNetworkException failure
        ? new WebLingoRuntimeError(failure.Kind switch
            {
                WebLingoNetworkErrorKind.Failed => WebLingoDiagnosticCodes.NetworkFailure,
                WebLingoNetworkErrorKind.NotAllowed => WebLingoDiagnosticCodes.NetworkPermissionDenied,
                _ => WebLingoDiagnosticCodes.InvalidOperation
            },
            Capitalized(failure.Message) + ".")
        : null;

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static WebLingoRuntimeError Wrong(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);
}
