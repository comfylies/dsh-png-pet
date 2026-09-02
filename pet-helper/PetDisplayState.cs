namespace PetHelper;

public sealed record PetDisplayState(string State, string Label, long Sequence)
{
    public PetAnimationKey AnimationKey => (State, Label) switch
    {
        ("idle", "") => PetAnimationKey.Idle,
        ("active", "思考中…") => PetAnimationKey.Thinking,
        ("active", "工作中…") => PetAnimationKey.Working,
        ("active", "思考中/工作中") => PetAnimationKey.Thinking,
        ("active", "输出中…") => PetAnimationKey.Responding,
        ("waiting", "等待你的操作") => PetAnimationKey.Waiting,
        ("question", "点击回到 Harness 回答") => PetAnimationKey.Question,
        ("success", "已完成") => PetAnimationKey.Success,
        ("error", "发生错误") => PetAnimationKey.Error,
        ("disconnected", "未连接") => PetAnimationKey.Disconnected,
        _ => PetAnimationKey.Disconnected,
    };

    private static readonly IReadOnlyDictionary<string, string> ExclusiveLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["idle"] = string.Empty,
            ["waiting"] = "等待你的操作",
            ["question"] = "点击回到 Harness 回答",
            ["success"] = "已完成",
            ["error"] = "发生错误",
            ["disconnected"] = "未连接",
        };

    public static readonly PetDisplayState Disconnected = new("disconnected", "未连接", 0);

    public static PetDisplayState From(string? state, IReadOnlyList<string>? activities, string? label, long sequence)
    {
        if (state is null || activities is null || label is null || sequence < 0)
        {
            return Disconnected;
        }

        if (state == "active")
        {
            var activityLabel = activities.Count switch
            {
                1 when activities[0] == "thinking" => "思考中…",
                1 when activities[0] == "working" => "工作中…",
                1 when activities[0] == "responding" => "输出中…",
                2 when activities[0] == "thinking" && activities[1] == "working" => "思考中/工作中",
                _ => null,
            };
            return activityLabel == label ? new PetDisplayState(state, label, sequence) : Disconnected;
        }

        return activities.Count == 0
            && ExclusiveLabels.TryGetValue(state, out var expected)
            && label == expected
            ? new PetDisplayState(state, label, sequence)
            : Disconnected;
    }
}
