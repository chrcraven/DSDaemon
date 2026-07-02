using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DSDaemon.Tests.Helpers {

    /// <summary>
    /// Writes minimal, synthetic Run8 .r8 static-data files matching the byte layouts
    /// validated in DSDaemon.Discovery.StaticImport — used so tests don't need to embed
    /// real (proprietary) Run8 game data.
    /// </summary>
    public static class StaticFileBuilder {

        public static void WriteTrackDatabase(
                string path, IEnumerable<(int Index, int[] Next, bool IsCtcSwitch)> sections) {
            using var stream = File.Create(path);
            using var w = new BinaryWriter(stream);
            var list = sections.ToList();

            w.Write(1);          // reserved
            w.Write(list.Count); // section count
            foreach (var (index, next, isCtc) in list) {
                w.Write(1); // section reserved
                w.Write(0); // node count — geometry is skipped by the parser, so no nodes needed
                w.Write(index);
                w.Write((byte)0); // switch lever position
                w.Write(next.Length);
                foreach (var n in next) w.Write(n);
                w.Write((byte)3);  // track type
                w.Write(0.0);      // retarder MPH
                w.Write((byte)0);  // is occupied
                w.Write((byte)0);  // switch stand left side
                w.Write(0);        // switch stand type
                w.Write((byte)(isCtc ? 1 : 0));
            }
        }

        public static void WriteBlockDetectorDatabase(
                string path, IEnumerable<(int Index, int[] Tracks)> detectors) {
            using var stream = File.Create(path);
            using var w = new BinaryWriter(stream);
            var list = detectors.ToList();

            w.Write(1);          // reserved
            w.Write(list.Count); // detector count
            foreach (var (index, tracks) in list) {
                w.Write(1); // reserved
                w.Write(index);
                w.Write(tracks.Length);
                foreach (var t in tracks) w.Write(t);
                w.Write(0); w.Write(0);                          // tile XZ
                w.Write(0f); w.Write(0f); w.Write(0f);            // position
            }
        }

        public static void WriteSwitchIconDatabase(
                string path, IEnumerable<(int RoutePrefix, int Index, int Version)> icons) {
            using var stream = File.Create(path);
            using var w = new BinaryWriter(stream);
            var list = icons.ToList();

            w.Write(1);          // reserved
            w.Write(list.Count); // icon count
            foreach (var (routePrefix, index, version) in list) {
                w.Write(version);
                w.Write(0); w.Write(0); w.Write(0); w.Write(0); // button rectangle
                w.Write(0f); w.Write(0f);                        // screen XY
                w.Write(routePrefix);
                w.Write(index);
                w.Write(0); // signal controller index count
                if (version == 2) w.Write("icon"); // BinaryWriter's native length-prefixed string
            }
        }
    }
}
