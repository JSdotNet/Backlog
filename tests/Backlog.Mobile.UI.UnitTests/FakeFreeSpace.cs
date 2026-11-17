namespace Backlog.Mobile.UI.UnitTests;

/// <summary>The phone's free space as the test sets it: plenty until a test says otherwise.</summary>
internal sealed class FakeFreeSpace : IFreeSpace
{
    public long? Available { get; set; } = 64L * 1024 * 1024 * 1024;

    public long? AvailableBytes(string folder) => Available;
}
