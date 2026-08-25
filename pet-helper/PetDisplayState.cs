namespace PetHelper;

public sealed record PetDisplayState(string State, string Label, long Sequence)
{
    private static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["idle"] = string.Empty,
            ["thinking"] = "思考中…",
            ["working"] = "工作中…",
            ["waiting"] = "等待你的操作",
            ["success"] = "已完成",
            ["error"] = "发生错误",
            ["disconnected"] = "未连接",
        };

    public static readonly PetDisplayState Disconnected = new("disconnected", "未连接", 0);

    public static PetDisplayState From(string? state, string? label, long sequence)
    {
        return state is not null
            && label is not null
            && sequence >= 0
            && Labels.TryGetValue(state, out var expected)
            && label == expected
            ? new PetDisplayState(state, label, sequence)
            : Disconnected;
    }
}
