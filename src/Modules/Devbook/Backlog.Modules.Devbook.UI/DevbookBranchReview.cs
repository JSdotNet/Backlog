using System.Collections.Concurrent;
using System.Text;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Compare;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Reviews the branch the Devbook pane reads: everything it changed against the
/// repository's default branch, and each of its commits against the one before.
/// <para>
/// Only for a branch source. A local clone is the reader's own working copy, and
/// what changed in it is a question for their own git, not for GitHub.
/// </para>
/// <para>
/// Everything comes from GitHub's REST API and nothing touches the disk. What
/// GitHub answered is kept in memory for the session, in caches with a fixed
/// number of entries that drop the least recently used: comparisons per
/// repository, branch and the two heads they were drawn between, so a branch
/// that moves is compared again; commits, trees and blobs by id, which never
/// change. A reader stepping back and forth through the commits pays for each
/// file once.
/// </para>
/// <para>
/// One instance serves every window, so what a load answers is handed back as a
/// <see cref="DevbookReviewHandle"/> the window holds, never kept here against an
/// alias another window could load over. And a cached read is started on no
/// one's cancellation token: a reader arrowing past a commit cancels their own
/// wait, never the read another scope or window shares.
/// </para>
/// <para>
/// It hands the pane the compare components' own vocabulary —
/// <see cref="ChangeScope"/>, <see cref="ChangeCommit"/>, <see cref="ChangedFile"/>
/// and <see cref="ComparedSection"/> — and a refusal as a sentence, never an
/// exception, the way <see cref="DevbookSourceSelection"/> does.
/// </para>
/// </summary>
public sealed class DevbookBranchReview(
    GitHubSettingsStore repositories,
    IGitHubBranchCatalog branches,
    IGitHubBranchComparison comparisons,
    IGitHubTreeClient trees,
    TimeProvider? clock = null,
    DevbookBranchComparisons? sharedComparisons = null)
{
    /// <summary>The id of the whole-branch scope. Not hex, so it can never be
    /// mistaken for a commit id in the picker's one id space.</summary>
    public const string BranchScopeId = "branch";

    /// <summary>What a request the reader moved on from answers. The pane has
    /// already stopped listening; this is only never an exception.</summary>
    public const string CancelledMessage = "The request was cancelled.";

    /// <summary>
    /// How many chapters are read from GitHub at once while a scope's counts are
    /// worked out. Enough that a scope of twenty chapters is not twenty round
    /// trips end to end; few enough that a large one does not queue a burst
    /// against the rate limit.
    /// </summary>
    private const int ParallelReads = 4;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    // Bounded by entry count, least recently used out first. Blobs dominate: a
    // chapter is a few kilobytes, so five hundred of them is a few megabytes. A
    // tree is cached already narrowed to the chapter folders, never whole.
    // Comparisons are shared with the branch overview, so a branch the overview
    // compared on opening the pane is reviewed without asking GitHub again.
    private readonly DevbookBranchComparisons _comparisons = sharedComparisons ?? new DevbookBranchComparisons(comparisons);
    private readonly BoundedTaskCache<GitHubCommitChange> _commits = new(256);
    private readonly BoundedTaskCache<TreeIndex> _trees = new(64);
    private readonly BoundedTaskCache<string> _blobs = new(500);

    /// <summary>Whether this scope reads a branch, and so has one to review.</summary>
    public bool CanReview(string? repositoryAlias) => Repository(repositoryAlias) is not null;

    /// <summary>
    /// The branch against the default branch: one scope for all of it, and its
    /// commits, newest first. Resolves both heads every time — two cheap calls —
    /// so a branch that moved since the last load is compared again.
    /// </summary>
    public async Task<DevbookReviewOutline> LoadAsync(string? repositoryAlias, CancellationToken cancellationToken = default)
    {
        if (Repository(repositoryAlias) is not { } repository)
        {
            return DevbookReviewOutline.Failed("Only a branch the pane reads from GitHub can be reviewed.");
        }

        try
        {
            var head = await branches.ResolveHeadAsync(repository, repository.DevbookBranch, cancellationToken).ConfigureAwait(false);
            if (head is null)
            {
                return DevbookReviewOutline.Failed(
                    $"The branch {repository.DevbookBranch ?? "the pane reads"} is not on GitHub any more.");
            }

            var @base = await branches.ResolveHeadAsync(repository, null, cancellationToken).ConfigureAwait(false);
            if (@base is null)
            {
                return DevbookReviewOutline.Failed($"GitHub did not say which branch {repository.FullName} treats as its default.");
            }

            var comparison = await _comparisons.CompareAsync(repository, @base.Sha, head.Sha, cancellationToken).ConfigureAwait(false);

            var handle = new DevbookReviewHandle(repository, @base.Branch, head.Branch, comparison);
            var prefixes = ChapterFolders(repository);

            var scope = new ChangeScope(
                BranchScopeId,
                $"All changes vs {@base.Branch}",
                Entries(comparison.Files, prefixes).Count);

            var now = _clock.GetUtcNow();
            IReadOnlyList<ChangeCommit> commits =
            [
                .. comparison.Commits
                    .Reverse()
                    .Select(commit => new ChangeCommit(commit.Sha, Short(commit.Sha), commit.Subject, Age(commit.AuthoredAt, now)))
            ];

            var notice = comparison.IsTruncated
                ? $"Showing {comparison.Commits.Count} of {comparison.TotalCommits} commits."
                : null;

            return new DevbookReviewOutline([scope], commits, notice, null, handle);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DevbookReviewOutline.Failed(CancelledMessage);
        }
        catch (Exception exception)
        {
            return DevbookReviewOutline.Failed(Refusal(exception));
        }
    }

    /// <summary>
    /// The devbook chapters a scope changed, each with what moved in it, and how
    /// many other files it changed besides.
    /// <para>
    /// The counts need both sides of every chapter, so choosing a scope reads
    /// them all — the tree before it once, and each chapter's two blobs, a few at
    /// a time. That is what the list's <c>+1 −0 ±2</c> costs, and it is paid once:
    /// the blobs are kept, and opening a chapter afterwards reads nothing.
    /// </para>
    /// </summary>
    public async Task<DevbookReviewFiles> FilesAsync(
        DevbookReviewHandle? review,
        string? scopeId,
        CancellationToken cancellationToken = default)
    {
        if (review is null) return DevbookReviewFiles.Failed("Load the branch's changes first.");

        try
        {
            var scope = await ScopeAsync(review, scopeId, cancellationToken).ConfigureAwait(false);

            return new DevbookReviewFiles(
                [.. scope.Chapters.Select(chapter => chapter.Row)],
                scope.OtherFiles,
                scope.BeforeLabel,
                scope.AfterLabel,
                null,
                scope.Notice);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DevbookReviewFiles.Failed(CancelledMessage);
        }
        catch (Exception exception)
        {
            return DevbookReviewFiles.Failed(Refusal(exception));
        }
    }

    /// <summary>One chapter of a scope, compared section by section.</summary>
    public async Task<DevbookReviewComparison> CompareAsync(
        DevbookReviewHandle? review,
        string? scopeId,
        string? path,
        CancellationToken cancellationToken = default)
    {
        if (review is null) return new DevbookReviewComparison(null, "Load the branch's changes first.");

        try
        {
            var scope = await ScopeAsync(review, scopeId, cancellationToken).ConfigureAwait(false);
            var chapter = scope.Chapters.FirstOrDefault(c => string.Equals(c.Row.Path, path, StringComparison.Ordinal));

            return chapter is null
                ? new DevbookReviewComparison(null, $"{path} is not a devbook chapter this change touched.")
                : new DevbookReviewComparison(chapter.Comparison, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new DevbookReviewComparison(null, CancelledMessage);
        }
        catch (Exception exception)
        {
            return new DevbookReviewComparison(null, Refusal(exception));
        }
    }

    /// <summary>What a failure says to a person. A cancellation nobody asked for
    /// is a request that ran out of time. Anything else — a blob GitHub encoded
    /// in a way it cannot be read in, a chapter the comparison could not parse —
    /// is still a sentence rather than an exception: the review is a reading the
    /// pane offers, and one failure in it must not take the window down. This
    /// class has no logger to hand it to, so the sentence carries the reason.</summary>
    private static string Refusal(Exception exception) => exception switch
    {
        GitHubException or GitHubNotConfiguredException => exception.Message,
        OperationCanceledException => "GitHub did not answer in time. Try again.",
        _ => $"The branch's changes could not be read: {exception.Message}"
    };

    private GitHubRepositoryRef? Repository(string? repositoryAlias)
    {
        if (string.IsNullOrWhiteSpace(repositoryAlias)) return null;

        return repositories.Current.Find(repositoryAlias) is { DevbookSource: DevbookSourceKind.Branch } repository
            ? repository
            : null;
    }

    private async Task<Scope> ScopeAsync(DevbookReviewHandle review, string? scopeId, CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(scopeId) ? BranchScopeId : scopeId;

        if (review.Scopes.TryGetValue(id, out var cached)) return cached;

        var repository = review.Repository;
        Scope scope;

        if (string.Equals(id, BranchScopeId, StringComparison.Ordinal))
        {
            var comparison = review.Comparison;
            scope = await BuildScopeAsync(
                repository,
                comparison.MergeBaseSha,
                comparison.Files,
                comparison.FilesMayBeTruncated,
                review.BaseBranch,
                review.HeadBranch,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var change = await _commits.ReadAsync(
                $"{Key(repository)}|{id}",
                () => comparisons.ReadCommitAsync(repository, id, CancellationToken.None),
                cancellationToken).ConfigureAwait(false);

            scope = await BuildScopeAsync(
                repository,
                change.Commit.ParentSha,
                change.Files,
                change.FilesMayBeTruncated,
                change.Commit.ParentSha is { } parent ? Short(parent) : "Nothing (first commit)",
                Short(change.Commit.Sha),
                cancellationToken).ConfigureAwait(false);
        }

        review.Scopes[id] = scope;
        return scope;
    }

    private async Task<Scope> BuildScopeAsync(
        GitHubRepositoryRef repository,
        string? beforeCommit,
        IReadOnlyList<GitHubChangedFile> files,
        bool filesMayBeTruncated,
        string beforeLabel,
        string afterLabel,
        CancellationToken cancellationToken)
    {
        var prefixes = ChapterFolders(repository);
        var entries = Entries(files, prefixes);

        // The tree before is listed only when a chapter needs a side from it: a
        // root commit has none, and a scope of nothing but new chapters asks it
        // nothing.
        var needsBefore = beforeCommit is not null && entries.Any(entry => entry.BeforePath is not null);
        var before = needsBefore
            ? await TreeAsync(repository, beforeCommit!, prefixes, cancellationToken).ConfigureAwait(false)
            : null;

        var chapters = await CompareAllAsync(repository, entries, before, cancellationToken).ConfigureAwait(false);

        var notices = new List<string>();
        if (filesMayBeTruncated)
        {
            notices.Add($"GitHub lists at most {GitHubBranchComparison.FileListLimit} changed files, so chapters past them are not shown.");
        }

        if (before is { Truncated: true })
        {
            notices.Add("GitHub cut the earlier version's file list short, so a chapter it left out reads as wholly new.");
        }

        return new Scope(
            chapters,
            files.Count - entries.Count,
            beforeLabel,
            afterLabel,
            notices.Count == 0 ? null : string.Join(" ", notices));
    }

    /// <summary>
    /// Both sides of every chapter, a few at a time. The first refusal stops the
    /// rest from waiting — through a token of this scope's own, which the shared
    /// reads never see — and is what the scope fails with.
    /// </summary>
    private async Task<Chapter[]> CompareAllAsync(
        GitHubRepositoryRef repository,
        IReadOnlyList<Entry> entries,
        TreeIndex? before,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var gate = new SemaphoreSlim(ParallelReads);

        var reads = entries.Select(async entry =>
        {
            await gate.WaitAsync(stop.Token).ConfigureAwait(false);
            try
            {
                var beforeText = before is not null && entry.BeforePath is { } beforePath && before.Blobs.TryGetValue(beforePath, out var beforeBlob)
                    ? await BlobAsync(repository, beforeBlob, stop.Token).ConfigureAwait(false)
                    : string.Empty;
                var afterText = entry.AfterBlob is { } afterBlob
                    ? await BlobAsync(repository, afterBlob, stop.Token).ConfigureAwait(false)
                    : string.Empty;

                return Compare(entry, beforeText, afterText);
            }
            catch
            {
                await stop.CancelAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        try
        {
            return await Task.WhenAll(reads).ConfigureAwait(false);
        }
        catch
        {
            // The refusal that stopped the others, not the cancellations it caused.
            cancellationToken.ThrowIfCancellationRequested();
            var refusal = reads
                .Where(read => read.IsFaulted)
                .Select(read => read.Exception!.InnerException!)
                .FirstOrDefault(exception => exception is not OperationCanceledException);

            if (refusal is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(refusal);
            throw;
        }
    }

    /// <summary>
    /// The changed files that are chapters, with which side of each to read.
    /// <para>
    /// A chapter is Markdown under a devbook folder on either side of the change.
    /// A rename keeps the path it had before beside it, and is compared with the
    /// text it had there. A chapter renamed out of the devbook — to a folder the
    /// devbook does not read, or to a name that is not Markdown — is gone as far
    /// as the devbook is concerned, so it reads as removed, under its old path.
    /// </para>
    /// </summary>
    private static List<Entry> Entries(IReadOnlyList<GitHubChangedFile> files, IReadOnlyList<string> prefixes)
    {
        var entries = new List<Entry>();

        foreach (var file in files)
        {
            var now = IsChapter(file.Path, prefixes);
            var was = file.PreviousPath is { } previous && IsChapter(previous, prefixes);

            if (file.Status is GitHubFileStatus.Renamed && file.PreviousPath is { } from)
            {
                if (now)
                {
                    entries.Add(new Entry(file.Path, ChangeKind.Changed, from, file.BlobSha, $"renamed from {from}"));
                }
                else if (was)
                {
                    entries.Add(new Entry(from, ChangeKind.Removed, from, null, $"moved out to {file.Path}"));
                }

                continue;
            }

            if (!now) continue;

            entries.Add(file.Status switch
            {
                GitHubFileStatus.Added => new Entry(file.Path, ChangeKind.Added, null, file.BlobSha, null),
                GitHubFileStatus.Removed => new Entry(file.Path, ChangeKind.Removed, file.Path, null, null),
                _ => new Entry(file.Path, ChangeKind.Changed, file.Path, file.BlobSha, null)
            });
        }

        entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        return entries;
    }

    private static Chapter Compare(Entry entry, string before, string after)
    {
        var comparison = MarkdownCompare.Compare(before, after);

        var added = 0;
        var removed = 0;
        var changed = 0;
        Tally(comparison, ref added, ref removed, ref changed);

        var separator = entry.Path.LastIndexOf('/');
        var directory = separator < 0 ? null : entry.Path[..separator];

        // The rename rides on the directory line, which the list already gives a
        // line of its own and wraps rather than truncates.
        if (entry.Note is { } note) directory = directory is null ? note : $"{directory} · {note}";

        return new Chapter(
            new ChangedFile(
                entry.Path,
                separator < 0 ? entry.Path : entry.Path[(separator + 1)..],
                directory,
                entry.Kind,
                added,
                removed,
                changed),
            comparison);
    }

    /// <summary>Headings and blocks counted together, as the compare view's own
    /// summary counts them, so the row and the view beside it agree.</summary>
    private static void Tally(ComparedSection section, ref int added, ref int removed, ref int changed)
    {
        foreach (var kind in section.Blocks.Select(block => block.Kind).Append(section.Kind))
        {
            switch (kind)
            {
                case ChangeKind.Added: added++; break;
                case ChangeKind.Removed: removed++; break;
                case ChangeKind.Changed: changed++; break;
            }
        }

        foreach (var child in section.Children) Tally(child, ref added, ref removed, ref changed);
    }

    /// <summary>A commit's files under the chapter folders only — the rest of a
    /// repository's tree is thousands of paths nothing here will ask about.</summary>
    private Task<TreeIndex> TreeAsync(
        GitHubRepositoryRef repository,
        string commitSha,
        IReadOnlyList<string> prefixes,
        CancellationToken cancellationToken) =>
        _trees.ReadAsync($"{Key(repository)}|{commitSha}|{string.Join(",", prefixes)}", async () =>
        {
            var listing = await trees.ListTreeAsync(repository, commitSha, CancellationToken.None).ConfigureAwait(false);

            var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in listing.Entries)
            {
                if (!entry.IsDirectory && IsChapter(entry.Path, prefixes)) blobs.TryAdd(entry.Path, entry.Sha);
            }

            return new TreeIndex(blobs, listing.Truncated);
        }, cancellationToken);

    private Task<string> BlobAsync(GitHubRepositoryRef repository, string blobSha, CancellationToken cancellationToken) =>
        _blobs.ReadAsync($"{Key(repository)}|{blobSha}", async () =>
        {
            var bytes = await trees.ReadBlobAsync(repository, blobSha, CancellationToken.None).ConfigureAwait(false);

            // GetString keeps a byte-order mark as U+FEFF, which would sit in
            // front of the first heading and stop it reading as one.
            return Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        }, cancellationToken);

    /// <summary>
    /// Whether a path is one of the devbook's own chapters: Markdown under a
    /// folder the repository's devbook setting has turned on — its override, or
    /// the folder under <c>.devbook/</c> and the legacy one at the root — or under
    /// <c>.github/instructions</c> for the instructions section. Anything else is
    /// counted, not compared: this compares sections, and a code file has none.
    /// </summary>
    private static bool IsChapter(string path, IReadOnlyList<string> prefixes) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        && prefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ChapterFolders(GitHubRepositoryRef repository) =>
        [.. DevbookChapterFolders.For(repository).Select(folder => folder.Prefix)];

    private static string Key(GitHubRepositoryRef repository) => repository.FullName.ToLowerInvariant();

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private static string Age(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } when) return string.Empty;

        var elapsed = now - when;
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed < TimeSpan.FromDays(30)) return $"{(int)elapsed.TotalDays}d ago";

        return when.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>One chapter of a scope: the path it is listed under, what
    /// happened to it, which path its earlier side is read from and which blob
    /// its later side is, and a note about a rename.</summary>
    private sealed record Entry(string Path, ChangeKind Kind, string? BeforePath, string? AfterBlob, string? Note);

    private sealed record TreeIndex(IReadOnlyDictionary<string, string> Blobs, bool Truncated);

    internal sealed record Scope(IReadOnlyList<Chapter> Chapters, int OtherFiles, string BeforeLabel, string AfterLabel, string? Notice);

    /// <summary>A chapter's row in the list and its comparison. Named for the
    /// row rather than the file it stands for: nothing here touches a disk, and
    /// a member named after the IO type reads as if it did.</summary>
    internal sealed record Chapter(ChangedFile Row, ComparedSection Comparison);
}

/// <summary>
/// One loaded review: the repository and branch it is about, the comparison it
/// was drawn from, and the scopes worked out from it so far. The window that
/// loaded it holds it; the service keeps none, so one window loading another
/// branch never changes what another window is reviewing.
/// </summary>
public sealed class DevbookReviewHandle
{
    internal DevbookReviewHandle(GitHubRepositoryRef repository, string baseBranch, string headBranch, GitHubComparison comparison)
    {
        Repository = repository;
        BaseBranch = baseBranch;
        HeadBranch = headBranch;
        Comparison = comparison;
    }

    /// <summary>The branch under review.</summary>
    public string HeadBranch { get; }

    /// <summary>The branch it is compared with.</summary>
    public string BaseBranch { get; }

    internal GitHubRepositoryRef Repository { get; }

    internal GitHubComparison Comparison { get; }

    internal ConcurrentDictionary<string, DevbookBranchReview.Scope> Scopes { get; } = new(StringComparer.Ordinal);
}

/// <summary>What the review's picker shows: the scopes, then the commits.</summary>
/// <param name="Notice">A sentence about the list itself — that GitHub listed
/// only some of the commits — or null.</param>
/// <param name="Error">Why there is nothing to review, or null.</param>
/// <param name="Review">What to hand back for the scopes' files; null with an
/// error.</param>
public sealed record DevbookReviewOutline(
    IReadOnlyList<ChangeScope> Scopes,
    IReadOnlyList<ChangeCommit> Commits,
    string? Notice,
    string? Error,
    DevbookReviewHandle? Review = null)
{
    internal static DevbookReviewOutline Failed(string error) => new([], [], null, error);
}

/// <summary>The chapters one scope changed, and how many other files it changed.</summary>
/// <param name="BeforeLabel">What the earlier side is called: the default branch,
/// or the parent commit.</param>
/// <param name="AfterLabel">What the later side is called: the branch, or the
/// commit.</param>
/// <param name="Notice">What GitHub left out of its answer, so the list may be
/// short, or null.</param>
public sealed record DevbookReviewFiles(
    IReadOnlyList<ChangedFile> Files,
    int OtherFiles,
    string BeforeLabel,
    string AfterLabel,
    string? Error,
    string? Notice = null)
{
    /// <summary>The one line that stands for every file that is not a chapter,
    /// or null when there are none.</summary>
    public string? OtherFilesNote => OtherFiles switch
    {
        0 => null,
        1 => "+1 other file not shown",
        _ => $"+{OtherFiles} other files not shown"
    };

    internal static DevbookReviewFiles Failed(string error) => new([], 0, string.Empty, string.Empty, error);
}

/// <summary>One chapter compared, or why it could not be.</summary>
public sealed record DevbookReviewComparison(ComparedSection? Section, string? Error);
