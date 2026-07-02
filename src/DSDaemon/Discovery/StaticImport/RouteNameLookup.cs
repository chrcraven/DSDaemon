using System.Collections.Generic;

namespace DSDaemon.Discovery.StaticImport {

    /// <summary>
    /// Route folder name → numeric Route ID, transcribed from the Route table in
    /// github.com/Puyodead1/Run8-V3-reverse-engineering's GeneralInfo.md. Used only as a
    /// fallback when a route's own DispatcherSwitchIconDatabase.r8 is missing or empty
    /// (it has no switches to read a RoutePrefix from) — RoutePrefix from that file is
    /// always preferred since it comes from the install itself rather than this
    /// hardcoded, potentially-stale table.
    /// </summary>
    public static class RouteNameLookup {
        public static readonly IReadOnlyDictionary<string, int> RouteIdsByName =
            new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase) {
                ["BNSF_MojaveSub"]          = 100,
                ["UP_FresnoSub_South"]      = 101,
                ["BNSF_NeedlesSub"]         = 110,
                ["BNSF_CajonSub"]           = 120,
                ["BNSF_SeligmanSub"]        = 130,
                ["CSX_ALine"]               = 140,
                ["BarstowYermo"]            = 150,
                ["CSX_SelkirkTerminal"]     = 170,
                ["BNSF_SanBernardinoSub"]   = 200,
                ["CSX_Waycross"]            = 210,
                ["CSX_Fitzgerald"]          = 230,
                ["CSX_MohawkSub"]           = 240,
                ["BNSF_BakersfieldSub"]     = 250,
                ["SP-UP_RosevilleSub"]      = 260,
                ["NS_AGS_Phase01"]          = 280,
                ["NS_PittsburghLine_East"]  = 290,
                ["NS_South_Fork_Secondary"] = 291,
                ["ArvinOakCreekBranches"]   = 310,
                ["TronaRailway"]            = 320,
                ["BNSF_UP_FresnoModesto"]   = 340,
                ["CSX_Savannah"]            = 380,
                ["CSX_PortOfSavannah"]      = 381,
                ["CSX_Baldwin"]             = 390,
            };
    }
}
