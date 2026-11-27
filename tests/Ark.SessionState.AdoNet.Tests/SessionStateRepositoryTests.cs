using System;
using System.IO;
using Ark.SessionState.AdoNet.Dialects;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Ark.SessionState.AdoNet.Tests
{
    /// <summary>
    /// Runs the repository against an in-memory SQLite database created from sql/sqlite.sql.
    /// </summary>
    public sealed class SessionStateRepositoryTests : IDisposable
    {
        private readonly SqliteConnection _keepAlive;
        private readonly string _connectionString;
        private DateTime _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public SessionStateRepositoryTests()
        {
            _connectionString = $"Data Source=sessions-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            // An in-memory shared-cache database lives as long as one connection stays open.
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
            using (var cmd = _keepAlive.CreateCommand())
            {
                cmd.CommandText = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sqlite.sql"));
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose() => _keepAlive.Dispose();

        private SessionStateRepository Repo(string app = "/app") =>
            new SessionStateRepository(SqliteFactory.Instance, _connectionString, SqlDialects.Sqlite,
                "AspNetSessionState", app, utcNow: () => _now);

        private static readonly byte[] Data = { 1, 2, 3, 4 };

        [Fact]
        public void Get_missing_session_returns_NotFound()
        {
            Assert.Equal(SessionLookupStatus.NotFound, Repo().Get("nope", exclusive: true).Status);
        }

        [Fact]
        public void Insert_then_Get_returns_items()
        {
            var repo = Repo();
            repo.Insert("s1", Data, 20, flags: 0);

            var result = repo.Get("s1", exclusive: false);

            Assert.Equal(SessionLookupStatus.Found, result.Status);
            Assert.Equal(Data, result.Items);
            Assert.Equal(20, result.Timeout);
            Assert.Equal(0, result.Flags);
        }

        [Fact]
        public void Exclusive_get_locks_session_for_other_callers()
        {
            var repo = Repo();
            repo.Insert("s1", Data, 20, 0);

            var first = repo.Get("s1", exclusive: true);
            _now = _now.AddSeconds(3);
            var second = repo.Get("s1", exclusive: true);
            var readOnly = repo.Get("s1", exclusive: false);

            Assert.Equal(SessionLookupStatus.Found, first.Status);
            Assert.Equal(1, first.LockId);
            Assert.Equal(SessionLookupStatus.Locked, second.Status);
            Assert.Equal(first.LockId, second.LockId);
            Assert.Equal(TimeSpan.FromSeconds(3), second.LockAge);
            Assert.Equal(SessionLookupStatus.Locked, readOnly.Status);
        }

        [Fact]
        public void Release_unlocks_and_next_lock_gets_new_lock_id()
        {
            var repo = Repo();
            repo.Insert("s1", Data, 20, 0);
            var first = repo.Get("s1", true);

            Assert.True(repo.Release("s1", first.LockId));
            var second = repo.Get("s1", true);

            Assert.Equal(SessionLookupStatus.Found, second.Status);
            Assert.Equal(first.LockId + 1, second.LockId);
        }

        [Fact]
        public void UpdateAndRelease_saves_items_only_with_matching_lock()
        {
            var repo = Repo();
            repo.Insert("s1", Data, 20, 0);
            var locked = repo.Get("s1", true);

            Assert.False(repo.UpdateAndRelease("s1", locked.LockId + 99, new byte[] { 9 }, 20));
            Assert.True(repo.UpdateAndRelease("s1", locked.LockId, new byte[] { 7, 7 }, 30));

            var result = repo.Get("s1", false);
            Assert.Equal(SessionLookupStatus.Found, result.Status);
            Assert.Equal(new byte[] { 7, 7 }, result.Items);
            Assert.Equal(30, result.Timeout);
        }

        [Fact]
        public void Expired_session_is_NotFound_and_deleted()
        {
            var repo = Repo();
            repo.Insert("s1", Data, 20, 0);

            _now = _now.AddMinutes(21);

            Assert.Equal(SessionLookupStatus.NotFound, repo.Get("s1", true).Status);
            Assert.Equal(0, repo.DeleteExpired());
        }

        [Fact]
        public void ResetTimeout_slides_expiry()
        {
            var repo = Repo();
            repo.Insert("s1", Data, 20, 0);

            _now = _now.AddMinutes(15);
            Assert.True(repo.ResetTimeout("s1"));
            _now = _now.AddMinutes(15);

            Assert.Equal(SessionLookupStatus.Found, repo.Get("s1", false).Status);
        }

        [Fact]
        public void Uninitialized_item_reports_flag_once()
        {
            var repo = Repo();
            repo.Insert("s1", null, 20, SessionStateRepository.InitializeItemFlag);

            var first = repo.Get("s1", true);
            repo.Release("s1", first.LockId);
            var second = repo.Get("s1", true);

            Assert.Equal(SessionStateRepository.InitializeItemFlag, first.Flags);
            Assert.Null(first.Items);
            Assert.Equal(0, second.Flags);
        }

        [Fact]
        public void Remove_requires_matching_lock()
        {
            var repo = Repo();
            repo.Insert("s1", Data, 20, 0);
            var locked = repo.Get("s1", true);

            Assert.False(repo.Remove("s1", locked.LockId + 1));
            Assert.True(repo.Remove("s1", locked.LockId));
            Assert.Equal(SessionLookupStatus.NotFound, repo.Get("s1", false).Status);
        }

        [Fact]
        public void Applications_are_isolated()
        {
            Repo("/a").Insert("s1", Data, 20, 0);

            Assert.Equal(SessionLookupStatus.NotFound, Repo("/b").Get("s1", false).Status);
            Assert.Equal(SessionLookupStatus.Found, Repo("/a").Get("s1", false).Status);
        }

        [Fact]
        public void Insert_replaces_an_expired_row_with_the_same_id()
        {
            var repo = Repo();
            repo.Insert("s1", Data, 1, 0);
            _now = _now.AddMinutes(5);

            repo.Insert("s1", new byte[] { 5 }, 20, 0);

            Assert.Equal(new byte[] { 5 }, repo.Get("s1", false).Items);
        }

        [Fact]
        public void DeleteExpired_removes_only_expired_rows()
        {
            var repo = Repo();
            repo.Insert("short", Data, 5, 0);
            repo.Insert("long", Data, 60, 0);

            _now = _now.AddMinutes(10);

            Assert.Equal(1, repo.DeleteExpired());
            Assert.Equal(SessionLookupStatus.Found, repo.Get("long", false).Status);
        }

        [Fact]
        public void Large_items_round_trip()
        {
            var big = new byte[1024 * 1024];
            new Random(42).NextBytes(big);
            var repo = Repo();

            repo.Insert("s1", big, 20, 0);

            Assert.Equal(big, repo.Get("s1", false).Items);
        }
    }
}
