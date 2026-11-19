using WebLingo.Core;
using WebLingo.Runtime;
namespace WebLingo.StdLib.Random;

/// <summary>Seeded, reproducible random numbers.</summary>
public sealed class WebLingoRandomModule : IWebLingoNativeModule
{
    public void Register(IWebLingoNativeRegistry registry)
    {
        var random = registry.Class("Random");
        random.DeclareConstructor(1, (ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
            out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            var seed = heap.Held(args[1]);
            if (seed.Kind != WebLingoValueKind.Int64)
            {
                error = new WebLingoArgument("Random", 0, 1, "the seed", "an integer").Wrong(args[1], heap);
                return false;
            }
            return random.TrySetHostObject(args[0], new Generator(seed.AsInt64), heap, out error);
        });
        random.DeclareMethod("next", 0, (ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
            out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!random.TryGetHostObject<Generator>(args[0], heap, out var generator, out error)) return false;
            const double unit = 1.0 / (1UL << 53);
            result = WebLingoValue.FromBinary64((generator.Next() >> 11) * unit);
            return true;
        });
        random.DeclareMethod("nextInt", 2, (ReadOnlySpan<WebLingoValue> args, WebLingoHeap heap,
            out WebLingoValue result, out WebLingoRuntimeError? error) =>
        {
            result = WebLingoValue.Null;
            if (!random.TryGetHostObject<Generator>(args[0], heap, out var generator, out error)) return false;
            var min = heap.Held(args[1]);
            var max = heap.Held(args[2]);
            if (min.Kind != WebLingoValueKind.Int64)
            {
                error = new WebLingoArgument("nextInt", 0, 2, "the inclusive lower bound", "an integer").Wrong(args[1], heap);
                return false;
            }
            if (max.Kind != WebLingoValueKind.Int64)
            {
                error = new WebLingoArgument("nextInt", 1, 2, "the exclusive upper bound", "an integer").Wrong(args[2], heap);
                return false;
            }
            if (min.AsInt64 >= max.AsInt64)
            {
                error = new WebLingoRuntimeError(WebLingoDiagnosticCodes.InvalidOperation,
                    "The inclusive lower bound of 'nextInt' must be less than its exclusive upper bound.");
                return false;
            }
            result = WebLingoValue.FromInt64(generator.NextInt(min.AsInt64, max.AsInt64));
            return true;
        });
    }

    private sealed class Generator(long seed)
    {
        private ulong _state = unchecked((ulong)seed);

        public ulong Next()
        {
            // SplitMix64. The arithmetic wraps modulo 2^64.
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                var mixed = _state;
                mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
                mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
                return mixed ^ (mixed >> 31);
            }
        }

        public long NextInt(long min, long max)
        {
            // Unsigned arithmetic represents ranges wider than a signed integer.
            var range = unchecked((ulong)max - (ulong)min);
            var threshold = unchecked(0UL - range) % range;
            ulong sample;
            do { sample = Next(); } while (sample < threshold);
            return unchecked((long)((ulong)min + sample % range));
        }
    }
}
