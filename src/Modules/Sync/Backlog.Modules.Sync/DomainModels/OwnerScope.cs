namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// Who the current request is, as read out of its device token and nowhere
/// else. Every slice that touches an owner's data takes one of these, so the
/// scope is a parameter a handler cannot forget rather than a check it might.
/// <para>
/// The service is what keeps a device inside its owner's data. Storage is not:
/// the service reaches its store under one identity that can see everything, so
/// the boundary is this value being threaded through every read and write, and
/// nothing underneath re-checks it (.arc42/adr/0005 §Identity).
/// </para>
/// </summary>
public readonly record struct OwnerScope(OwnerId OwnerId, DeviceId DeviceId);
