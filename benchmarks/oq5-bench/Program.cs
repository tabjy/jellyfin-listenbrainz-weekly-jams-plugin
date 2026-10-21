// OQ-5 benchmark: per-track vs (paged) bulk provider-ID resolution.
//
// Mirrors Jellyfin 10.11's exact EF shapes (Jellyfin.Server.Implementations/Item/
// BaseItemRepository.cs ~L2448-2462) and schema (JellyfinDbModelSnapshot.cs):
//   BaseItemProvider: PK (ItemId, ProviderId), index (ProviderId, ProviderValue, ItemId)
//   per-track:  e => e.Provider.Select(f => f.ProviderId + ":" + f.ProviderValue).Any(f => sel.Contains(f))
//   bulk:       e => e.Provider.Any(f => any.Contains(f.ProviderId))
// Scale: 19,061 audio items (the dry-run library), ~4.7 provider rows per item.

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

var dbPath = "/tmp/opencode/oq5-bench/oq5.db";
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var db = new BenchContext(dbPath))
{
    db.Database.EnsureCreated();
    Console.WriteLine("seeding 19,061 items x ~4.7 provider rows...");
    var rng = new Random(42);
    const int itemCount = 19_061;
    var allMbids = new string[itemCount];
    for (var i = 0; i < itemCount; i++)
    {
        allMbids[i] = Guid.NewGuid().ToString();
    }

    // the "playlist": 50 recording MBIDs that exist in the library
    var playlist = new HashSet<string>();
    while (playlist.Count < 50)
    {
        playlist.Add(allMbids[rng.Next(itemCount)]);
    }

    for (var i = 0; i < itemCount; i += 500)
    {
        var items = new List<ItemEntity>();
        for (var j = i; j < Math.Min(i + 500, itemCount); j++)
        {
            var providers = new List<ProviderEntity>();
            // 97.4% carry MusicBrainzRecording (dry-run tagging coverage)
            if (rng.NextDouble() < 0.974)
            {
                providers.Add(new ProviderEntity { ProviderId = "MusicBrainzRecording", ProviderValue = allMbids[j] });
            }
            if (rng.NextDouble() < 0.95)
            {
                providers.Add(new ProviderEntity { ProviderId = "MusicBrainzArtist", ProviderValue = Guid.NewGuid().ToString() });
            }
            if (rng.NextDouble() < 0.95)
            {
                providers.Add(new ProviderEntity { ProviderId = "MusicBrainzAlbum", ProviderValue = Guid.NewGuid().ToString() });
            }
            if (rng.NextDouble() < 0.90)
            {
                providers.Add(new ProviderEntity { ProviderId = "MusicBrainzAlbumArtist", ProviderValue = Guid.NewGuid().ToString() });
            }
            if (rng.NextDouble() < 0.90)
            {
                providers.Add(new ProviderEntity { ProviderId = "MusicBrainzReleaseGroup", ProviderValue = Guid.NewGuid().ToString() });
            }
            items.Add(new ItemEntity { Id = Guid.NewGuid(), Name = $"Track {j}", Providers = providers });
        }
        db.Items.AddRange(items);
        db.SaveChanges();
    }
    var mappingCount = db.Providers.Count();
    Console.WriteLine($"seeded: {itemCount} items, {mappingCount} provider rows "
                      + $"({mappingCount / (double)itemCount:F1} per item)");
}

using (var db = new BenchContext(dbPath))
{
    var playlistMbids = db.Providers
        .Where(p => p.ProviderId == "MusicBrainzRecording")
        .OrderBy(_ => EF.Functions.Random())
        .Take(50)
        .Select(p => p.ProviderValue)
        .ToList();

    // ---- warmup (EF query compilation + SQLite page cache) ----------------
    _ = db.Items.Where(i => i.Providers.Any(p => p.ProviderId == "MusicBrainzRecording")).Count();
    _ = db.Items.Where(i => i.Providers.Select(p => p.ProviderId + ":" + p.ProviderValue).Any(f => f == "x")).Count();

    // ---- A) per-track, Jellyfin's includeSelected shape --------------------
    var perTrackTotal = TimeSpan.Zero;
    var resolvedItems = 0;
    foreach (var mbid in playlistMbids)
    {
        var sel = new[] { "MusicBrainzRecording:" + mbid };
        var sw = Stopwatch.StartNew();
        var items = db.Items
            .Where(i => i.Providers.Select(p => p.ProviderId + ":" + p.ProviderValue).Any(f => sel.Contains(f)))
            .ToList();
        sw.Stop();
        perTrackTotal += sw.Elapsed;
        resolvedItems += items.Count;
    }
    Console.WriteLine($"\n[A] per-track (concat shape, 50 queries): {perTrackTotal.TotalMilliseconds:F0} ms total, "
                      + $"{perTrackTotal.TotalMilliseconds / 50:F1} ms/query, {resolvedItems} items resolved");

    // ---- A2) per-track, ideal tuple shape (NOT what Jellyfin emits) ---------
    var tupleTotal = TimeSpan.Zero;
    foreach (var mbid in playlistMbids)
    {
        var sw = Stopwatch.StartNew();
        var items = db.Items
            .Where(i => i.Providers.Any(p => p.ProviderId == "MusicBrainzRecording" && p.ProviderValue == mbid))
            .ToList();
        sw.Stop();
        tupleTotal += sw.Elapsed;
    }
    Console.WriteLine($"[A2] per-track (tuple shape, 50 queries): {tupleTotal.TotalMilliseconds:F0} ms total, "
                      + $"{tupleTotal.TotalMilliseconds / 50:F1} ms/query  (curiosity: not reachable via HasAnyProviderId)");

    // ---- B) bulk, Jellyfin's includeAny shape -------------------------------
    var bulkSw = Stopwatch.StartNew();
    var bulkItems = db.Items
        .Where(i => i.Providers.Any(p => p.ProviderId == "MusicBrainzRecording"))
        .ToList();
    bulkSw.Stop();
    Console.WriteLine($"[B] bulk (any-provider shape, 1 query): {bulkSw.Elapsed.TotalMilliseconds:F0} ms, "
                      + $"{bulkItems.Count} items materialized");

    // ---- B2) paged bulk (streaming filter shape) -----------------------------
    var pagedSw = Stopwatch.StartNew();
    var kept = new List<ItemEntity>();
    var pagedMaterialized = 0;
    const int pageSize = 5_000;
    for (var offset = 0; ; offset += pageSize)
    {
        var page = db.Items
            .Include(i => i.Providers)
            .Where(i => i.Providers.Any(p => p.ProviderId == "MusicBrainzRecording"))
            .OrderBy(i => i.Id)
            .Skip(offset)
            .Take(pageSize)
            .ToList();
        if (page.Count == 0) break;
        pagedMaterialized += page.Count;
        kept.AddRange(page.Where(i => i.Providers.Any(p => p.ProviderValue != null && playlistMbids.Contains(p.ProviderValue))));
        if (page.Count < pageSize) break;
    }
    pagedSw.Stop();
    Console.WriteLine($"[B2] paged bulk (5k pages + streaming filter): {pagedSw.Elapsed.TotalMilliseconds:F0} ms, "
                      + $"{pagedMaterialized} items materialized (with provider rows), {kept.Count} candidates kept in memory");
}

Console.WriteLine("\ndone.");

public class ItemEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<ProviderEntity> Providers { get; set; } = new();
}

public class ProviderEntity
{
    public Guid ItemId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderValue { get; set; } = string.Empty;
    public ItemEntity Item { get; set; } = null!;
}

public class BenchContext(string dbPath) : DbContext
{
    public DbSet<ItemEntity> Items => Set<ItemEntity>();
    public DbSet<ProviderEntity> Providers => Set<ProviderEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={dbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var p = modelBuilder.Entity<ProviderEntity>();
        p.HasKey(x => new { x.ItemId, x.ProviderId });
        p.HasIndex(x => new { x.ProviderId, x.ProviderValue, x.ItemId });
        p.HasOne(x => x.Item).WithMany(i => i.Providers).HasForeignKey(x => x.ItemId);
    }
}
