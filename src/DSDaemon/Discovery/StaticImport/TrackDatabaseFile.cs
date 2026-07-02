using System;
using System.Collections.Generic;
using System.IO;

namespace DSDaemon.Discovery.StaticImport {

    public sealed class TrackSection {
        public int Index { get; init; }
        public int[] NextIndices { get; init; } = Array.Empty<int>();
        public bool IsCtcSwitch { get; init; }
    }

    /// <summary>
    /// Parses Run8's TrackDatabase.r8 — per-route track section geometry and the
    /// section-to-section connectivity graph ("Next Section Indices").
    ///
    /// Format reverse-engineered from github.com/Puyodead1/Run8-V3-reverse-engineering
    /// (docs/TrackDatabase.md). That doc's TrackNode offset table has an error — it
    /// implies a 75-byte node, but a real TrackDatabase.r8 only parses to an exact,
    /// error-free byte count for every section across the whole file when the node is
    /// 79 bytes (found by brute-forcing node width against real file bytes; the node's
    /// "Tangent Degrees" field is a full Vector3, not the truncated field the doc's
    /// offsets imply). Node geometry itself is skipped here — only section identity and
    /// connectivity are needed to derive block adjacency.
    /// </summary>
    public static class TrackDatabaseFile {

        private const int NodeSizeBytes = 79;

        public static List<TrackSection> Load(string path) {
            using var stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);

            r.ReadInt32(); // reserved
            int sectionCount = r.ReadInt32();
            var sections = new List<TrackSection>(sectionCount);

            for (int s = 0; s < sectionCount; s++) {
                r.ReadInt32(); // section reserved
                int nodeCount = r.ReadInt32();
                r.BaseStream.Seek((long)nodeCount * NodeSizeBytes, SeekOrigin.Current);

                int index = r.ReadInt32();
                r.ReadByte(); // switch lever position
                int nextCount = r.ReadInt32();
                var next = new int[nextCount];
                for (int i = 0; i < nextCount; i++) next[i] = r.ReadInt32();
                r.ReadByte();   // track type
                r.ReadDouble(); // retarder MPH
                r.ReadByte();   // is occupied
                r.ReadByte();   // switch stand left side
                r.ReadInt32();  // switch stand type
                bool isCtcSwitch = r.ReadByte() != 0;

                sections.Add(new TrackSection { Index = index, NextIndices = next, IsCtcSwitch = isCtcSwitch });
            }
            return sections;
        }
    }
}
