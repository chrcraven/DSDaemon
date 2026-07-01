using DSDaemon.Messages;

namespace DSDaemon.Tests.Helpers {
    /// <summary>
    /// Fluent factory for TrainData. Defaults represent a normal AI train doing
    /// 30 mph in a 45 mph zone — all PTC checks should pass.
    /// </summary>
    internal static class TrainBuilder {
        public static TrainData Build(
            int    trainId         = 1,
            string initials        = "UP",
            int    locoNumber      = 1000,
            string symbol          = "TEST-01",
            float  speedMph        = 30f,
            int    speedLimitMph   = 45,
            int    blockId         = 100,
            EEngineerType engineer = EEngineerType.AI,
            string engineerName    = "Auto",
            bool   held            = false,
            bool   relinquish      = false,
            int    weightTons      = 5000,
            int    lengthFeet      = 6000,
            int    axleCount       = 200,
            float  hpPerTon        = 1.5f
        ) => new TrainData {
            TrainID               = trainId,
            RailroadInitials      = initials,
            LocoNumber            = locoNumber,
            TrainSymbol           = symbol,
            TrainSpeedMph         = speedMph,
            TrainSpeedLimitMPH    = speedLimitMph,
            BlockID               = blockId,
            EngineerType          = engineer,
            EngineerName          = engineerName,
            HoldingForDispatcher  = held,
            RelinquishWhenStopped = relinquish,
            TrainWeightTons       = weightTons,
            TrainLengthFeet       = lengthFeet,
            AxleCount             = axleCount,
            HpPerTon              = hpPerTon,
        };
    }
}
