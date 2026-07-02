using System;
using System.IO;
using System.Linq;
using DSDaemon.Discovery;
using DSDaemon.Discovery.StaticImport;
using DSDaemon.Tests.Helpers;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// Parsers and importer for static Run8 game-file route data (TrackDatabase.r8,
    /// BlockDetectorDatabase.r8, DispatcherSwitchIconDatabase.r8), exercised against
    /// synthetic fixtures built by StaticFileBuilder rather than real game data.
    /// </summary>
    public class StaticRouteMapImportTests : IDisposable {
        private readonly string _root;

        public StaticRouteMapImportTests() {
            _root = Path.Combine(Path.GetTempPath(), "dsdaemon-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(_root);
        }

        public void Dispose() {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private static void Log(string message, ConsoleColor color) { /* swallow in tests */ }

        // ── Individual parsers ──────────────────────────────────────────────────

        [Fact]
        public void TrackDatabaseFile_ParsesSectionsAndConnectivity() {
            var path = Path.Combine(_root, "TrackDatabase.r8");
            StaticFileBuilder.WriteTrackDatabase(path, new[] {
                (Index: 0, Next: new[] { 1 },    IsCtcSwitch: false),
                (Index: 1, Next: new[] { 0, 2 },  IsCtcSwitch: true),
                (Index: 2, Next: new[] { 1 },    IsCtcSwitch: false),
            });

            var sections = TrackDatabaseFile.Load(path);

            Assert.Equal(3, sections.Count);
            Assert.Equal(new[] { 1 }, sections[0].NextIndices);
            Assert.Equal(new[] { 0, 2 }, sections[1].NextIndices);
            Assert.True(sections[1].IsCtcSwitch);
            Assert.False(sections[0].IsCtcSwitch);
        }

        [Fact]
        public void BlockDetectorDatabaseFile_ParsesIndexAndTracks() {
            var path = Path.Combine(_root, "BlockDetectorDatabase.r8");
            StaticFileBuilder.WriteBlockDetectorDatabase(path, new[] {
                (Index: 19, Tracks: new[] { 100, 101, 102 }),
                (Index: 20, Tracks: new[] { 200 }),
            });

            var detectors = BlockDetectorDatabaseFile.Load(path);

            Assert.Equal(2, detectors.Count);
            Assert.Equal(19, detectors[0].Index);
            Assert.Equal(new[] { 100, 101, 102 }, detectors[0].TrackIndices);
            Assert.Equal(20, detectors[1].Index);
        }

        [Fact]
        public void DispatcherSwitchIconDatabaseFile_ParsesRoutePrefixAndIndex_Version2() {
            var path = Path.Combine(_root, "DispatcherSwitchIconDatabase.r8");
            StaticFileBuilder.WriteSwitchIconDatabase(path, new[] {
                (RoutePrefix: 110, Index: 0, Version: 2),
                (RoutePrefix: 110, Index: 1, Version: 2),
            });

            var icons = DispatcherSwitchIconDatabaseFile.Load(path);

            Assert.Equal(2, icons.Count);
            Assert.All(icons, i => Assert.Equal(110, i.RoutePrefix));
            Assert.Equal(new[] { 0, 1 }, icons.Select(i => i.Index));
        }

        [Fact]
        public void DispatcherSwitchIconDatabaseFile_Version1_HasNoTrailingString() {
            // Version 1 icons carry no image-name field; the parser must not try to
            // read one, or it will misparse every subsequent icon in the file.
            var path = Path.Combine(_root, "DispatcherSwitchIconDatabase.r8");
            StaticFileBuilder.WriteSwitchIconDatabase(path, new[] {
                (RoutePrefix: 110, Index: 5, Version: 1),
                (RoutePrefix: 110, Index: 6, Version: 1),
            });

            var icons = DispatcherSwitchIconDatabaseFile.Load(path);

            Assert.Equal(2, icons.Count);
            Assert.Equal(5, icons[0].Index);
            Assert.Equal(6, icons[1].Index);
        }

        // ── Importer: adjacency derivation ──────────────────────────────────────

        [Fact]
        public void ImportAll_DerivesBlockAdjacency_FromCrossBlockSectionConnectivity() {
            var routeDir = Path.Combine(_root, "BNSF_NeedlesSub");
            Directory.CreateDirectory(routeDir);

            // Two blocks (10 and 20), each made of two track sections; sections 1 and 2
            // straddle the block boundary, so blocks 10 and 20 should come out adjacent.
            StaticFileBuilder.WriteTrackDatabase(Path.Combine(routeDir, "TrackDatabase.r8"), new[] {
                (Index: 0, Next: new[] { 1 },       IsCtcSwitch: false),
                (Index: 1, Next: new[] { 0, 2 },    IsCtcSwitch: false), // block 10 → block 20 boundary
                (Index: 2, Next: new[] { 1, 3 },    IsCtcSwitch: false),
                (Index: 3, Next: new[] { 2 },       IsCtcSwitch: false),
            });
            StaticFileBuilder.WriteBlockDetectorDatabase(Path.Combine(routeDir, "BlockDetectorDatabase.r8"), new[] {
                (Index: 10, Tracks: new[] { 0, 1 }),
                (Index: 20, Tracks: new[] { 2, 3 }),
            });
            StaticFileBuilder.WriteSwitchIconDatabase(Path.Combine(routeDir, "DispatcherSwitchIconDatabase.r8"),
                Array.Empty<(int, int, int)>());

            var map = new RouteMap();
            var result = StaticRouteMapImporter.ImportAll(map, _root, Log);

            Assert.Equal(1, result.RoutesImported);
            Assert.Equal(1, result.EdgesRecorded);
            // Composite BlockID = route*1000 + local block, matching RouteDiscoveryEngine's
            // observed live TrainData.BlockID encoding.
            Assert.Equal(1, map.GetAdjacencyConfidence(110, 110010, 110020));
            Assert.Equal(1, map.GetAdjacencyConfidence(110, 110020, 110010));
        }

        [Fact]
        public void ImportAll_SkipsRoute_WhenRouteIdCannotBeResolved() {
            var routeDir = Path.Combine(_root, "SomeUnknownRoute");
            Directory.CreateDirectory(routeDir);
            StaticFileBuilder.WriteTrackDatabase(Path.Combine(routeDir, "TrackDatabase.r8"), new[] {
                (Index: 0, Next: Array.Empty<int>(), IsCtcSwitch: false),
            });
            StaticFileBuilder.WriteBlockDetectorDatabase(Path.Combine(routeDir, "BlockDetectorDatabase.r8"), new[] {
                (Index: 1, Tracks: new[] { 0 }),
            });
            // No switch-icon file, and the folder name isn't in RouteNameLookup — the
            // importer must skip this route rather than guess a Route ID for it.

            var map = new RouteMap();
            var result = StaticRouteMapImporter.ImportAll(map, _root, Log);

            Assert.Equal(0, result.RoutesImported);
            Assert.Empty(map.Routes);
        }

        [Fact]
        public void ImportAll_FallsBackToFolderName_WhenSwitchFileMissing() {
            // BNSF_NeedlesSub is in RouteNameLookup (Route ID 110); no switch-icon file
            // present at all, so resolution must fall back to the folder name.
            var routeDir = Path.Combine(_root, "BNSF_NeedlesSub");
            Directory.CreateDirectory(routeDir);
            StaticFileBuilder.WriteTrackDatabase(Path.Combine(routeDir, "TrackDatabase.r8"), new[] {
                (Index: 0, Next: new[] { 1 }, IsCtcSwitch: false),
                (Index: 1, Next: new[] { 0 }, IsCtcSwitch: false),
            });
            StaticFileBuilder.WriteBlockDetectorDatabase(Path.Combine(routeDir, "BlockDetectorDatabase.r8"), new[] {
                (Index: 1, Tracks: new[] { 0 }),
                (Index: 2, Tracks: new[] { 1 }),
            });

            var map = new RouteMap();
            var result = StaticRouteMapImporter.ImportAll(map, _root, Log);

            Assert.Equal(1, result.RoutesImported);
            Assert.True(map.Routes.ContainsKey(110));
        }

        [Fact]
        public void ImportAll_SkipsDirectory_MissingRequiredFiles() {
            // A directory with only one of the two required files isn't a route folder.
            var notARoute = Path.Combine(_root, "NotARoute");
            Directory.CreateDirectory(notARoute);
            StaticFileBuilder.WriteTrackDatabase(Path.Combine(notARoute, "TrackDatabase.r8"),
                Array.Empty<(int, int[], bool)>());

            var map = new RouteMap();
            var result = StaticRouteMapImporter.ImportAll(map, _root, Log);

            Assert.Equal(0, result.RoutesImported);
        }
    }
}
