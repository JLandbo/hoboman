using Hoboman.Core.Requests;

namespace Hoboman.Core.Environments;

public sealed record ApiEnvironment(string Name, IReadOnlyList<KeyValue> Variables);
