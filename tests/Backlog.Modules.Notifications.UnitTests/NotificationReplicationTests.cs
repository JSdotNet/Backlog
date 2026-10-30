using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Extensions;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// <see cref="INotificationReplication"/>, what the sync exchange reads off the owner instance
/// (local ADR 0039): the open notifications and any one as it stands, whole, and only on an
/// owner.
/// </summary>
public sealed class NotificationReplicationTests
{
    private readonly InMemoryNotificationRepository _store = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_open_notifications_are_read_whole_and_a_settled_one_is_read_as_it_stands()
    {
        await using var provider = Owner();
        var notifications = provider.GetRequiredService<INotifications>();
        var replication = provider.GetRequiredService<INotificationReplication>();

        var asked = (await notifications.PublishAsync(Question(), Cancellation)).Value;
        var told = (await notifications.PublishAsync(Alert(), Cancellation)).Value;
        await provider.GetRequiredService<INotificationReplies>().AcknowledgeAsync(told, Cancellation);

        var open = Assert.Single(await replication.ListOpenAsync(Cancellation));
        Assert.Equal(asked, open.Id);
        Assert.Equal(Drafts.Owner, open.OwnerInstanceId);
        Assert.Equal(NotificationKind.Question, open.Kind);
        Assert.Equal(Session, open.Subject);
        Assert.Equal(["yes", "no"], open.AnswerSpec!.Choices.Select(choice => choice.Id));

        var settled = await replication.GetAsync(told, Cancellation);
        Assert.NotNull(settled);
        Assert.Equal(NotificationState.Acknowledged, settled.State);
        Assert.NotNull(settled.SettledAt);

        Assert.Null(await replication.GetAsync(Guid.NewGuid(), Cancellation));
    }

    [Fact]
    public async Task An_accepted_answer_is_part_of_the_snapshot()
    {
        await using var provider = Owner();
        var id = (await provider.GetRequiredService<INotifications>().PublishAsync(Question(), Cancellation)).Value;

        await provider.GetRequiredService<INotificationReplies>().AcceptReplyAsync(
            new NotificationReply(Guid.NewGuid(), id, Drafts.Owner, Phone, ["yes"], null, Now),
            Cancellation);

        var snapshot = await provider.GetRequiredService<INotificationReplication>().GetAsync(id, Cancellation);
        Assert.Equal(NotificationState.Answered, snapshot!.State);
        Assert.Equal(["yes"], snapshot.Answer!.ChoiceIds);
    }

    [Fact]
    public void A_viewer_has_nothing_to_carry()
    {
        var services = new ServiceCollection();
        services.AddNotificationsModule(NotificationsRole.Viewer);

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<INotificationReplication>());
    }

    private ServiceProvider Owner()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationRepository>(_store);
        services.AddSingleton<IDeviceIdentitySource>(new FixedDevice(Drafts.Owner));
        services.AddNotificationsModule();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
