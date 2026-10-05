namespace Hoboman.Core.Scripts;

public enum ScriptOutput { Json, Html, Xml, Text }

public static class ScriptOutputs
{
    extension(ScriptOutput output)
    {
        // The type tells the app and the CLI what the output is, so it is shown as such.
        public string ContentType => output switch
        {
            ScriptOutput.Html => "text/html; charset=utf-8",
            ScriptOutput.Xml => "application/xml; charset=utf-8",
            ScriptOutput.Text => "text/plain; charset=utf-8",
            _ => "application/json",
        };
    }
}
