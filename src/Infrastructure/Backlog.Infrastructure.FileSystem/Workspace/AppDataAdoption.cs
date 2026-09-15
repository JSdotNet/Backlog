namespace Backlog.Infrastructure.FileSystem;

/// <summary>What came of asking <see cref="PackagedAppData.Adopt"/> to bring
/// a packaged install's redirected state into the folder the app names.
/// Four answers rather than a boolean because the host logs them and a
/// person reading that log after an upgrade wants to know which: nothing was
/// there, it was already done, it was done now, or it could not be.</summary>
public sealed record AppDataAdoption(AppDataAdoptionOutcome Outcome, string? Error)
{
    /// <summary>No redirected folder, or one with nothing of the app's in it.</summary>
    public static AppDataAdoption NothingToAdopt { get; } = new(AppDataAdoptionOutcome.NothingToAdopt, null);

    /// <summary>The real folder already holds settings or a database — an
    /// earlier start adopted, or the install never redirected.</summary>
    public static AppDataAdoption AlreadyInPlace { get; } = new(AppDataAdoptionOutcome.AlreadyInPlace, null);

    /// <summary>The state was copied across; the redirected folder was left as it was.</summary>
    public static AppDataAdoption Adopted { get; } = new(AppDataAdoptionOutcome.Adopted, null);

    /// <summary>The copy did not finish, for the reason given. The settings
    /// file was not written, so the next start tries again.</summary>
    public static AppDataAdoption Failed(string error) => new(AppDataAdoptionOutcome.Failed, error);
}

public enum AppDataAdoptionOutcome
{
    NothingToAdopt,
    AlreadyInPlace,
    Adopted,
    Failed,
}
