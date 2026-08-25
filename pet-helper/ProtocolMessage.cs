namespace PetHelper;

public abstract record ProtocolMessage(int Version, string Kind);

public sealed record HelloMessage() : ProtocolMessage(2, "hello");

public sealed record ShutdownMessage() : ProtocolMessage(2, "shutdown");

public sealed record ConfigMessage(double Scale, bool ReducedMotion) : ProtocolMessage(2, "config");

public sealed record StateMessage(string State, string Label, long Sequence) : ProtocolMessage(2, "state");
