using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DSDaemon.Discovery.StaticImport {

    public readonly struct StaticImportResult {
        public int RoutesImported { get; init; }
        public int EdgesRecorded { get; init; }
    }

    /// <summary>
    /// Builds RouteMap block adjacency directly from Run8's installed game files, as a
    /// static baseline alongside (not instead of) empirical scouting — see CLAUDE.md's
    /// "Static route-map import" section for the file formats and the correlation this
    /// relies on.
    ///
    /// Expects routesRootDir to contain one subdirectory per installed route (as Run8
    /// itself lays them out), each holding that route's own TrackDatabase.r8 /
    /// BlockDetectorDatabase.r8 / DispatcherSwitchIconDatabase.r8. A directory missing
    /// either of the first two files is treated as not being a route folder and skipped.
    /// </summary>
    public static class StaticRouteMapImporter {

        private const string TrackDatabaseFileName = "TrackDatabase.r8";
        private const string BlockDetectorFileName = "BlockDetectorDatabase.r8";
        private const string SwitchIconFileName    = "DispatcherSwitchIconDatabase.r8";

        public static StaticImportResult ImportAll(RouteMap map, string routesRootDir, Action<string, ConsoleColor> log) {
            if (!Directory.Exists(routesRootDir)) {
                log($"[STATIC] Routes directory not found: {routesRootDir}", ConsoleColor.Red);
                return new StaticImportResult();
            }

            int routesImported = 0;
            int edgesRecorded  = 0;

            foreach (var dir in Directory.EnumerateDirectories(routesRootDir)
                                          .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
                var trackPath = Path.Combine(dir, TrackDatabaseFileName);
                var blockPath = Path.Combine(dir, BlockDetectorFileName);
                if (!File.Exists(trackPath) || !File.Exists(blockPath)) continue;

                var folderName = Path.GetFileName(dir);
                int? routeId = ResolveRouteId(dir, folderName, log);
                if (routeId == null) {
                    log($"[STATIC] {folderName}: couldn't determine Route ID (no switches to read a " +
                        $"RoutePrefix from, and folder name isn't in the known route table) — skipped",
                        ConsoleColor.DarkYellow);
                    continue;
                }

                var (edges, sectionsParsed, ctcSwitchCount) = DeriveEdges(trackPath, blockPath);
                foreach (var (a, b) in edges) {
                    map.RecordAdjacency(routeId.Value, routeId.Value * 1000 + a, routeId.Value * 1000 + b);
                    edgesRecorded++;
                }

                routesImported++;
                log($"[STATIC] {folderName} (route {routeId}): {sectionsParsed} sections, " +
                    $"{ctcSwitchCount} CTC switches, {edges.Count} block edges derived",
                    ConsoleColor.Cyan);
            }

            return new StaticImportResult { RoutesImported = routesImported, EdgesRecorded = edgesRecorded };
        }

        /// <summary>
        /// Prefers the RoutePrefix baked into the route's own switch-icon file (comes
        /// from the install itself); falls back to matching the folder name against the
        /// hardcoded GeneralInfo.md route table when that file is missing/empty (a route
        /// with no CTC switches) or, defensively, inconsistent.
        /// </summary>
        private static int? ResolveRouteId(string dir, string folderName, Action<string, ConsoleColor> log) {
            var switchPath = Path.Combine(dir, SwitchIconFileName);
            if (File.Exists(switchPath)) {
                var prefixes = DispatcherSwitchIconDatabaseFile.Load(switchPath)
                                                                .Select(i => i.RoutePrefix)
                                                                .Distinct()
                                                                .ToList();
                if (prefixes.Count == 1) return prefixes[0];
                if (prefixes.Count > 1)
                    log($"[STATIC] {folderName}: {SwitchIconFileName} has inconsistent RoutePrefix values " +
                        $"({string.Join(",", prefixes)}) — not trusting it, falling back to folder name",
                        ConsoleColor.DarkYellow);
            }
            return RouteNameLookup.RouteIdsByName.TryGetValue(folderName, out int id) ? id : null;
        }

        /// <summary>
        /// Groups TrackDatabase.r8 sections by their owning BlockDetectorDatabase.r8
        /// block, then derives every distinct cross-block edge from the sections' Next
        /// Section Indices connectivity. Two blocks are adjacent if any section in one
        /// connects directly to any section in the other; sections not covered by any
        /// block detector (e.g. ones belonging to a neighbouring route not present under
        /// routesRootDir) simply can't contribute an edge and are skipped.
        /// </summary>
        private static (HashSet<(int, int)> edges, int sectionsParsed, int ctcSwitchCount) DeriveEdges(
                string trackPath, string blockPath) {
            var sections  = TrackDatabaseFile.Load(trackPath);
            var detectors = BlockDetectorDatabaseFile.Load(blockPath);

            var blockBySection = new Dictionary<int, int>();
            foreach (var d in detectors)
                foreach (var trackIndex in d.TrackIndices)
                    blockBySection[trackIndex] = d.Index;

            var edges = new HashSet<(int, int)>();
            foreach (var section in sections) {
                if (!blockBySection.TryGetValue(section.Index, out int blockA)) continue;
                foreach (var nextIndex in section.NextIndices) {
                    if (!blockBySection.TryGetValue(nextIndex, out int blockB)) continue;
                    if (blockA == blockB) continue;
                    edges.Add(blockA < blockB ? (blockA, blockB) : (blockB, blockA));
                }
            }

            int ctcSwitchCount = sections.Count(s => s.IsCtcSwitch);
            return (edges, sections.Count, ctcSwitchCount);
        }
    }
}
