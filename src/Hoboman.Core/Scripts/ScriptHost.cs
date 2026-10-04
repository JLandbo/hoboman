using System.Text.RegularExpressions;
using Acornima;
using Jint;
using Jint.Native;
using Jint.Native.Json;
using Jint.Runtime;
using JsonElement = System.Text.Json.JsonElement;

namespace Hoboman.Core.Scripts;

// The only place that knows Jint. A script gets the values of the run as a frozen vars object, and what it returns is its output as JSON.
// It has no access to files, the network or .NET, and its limits stop it rather than the app.
public static class ScriptHost
{
    static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);
    static readonly ScriptParsingOptions _parsing = new() { AllowReturnOutsideFunction = true };

    // Tells of a syntax error with its file and line, so it is found before anything is sent.
    public static string? SyntaxErrorIn(string name, string code)
    {
        try
        {
            Engine.PrepareScript(code, name, strict: true, new ScriptPreparationOptions { ParsingOptions = _parsing });
            return null;
        }
        // Code nested too deeply cannot be prepared either, which is no parse error.
        catch (ScriptPreparationException exception)
        {
            return exception.InnerException?.Message ?? exception.Message;
        }
    }

    // A script that returns nothing gives null, as it may only check the values.
    public static string? Run(string name, string code, IReadOnlyDictionary<string, JsonElement> values, CancellationToken cancellationToken)
    {
        using var engine = new Engine(options =>
        {
            options.Strict().DisableStringCompilation().TimeoutInterval(_timeout).RegexTimeoutInterval(_timeout).LimitRecursion(256).Constraint(new HeldMemoryLimit()).CancellationToken(cancellationToken);
            // Without it, a deep JSON.stringify ends the whole process, which the recursion limit does not stop.
            options.Constraints.StackOverflowGuard = true;
        });
        try
        {
            var parser = new JsonParser(engine);
            var vars = new JsObject(engine);
            foreach (var (key, value) in values)
            {
                vars.Set(key, parser.Parse(value.GetRawText()));
            }
            engine.SetValue("vars", vars);
            engine.Execute("Object.freeze(vars)");
            var output = new JsonSerializer(engine).Serialize(engine.Evaluate(code, name, _parsing));
            return output.IsUndefined() ? null : output.AsString();
        }
        catch (JavaScriptException exception)
        {
            throw new ScriptException(exception.Location.Start.Line > 0 ? $"{name}:{exception.Location.Start.Line}: {exception.Message}" : $"{name}: {exception.Message}");
        }
        catch (ExecutionCanceledException)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception) when (exception is JintException or TimeoutException or RegexMatchTimeoutException or ParseErrorException)
        {
            throw new ScriptException($"{name}: {exception.Message}");
        }
    }

    // Counts what the app holds while the script runs, not what the script throws away, as only memory that builds up harms the app.
    // Measuring it costs, so it is done each time the script has allocated another 64 MB.
    sealed class HeldMemoryLimit : Constraint
    {
        const long _limit = 512L * 1024 * 1024;
        const long _interval = 64L * 1024 * 1024;
        long _held;
        long _nextCheck;

        public override void Check()
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            if (allocated < _nextCheck)
            {
                return;
            }
            _nextCheck = allocated + _interval;
            // Garbage that is not collected yet only counts until a collection shows what is really held.
            if (GC.GetTotalMemory(false) - _held > _limit && GC.GetTotalMemory(true) - _held > _limit)
            {
                throw new MemoryLimitExceededException($"The script holds more than {_limit / 1024 / 1024} MB of memory");
            }
        }

        public override void Reset()
        {
            _held = GC.GetTotalMemory(false);
            _nextCheck = GC.GetAllocatedBytesForCurrentThread() + _interval;
        }
    }
}
