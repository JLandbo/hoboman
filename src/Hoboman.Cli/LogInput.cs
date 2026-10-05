namespace Hoboman.Cli;

// Without a run and without Last, the newest runs are listed, as many as Count, or as many as history lists when it is not given.
sealed record LogInput(string Workflow, string? Run, bool Last, bool Follow, int? Count = null)
{
    // A run is given by its id or as the newest, not both, only one run can be followed, and a count only goes with the list.
    public bool IsValid => !(Run is not null && Last) && !(Follow && Run is null && !Last) && (Count is null || Count >= 1 && Run is null && !Last);
}
