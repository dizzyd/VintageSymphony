using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;
using VS = VintageSymphony.VintageSymphony;

namespace VintageSymphony.Tests
{
    /// <summary>
    /// Downloading a music source, over a real socket, without leaving the machine.
    ///
    /// The pack is served from a listener inside this process, so the harness keeps the
    /// property that matters: boot.sh points the proxy variables at a closed port so the
    /// client can never reach the auth server and put the player's real session at risk.
    /// Only loopback is opted back in, and only for this client - see UseProxy below.
    /// </summary>
    public class MusicDownloadTests
    {
        const string SourceId = "vsdownloadtest";
        const int Port = 18099;

        static Music.MusicSources Sources => VS.MusicSources;

        [VsTest(TimeoutMs = 180000), RequiresClient]
        public async Task ASourceIsFetchedUnpackedAndRecorded()
        {
            var served = Path.Combine(Path.GetTempPath(), "vs-download-test");
            Directory.CreateDirectory(served);
            var archive = Path.Combine(served, "pack.zip");
            BuildFixtureArchive(archive);
            AssertArchiveTriesToEscape(archive);

            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Start();
            using var serving = new CancellationTokenSource();
            var server = ServeAsync(listener, archive, serving.Token);

            // The client's proxy is aimed at a dead port to keep it off the network; this
            // one opts out, which reaches loopback and nothing else.
            using var direct = new HttpClient(new HttpClientHandler { UseProxy = false });

            var source = new Music.MusicSource
            {
                Id = SourceId,
                Name = "download test",
                Enabled = true,
                Url = $"http://127.0.0.1:{Port}/pack.zip"
            };

            try
            {
                Sources.Sources.Add(source);
                var installer = new Music.MusicSourceInstaller(Sources, Capi.Logger, direct);

                var release = await installer.CheckAsync(source);
                Assert.NotNull(release, "the source offered something");
                Assert.Greater(release.SizeBytes, 0L, "size came back from the HEAD request");

                var progress = new System.Collections.Generic.List<float>();
                await installer.InstallAsync(source, release, progress.Add, CancellationToken.None);

                // The install runs off the game thread; come back before touching anything else.
                await OnClient();

                var musicPath = Sources.MusicPathOf(source);
                Assert.True(File.Exists(Path.Combine(musicPath, "fixture.ogg")), "the track was unpacked");
                Assert.True(File.Exists(Path.Combine(musicPath, Music.TrackManifest.FileName)),
                    "the manifest came with it");
                // This fixture publishes no version, so whether it is installed is a
                // question for the disk rather than for the recorded version.
                Assert.True(Sources.HasMusicOnDisk(source), "the source counts as installed");
                Assert.Greater(progress.Count, 0, "progress was reported");

                // An entry that tries to climb out is dropped, not written somewhere else
                // and not quietly kept under a tidied-up name.
                Assert.False(File.Exists(Path.Combine(Sources.DirectoryOf(source), "escaped.ogg")),
                    "../escaped.ogg was not written outside the music folder");
                Assert.False(File.Exists(Path.Combine(musicPath, "escaped.ogg")),
                    "../escaped.ogg was not kept as a track either");
                Assert.Equal(2, Directory.GetFiles(musicPath).Length,
                    "only the two real entries were unpacked");

                Log($"unpacked {Directory.GetFiles(musicPath).Length} files, " +
                    $"{release.SizeBytes / 1024} KB, {progress.Count} progress reports");
            }
            finally
            {
                serving.Cancel();
                listener.Stop();
                Sources.Sources.RemoveAll(s => s.Id == SourceId);
                Sources.Save();
                Delete(Sources.DirectoryOf(source));
                Delete(served);
            }
        }


        /// <summary>
        /// A pack in the game's own format, installed while the game is running, plays
        /// without a restart.
        ///
        /// Asset origins are folded into the manager once during startup, so a folder that
        /// appears later is invisible until it is added to Origins and the music category
        /// is reloaded - and the game parsed every musicconfig at startup, so this one has
        /// to be read here instead. Both of those are what the dialog does after a
        /// successful install.
        /// </summary>
        [VsTest(TimeoutMs = 180000), RequiresClient]
        public async Task AGameFormatPackInstalledNowPlaysWithoutRestarting()
        {
            const string id = "vshotloadtest";
            var served = Path.Combine(Path.GetTempPath(), "vs-hotload-test");
            Directory.CreateDirectory(served);
            var archive = Path.Combine(served, "pack.zip");

            BuildFixtureArchive(archive, "musicconfig.json",
                "{ \"tracks\": [ { \"$type\": \"VintageSymphony.Engine.MusicTrack, VintageSymphony\", " +
                "\"file\": \"fixture\", \"situation\": \"fight\", \"title\": \"Hot Loaded\", " +
                "\"minSunLight\": 0 } ] }");

            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port + 1}/");
            listener.Start();
            using var serving = new CancellationTokenSource();
            var server = ServeAsync(listener, archive, serving.Token);
            using var direct = new HttpClient(new HttpClientHandler { UseProxy = false });

            var source = new Music.MusicSource
            {
                Id = id, Name = "hot load test", Enabled = true,
                Url = $"http://127.0.0.1:{Port + 1}/pack.zip"
            };

            try
            {
                Sources.Sources.Add(source);
                var installer = new Music.MusicSourceInstaller(Sources, Capi.Logger, direct);
                var release = await installer.CheckAsync(source);
                await installer.InstallAsync(source, release, _ => { }, CancellationToken.None);
                await OnClient();

                Sources.RegisterOriginNow(Capi, source);
                VS.MusicEngine.ReloadTracks();

                await Until(() => PoolHas(id), 300, "the new pack joins the pool without a restart");

                var track = Pool().First(t => t.Location?.Domain == id);
                Assert.Equal("Hot Loaded", track.Title, "the title came from the pack's own config");
                Assert.True(track.TrackSituations.Contains(Situations.Situation.Fight), "and its situation");
                Log("pool now holds " + Pool().Count + " tracks including " + track.Location);
            }
            finally
            {
                serving.Cancel();
                listener.Stop();
                Sources.Sources.RemoveAll(s => s.Id == id);
                Sources.Save();
                Delete(Sources.DirectoryOf(source));
                Delete(served);
                VS.MusicEngine.ReloadTracks();
            }
        }

        static System.Collections.Generic.List<Engine.MusicTrack> Pool() =>
            ((Engine.MusicCurator)typeof(Engine.MusicEngine)
                .GetField("musicCurator", System.Reflection.BindingFlags.Instance |
                                          System.Reflection.BindingFlags.NonPublic)
                .GetValue(VS.MusicEngine)).Tracks;

        static bool PoolHas(string domain) => Pool().Any(t => t.Location?.Domain == domain);


        /// <summary>
        /// Pressing Update on something already installed replaces it. The origin is
        /// already registered by then, so the reload has to happen anyway - without it the
        /// files change on disk while the asset manager keeps serving what it cached, and
        /// the update looks like it did nothing.
        /// </summary>
        [VsTest(TimeoutMs = 180000), RequiresClient]
        public async Task UpdatingAnInstalledSourceReplacesWhatIsPlaying()
        {
            const string id = "vsupdatetest";
            var served = Path.Combine(Path.GetTempPath(), "vs-update-test");
            Directory.CreateDirectory(served);
            var archive = Path.Combine(served, "pack.zip");

            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port + 2}/");
            listener.Start();
            using var serving = new CancellationTokenSource();
            var server = ServeAsync(listener, archive, serving.Token);
            using var direct = new HttpClient(new HttpClientHandler { UseProxy = false });

            var source = new Music.MusicSource
            {
                Id = id, Name = "update test", Enabled = true,
                Url = $"http://127.0.0.1:{Port + 2}/pack.zip"
            };

            try
            {
                Sources.Sources.Add(source);
                var installer = new Music.MusicSourceInstaller(Sources, Capi.Logger, direct);

                await InstallVersion(installer, source, archive, "Version One");
                await Until(() => TitleOf(id) == "Version One", 300, "the first version plays");

                await InstallVersion(installer, source, archive, "Version Two");
                await Until(() => TitleOf(id) == "Version Two", 300,
                    "the update replaced it without a restart");

                Log("pool went from Version One to " + TitleOf(id));
            }
            finally
            {
                serving.Cancel();
                listener.Stop();
                Sources.Sources.RemoveAll(s => s.Id == id);
                Sources.Save();
                Delete(Sources.DirectoryOf(source));
                Delete(served);
                VS.MusicEngine.ReloadTracks();
            }
        }

        static async Task InstallVersion(Music.MusicSourceInstaller installer, Music.MusicSource source,
            string archive, string title)
        {
            BuildFixtureArchive(archive, "musicconfig.json",
                "{ \"tracks\": [ { \"$type\": \"VintageSymphony.Engine.MusicTrack, VintageSymphony\", " +
                $"\"file\": \"fixture\", \"situation\": \"fight\", \"title\": \"{title}\", " +
                "\"minSunLight\": 0 } ] }");

            var release = await installer.CheckAsync(source);
            Assert.NotNull(release, "the source offered " + title);
            await installer.InstallAsync(source, release, _ => { }, CancellationToken.None);
            await OnClient();

            Sources.RegisterOriginNow(Capi, source);
            VS.MusicEngine.ReloadTracks();
        }

        static string TitleOf(string domain) =>
            Pool().FirstOrDefault(t => t.Location?.Domain == domain)?.Title;

        /// <summary>
        /// Removing a source takes its music with it, so it asks first - one press arms
        /// the button, a second one goes through with it.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task RemovingASourceTakesTwoPressesAndDeletesItsFiles()
        {
            const string id = "vsremovetest";
            await OnClient();

            var source = new Music.MusicSource { Id = id, Name = "remove test", Enabled = true };
            var musicPath = Sources.MusicPathOf(source);
            Directory.CreateDirectory(musicPath);
            File.WriteAllText(Path.Combine(musicPath, "placeholder.txt"), "music would live here");
            Sources.Sources.Add(source);

            var dialog = VS.ConfigurationDialog;
            dialog.TryOpen();
            await Frames.Wait(5);

            try
            {
                Press(dialog, "vscfg_rm_" + id);
                Assert.Equal("Sure?", dialog.SingleComposer.GetButton("vscfg_rm_" + id).Text,
                    "the first press only arms it");
                Assert.True(Sources.Sources.Any(s => s.Id == id), "and removes nothing");

                Press(dialog, "vscfg_rm_" + id);
                Assert.False(Sources.Sources.Any(s => s.Id == id), "the second press removes it");
                Assert.False(Directory.Exists(Sources.DirectoryOf(source)), "and deletes its files");
            }
            finally
            {
                dialog.TryClose();
                Sources.Sources.RemoveAll(s => s.Id == id);
                Sources.Save();
                Delete(Sources.DirectoryOf(source));
            }
        }

        static void Press(GuiDialog dialog, string key)
        {
            var button = dialog.SingleComposer.GetButton(key);
            Assert.NotNull(button, "button " + key);
            var at = new MouseEvent(
                (int)(button.Bounds.absX + 10), (int)(button.Bounds.absY + 10),
                Vintagestory.API.Common.EnumMouseButton.Left, 0);
            button.OnMouseDownOnElement(Capi, at);
            button.OnMouseUpOnElement(Capi, at);
        }

        /// <summary>
        /// A pack shaped like a mod, plus an entry that tries to escape the folder it is
        /// unpacked into. Built with SharpZipLib because that is what the game has loaded -
        /// System.IO.Compression is only pulled in when the installer itself runs, so the
        /// in-game compiler cannot reference it here.
        /// </summary>
        static void BuildFixtureArchive(string path, string manifestName = null, string manifest = null)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var zip = new ZipOutputStream(stream);

            Write(zip, "assets/bobs/music/fixture.ogg", new string('o', 4096));
            Write(zip, "assets/bobs/music/" + (manifestName ?? Music.TrackManifest.FileName),
                manifest ?? "{ \"tracks\": [ { \"file\": \"fixture.ogg\", \"situations\": [\"fight\"] } ] }");
            Write(zip, "../escaped.ogg", "should never be written");
        }

        static void Write(ZipOutputStream zip, string entryName, string content)
        {
            zip.PutNextEntry(new ZipEntry(entryName));
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            zip.Write(bytes, 0, bytes.Length);
            zip.CloseEntry();
        }

        /// <summary>
        /// The escaping entry only tests anything if it survived being written, so check
        /// the archive says what this test thinks it says.
        /// </summary>
        static void AssertArchiveTriesToEscape(string path)
        {
            using var zip = new ZipFile(path);
            var names = zip.Cast<ZipEntry>().Select(e => e.Name).ToList();
            Assert.True(names.Any(n => n.Contains("..")),
                "the fixture really does contain an escaping entry: " + string.Join(", ", names));
        }

        static async Task ServeAsync(HttpListener listener, string file, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    return; // listener stopped
                }

                var bytes = await File.ReadAllBytesAsync(file, cancellation);
                context.Response.ContentLength64 = bytes.Length;

                // A HEAD is the size probe; only a GET carries the pack.
                if (context.Request.HttpMethod == "GET")
                {
                    await context.Response.OutputStream.WriteAsync(bytes, cancellation);
                }

                context.Response.Close();
            }
        }

        static void Delete(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
