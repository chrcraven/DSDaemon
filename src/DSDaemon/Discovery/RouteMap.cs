using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DSDaemon.Discovery {

    public sealed class BlockInfo {
        public string Name { get; set; } = "";
        /// <summary>Adjacent block ID → number of times that transition was observed.</summary>
        public Dictionary<int, int> AdjacentBlocks { get; set; } = new();
    }

    public sealed class RouteInfo {
        public string Name { get; set; } = "";
        public Dictionary<int, BlockInfo> Blocks { get; set; } = new();
    }

    public sealed class RouteMap {
        public Dictionary<int, RouteInfo> Routes { get; set; } = new();

        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { WriteIndented = true };

        /// <summary>
        /// Records one observed block transition, incrementing confidence on both directions
        /// of the edge. Transitions involving block 0 (unknown) are silently ignored.
        /// </summary>
        public void RecordAdjacency(int route, int fromBlock, int toBlock) {
            if (fromBlock == 0 || toBlock == 0 || fromBlock == toBlock) return;

            var ri   = GetOrAddRoute(route);
            var from = GetOrAddBlock(ri, fromBlock);
            var to   = GetOrAddBlock(ri, toBlock);

            from.AdjacentBlocks.TryGetValue(toBlock,   out int fwd);
            to.AdjacentBlocks.TryGetValue(fromBlock, out int rev);
            from.AdjacentBlocks[toBlock]   = fwd + 1;
            to.AdjacentBlocks[fromBlock]   = rev + 1;
        }

        public int GetAdjacencyConfidence(int route, int fromBlock, int toBlock) {
            if (!Routes.TryGetValue(route, out var ri)) return 0;
            if (!ri.Blocks.TryGetValue(fromBlock, out var bi)) return 0;
            bi.AdjacentBlocks.TryGetValue(toBlock, out int n);
            return n;
        }

        public IReadOnlyDictionary<int, int> GetAdjacentBlocks(int route, int blockId) {
            if (!Routes.TryGetValue(route, out var ri)) return _empty;
            if (!ri.Blocks.TryGetValue(blockId, out var bi)) return _empty;
            return bi.AdjacentBlocks;
        }

        private static readonly Dictionary<int, int> _empty = new();

        private RouteInfo GetOrAddRoute(int route) {
            if (!Routes.TryGetValue(route, out var ri))
                Routes[route] = ri = new RouteInfo();
            return ri;
        }

        private static BlockInfo GetOrAddBlock(RouteInfo ri, int blockId) {
            if (!ri.Blocks.TryGetValue(blockId, out var bi))
                ri.Blocks[blockId] = bi = new BlockInfo();
            return bi;
        }

        public static RouteMap LoadOrCreate(string path) {
            if (!File.Exists(path)) return new RouteMap();
            try {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<RouteMap>(json, JsonOpts) ?? new RouteMap();
            } catch {
                return new RouteMap();
            }
        }

        public void Save(string path) {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
        }
    }
}
