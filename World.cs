using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace RaylibUltralightApp
{
    public class World
    {
        public int RenderDistance { get; set; } = 4; // Chunk radius around player
        public int MaxChunksPerFrame { get; set; } = 2; // Smooth chunk loading budget per frame

        private readonly Dictionary<(int x, int z), Chunk> chunks = new Dictionary<(int x, int z), Chunk>();

        public int TotalLoadedChunks => chunks.Count;

        public int TotalRawQuads
        {
            get
            {
                int sum = 0;
                foreach (var chunk in chunks.Values)
                {
                    sum += chunk.RawQuadCount;
                }
                return sum;
            }
        }

        public int TotalGreedyQuads
        {
            get
            {
                int sum = 0;
                foreach (var chunk in chunks.Values)
                {
                    sum += chunk.GreedyQuadCount;
                }
                return sum;
            }
        }

        public float QuadReductionPercentage
        {
            get
            {
                int raw = TotalRawQuads;
                if (raw == 0) return 0f;
                int greedy = TotalGreedyQuads;
                return (1.0f - ((float)greedy / raw)) * 100.0f;
            }
        }

        public void Update(Vector3 playerPosition)
        {
            int centerCX = (int)Math.Floor(playerPosition.X / Chunk.SIZE_X);
            int centerCZ = (int)Math.Floor(playerPosition.Z / Chunk.SIZE_Z);

            // 1. Unload out-of-range chunks
            int unloadRadius = RenderDistance + 1;
            List<(int x, int z)> toRemove = new List<(int x, int z)>();

            foreach (var key in chunks.Keys)
            {
                int dx = key.x - centerCX;
                int dz = key.z - centerCZ;
                if ((dx * dx + dz * dz) > (unloadRadius * unloadRadius))
                {
                    toRemove.Add(key);
                }
            }

            foreach (var key in toRemove)
            {
                if (chunks.TryGetValue(key, out Chunk? chunk))
                {
                    chunk.Unload();
                    chunks.Remove(key);
                }
            }

            // 2. Queue missing chunks in radius around player (sorted by distance from center)
            List<ChunkCandidate> candidates = new List<ChunkCandidate>();

            for (int dx = -RenderDistance; dx <= RenderDistance; dx++)
            {
                for (int dz = -RenderDistance; dz <= RenderDistance; dz++)
                {
                    int distSq = dx * dx + dz * dz;
                    if (distSq <= RenderDistance * RenderDistance)
                    {
                        int cx = centerCX + dx;
                        int cz = centerCZ + dz;
                        var key = (cx, cz);

                        if (!chunks.ContainsKey(key))
                        {
                            candidates.Add(new ChunkCandidate { CX = cx, CZ = cz, DistSq = distSq });
                        }
                    }
                }
            }

            // Sort candidates so closest chunks load first
            candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));

            // Load budget per frame
            int loadedThisFrame = 0;
            foreach (var cand in candidates)
            {
                if (loadedThisFrame >= MaxChunksPerFrame) break;

                var key = (cand.CX, cand.CZ);
                Chunk newChunk = new Chunk(cand.CX, cand.CZ);
                chunks[key] = newChunk;
                loadedThisFrame++;
            }
        }

        public void Draw()
        {
            foreach (var chunk in chunks.Values)
            {
                if (chunk.IsLoaded)
                {
                    Raylib.DrawModel(chunk.Model, Vector3.Zero, 1.0f, Color.White);
                }
            }
        }

        public void Cleanup()
        {
            foreach (var chunk in chunks.Values)
            {
                chunk.Unload();
            }
            chunks.Clear();
        }

        private struct ChunkCandidate
        {
            public int CX;
            public int CZ;
            public int DistSq;
        }
    }
}
