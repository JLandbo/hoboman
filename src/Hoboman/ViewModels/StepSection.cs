namespace Hoboman.ViewModels;

// The tabs of a step: a request has all but Code, and a script has Code and Saves.
public enum StepSection { Params, Headers, Body, Auth, Retry, Saves, Code }
