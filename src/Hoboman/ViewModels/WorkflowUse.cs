namespace Hoboman.ViewModels;

// A name a step uses and where its value comes from. The value itself is never shown, as it is only known while the workflow runs.
public sealed record WorkflowUse(string Name, string Source, bool IsMissing);
