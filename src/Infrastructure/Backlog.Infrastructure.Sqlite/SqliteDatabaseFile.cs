using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite;

/// <summary>
/// Copies the backlog database from one folder to another while the app may
/// still be using it.
/// <para>
/// Not <see cref="File.Copy(string, string)"/>, and deliberately: the database
/// runs in WAL mode, so the newest writes can sit in <c>backlog.db-wal</c>
/// rather than in the file itself, and a copy of the file alone would arrive
/// missing them. SQLite's own backup API reads through the connection instead,
/// so what lands in the destination is the database as it currently reads —
/// journal included — and the pooled handles other repositories keep on the
/// source do not get in the way.
/// </para>
/// </summary>
public static class SqliteDatabaseFile
{
    /// <summary>Copies the database at <paramref name="sourcePath"/> to a new
    /// file at <paramref name="destinationPath"/>. The destination must not
    /// exist yet: whether an existing backlog may be replaced is the caller's
    /// decision, and this refuses to make it by accident.</summary>
    public static void CopyTo(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            throw new IOException($"A database already exists at {destinationPath}.");
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        source.Open();
        destination.Open();

        try
        {
            source.BackupDatabase(destination);
        }
        catch
        {
            // A half-written destination would read as an empty backlog on
            // the next open, which is the one outcome worse than the failure
            // itself. Take it back out, then let the caller report the error.
            destination.Close();
            SqliteConnection.ClearPool(destination);
            TryDelete(destinationPath);
            throw;
        }
        finally
        {
            // Both connections were opened here for this one copy and belong to
            // nobody else, so they leave the pool with it: a pooled handle on
            // the destination would keep the file open under whatever the
            // caller does next with it, and one on the source is one more
            // reason the old folder cannot be deleted.
            destination.Close();
            source.Close();
            SqliteConnection.ClearPool(destination);
            SqliteConnection.ClearPool(source);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort only; the caller is about to report the real failure.
        }
    }
}
