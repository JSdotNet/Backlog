namespace Backlog.UI.Components.Feedback;

public enum SpinnerSize
{
    Small,
    Medium,
    Large
}

public enum SaveState
{
    Idle,
    Saving,
    Saved,
    Failed
}

public enum ToastSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>How tall a placeholder bar is drawn: the shape of the thing it is
/// standing in for, so the block reads as content rather than as decoration.</summary>
public enum SkeletonShape
{
    Text,
    Heading,
    Block
}
