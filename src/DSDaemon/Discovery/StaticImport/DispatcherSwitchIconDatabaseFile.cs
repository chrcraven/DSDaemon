using System.Collections.Generic;
using System.IO;

namespace DSDaemon.Discovery.StaticImport {

    public sealed class SwitchIcon {
        public int RoutePrefix { get; init; }
        public int Index { get; init; }
    }

    /// <summary>
    /// Parses Run8's DispatcherSwitchIconDatabase.r8. RoutePrefix is the numeric Route ID
    /// (matches the Route Name→ID table in the Run8-V3-reverse-engineering repo's
    /// GeneralInfo.md, e.g. 110 for BNSF_NeedlesSub) and Index is the local part of the
    /// live wire SwitchID — both confirmed directly against a real file (every entry in
    /// the Needles Sub sample carried RoutePrefix==110).
    ///
    /// The optional trailing image name (present when Version==2, the only version
    /// observed) uses .NET's native BinaryWriter/BinaryReader string encoding — a 7-bit
    /// length prefix followed by UTF-8 bytes — so BinaryReader.ReadString() decodes it
    /// directly; it is not the nibble-swapped R8String encoding used elsewhere in Run8's
    /// file formats.
    /// </summary>
    public static class DispatcherSwitchIconDatabaseFile {
        public static List<SwitchIcon> Load(string path) {
            using var stream = File.OpenRead(path);
            using var r = new BinaryReader(stream);

            r.ReadInt32(); // reserved
            int count = r.ReadInt32();
            var icons = new List<SwitchIcon>(count);

            for (int i = 0; i < count; i++) {
                int version = r.ReadInt32();
                r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); // button rectangle
                r.ReadSingle(); r.ReadSingle();                             // screen XY
                int routePrefix = r.ReadInt32();
                int index = r.ReadInt32();
                int scCount = r.ReadInt32();
                for (int s = 0; s < scCount; s++) r.ReadInt32(); // signal controller indices
                if (version == 2) r.ReadString();

                icons.Add(new SwitchIcon { RoutePrefix = routePrefix, Index = index });
            }
            return icons;
        }
    }
}
