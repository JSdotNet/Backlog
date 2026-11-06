using System.ComponentModel.DataAnnotations;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Modules.Sync.Api.Options;

/// <summary>
/// The dictation relay's settings (local ADR 0045 §2, §3): the cap and the audio allowlist
/// the route enforces, and where Foundry's transcription deployment is. Settings rather than
/// constants, bound and validated at start (inherited ADR 0018).
/// <para>
/// With no <see cref="Endpoint"/> the service registers no transcriber: the module's
/// stand-in answers, the route says 503, and the phone dictates on the device.
/// </para>
/// </summary>
public sealed class SyncTranscriptionOptions : IValidatableObject
{
    /// <summary>The section this binds to, named for the module that owns it.</summary>
    public const string SectionName = "Modules:Sync:Transcription";

    /// <summary>The allowlist when the setting names none —
    /// <see cref="SyncTranscriptionLimits.DefaultContentTypes"/>, which the phone reads
    /// too.</summary>
    public static IReadOnlyList<string> DefaultContentTypes => SyncTranscriptionLimits.DefaultContentTypes;

    /// <summary>The most one recording may weigh: 25 MB by default, and never more, because
    /// that is Foundry's own file limit and a larger upload would only be refused there.</summary>
    [Range(1, SyncTranscriptionLimits.DefaultMaxBytes)]
    public long MaxBytes { get; set; } = SyncTranscriptionLimits.DefaultMaxBytes;

    /// <summary>
    /// The media types a recording may declare, compared without their parameters and
    /// without regard to case. Empty means <see cref="DefaultContentTypes"/> — empty rather
    /// than pre-filled because the binder appends a configured array to one the property
    /// already holds, so a pre-filled default could only be widened, never narrowed.
    /// </summary>
    public string[] ContentTypes { get; set; } = [];

    /// <summary>The Foundry account endpoint. None: the route answers 503.</summary>
    public string? Endpoint { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string Deployment { get; set; } = AzureFoundryTranscriptionOptions.DefaultDeployment;

    [Required(AllowEmptyStrings = false)]
    public string ApiVersion { get; set; } = AzureFoundryTranscriptionOptions.DefaultApiVersion;

    /// <summary>The managed identity, deployed; <c>None</c> against the local stand-in, which
    /// the AppHost sets.</summary>
    public AzureFoundryTranscriptionCredential Credential { get; set; } = AzureFoundryTranscriptionCredential.ManagedIdentity;

    /// <summary>Whether <paramref name="mediaType"/> — the declared type with its parameters
    /// removed — is on the list in force.</summary>
    public bool Allows(string mediaType)
    {
        IReadOnlyList<string> allowed = ContentTypes.Length > 0 ? ContentTypes : DefaultContentTypes;

        return allowed.Any(type => string.Equals(type.Trim(), mediaType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The endpoint as a URI, or null when none is set.</summary>
    public Uri? EndpointUri() =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ? uri : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Endpoint)) yield break;

        if (EndpointUri() is not { } uri || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            yield return new ValidationResult(
                $"'{SectionName}:Endpoint' must be an absolute http or https URL; it was '{Endpoint}'.",
                [nameof(Endpoint)]);
            yield break;
        }

        // An Entra bearer goes out with every call under a credential, so it never travels in
        // clear text (guideline 0013). Only the local stand-in, which takes none, is plain http.
        if (uri.Scheme == Uri.UriSchemeHttp && Credential != AzureFoundryTranscriptionCredential.None)
        {
            yield return new ValidationResult(
                $"'{SectionName}:Endpoint' must be https when '{SectionName}:Credential' is {Credential}; only Credential=None may use http.",
                [nameof(Endpoint), nameof(Credential)]);
        }
    }
}
