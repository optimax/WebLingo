using System.Text.RegularExpressions;
using WebLingo.Core;
namespace WebLingo.Runtime;

// Regular expressions (specification, section 3.6, "Regular expressions"; the owner's answers, 16 November 2026): .NET's
// syntax, and its engine for now, a value of a kind of its own, its class Regex, made by new Regex(pattern, flags), with no
// literal: r.test(s) or r.isMatch(s), r.match(s) or r.exec(s), r.matchAll(s) or r.matches(s), r.replace(s, replacement)
// and r.split(s), and a string's replace, split, match and matchAll given one; and r.pattern and r.flags. A match is a
// new array of its groups' text, the whole match first, a group that took part in none null. Each operation's time is
// limited (WebLingoExecutionContext.RegexTimeoutMilliseconds), so that a slow match is a TimeoutError (WL3010).
public static partial class WebLingoMembers
{
    private static bool IsRegexMethod(WebLingoMember member) =>
        member is WebLingoMember.Test or WebLingoMember.Match or WebLingoMember.MatchAll or WebLingoMember.Replace or WebLingoMember.Split;

    private static bool IsRegexProperty(WebLingoMember member) => member is WebLingoMember.Pattern or WebLingoMember.Flags;

    private static bool TryCallRegex(WebLingoValue target, WebLingoMember member, string name, WebLingoValue[] values, int first, int count,
        WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var arity = member == WebLingoMember.Replace ? 2 : 1;
        if (count != arity) return WrongArgumentCount(name, arity, count, out error);
        if (!TryText(new(name, 0, arity, "the text", "a string"), values[first], heap, out var text, out error)) return false;
        var replacement = "";
        if (member == WebLingoMember.Replace && !TryText(new(name, 1, 2, "the replacement", "a string"), values[first + 1], heap, out replacement, out error))
        {
            return false;
        }
        return TryRegex(target, member, text, replacement, heap, out result, out error);
    }

    /// <summary>A regular expression's operation on a text, a string's given one included.</summary>
    private static bool TryRegex(WebLingoValue regexValue, WebLingoMember member, string text, string replacement, WebLingoHeap heap,
        out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var (regex, pattern, flags) = heap.GetRegex(regexValue);
        try
        {
            switch (member)
            {
                case WebLingoMember.Test:
                    return Done(WebLingoValue.FromBoolean(regex.IsMatch(text)), out result, out error);
                case WebLingoMember.Match:
                {
                    var match = regex.Match(text);
                    return match.Success ? TryGroups(match, heap, out result, out error) : Done(WebLingoValue.Null, out result, out error);
                }
                case WebLingoMember.MatchAll:
                {
                    var matches = new List<WebLingoValue>();
                    heap.AddRoots(matches);
                    try
                    {
                        for (var match = regex.Match(text); match.Success; match = match.NextMatch())
                        {
                            if (!TryGroups(match, heap, out var groups, out error)) return false;
                            matches.Add(groups);
                        }
                        return heap.TryAllocateArray([.. matches], 0, matches.Count, out result, out error);
                    }
                    finally
                    {
                        heap.RemoveLastRoots(matches);
                    }
                }
                case WebLingoMember.Replace:
                {
                    // .NET's substitutions, $1, ${name}, $0 and $$; a result beyond the memory left refused as it grows.
                    var room = heap.RoomLeft;
                    var grown = (long)text.Length;
                    var replaced = regex.Replace(text, match =>
                    {
                        var piece = match.Result(replacement);
                        grown += piece.Length - match.Length;
                        if (grown > room) throw new InsufficientRoom(grown);
                        return piece;
                    });
                    return heap.TryAllocateString(replaced, out result, out error);
                }
                default:
                    return TryStrings(regex.Split(text), heap, out result, out error);
            }
        }
        catch (RegexMatchTimeoutException timeout)
        {
            var milliseconds = (int)timeout.MatchTimeout.TotalMilliseconds;
            var duration = milliseconds switch
            {
                1 => "1 millisecond",
                WebLingoHeap.DefaultRegexTimeoutMilliseconds => "1 second",
                _ => milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " milliseconds"
            };
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.Timeout,
                $"The regular expression /{pattern}/{flags} took longer than {duration} to match.");
            return false;
        }
        catch (InsufficientRoom room)
        {
            error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.BudgetExceeded,
                $"A string of {room.Length} characters does not fit in the memory this execution may use.");
            return false;
        }
    }

    /// <summary><c>r.pattern</c> and <c>r.flags</c>, as <c>new Regex(pattern, flags)</c> was given them.</summary>
    private static bool TryGetRegexProperty(WebLingoValue target, WebLingoMember member, WebLingoHeap heap, out WebLingoValue result,
        out WebLingoRuntimeError? error)
    {
        var (_, pattern, flags) = heap.GetRegex(target);
        return heap.TryAllocateString(member == WebLingoMember.Pattern ? pattern : flags, out result, out error);
    }

    /// <summary>A match's groups, in .NET's numbering, the whole match first, a group that matched nothing <c>null</c>.</summary>
    private static bool TryGroups(Match match, WebLingoHeap heap, out WebLingoValue result, out WebLingoRuntimeError? error)
    {
        result = WebLingoValue.Null;
        var groups = new List<WebLingoValue>(match.Groups.Count);
        heap.AddRoots(groups);
        try
        {
            for (var i = 0; i < match.Groups.Count; i++)
            {
                var group = match.Groups[i];
                if (!group.Success)
                {
                    groups.Add(WebLingoValue.Null);
                    continue;
                }
                if (!heap.TryAllocateString(group.Value, out var piece, out error)) return false;
                groups.Add(piece);
            }
            return heap.TryAllocateArray([.. groups], 0, groups.Count, out result, out error);
        }
        finally
        {
            heap.RemoveLastRoots(groups);
        }
    }

    /// <summary>A replacement's result grown beyond the memory left, which stops it before it is built whole.</summary>
    private sealed class InsufficientRoom(long length) : Exception
    {
        public long Length { get; } = length;
    }
}
