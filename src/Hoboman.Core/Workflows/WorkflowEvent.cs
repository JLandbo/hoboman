using System.Text.Json.Serialization;

namespace Hoboman.Core.Workflows;

// The type is written first and the small fields before the headers and the body, so a line cut short can still be read.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RunStarted), "run.started")]
[JsonDerivedType(typeof(StepStarted), "step.started")]
[JsonDerivedType(typeof(StepFinished), "step.finished")]
[JsonDerivedType(typeof(StepSkipped), "step.skipped")]
[JsonDerivedType(typeof(StepCancelled), "step.cancelled")]
[JsonDerivedType(typeof(RunFinished), "run.finished")]
public abstract record WorkflowEvent;
