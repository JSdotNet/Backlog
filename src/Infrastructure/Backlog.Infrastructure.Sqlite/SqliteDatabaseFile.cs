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

    /// <summary>Whether the database at <paramref name="path"/> holds no task
    /// at all — the file the repository creates when it opens a folder that
    /// had none, before anything was written. A file that cannot be read is
    /// reported as not empty: whoever asks is deciding whether the file may be
    /// replaced, and a file this cannot see inside is one it must not vouch
    /// for.</summary>
    public static bool IsEmpty(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            connection.Open();

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'tasks') = 0 " +
                    "OR NOT EXISTS (SELECT 1 FROM tasks);";
                return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 1;
            }
            finally
            {
                connection.Close();
                SqliteConnection.ClearPool(connection);
            }
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>Removes the database at <paramref name="path"/> and the
    /// journal files SQLite keeps beside it, after letting go of every pooled
    /// connection to it — a pooled handle is an open file, and an open file
    /// does not delete on Windows.</summary>
    public static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        TryDelete(path + "-wal");
        TryDelete(path + "-shm");
        TryDelete(path + "-journal");
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
