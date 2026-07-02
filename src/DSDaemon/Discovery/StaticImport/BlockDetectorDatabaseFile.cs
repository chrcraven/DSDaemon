using System;
using System.Collections.Generic;
using System.IO;

namespace DSDaemon.Discovery.StaticImport {

    public sealed class BlockDetector {
        public int Index { get; init; }
        public int[] TrackIndices { get; init; } = Array.Empty<int>();
    }

    /// <summary>
    /// Parses Run8's BlockDetectorDatabase.r8 — the bridge between TrackDatabase.r8's
    /// section-geometry ID space and the live dispatcher BlockID space.
    ///
    /// BlockDetector.Index is the local part of the live BlockID reported over WCF
    /// (TrainData.BlockID = route*1000 + Index; see CLAUDE.md's note on the composite
    /// encoding). TrackIndices are TrackDatabase.r8 Section Index values belonging to
    /// that block. Verified against real files: every TrackIndices entry resolved to a
    /// real TrackDatabase.r8 section (100% of 3,796 refs in the Needles Sub sample).
    /// </summary>
    public static class BlockDetectorDatabaseFile {
        public static List<BlockDetector> Load(string path) {
            using var stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);

            r.ReadInt32(); // reserved
            int count = r.ReadInt32();
            var detectors = new List<BlockDetector>(count);

            for (int i = 0; i < count; i++) {
                r.ReadInt32(); // reserved
                int index = r.ReadInt32();
                int trackCount = r.ReadInt32();
                var tracks = new int[trackCount];
                for (int t = 0; t < trackCount; t++) tracks[t] = r.ReadInt32();
                r.ReadInt32(); r.ReadInt32();                    // tile XZ
                r.ReadSingle(); r.ReadSingle(); r.ReadSingle();   // position

                detectors.Add(new BlockDetector { Index = index, TrackIndices = tracks });
            }
            return detectors;
        }
    }
}
