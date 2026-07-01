using System.Runtime.Serialization;

namespace DSDaemon.Messages {
    /// <summary>
    /// DataMember names use the k__BackingField convention because the original
    /// Run8/iecc8 TrainData was compiled from auto-properties, so the WCF binary
    /// encoding uses those backing-field names on the wire.
    /// </summary>
    [DataContract(Namespace = "http://schemas.datacontract.org/2004/07/DispatcherComms.MessagesFromRun8")]
    public struct TrainData {
        [DataMember(Name = "<AxleCount>k__BackingField")]         public int AxleCount;
        [DataMember(Name = "<BlockID>k__BackingField")]           public int BlockID;
        [DataMember(Name = "<EngineerName>k__BackingField")]      public string EngineerName;
        [DataMember(Name = "<EngineerType>k__BackingField")]      public EEngineerType EngineerType;
        [DataMember(Name = "<HoldingForDispatcher>k__BackingField")] public bool HoldingForDispatcher;
        [DataMember(Name = "<HpPerTon>k__BackingField")]          public float HpPerTon;
        [DataMember(Name = "<LocoNumber>k__BackingField")]        public int LocoNumber;
        [DataMember(Name = "<RailroadInitials>k__BackingField")]  public string RailroadInitials;
        [DataMember(Name = "<RelinquishWhenStopped>k__BackingField")] public bool RelinquishWhenStopped;
        [DataMember(Name = "<TrainID>k__BackingField")]           public int TrainID;
        [DataMember(Name = "<TrainLengthFeet>k__BackingField")]   public int TrainLengthFeet;
        [DataMember(Name = "<TrainSpeedLimitMPH>k__BackingField")] public int TrainSpeedLimitMPH;
        [DataMember(Name = "<TrainSpeedMph>k__BackingField")]     public float TrainSpeedMph;
        [DataMember(Name = "<TrainSymbol>k__BackingField")]       public string TrainSymbol;
        [DataMember(Name = "<TrainWeightTons>k__BackingField")]   public int TrainWeightTons;

        public override string ToString() =>
            $"Train#{TrainID} {RailroadInitials}{LocoNumber} [{TrainSymbol}] " +
            $"{TrainSpeedMph:F1}/{TrainSpeedLimitMPH}mph blk={BlockID} " +
            $"{TrainWeightTons}t {TrainLengthFeet}ft eng={EngineerType}:{EngineerName}";
    }
}
