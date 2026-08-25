namespace PetHelper;

public abstract record ProtocolMessage(int Version, string Kind);

public sealed record HelloMessage() : ProtocolMessage(3, "hello");

public sealed record ShutdownMessage() : ProtocolMessage(3, "shutdown");

public sealed record ConfigMessage(double Scale, bool ReducedMotion) : ProtocolMessage(3, "config");

public sealed record StateMessage(string State, IReadOnlyList<string> Activities, string Label, long Sequence) : ProtocolMessage(3, "state");
