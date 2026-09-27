using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace RaylibUltralightApp
{
    public class Chunk
    {
        public const int SIZE_X = 16;
        public const int SIZE_Y = 32;
        public const int SIZE_Z = 16;

        public int ChunkX { get; }
        public int ChunkZ { get; }

        private readonly BlockType[,,] voxels = new BlockType[SIZE_X, SIZE_Y, SIZE_Z];

        public Mesh Mesh { get; private set; }
        public Model Model { get; private set; }
        public bool IsLoaded { get; private set; }

        public int RawQuadCount { get; private set; }
        public int GreedyQuadCount { get; private set; }

        public Chunk(int chunkX, int chunkZ)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            GenerateTerrain();
            BuildMesh();
        }

        private void GenerateTerrain()
        {
            int worldXOffset = ChunkX * SIZE_X;
            int worldZOffset = ChunkZ * SIZE_Z;

            for (int x = 0; x < SIZE_X; x++)
            {
                int wx = worldXOffset + x;
                for (int z = 0; z < SIZE_Z; z++)
                {
                    int wz = worldZOffset + z;

                    // Compute height using overlapping sine waves
                    float heightVal = 14.0f + (float)(
                        Math.Sin(wx * 0.07f) * 5.0f +
                        Math.Cos(wz * 0.07f) * 5.0f +
                        Math.Sin((wx + wz) * 0.12f) * 3.0f
                    );

                    int height = Math.Clamp((int)heightVal, 3, SIZE_Y - 6);

                    for (int y = 0; y < SIZE_Y; y++)
                    {
                        if (y == 0)
                        {
                            voxels[x, y, z] = BlockType.Bedrock;
                        }
                        else if (y < height - 3)
                        {
                            voxels[x, y, z] = BlockType.Stone;
                        }
                        else if (y < height)
                        {
                            voxels[x, y, z] = BlockType.Dirt;
                        }
                        else if (y == height)
                        {
                            voxels[x, y, z] = (height <= 9) ? BlockType.Sand : BlockType.Grass;
                        }
                        else if (y <= 9 && height <= 9)
                        {
                            voxels[x, y, z] = BlockType.Water;
                        }
                        else
                        {
                            voxels[x, y, z] = BlockType.Air;
                        }
                    }

                    // Tree generation on grass with deterministic placement
                    if (height > 9 && height < SIZE_Y - 7)
                    {
                        int hash = Math.Abs((wx * 73856093) ^ (wz * 19349663));
                        if (hash % 47 == 0 && x >= 2 && x <= SIZE_X - 3 && z >= 2 && z <= SIZE_Z - 3)
                        {
                            int trunkHeight = 4;
                            for (int ty = 1; ty <= trunkHeight; ty++)
                            {
                                if (height + ty < SIZE_Y)
                                    voxels[x, height + ty, z] = BlockType.Wood;
                            }

                            // Leaves canopy
                            int leafBase = height + trunkHeight - 1;
                            for (int lx = -2; lx <= 2; lx++)
                            {
                                for (int lz = -2; lz <= 2; lz++)
                                {
                                    for (int ly = 0; ly <= 2; ly++)
                                    {
                                        if (Math.Abs(lx) == 2 && Math.Abs(lz) == 2 && ly == 2) continue;
                                        int px = x + lx;
                                        int pz = z + lz;
                                        int py = leafBase + ly;

                                        if (px >= 0 && px < SIZE_X && pz >= 0 && pz < SIZE_Z && py < SIZE_Y)
                                        {
                                            if (voxels[px, py, pz] == BlockType.Air)
                                            {
                                                voxels[px, py, pz] = BlockType.Leaves;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public BlockType GetBlock(int x, int y, int z)
        {
            if (x < 0 || x >= SIZE_X || y < 0 || y >= SIZE_Y || z < 0 || z >= SIZE_Z)
            {
                return BlockType.Air;
            }
            return voxels[x, y, z];
        }

        private struct Quad
        {
            public Vector3 Position; // Min corner (x,y,z)
            public float Width;      // u dimension length
            public float Height;     // v dimension length
            public int Direction;    // 0: -Z, 1: +Z, 2: -X, 3: +X, 4: -Y, 5: +Y
            public BlockType Block;
        }

        private unsafe void BuildMesh()
        {
            List<Quad> quads = RunGreedyMesher();
            GreedyQuadCount = quads.Count;

            if (quads.Count == 0)
            {
                IsLoaded = false;
                return;
            }

            int vertexCount = quads.Count * 4;
            int triangleCount = quads.Count * 2;

            float* vertices = (float*)Raylib.MemAlloc((uint)(vertexCount * 3 * sizeof(float)));
            float* normals = (float*)Raylib.MemAlloc((uint)(vertexCount * 3 * sizeof(float)));
            byte* colors = (byte*)Raylib.MemAlloc((uint)(vertexCount * 4 * sizeof(byte)));
            ushort* indices = (ushort*)Raylib.MemAlloc((uint)(triangleCount * 3 * sizeof(ushort)));

            int vIdx = 0;
            int nIdx = 0;
            int cIdx = 0;
            int iIdx = 0;

            float worldX = ChunkX * SIZE_X;
            float worldZ = ChunkZ * SIZE_Z;

            for (int q = 0; q < quads.Count; q++)
            {
                Quad quad = quads[q];
                ushort baseVertIndex = (ushort)(q * 4);

                BlockFace face;
                Vector3 normal;

                switch (quad.Direction)
                {
                    case 0: // -Z
                        face = BlockFace.Side;
                        normal = new Vector3(0, 0, -1);
                        break;
                    case 1: // +Z
                        face = BlockFace.Side;
                        normal = new Vector3(0, 0, 1);
                        break;
                    case 2: // -X
                        face = BlockFace.Side;
                        normal = new Vector3(-1, 0, 0);
                        break;
                    case 3: // +X
                        face = BlockFace.Side;
                        normal = new Vector3(1, 0, 0);
                        break;
                    case 4: // -Y
                        face = BlockFace.Bottom;
                        normal = new Vector3(0, -1, 0);
                        break;
                    case 5: // +Y
                    default:
                        face = BlockFace.Top;
                        normal = new Vector3(0, 1, 0);
                        break;
                }

                Color col = BlockHelper.GetBlockColor(quad.Block, face);

                // Define 4 quad vertices
                Vector3 v0, v1, v2, v3;

                float x = worldX + quad.Position.X;
                float y = quad.Position.Y;
                float z = worldZ + quad.Position.Z;
                float w = quad.Width;
                float h = quad.Height;

                switch (quad.Direction)
                {
                    case 0: // -Z
                        v0 = new Vector3(x, y, z);
                        v1 = new Vector3(x, y + h, z);
                        v2 = new Vector3(x + w, y + h, z);
                        v3 = new Vector3(x + w, y, z);
                        break;
                    case 1: // +Z
                        v0 = new Vector3(x, y, z + 1);
                        v1 = new Vector3(x + w, y, z + 1);
                        v2 = new Vector3(x + w, y + h, z + 1);
                        v3 = new Vector3(x, y + h, z + 1);
                        break;
                    case 2: // -X
                        v0 = new Vector3(x, y, z);
                        v1 = new Vector3(x, y, z + w);
                        v2 = new Vector3(x, y + h, z + w);
                        v3 = new Vector3(x, y + h, z);
                        break;
                    case 3: // +X
                        v0 = new Vector3(x + 1, y, z + w);
                        v1 = new Vector3(x + 1, y, z);
                        v2 = new Vector3(x + 1, y + h, z);
                        v3 = new Vector3(x + 1, y + h, z + w);
                        break;
                    case 4: // -Y
                        v0 = new Vector3(x, y, z);
                        v1 = new Vector3(x + w, y, z);
                        v2 = new Vector3(x + w, y, z + h);
                        v3 = new Vector3(x, y, z + h);
                        break;
                    case 5: // +Y
                    default:
                        v0 = new Vector3(x, y + 1, z + h);
                        v1 = new Vector3(x + w, y + 1, z + h);
                        v2 = new Vector3(x + w, y + 1, z);
                        v3 = new Vector3(x, y + 1, z);
                        break;
                }

                Vector3[] verts = new Vector3[] { v0, v1, v2, v3 };

                for (int i = 0; i < 4; i++)
                {
                    vertices[vIdx++] = verts[i].X;
                    vertices[vIdx++] = verts[i].Y;
                    vertices[vIdx++] = verts[i].Z;

                    normals[nIdx++] = normal.X;
                    normals[nIdx++] = normal.Y;
                    normals[nIdx++] = normal.Z;

                    colors[cIdx++] = col.R;
                    colors[cIdx++] = col.G;
                    colors[cIdx++] = col.B;
                    colors[cIdx++] = col.A;
                }

                // Quad triangles (0, 1, 2) and (0, 2, 3)
                indices[iIdx++] = (ushort)(baseVertIndex + 0);
                indices[iIdx++] = (ushort)(baseVertIndex + 1);
                indices[iIdx++] = (ushort)(baseVertIndex + 2);

                indices[iIdx++] = (ushort)(baseVertIndex + 0);
                indices[iIdx++] = (ushort)(baseVertIndex + 2);
                indices[iIdx++] = (ushort)(baseVertIndex + 3);
            }

            Mesh mesh = new Mesh
            {
                VertexCount = vertexCount,
                TriangleCount = triangleCount,
                Vertices = vertices,
                Normals = normals,
                Colors = colors,
                Indices = indices
            };

            Raylib.UploadMesh(ref mesh, false);
            Mesh = mesh;
            Model = Raylib.LoadModelFromMesh(mesh);
            IsLoaded = true;
        }

        private List<Quad> RunGreedyMesher()
        {
            List<Quad> quads = new List<Quad>();
            int rawCount = 0;

            // Sweep through all 6 faces
            for (int dir = 0; dir < 6; dir++)
            {
                int mainAxisMax = (dir == 2 || dir == 3) ? SIZE_X : ((dir == 4 || dir == 5) ? SIZE_Y : SIZE_Z);
                int uMax = (dir == 2 || dir == 3) ? SIZE_Z : SIZE_X;
                int vMax = (dir == 4 || dir == 5) ? SIZE_Z : SIZE_Y;

                for (int slice = 0; slice < mainAxisMax; slice++)
                {
                    BlockType[,] mask = new BlockType[uMax, vMax];

                    // Populate mask for current slice
                    for (int u = 0; u < uMax; u++)
                    {
                        for (int v = 0; v < vMax; v++)
                        {
                            int x = 0, y = 0, z = 0;
                            int nx = 0, ny = 0, nz = 0;

                            GetSliceCoordinates(dir, slice, u, v, out x, out y, out z, out nx, out ny, out nz);

                            BlockType currentBlock = voxels[x, y, z];

                            if (currentBlock != BlockType.Air)
                            {
                                BlockType neighborBlock = GetBlock(nx, ny, nz);
                                bool neighborTransparent = BlockHelper.IsTransparent(neighborBlock);

                                // Special case for water: don't render water-to-water internal faces
                                if (currentBlock == BlockType.Water && neighborBlock == BlockType.Water)
                                {
                                    neighborTransparent = false;
                                }

                                if (neighborTransparent)
                                {
                                    mask[u, v] = currentBlock;
                                    rawCount++;
                                }
                            }
                        }
                    }

                    // Greedy merge on 2D mask
                    for (int v = 0; v < vMax; v++)
                    {
                        for (int u = 0; u < uMax; u++)
                        {
                            BlockType type = mask[u, v];
                            if (type == BlockType.Air) continue;

                            // Compute width w
                            int w = 1;
                            while (u + w < uMax && mask[u + w, v] == type)
                            {
                                w++;
                            }

                            // Compute height h
                            int h = 1;
                            bool canExpand = true;
                            while (v + h < vMax && canExpand)
                            {
                                for (int i = 0; i < w; i++)
                                {
                                    if (mask[u + i, v + h] != type)
                                    {
                                        canExpand = false;
                                        break;
                                    }
                                }
                                if (canExpand) h++;
                            }

                            // Store merged quad position in chunk coordinates
                            int startX = 0, startY = 0, startZ = 0;
                            int dummy1 = 0, dummy2 = 0, dummy3 = 0;
                            GetSliceCoordinates(dir, slice, u, v, out startX, out startY, out startZ, out dummy1, out dummy2, out dummy3);

                            quads.Add(new Quad
                            {
                                Position = new Vector3(startX, startY, startZ),
                                Width = w,
                                Height = h,
                                Direction = dir,
                                Block = type
                            });

                            // Clear mask for merged cells
                            for (int j = 0; j < h; j++)
                            {
                                for (int i = 0; i < w; i++)
                                {
                                    mask[u + i, v + j] = BlockType.Air;
                                }
                            }

                            u += w - 1;
                        }
                    }
                }
            }

            RawQuadCount = rawCount;
            return quads;
        }

        private static void GetSliceCoordinates(int dir, int slice, int u, int v,
            out int x, out int y, out int z,
            out int nx, out int ny, out int nz)
        {
            switch (dir)
            {
                case 0: // -Z
                    x = u; y = v; z = slice;
                    nx = x; ny = y; nz = z - 1;
                    break;
                case 1: // +Z
                    x = u; y = v; z = slice;
                    nx = x; ny = y; nz = z + 1;
                    break;
                case 2: // -X
                    x = slice; y = v; z = u;
                    nx = x - 1; ny = y; nz = z;
                    break;
                case 3: // +X
                    x = slice; y = v; z = u;
                    nx = x + 1; ny = y; nz = z;
                    break;
                case 4: // -Y
                    x = u; y = slice; z = v;
                    nx = x; ny = y - 1; nz = z;
                    break;
                case 5: // +Y
                default:
                    x = u; y = slice; z = v;
                    nx = x; ny = y + 1; nz = z;
                    break;
            }
        }

        public void Unload()
        {
            if (IsLoaded)
            {
                Raylib.UnloadModel(Model); // UnloadModel also unloads attached mesh in Raylib
                IsLoaded = false;
            }
        }
    }
}
