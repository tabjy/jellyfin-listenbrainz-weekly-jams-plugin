using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Playlists;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Tests;

public sealed class PlaylistWriterTests
{
    private readonly FakePlaylistManager _playlistManager = new();
    private readonly FakePlaylistLookup _playlistLookup = new();
    private readonly FakeUserManager _userManager = new();
    private readonly PlaylistWriter _writer;

    public PlaylistWriterTests()
    {
        _writer = new PlaylistWriter(
            _playlistManager,
            _playlistLookup,
            _userManager,
            NullLogger<PlaylistWriter>.Instance);
    }

    [Fact]
    public async Task NoExistingPlaylist_CreatesWithConfiguredOptions()
    {
        var owner = _userManager.AddUser("owner", isAdmin: true);
        var itemIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var playlistId = await _writer.WriteAsync(
            existingPlaylistId: null,
            itemIds,
            new SyncOptions("tabjy", "Weekly Jams", owner.Id, IsPublic: true),
            CancellationToken.None);

        Assert.Equal(playlistId, _playlistManager.CreatedPlaylistId);
        var request = _playlistManager.CreateRequest!;
        Assert.Equal("Weekly Jams", request.Name);
        Assert.Equal(itemIds, request.ItemIdList);
        Assert.Equal(owner.Id, request.UserId);
        Assert.True(request.Public);
    }

    [Fact]
    public async Task NoOwnerConfigured_DefaultsToFirstAdmin()
    {
        _userManager.AddUser("listener", isAdmin: false);
        var admin = _userManager.AddUser("admin", isAdmin: true);

        await _writer.WriteAsync(
            existingPlaylistId: null,
            [Guid.NewGuid()],
            new SyncOptions("tabjy", "Weekly Jams", OwningUserId: null, IsPublic: false),
            CancellationToken.None);

        Assert.Equal(admin.Id, _playlistManager.CreateRequest!.UserId);
    }

    [Fact]
    public async Task ExistingPlaylist_ReplacesContentsAndPreservesName()
    {
        var owner = _userManager.AddUser("owner", isAdmin: true);
        var playlistId = Guid.NewGuid();
        _playlistLookup.Playlists[playlistId] = new Playlist { Id = playlistId, Name = "My Renamed Jams" };
        var itemIds = new[] { Guid.NewGuid() };

        var result = await _writer.WriteAsync(
            playlistId,
            itemIds,
            new SyncOptions("tabjy", "Weekly Jams", owner.Id, IsPublic: true),
            CancellationToken.None);

        Assert.Equal(playlistId, result);
        Assert.Null(_playlistManager.CreatedPlaylistId);
        var request = _playlistManager.UpdateRequest!;
        Assert.Equal(playlistId, request.Id);
        Assert.Equal(itemIds, request.Ids);
        // D-12: Name stays null so user renames survive (verified server behavior).
        Assert.Null(request.Name);
        Assert.Equal(owner.Id, request.UserId);
    }

    [Fact]
    public async Task DeletedPlaylist_IsRecreated()
    {
        var owner = _userManager.AddUser("owner", isAdmin: true);
        var staleId = Guid.NewGuid(); // not in the lookup -> deleted (FR-19)

        await _writer.WriteAsync(
            staleId,
            [Guid.NewGuid()],
            new SyncOptions("tabjy", "Weekly Jams", owner.Id, IsPublic: true),
            CancellationToken.None);

        Assert.NotNull(_playlistManager.CreateRequest);
    }

    private sealed class FakePlaylistManager : IPlaylistManager
    {
        public PlaylistCreationRequest? CreateRequest { get; private set; }

        public PlaylistUpdateRequest? UpdateRequest { get; private set; }

        public Guid? CreatedPlaylistId { get; private set; }

        public Task<PlaylistCreationResult> CreatePlaylist(PlaylistCreationRequest request)
        {
            CreateRequest = request;
            CreatedPlaylistId = Guid.NewGuid();
            return Task.FromResult(new PlaylistCreationResult(CreatedPlaylistId.Value.ToString()));
        }

        public Task UpdatePlaylist(PlaylistUpdateRequest request)
        {
            UpdateRequest = request;
            return Task.CompletedTask;
        }

        public Playlist GetPlaylistForUser(Guid playlistId, Guid userId) => throw new NotSupportedException();

        public IEnumerable<Playlist> GetPlaylists(Guid userId) => throw new NotSupportedException();

        public Task AddUserToShares(PlaylistUserUpdateRequest request) => throw new NotSupportedException();

        public Task RemoveUserFromShares(Guid playlistId, Guid userId, PlaylistUserPermissions share) => throw new NotSupportedException();

        public Task AddItemToPlaylistAsync(Guid playlistId, IReadOnlyCollection<Guid> itemIds, Guid userId) => throw new NotSupportedException();

        public Task RemoveItemFromPlaylistAsync(string playlistId, IEnumerable<string> entryIds) => throw new NotSupportedException();

        public Folder GetPlaylistsFolder() => throw new NotSupportedException();

        public Folder GetPlaylistsFolder(Guid userId) => throw new NotSupportedException();

        public Task MoveItemAsync(string playlistId, string entryId, int newIndex, Guid callingUserId) => throw new NotSupportedException();

        public Task RemovePlaylistsAsync(Guid userId) => throw new NotSupportedException();

        public void SavePlaylistFile(Playlist item) => throw new NotSupportedException();
    }

    private sealed class FakePlaylistLookup : IPlaylistLookup
    {
        public Dictionary<Guid, Playlist> Playlists { get; } = [];

        public Playlist? GetPlaylist(Guid playlistId)
            => Playlists.TryGetValue(playlistId, out var playlist) ? playlist : null;
    }

    private sealed class FakeUserManager : IUserManager
    {
#pragma warning disable CS0067 // Fake: event never raised.
        public event EventHandler<GenericEventArgs<User>>? OnUserUpdated;
#pragma warning restore CS0067

        public List<User> Users { get; } = [];

        public User AddUser(string name, bool isAdmin)
        {
            var user = new User(name, "auth", "reset");
            if (isAdmin)
            {
                user.Permissions.Add(new Permission(PermissionKind.IsAdministrator, true));
            }

            Users.Add(user);
            return user;
        }

        public IEnumerable<User> GetUsers() => Users;

        public IEnumerable<Guid> GetUsersIds() => throw new NotSupportedException();

        public Task InitializeAsync() => throw new NotSupportedException();

        public User? GetUserById(Guid id) => throw new NotSupportedException();

        public User? GetFirstUser() => throw new NotSupportedException();

        public User? GetUserByName(string name) => throw new NotSupportedException();

        public Task RenameUser(Guid userId, string oldName, string newName) => throw new NotSupportedException();

        public Task UpdateUserAsync(User user) => throw new NotSupportedException();

        public Task<User> CreateUserAsync(string name) => throw new NotSupportedException();

        public Task DeleteUserAsync(Guid userId) => throw new NotSupportedException();

        public Task ResetPassword(Guid userId) => throw new NotSupportedException();

        public Task ChangePassword(Guid userId, string newPassword) => throw new NotSupportedException();

        public UserDto GetUserDto(User user, string? remoteEndPoint = null) => throw new NotSupportedException();

        public Task<User?> AuthenticateUser(string username, string password, string remoteEndPoint, bool isUserSession) => throw new NotSupportedException();

        public Task<ForgotPasswordResult> StartForgotPasswordProcess(string enteredUsername, bool isInNetwork) => throw new NotSupportedException();

        public Task<PinRedeemResult> RedeemPasswordResetPin(string pin) => throw new NotSupportedException();

        public Task UpdateConfigurationAsync(Guid userId, UserConfiguration config) => throw new NotSupportedException();

        public Task UpdatePolicyAsync(Guid userId, UserPolicy policy) => throw new NotSupportedException();

        public Task ClearProfileImageAsync(User user) => throw new NotSupportedException();

        public NameIdPair[] GetAuthenticationProviders() => throw new NotSupportedException();

        public NameIdPair[] GetPasswordResetProviders() => throw new NotSupportedException();
    }
}
