using WebLingo.Core;
using WebLingo.Platform.Abstractions;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Mail;

/// <summary>
/// <c>std:mail</c>, a standard module (specification, section 7.17), a native module installed with the runtime in the
/// <c>std</c> scheme's folder as <c>mail.dll</c>: <c>send(message, server)</c>, asynchronous, a task of the message's
/// <c>Message-ID</c>, sent through the platform's mail (<see cref="WebLingoHeap.Mail"/>) off the worker. A message is a
/// map, <c>{ from, to, cc, bcc, replyTo, subject, text, html, headers, attachments }</c>, every key checked, one it does not
/// know an error naming the field it may have meant, and every value's kind; an attachment a map, <c>{ path }</c>, a file
/// read by the host as <c>std:fs</c> reads one, or <c>{ name, content, type }</c>. The server, where given, is a configured
/// server's name, or a map: <c>{ token }</c> or <c>{ server, token }</c>, a configured server signed in to with a token, or
/// a server of the script's own, <c>{ host, port, user, password, token, tls, thumbprint }</c>, one under <c>wasp</c> only
/// where <c>"network"</c> allows its host. A message the server cannot take is a <c>MailError</c> (WL3023).
/// </summary>
public sealed class WebLingoMailModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry) => registry.Function("send", 2, 1, Send);

    private static readonly string[] MessageFields = ["from", "to", "cc", "bcc", "replyTo", "subject", "text", "html", "headers", "attachments"];
    private static readonly string[] AttachmentFields = ["path", "name", "content", "type"];
    private static readonly string[] ServerFields = ["server", "host", "port", "user", "password", "token", "tls", "thumbprint"];

    private static readonly WebLingoArgument MessageArgument = new("send", 0, 2, "the message",
        "a map, { from: \"…\", to: \"…\", subject: \"…\", text: \"…\" }");
    private static readonly WebLingoArgument ServerArgument = new("send", 1, 2, "the server",
        "a configured server's name, \"newsletter\", or a map, { host: \"smtp.example.com\", … }");

    /// <summary>An attachment as the script gave it: its file's path, to be read off the worker, or its content.</summary>
    private sealed record Pending(string? Path, string Name, string Type, byte[]? Content);

    /// <summary>A file an attachment names that cannot be read.</summary>
    private sealed class AttachmentFailure(string path, WebLingoFileSystemException failure) : Exception(failure.Message)
    {
        public string Path { get; } = path;
        public WebLingoFileSystemException Failure { get; } = failure;
    }

    private static bool Send(ReadOnlySpan<WebLingoValue> a, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var given = heap.Held(a[0]);
        if (given.Kind != WebLingoValueKind.Map)
        {
            error = MessageArgument.Wrong(given, heap);
            return false;
        }
        if (!TryMessage(given, heap, out var message, out var pending, out error)
            || !TryServer(heap.Held(a[1]), heap, out var named, out var own, out var token, out error))
        {
            return false;
        }
        if (heap.Mail is not { } mail)
        {
            error = Wrong("This host gives scripts no mail.");
            return false;
        }
        var files = heap.FileSystem;
        if (files is null && pending.Exists(attachment => attachment.Path is not null))
        {
            error = Wrong("This host gives scripts no files, which an attachment's path names.");
            return false;
        }
        if (heap.Scheduler is not { } scheduler)
        {
            error = Wrong("std:mail's functions need a VM that runs tasks.");
            return false;
        }
        string Work()
        {
            var attachments = new List<WebLingoMailAttachment>();
            foreach (var attachment in pending)
            {
                var content = attachment.Content;
                if (attachment.Path is { } path)
                {
                    try
                    {
                        content = files!.ReadBytes(path);
                    }
                    catch (WebLingoFileSystemException failure)
                    {
                        throw new AttachmentFailure(path, failure);
                    }
                }
                attachments.Add(new WebLingoMailAttachment(attachment.Name, attachment.Type, content!));
            }
            var whole = message with { Attachments = attachments };
            return own is null ? mail.Send(named, token, whole) : mail.Send(own, token, whole);
        }
        return scheduler.TryRunOffWorker(Work, Sent, Failed, out result, out error);
    }

    private static bool Sent(string messageId, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error) =>
        heap.TryAllocateString(messageId, out result, out error);

    private static WebLingoRuntimeError? Failed(Exception exception) => exception switch
    {
        WebLingoMailException { Kind: WebLingoMailErrorKind.Invalid } invalid => Wrong($"The message cannot be sent: {invalid.Message}."),
        WebLingoMailException failure => new WebLingoRuntimeError(WebLingoDiagnosticCodes.MailFailure,
            $"The message cannot be sent: {failure.Message}.")
        {
            Fields = [new("reply", failure.Reply is { } reply ? (long)reply : null)]
        },
        AttachmentFailure attachment => new WebLingoRuntimeError(attachment.Failure.Kind == WebLingoFileErrorKind.NotAllowed
            ? WebLingoDiagnosticCodes.FilePermissionDenied
            : WebLingoDiagnosticCodes.IOFailure, $"'{attachment.Path}' cannot be attached: {attachment.Failure.Message}."),
        _ => null
    };

    private static WebLingoRuntimeError Wrong(string message) => new(WebLingoDiagnosticCodes.InvalidOperation, message);

    /// <summary>The message, checked, its attachments not yet read, and each attachment as the script gave it.</summary>
    private static bool TryMessage(WebLingoValue map, WebLingoHeap heap, out WebLingoMailMessage message, out List<Pending> pending,
        out WebLingoRuntimeError? error)
    {
        message = null!;
        pending = [];
        WebLingoMailAddress? from = null;
        List<WebLingoMailAddress> to = [], cc = [], bcc = [], replyTo = [];
        string? subject = null, text = null, html = null;
        var headers = new List<KeyValuePair<string, string>>();
        for (var i = 0; i < heap.MapCount(map); i++)
        {
            var (key, value) = heap.MapEntryAt(map, i);
            (key, value) = (heap.Held(key), heap.Held(value));
            if (key.Kind != WebLingoValueKind.String)
            {
                error = Wrong($"{MessageArgument.Named} must name its fields by strings, not {WebLingoValueFormatter.Describe(key, heap)}.");
                return false;
            }
            var field = heap.GetString(key);
            if (value.Kind == WebLingoValueKind.Null && Array.IndexOf(MessageFields, field) >= 0) continue; // As if not given.
            var ok = field switch
            {
                "from" => TryAddress(value, heap, field, out from, out error),
                "to" => TryAddresses(value, heap, field, to, out error),
                "cc" => TryAddresses(value, heap, field, cc, out error),
                "bcc" => TryAddresses(value, heap, field, bcc, out error),
                "replyTo" => TryAddresses(value, heap, field, replyTo, out error),
                "subject" => TryText(value, heap, field, oneLine: true, out subject, out error),
                "text" => TryText(value, heap, field, oneLine: false, out text, out error),
                "html" => TryText(value, heap, field, oneLine: false, out html, out error),
                "headers" => TryHeaders(value, heap, headers, out error),
                "attachments" => TryAttachments(value, heap, pending, out error),
                _ => Unknown(field, "a message", MessageFields, out error)
            };
            if (!ok) return false;
        }
        if (from is null)
        {
            error = Wrong("A message must have from, its sender's address.");
            return false;
        }
        if (to.Count + cc.Count + bcc.Count == 0)
        {
            error = Wrong("A message must have at least one recipient, in to, cc or bcc.");
            return false;
        }
        message = new WebLingoMailMessage(from, to, cc, bcc, replyTo, subject ?? "", text, html, headers, []);
        error = null;
        return true;
    }

    private static bool TryText(WebLingoValue value, WebLingoHeap heap, string field, bool oneLine, out string? text, out WebLingoRuntimeError? error)
    {
        text = null;
        if (value.Kind != WebLingoValueKind.String)
        {
            error = Wrong($"{MessageArgument.Named} must give {field} a string, not {WebLingoValueFormatter.Describe(value, heap)}.");
            return false;
        }
        text = heap.GetString(value);
        if (oneLine && HasLineBreak(text))
        {
            error = Wrong($"The message's {field} must hold no line break.");
            return false;
        }
        error = null;
        return true;
    }

    private static bool TryAddresses(WebLingoValue value, WebLingoHeap heap, string field, List<WebLingoMailAddress> addresses,
        out WebLingoRuntimeError? error)
    {
        if (value.Kind == WebLingoValueKind.String)
        {
            if (!TryAddress(value, heap, field, out var address, out error)) return false;
            addresses.Add(address!);
            return true;
        }
        if (value.Kind != WebLingoValueKind.Array)
        {
            error = Wrong($"{MessageArgument.Named} must give {field} an address, a string, or an array of them, not "
                          + $"{WebLingoValueFormatter.Describe(value, heap)}.");
            return false;
        }
        foreach (var element in heap.GetArray(value))
        {
            if (!TryAddress(heap.Held(element), heap, field, out var address, out error)) return false;
            addresses.Add(address!);
        }
        error = null;
        return true;
    }

    /// <summary>
    /// An address as a script writes it, <c>ann@example.com</c>, <c>Ann &lt;ann@example.com&gt;</c> or
    /// <c>"Smith, Ann" &lt;ann@example.com&gt;</c>, its address ASCII, as mail without SMTPUTF8 needs.
    /// </summary>
    private static bool TryAddress(WebLingoValue value, WebLingoHeap heap, string field, out WebLingoMailAddress? address,
        out WebLingoRuntimeError? error)
    {
        address = null;
        if (value.Kind != WebLingoValueKind.String)
        {
            error = Wrong($"The message's {field} must hold addresses, each a string, not {WebLingoValueFormatter.Describe(value, heap)}.");
            return false;
        }
        var text = heap.GetString(value);
        if (HasLineBreak(text))
        {
            error = Wrong($"The message's {field} must hold no line break.");
            return false;
        }
        var trimmed = text.Trim();
        string? name = null;
        var bare = trimmed;
        var open = trimmed.LastIndexOf('<');
        if (open >= 0 && trimmed.EndsWith('>'))
        {
            bare = trimmed[(open + 1)..^1].Trim();
            name = trimmed[..open].Trim();
            if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
                name = name[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
            if (name.Length == 0) name = null;
        }
        foreach (var c in bare)
        {
            if (c > '~')
            {
                error = Wrong($"The message's {field} has '{bare}', an address that is not ASCII, which mail cannot use without SMTPUTF8, "
                              + "not built yet.");
                return false;
            }
        }
        if (!IsAddress(bare))
        {
            error = Wrong($"The message's {field} has '{text}', which is not an address, \"ann@example.com\" or \"Ann <ann@example.com>\".");
            return false;
        }
        address = new WebLingoMailAddress(name, bare);
        error = null;
        return true;
    }

    /// <summary>Whether <paramref name="address"/> is a mailbox SMTP takes: a dot-atom, an <c>@</c>, and a domain or an address literal.</summary>
    private static bool IsAddress(string address)
    {
        var at = address.LastIndexOf('@');
        if (at < 1 || at > 64 || address.Length > 254) return false;
        var (local, domain) = (address[..at], address[(at + 1)..]);
        if (local.StartsWith('.') || local.EndsWith('.') || local.Contains("..", StringComparison.Ordinal)) return false;
        foreach (var c in local)
        {
            if (!char.IsAsciiLetterOrDigit(c) && !"!#$%&'*+-/=?^_`{|}~.".Contains(c)) return false;
        }
        if (domain.Length > 2 && domain[0] == '[' && domain[^1] == ']')
        {
            foreach (var c in domain[1..^1])
            {
                if (!char.IsAsciiHexDigit(c) && c is not ('.' or ':') && !"IPv".Contains(c)) return false;
            }
            return true;
        }
        var labels = domain.Split('.');
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-') return false;
            foreach (var c in label)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
            }
        }
        return true;
    }

    private static bool HasLineBreak(string text) => text.Contains('\r') || text.Contains('\n');

    /// <summary>Whether every character of <paramref name="text"/> is as <paramref name="test"/> asks, LINQ being kept out of the core.</summary>
    private static bool Every(string text, Func<char, bool> test)
    {
        foreach (var c in text)
        {
            if (!test(c)) return false;
        }
        return true;
    }

    /// <summary>The message's own fields' headers, and those of its parts, which <c>headers</c> may not give.</summary>
    private static bool IsReserved(string name) =>
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase)
        || Array.Exists(["from", "to", "cc", "bcc", "reply-to", "subject", "date", "message-id", "mime-version"],
            reserved => string.Equals(reserved, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryHeaders(WebLingoValue value, WebLingoHeap heap, List<KeyValuePair<string, string>> headers, out WebLingoRuntimeError? error)
    {
        const string Must = "a map of names to strings, { \"X-Mailer\": \"Shop\" }";
        if (value.Kind != WebLingoValueKind.Map)
        {
            error = Wrong($"{MessageArgument.Named} must give headers {Must}, not {WebLingoValueFormatter.Describe(value, heap)}.");
            return false;
        }
        for (var i = 0; i < heap.MapCount(value); i++)
        {
            var (key, entry) = heap.MapEntryAt(value, i);
            (key, entry) = (heap.Held(key), heap.Held(entry));
            if (key.Kind != WebLingoValueKind.String || entry.Kind != WebLingoValueKind.String)
            {
                error = Wrong($"{MessageArgument.Named} must give headers {Must}.");
                return false;
            }
            var (name, text) = (heap.GetString(key), heap.GetString(entry));
            if (name.Length == 0 || !Every(name, c => c is >= '!' and <= '~' && c != ':'))
            {
                error = Wrong($"'{name}' is not a header's name.");
                return false;
            }
            if (IsReserved(name))
            {
                error = Wrong($"The message's headers may not give '{name}', which the message itself gives.");
                return false;
            }
            if (HasLineBreak(text))
            {
                error = Wrong($"The message's header '{name}' must hold no line break.");
                return false;
            }
            headers.Add(new(name, text));
        }
        error = null;
        return true;
    }

    private static bool TryAttachments(WebLingoValue value, WebLingoHeap heap, List<Pending> pending, out WebLingoRuntimeError? error)
    {
        const string Must = "an array of maps, { path: \"…\" } or { name: \"…\", content: … }";
        if (value.Kind != WebLingoValueKind.Array)
        {
            error = Wrong($"{MessageArgument.Named} must give attachments {Must}, not {WebLingoValueFormatter.Describe(value, heap)}.");
            return false;
        }
        foreach (var element in heap.GetArray(value))
        {
            var attachment = heap.Held(element);
            if (attachment.Kind != WebLingoValueKind.Map)
            {
                error = Wrong($"The message's attachments must each be a map, {{ path: \"…\" }} or {{ name: \"…\", content: … }}, not "
                              + $"{WebLingoValueFormatter.Describe(attachment, heap)}.");
                return false;
            }
            string? path = null, name = null, type = null;
            byte[]? content = null;
            var fromText = false;
            for (var i = 0; i < heap.MapCount(attachment); i++)
            {
                var (key, entry) = heap.MapEntryAt(attachment, i);
                (key, entry) = (heap.Held(key), heap.Held(entry));
                var field = key.Kind == WebLingoValueKind.String ? heap.GetString(key) : "";
                if (Array.IndexOf(AttachmentFields, field) < 0)
                {
                    if (key.Kind == WebLingoValueKind.String) return Unknown(field, "an attachment", AttachmentFields, out error);
                    error = Wrong($"An attachment must name its fields by strings, not {WebLingoValueFormatter.Describe(key, heap)}.");
                    return false;
                }
                if (field == "content" && entry.Kind is WebLingoValueKind.String or WebLingoValueKind.Bytes)
                {
                    fromText = entry.Kind == WebLingoValueKind.String;
                    content = fromText ? WebLingoTextCodec.Encode(heap.GetString(entry), WebLingoCharset.Utf8) : heap.GetBytes(entry);
                    continue;
                }
                if (entry.Kind != WebLingoValueKind.String)
                {
                    error = Wrong($"An attachment's {field} must be {(field == "content" ? "a string or bytes" : "a string")}, not "
                                  + $"{WebLingoValueFormatter.Describe(entry, heap)}.");
                    return false;
                }
                var text = heap.GetString(entry);
                switch (field)
                {
                    case "path": path = text; break;
                    case "name": name = text; break;
                    default: type = text; break;
                }
            }
            if ((path is null) == (content is null))
            {
                error = Wrong(path is null ? "An attachment must have a path, or a name and content." : "An attachment has a path or content, not both.");
                return false;
            }
            if (path is not null && path.Length == 0)
            {
                error = Wrong("An attachment's path must name a file.");
                return false;
            }
            name ??= path is null ? null : path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
            if (name is null)
            {
                error = Wrong("An attachment with content must have a name.");
                return false;
            }
            if (name.Length is 0 or > 255 || name.IndexOfAny(['/', '\\']) >= 0 || !Every(name, c => !char.IsControl(c)))
            {
                error = Wrong($"An attachment's name must be a file's name, without a folder: '{name}'.");
                return false;
            }
            if (type is not null && !IsMediaType(type))
            {
                error = Wrong($"An attachment's type must be a type, \"application/pdf\", not '{type}'.");
                return false;
            }
            if (type is null)
            {
                type = TypeOf(name);
                if (fromText && type.StartsWith("text/", StringComparison.Ordinal)) type += "; charset=utf-8";
            }
            pending.Add(new Pending(path, name, type, content));
        }
        error = null;
        return true;
    }

    /// <summary>Whether <paramref name="type"/> is a media type, <c>type/subtype</c>, parameters after it as they are.</summary>
    private static bool IsMediaType(string type)
    {
        if (HasLineBreak(type)) return false;
        var semicolon = type.IndexOf(';');
        var bare = (semicolon < 0 ? type : type[..semicolon]).Trim();
        var slash = bare.IndexOf('/');
        return slash > 0 && slash < bare.Length - 1 && Every(bare, c => char.IsAsciiLetterOrDigit(c) || "/!#$&^_.+-".Contains(c))
               && bare.IndexOf('/', slash + 1) < 0;
    }

    /// <summary>A file's type by its name's extension, <c>application/octet-stream</c> where it is none of these.</summary>
    private static string TypeOf(string name)
    {
        var dot = name.LastIndexOf('.');
        var extension = dot < 0 ? "" : name[(dot + 1)..].ToLowerInvariant();
        return extension switch
        {
            "txt" => "text/plain",
            "htm" or "html" => "text/html",
            "csv" => "text/csv",
            "ics" => "text/calendar",
            "json" => "application/json",
            "xml" => "application/xml",
            "pdf" => "application/pdf",
            "zip" => "application/zip",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// The server a send names: none, the configuration's own choice; a configured server's name; a map naming one,
    /// <c>{ server, token }</c>, or none, <c>{ token }</c>; or a server of the script's own, <c>{ host, … }</c>, its host
    /// one the application may reach.
    /// </summary>
    private static bool TryServer(WebLingoValue value, WebLingoHeap heap, out string? named, out WebLingoMailServer? own, out string? token,
        out WebLingoRuntimeError? error)
    {
        (named, own, token, error) = (null, null, null, null);
        if (value.Kind == WebLingoValueKind.Null) return true;
        if (value.Kind == WebLingoValueKind.String && heap.GetString(value).Length > 0)
        {
            named = heap.GetString(value);
            return true;
        }
        if (value.Kind != WebLingoValueKind.Map)
        {
            error = ServerArgument.Wrong(value, heap);
            return false;
        }
        var given = new Dictionary<string, WebLingoValue>(StringComparer.Ordinal);
        for (var i = 0; i < heap.MapCount(value); i++)
        {
            var (key, entry) = heap.MapEntryAt(value, i);
            (key, entry) = (heap.Held(key), heap.Held(entry));
            var field = key.Kind == WebLingoValueKind.String ? heap.GetString(key) : "";
            if (Array.IndexOf(ServerFields, field) < 0)
            {
                if (key.Kind == WebLingoValueKind.String) return Unknown(field, "a server", ServerFields, out error);
                error = Wrong($"{ServerArgument.Named} must name its fields by strings, not {WebLingoValueFormatter.Describe(key, heap)}.");
                return false;
            }
            if (entry.Kind == WebLingoValueKind.Null) continue;
            if (field == "port" ? entry.Kind != WebLingoValueKind.Int64 || entry.AsInt64 is < 1 or > 65535 : entry.Kind != WebLingoValueKind.String)
            {
                error = Wrong($"A server's {field} must be {(field == "port" ? "an integer from 1 to 65535" : "a string")}, not "
                              + $"{WebLingoValueFormatter.Describe(entry, heap)}.");
                return false;
            }
            given[field] = entry;
        }
        string? Text(string field) => given.TryGetValue(field, out var entry) ? heap.GetString(entry) : null;
        token = Text("token");
        if (!given.ContainsKey("host"))
        {
            foreach (var field in new[] { "port", "user", "password", "tls", "thumbprint" })
            {
                if (!given.ContainsKey(field)) continue;
                error = Wrong($"A server that gives its {field} must give its host too; a configured server's settings are its configuration's.");
                return false;
            }
            named = Text("server");
            return true;
        }
        if (given.ContainsKey("server"))
        {
            error = Wrong("A server is a configured server, by its name, or a server of its own, by its host, not both.");
            return false;
        }
        var host = Text("host")!;
        var tlsName = Text("tls") ?? "starttls";
        WebLingoMailTls? tls = tlsName switch
        {
            "starttls" => WebLingoMailTls.StartTls,
            "implicit" => WebLingoMailTls.Implicit,
            "none" => WebLingoMailTls.None,
            _ => null
        };
        if (tls is null)
        {
            error = Wrong($"A server's tls must be \"starttls\", \"implicit\" or \"none\", not '{tlsName}'.");
            return false;
        }
        var (user, password) = (Text("user"), Text("password"));
        if (user is null && (password is not null || token is not null))
        {
            error = Wrong($"A server's {(password is not null ? "password" : "token")} needs its user.");
            return false;
        }
        if (heap.Network is not { } network || !network.Allows(host))
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.NetworkPermissionDenied, $"'{host}' is not a host this application may reach.");
            return false;
        }
        var port = given.TryGetValue("port", out var number) ? (int)number.AsInt64 : WebLingoMailServer.DefaultPort(tls.Value);
        own = new WebLingoMailServer(host, port, tls.Value, user, password is null ? null : WebLingoSecret.Given(password), Text("thumbprint"));
        return true;
    }

    /// <summary>
    /// A field that is not one, its error naming the field it may have meant, one or two letters from it, as
    /// <c>'subjet' is not a field of a message; did you mean subject?</c>, or else every field.
    /// </summary>
    private static bool Unknown(string field, string of, string[] fields, out WebLingoRuntimeError? error)
    {
        var (best, distance) = ("", int.MaxValue);
        foreach (var known in fields)
        {
            var apart = Distance(field.ToLowerInvariant(), known.ToLowerInvariant());
            if (apart < distance) (best, distance) = (known, apart);
        }
        error = Wrong(distance <= Math.Max(1, Math.Min(2, field.Length / 3))
            ? $"'{field}' is not a field of {of}; did you mean {best}?"
            : $"'{field}' is not a field of {of}, whose fields are {string.Join(", ", fields[..^1])} and {fields[^1]}.");
        return false;
    }

    /// <summary>
    /// The fewest letters added, removed or changed, or two beside each other swapped, that make one text the other (the
    /// optimal string alignment distance), so that <c>hots</c> is one from <c>host</c>.
    /// </summary>
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j], d[i, j - 1]) + 1, d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
    }
}
